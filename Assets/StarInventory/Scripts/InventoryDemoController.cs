using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class InventoryDemoController : MonoBehaviour
{
    public ItemDatabase db;

    [Header("UI")]
    public Transform gridRoot;
    public InventoryItemView itemViewPrefab;
    public Button toggleModeButton;
    public TextMeshProUGUI modeLabel;

    [Header("Config")]
    public ViewPreset viewPreset = ViewPreset.Angle45;
    public int resolution = 256;

    private readonly List<InventoryItemView> views = new List<InventoryItemView>();

    private void Start()
    {
        if (toggleModeButton != null)
            toggleModeButton.onClick.AddListener(ToggleMode);

        BuildGrid();
        RefreshAll();
    }

    private void BuildGrid()
    {
        foreach (Transform c in gridRoot) Destroy(c.gameObject);
        views.Clear();

        if (db == null) return;

        for (int i = 0; i < db.items.Count; i++)
        {
            var e = db.items[i];
            if (e == null || e.prefab == null) continue;

            string variant = (e.variantIds != null && e.variantIds.Length > 0) ? e.variantIds[0] : "";

            var v = Instantiate(itemViewPrefab, gridRoot);
            v.Bind(e.itemId, variant, viewPreset, resolution, () => Task.FromResult(e.prefab));
            views.Add(v);
        }
    }

    private void ToggleMode()
    {
        var svc = IconService.Instance;
        svc.iconMode = (svc.iconMode == InventoryIconMode.Cached2D) ? InventoryIconMode.Realtime3D : InventoryIconMode.Cached2D;
        RefreshAll();
    }

    private void RefreshAll()
    {
        var svc = IconService.Instance;
        if (modeLabel != null) modeLabel.text = svc.iconMode.ToString();

        for (int i = 0; i < views.Count; i++)
            views[i].Refresh();
    }
}
