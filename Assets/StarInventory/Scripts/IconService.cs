using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class IconService : MonoBehaviour
{
    public static IconService Instance { get; private set; }

    [Header("Mode")]
    public InventoryIconMode iconMode = InventoryIconMode.Cached2D;

    [Header("Cache")]
    public string iconCacheVersion = "v1";
    public int defaultResolution = 256;

    [Header("Generation budget")]
    public int maxCapturesPerFrame = 1;

    [Header("Rig")]
    public IconCaptureRig rig;

    [Header("Variant Applier (optional)")]
    public MonoBehaviour variantApplierBehaviour;

    private IIconVariantApplier VariantApplier => variantApplierBehaviour as IIconVariantApplier;

    private readonly Dictionary<IconKey, Sprite> mem = new Dictionary<IconKey, Sprite>();
    private readonly Dictionary<IconKey, List<Action<Sprite>>> waiters = new Dictionary<IconKey, List<Action<Sprite>>>();

    private DiskCache disk;
    private RequestQueue queue;

    private Sprite placeholder;

    public event Action<int, int> OnQueueProgress;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        disk = new DiskCache(System.IO.Path.Combine(Application.persistentDataPath, "IconCache"));

        queue = gameObject.AddComponent<RequestQueue>();
        queue.maxCapturesPerFrame = Mathf.Max(1, maxCapturesPerFrame);
        queue.OnProgress += (pending, total) => OnQueueProgress?.Invoke(pending, total);

        if (rig == null)
        {
            var go = new GameObject("IconCaptureRig");
            go.transform.SetParent(transform, false);
            rig = go.AddComponent<IconCaptureRig>();
        }

        placeholder = CreatePlaceholderSprite();

        StartCoroutine(ProcessQueueLoop());
    }

    public IconKey MakeKey(string itemId, string variantId, ViewPreset view, int resolution = 0)
    {
        int res = resolution > 0 ? resolution : defaultResolution;
        return new IconKey(itemId, variantId, view, res, iconCacheVersion);
    }

    // Returns immediately: cached sprite or placeholder. Updates via callback when ready.
    public Sprite GetIconAsync(IconKey key, Func<Task<GameObject>> prefabProvider, Action<Sprite> onReady)
    {
        if (iconMode == InventoryIconMode.Realtime3D)
        {
            // Realtime3D path handled by UI cell renderer; this service returns placeholder.
            return placeholder;
        }

        if (mem.TryGetValue(key, out var s) && s != null)
        {
            onReady?.Invoke(s);
            return s;
        }

        RegisterWaiter(key, onReady);

        // Try disk async first; if missing, enqueue capture.
        _ = TryLoadFromDiskOrEnqueue(key, prefabProvider);

        return placeholder;
    }

    private async Task TryLoadFromDiskOrEnqueue(IconKey key, Func<Task<GameObject>> prefabProvider)
    {
        // De-dupe: if already in waiters, we still proceed once; disk load is cheap.
        string path = disk.GetPath(key);
        byte[] png = await disk.ReadAsync(path);

        if (png != null && png.Length > 0)
        {
            // Must create Texture2D/Sprite on main thread
            UnityMainThread(() =>
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (tex.LoadImage(png, false))
                {
                    var spr = SpriteFromTexture(tex);
                    mem[key] = spr;
                    ResolveWaiters(key, spr);
                }
                else
                {
                    Destroy(tex);
                    queue.Enqueue(key);
                }
            });

            return;
        }

        // Disk miss -> capture
        queue.Enqueue(key);

        // Store prefab provider for capture step
        lock (prefabProviders)
            prefabProviders[key] = prefabProvider;
    }

    // Prefab providers stored per key (runtime-only)
    private readonly Dictionary<IconKey, Func<Task<GameObject>>> prefabProviders = new Dictionary<IconKey, Func<Task<GameObject>>>();

    private IEnumerator ProcessQueueLoop()
    {
        while (true)
        {
            int budget = Mathf.Max(1, maxCapturesPerFrame);
            int doneThisFrame = 0;

            while (doneThisFrame < budget && queue.TryDequeue(out var key))
            {
                yield return CaptureAndCache(key);
                queue.MarkDone(key);
                doneThisFrame++;
            }

            yield return null;
        }
    }

    private IEnumerator CaptureAndCache(IconKey key)
    {
        Func<Task<GameObject>> provider = null;
        lock (prefabProviders)
            prefabProviders.TryGetValue(key, out provider);

        if (provider == null)
        {
            ResolveWaiters(key, placeholder);
            yield break;
        }

        // Addressables/direct provider returns prefab
        var task = provider();
        while (!task.IsCompleted) yield return null;

        var prefab = task.Result;
        if (prefab == null)
        {
            ResolveWaiters(key, placeholder);
            yield break;
        }

        Texture2D captured = null;
        yield return rig.CaptureToTexture2D(
            prefab,
            VariantApplier,
            key.variantId,
            key.view,
            key.resolution,
            tex => captured = tex);

        if (captured == null)
        {
            ResolveWaiters(key, placeholder);
            yield break;
        }

        // Create sprite, store in memory
        var spr = SpriteFromTexture(captured);
        mem[key] = spr;
        ResolveWaiters(key, spr);

        // Encode + write disk (async write)
        byte[] png = captured.EncodeToPNG();
        string path = disk.GetPath(key);
        _ = disk.WriteAsync(path, png);
    }

    private void RegisterWaiter(IconKey key, Action<Sprite> cb)
    {
        if (cb == null) return;

        if (!waiters.TryGetValue(key, out var list))
        {
            list = new List<Action<Sprite>>();
            waiters[key] = list;
        }
        list.Add(cb);
    }

    private void ResolveWaiters(IconKey key, Sprite sprite)
    {
        if (!waiters.TryGetValue(key, out var list)) return;
        waiters.Remove(key);

        for (int i = 0; i < list.Count; i++)
        {
            try { list[i]?.Invoke(sprite); } catch { /* swallow */ }
        }

        lock (prefabProviders)
            prefabProviders.Remove(key);
    }

    private static Sprite SpriteFromTexture(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreatePlaceholderSprite()
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        tex.SetPixels32(new[]
        {
            new Color32(60,60,60,255), new Color32(90,90,90,255),
            new Color32(90,90,90,255), new Color32(60,60,60,255)
        });
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
    }

    // Main-thread marshal (no Editor API)
    private static readonly Queue<Action> mainThread = new Queue<Action>();

    private void Update()
    {
        lock (mainThread)
        {
            while (mainThread.Count > 0)
                mainThread.Dequeue()?.Invoke();
        }
    }

    private static void UnityMainThread(Action a)
    {
        lock (mainThread) mainThread.Enqueue(a);
    }

    // Optional invalidation: bump iconCacheVersion externally. Filename includes it.

    // Optional eviction (simple)
    public void ClearMemoryCache()
    {
        foreach (var kv in mem)
        {
            if (kv.Value == null) continue;
            var tex = kv.Value.texture;
            Destroy(kv.Value);
            if (tex != null) Destroy(tex);
        }
        mem.Clear();
    }
}
