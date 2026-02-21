using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public sealed class Realtime3DIconRenderer : MonoBehaviour
{
    public RawImage target;
    public int resolution = 256;

    public int captureLayer = 29;
    public float paddingPercent = 0.10f;
    public float cameraDistance = 8f;
    public float orthSize = 2f;

    public MonoBehaviour variantApplierBehaviour;
    private IIconVariantApplier VariantApplier => variantApplierBehaviour as IIconVariantApplier;

    private Camera cam;
    private RenderTexture rt;
    private GameObject root;
    private GameObject inst;
    private Coroutine loop;

    public void Play(Func<Task<GameObject>> prefabProvider, string variantId, ViewPreset view)
    {
        Stop();
        loop = StartCoroutine(Run(prefabProvider, variantId, view));
    }

    public void Stop()
    {
        if (loop != null) StopCoroutine(loop);
        loop = null;

        if (inst != null) Destroy(inst);
        inst = null;

        if (root != null) Destroy(root);
        root = null;

        if (cam != null) Destroy(cam.gameObject);
        cam = null;

        if (rt != null)
        {
            rt.Release();
            Destroy(rt);
            rt = null;
        }
    }

    private IEnumerator Run(Func<Task<GameObject>> prefabProvider, string variantId, ViewPreset view)
    {
        if (target == null) yield break;

        root = new GameObject("Realtime3DIconRoot");
        root.transform.SetParent(transform, false);
        SetLayerRecursively(root, captureLayer);
       

        var camGO = new GameObject("Realtime3DIconCamera");
        camGO.transform.SetParent(transform, false);
        cam = camGO.AddComponent<Camera>();
        cam.enabled = true;
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.cullingMask = 1 << captureLayer;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        cam.orthographicSize = 2f;
/*
        var lightGO = new GameObject("Realtime3DIconLight");
        lightGO.transform.SetParent(transform, false);
        var l = lightGO.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = Color.white;
        l.intensity = 1.2f;
        lightGO.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
        lightGO.layer = captureLayer;
        */
        rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        cam.targetTexture = rt;
        target.texture = rt;

        var task = prefabProvider();
        while (!task.IsCompleted) yield return null;

        var prefab = task.Result;
        if (prefab == null) yield break;

        inst = Instantiate(prefab, root.transform);
        SetLayerRecursively(inst, captureLayer);

        VariantApplier?.Apply(inst, variantId);
        inst.transform.rotation = view switch
        {
            ViewPreset.Front => Quaternion.Euler(0f, 180f, 0f),
            ViewPreset.Side => Quaternion.Euler(0f, 90f, 0f),
            _ => Quaternion.Euler(20f, 225f, 0f),
        };

        if (!TryGetBounds(inst, out var b)) yield break;

        inst.transform.position = inst.transform.position - b.center;
        TryGetBounds(inst, out b);

        float extX = b.extents.x;
        float extY = b.extents.y;
        float size = Mathf.Max(extY, extX) * (1f + paddingPercent);
        cam.orthographicSize = orthSize;
        cam.transform.position = new Vector3(0f, 0f, -cameraDistance);
        cam.transform.rotation = Quaternion.identity;

        // Keep updating (optional spin to show "realtime" cost)
       /* while (true)
        {
            if (inst != null) inst.transform.Rotate(Vector3.up, 30f * Time.deltaTime, Space.World);
            yield return null;
        }*/
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
            if (!has) { bounds = rends[i].bounds; has = true; }
            else bounds.Encapsulate(rends[i].bounds);
        }
        return has;
    }
}
