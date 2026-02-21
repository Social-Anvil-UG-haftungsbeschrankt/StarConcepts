using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/ItemDatabase")]
public sealed class ItemDatabase : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string itemId;
        public GameObject prefab;
        public string[] variantIds;
    }

    public List<Entry> items = new List<Entry>();

    public Entry Get(string itemId)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].itemId == itemId) return items[i];
        return null;
    }
}
