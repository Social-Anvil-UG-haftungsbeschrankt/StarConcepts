using System.Collections;
using UnityEngine;

public sealed class IconCaptureRig : MonoBehaviour
{
    [Header("Layer")]
    [Tooltip("All capture objects will be placed on this layer.")]
    public int captureLayer = 30;

    [Header("Camera")]
    public Camera captureCamera;
    public float paddingPercent = 0.10f;
    public float cameraDistance = 10f;
    public bool preferTransparentClear = true;

    [Header("Chroma-key fallback")]
    public bool forceChromaKey = false;
    public Color chromaKeyColor = new Color(1f, 0f, 1f, 1f);
    public float chromaTolerance = 0.02f;

    [Header("Lighting")]
    public Light keyLight;
    public Light fillLight;

    private RenderTexture rt;
    private int rtRes = 256;

    private readonly WaitForEndOfFrame waitEnd = new WaitForEndOfFrame();

    public void Ensure(int resolution)
    {
        if (resolution <= 0) resolution = 256;

        if (captureCamera == null)
        {
            var camGO = new GameObject("IconCaptureCamera");
            camGO.transform.SetParent(transform, false);
            captureCamera = camGO.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.nearClipPlane = 0.01f;
            captureCamera.farClipPlane = 100f;
        }

        if (keyLight == null)
        {
            var l0 = new GameObject("KeyLight");
            l0.transform.SetParent(transform, false);
            keyLight = l0.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Color.white;
            keyLight.intensity = 1.2f;
            l0.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
        }

        if (fillLight == null)
        {
            var l1 = new GameObject("FillLight");
            l1.transform.SetParent(transform, false);
            fillLight = l1.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = Color.white;
            fillLight.intensity = 0.35f;
            l1.transform.rotation = Quaternion.Euler(20f, 220f, 0f);
        }

        gameObject.layer = captureLayer;
        captureCamera.cullingMask = 1 << captureLayer;

        EnsureRT(resolution);
    }

    private void EnsureRT(int resolution)
    {
        if (rt != null && rtRes == resolution && rt.IsCreated()) return;

        rtRes = resolution;

        if (rt != null)
        {
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        rt = new RenderTexture(rtRes, rtRes, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        rt.Create();

        captureCamera.targetTexture = rt;
    }

    public IEnumerator CaptureToTexture2D(
        GameObject prefab,
        IIconVariantApplier variantApplier,
        string variantId,
        ViewPreset viewPreset,
        int resolution,
        System.Action<Texture2D> onComplete)
    {
        Ensure(resolution);

        if (prefab == null)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        var root = new GameObject("IconCaptureRoot");
        root.transform.SetParent(transform, false);
        SetLayerRecursively(root, captureLayer);

        var inst = Instantiate(prefab, root.transform);
        inst.name = prefab.name + "_IconCapture";
        SetLayerRecursively(inst, captureLayer);

        variantApplier?.Apply(inst, variantId);

        // Disable any AudioSources to avoid surprises in builds
        foreach (var a in inst.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;

        // Compute bounds
        if (!TryGetBounds(inst, out Bounds b))
        {
            Destroy(root);
            onComplete?.Invoke(null);
            yield break;
        }

        // Center object to origin
        inst.transform.position = inst.transform.position - b.center;

        // Apply view preset (rotate object deterministically)
        inst.transform.rotation = GetRotation(viewPreset);

        // Recompute bounds after rotation to fit better
        TryGetBounds(inst, out b);

        // Camera framing (orthographic)
        float aspect = 1f;
        float extX = b.extents.x;
        float extY = b.extents.y;

        float size = Mathf.Max(extY, extX / Mathf.Max(0.0001f, aspect));
        size *= (1f + Mathf.Max(0f, paddingPercent));

        captureCamera.orthographic = true;
        captureCamera.orthographicSize = Mathf.Max(0.01f, size);

        captureCamera.transform.position = new Vector3(0f, 0f, -cameraDistance);
        captureCamera.transform.rotation = Quaternion.identity;

        bool chroma = forceChromaKey || !preferTransparentClear;

        if (chroma)
        {
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = chromaKeyColor;
        }
        else
        {
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(0, 0, 0, 0);
        }

        // Render
        yield return waitEnd;
        captureCamera.Render();
        yield return waitEnd;

        // Readback
        var prev = RenderTexture.active;
        RenderTexture.active = rt;

        var tex = new Texture2D(rtRes, rtRes, TextureFormat.RGBA32, false, false);
        tex.ReadPixels(new Rect(0, 0, rtRes, rtRes), 0, 0);
        tex.Apply(false, false);

        RenderTexture.active = prev;

        if (chroma)
        {
            ApplyChromaKey(tex, chromaKeyColor, chromaTolerance);
        }

        Destroy(root);

        onComplete?.Invoke(tex);
    }

    private static Quaternion GetRotation(ViewPreset preset)
    {
        return preset switch
        {
            ViewPreset.Front => Quaternion.Euler(0f, 180f, 0f),
            ViewPreset.Side => Quaternion.Euler(0f, 90f, 0f),
            _ => Quaternion.Euler(20f, 225f, 0f), // Angle45
        };
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        bounds = default;

        bool has = false;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            if (!has)
            {
                bounds = rends[i].bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(rends[i].bounds);
            }
        }
        return has;
    }

    private static void ApplyChromaKey(Texture2D tex, Color key, float tol)
    {
        if (tex == null) return;

        var pixels = tex.GetPixels32();
        byte kr = (byte)Mathf.RoundToInt(key.r * 255f);
        byte kg = (byte)Mathf.RoundToInt(key.g * 255f);
        byte kb = (byte)Mathf.RoundToInt(key.b * 255f);

        int t = Mathf.RoundToInt(tol * 255f);

        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            int dr = Mathf.Abs(p.r - kr);
            int dg = Mathf.Abs(p.g - kg);
            int db = Mathf.Abs(p.b - kb);

            if (dr <= t && dg <= t && db <= t)
            {
                p.a = 0;
                pixels[i] = p;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
    }
}
