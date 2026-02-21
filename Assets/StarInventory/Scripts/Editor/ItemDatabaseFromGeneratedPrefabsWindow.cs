using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public sealed class ItemDatabaseFromGeneratedPrefabsWindow : EditorWindow
{
    [Header("Generator Output Root")]
    [SerializeField] private DefaultAsset outputRootFolder;     // same root used by the generator
    [SerializeField] private string prefabsSubfolder = "Prefabs";

    [Header("Database Output")]
    [SerializeField] private DefaultAsset databaseFolder;       // where ItemDatabase.asset will be created
    [SerializeField] private string databaseAssetName = "ItemDatabase.asset";

    [Header("Parsing")]
    [Tooltip("If enabled, tries to collapse name suffixes like _V00 / _V01 into variants.")]
    [SerializeField] private bool parseVariantSuffix = true;

    [Tooltip("Variant ID regex, default matches _V00, _V01...  Group 1 is variantId without underscore.")]
    [SerializeField] private string variantRegex = @"_V(\d{2,4})$";

    [Tooltip("If enabled, itemId will be the base prefab name without variant suffix; otherwise uses full prefab name.")]
    [SerializeField] private bool itemIdIsBaseName = true;

    [Header("Behaviour")]
    [SerializeField] private bool replaceEntireDatabase = true;
    [SerializeField] private bool sortByItemId = true;

    [MenuItem("Tools/StarConcepts/Build ItemDatabase From Generated Prefabs")]
    public static void Open()
    {
        var w = GetWindow<ItemDatabaseFromGeneratedPrefabsWindow>();
        w.titleContent = new GUIContent("ItemDB Builder");
        w.minSize = new Vector2(520, 360);
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("ItemDatabase builder (from generated Prefabs)", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            outputRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Root Folder", outputRootFolder, typeof(DefaultAsset), false);
            prefabsSubfolder = EditorGUILayout.TextField("Prefabs Subfolder", prefabsSubfolder);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            databaseFolder = (DefaultAsset)EditorGUILayout.ObjectField("Database Folder", databaseFolder, typeof(DefaultAsset), false);
            databaseAssetName = EditorGUILayout.TextField("Database Asset Name", databaseAssetName);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            parseVariantSuffix = EditorGUILayout.Toggle("Parse Variant Suffix", parseVariantSuffix);
            variantRegex = EditorGUILayout.TextField("Variant Regex", variantRegex);
            itemIdIsBaseName = EditorGUILayout.Toggle("ItemId = BaseName", itemIdIsBaseName);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            replaceEntireDatabase = EditorGUILayout.Toggle("Replace Entire Database", replaceEntireDatabase);
            sortByItemId = EditorGUILayout.Toggle("Sort By ItemId", sortByItemId);
        }

        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(outputRootFolder == null || databaseFolder == null))
        {
            if (GUILayout.Button("Build / Update ItemDatabase", GUILayout.Height(40)))
                Build();
        }
    }

    private void Build()
    {
        string rootPath = AssetDatabase.GetAssetPath(outputRootFolder);
        if (string.IsNullOrEmpty(rootPath) || !rootPath.StartsWith("Assets", StringComparison.Ordinal))
            throw new Exception("Output root must be inside Assets/");

        string prefabsPath = CombineAssetPath(rootPath, prefabsSubfolder);
        if (!AssetDatabase.IsValidFolder(prefabsPath))
            throw new Exception($"Prefabs folder not found: {prefabsPath}");

        string dbFolderPath = AssetDatabase.GetAssetPath(databaseFolder);
        if (string.IsNullOrEmpty(dbFolderPath) || !dbFolderPath.StartsWith("Assets", StringComparison.Ordinal))
            throw new Exception("Database folder must be inside Assets/");

        string dbPath = CombineAssetPath(dbFolderPath, databaseAssetName);
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(db, dbPath);
            AssetDatabase.SaveAssets();
        }

        var groups = ScanPrefabsGrouped(prefabsPath);

        Undo.RecordObject(db, "Build ItemDatabase");

        if (replaceEntireDatabase)
            db.items.Clear();

        foreach (var kv in groups)
        {
            string itemId = kv.Key;
            var variants = kv.Value;

            // Pick a representative prefab for Entry.prefab (prefer V00 if exists).
            GameObject chosen = null;
            if (variants.TryGetValue("00", out var v00)) chosen = v00;
            if (chosen == null)
            {
                foreach (var p in variants.Values) { chosen = p; break; }
            }

            var entry = new ItemDatabase.Entry
            {
                itemId = itemId,
                prefab = chosen,
                variantIds = BuildVariantIdsArray(variants)
            };

            if (!replaceEntireDatabase)
            {
                int idx = FindEntryIndex(db, itemId);
                if (idx >= 0) db.items[idx] = entry;
                else db.items.Add(entry);
            }
            else
            {
                db.items.Add(entry);
            }
        }

        if (sortByItemId)
            db.items.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));

        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private Dictionary<string, Dictionary<string, GameObject>> ScanPrefabsGrouped(string prefabsPath)
    {
        var result = new Dictionary<string, Dictionary<string, GameObject>>(StringComparer.Ordinal);
        var rx = new Regex(variantRegex, RegexOptions.Compiled);

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabsPath });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string name = prefab.name;
            string baseName = name;
            string variantId = "";

            if (parseVariantSuffix)
            {
                var m = rx.Match(name);
                if (m.Success)
                {
                    baseName = name.Substring(0, m.Index);
                    variantId = m.Groups.Count > 1 ? m.Groups[1].Value : "";
                }
            }

            string itemId = itemIdIsBaseName ? baseName : name;

            if (!result.TryGetValue(itemId, out var map))
            {
                map = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                result[itemId] = map;
            }

            // If no variant parsed, store as "default"
            if (string.IsNullOrEmpty(variantId)) variantId = "default";

            // Keep first hit per variantId to avoid duplicates.
            if (!map.ContainsKey(variantId))
                map[variantId] = prefab;
        }

        return result;
    }

    private static string[] BuildVariantIdsArray(Dictionary<string, GameObject> variants)
    {
        var keys = new List<string>(variants.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys.ToArray();
    }

    private static int FindEntryIndex(ItemDatabase db, string itemId)
    {
        for (int i = 0; i < db.items.Count; i++)
        {
            if (db.items[i].itemId == itemId) return i;
        }
        return -1;
    }

    private static string CombineAssetPath(string a, string b)
    {
        string p = Path.Combine(a, b).Replace("\\", "/");
        return p;
    }
}
