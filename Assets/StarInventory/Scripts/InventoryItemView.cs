using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class InventoryItemView : MonoBehaviour
{
    [Header("Cached2D")]
    public Image iconImage;

    [Header("Realtime3D")]
    public RawImage realtimeRawImage;
    public Realtime3DIconRenderer realtimeRenderer;

    [Header("Text")]
    public TextMeshProUGUI label;

    private string itemId;
    private string variantId;
    private ViewPreset view;
    private int resolution;
    private Func<Task<GameObject>> prefabProvider;

    public void Bind(string itemId, string variantId, ViewPreset view, int resolution, Func<Task<GameObject>> prefabProvider)
    {
        this.itemId = itemId;
        this.variantId = variantId;
        this.view = view;
        this.resolution = resolution;
        this.prefabProvider = prefabProvider;

        if (label != null) label.text = itemId;
    }

    public void Refresh()
    {
        var svc = IconService.Instance;

        bool cached2D = svc.iconMode == InventoryIconMode.Cached2D;

        if (iconImage != null) iconImage.gameObject.SetActive(cached2D);
        if (realtimeRawImage != null) realtimeRawImage.gameObject.SetActive(!cached2D);

        if (cached2D)
        {
            var key = svc.MakeKey(itemId, variantId, view, resolution);
            Sprite s = svc.GetIconAsync(key, prefabProvider, spr =>
            {
                if (iconImage != null) iconImage.sprite = spr;
            });

            if (iconImage != null) iconImage.sprite = s;

            if (realtimeRenderer != null) realtimeRenderer.Stop();
        }
        else
        {
            if (realtimeRenderer != null)
                realtimeRenderer.Play(prefabProvider, variantId, view);
        }
    }
}
