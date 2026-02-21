using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class ProceduralMeshLibraryGeneratorWindow : EditorWindow
{
    [Header("Output")]
    [SerializeField] private DefaultAsset outputRootFolder;          // e.g. Assets/Generated/MeshLibrary
    [SerializeField] private string meshesSubfolder = "Meshes";
    [SerializeField] private string texturesSubfolder = "Textures";
    [SerializeField] private string materialsSubfolder = "Materials";
    [SerializeField] private string prefabsSubfolder = "Prefabs";

    [Header("Counts")]
    [SerializeField] private int meshVariants = 25;                  // how many unique meshes
    [SerializeField] private int materialVariantsPerMesh = 4;        // how many material variants per mesh
    [SerializeField] private int textureResolution = 256;            // 128/256/512

    [Header("Mesh Shape")]
    [SerializeField] private PrimitiveBase primitiveBase = PrimitiveBase.IcoSphere;
    [SerializeField] private int baseSubdivisions = 2;               // for icosphere
    [SerializeField] private int planeCuts = 0;                      // optional: slice bottom to create stable base (0=off)
    [SerializeField] private float uniformScaleMin = 0.35f;
    [SerializeField] private float uniformScaleMax = 1.15f;

    [Header("Rock Noise")]
    [SerializeField] private float displacementStrengthMin = 0.08f;
    [SerializeField] private float displacementStrengthMax = 0.35f;
    [SerializeField] private float noiseFrequencyMin = 0.8f;
    [SerializeField] private float noiseFrequencyMax = 3.2f;
    [SerializeField] private int noiseOctavesMin = 2;
    [SerializeField] private int noiseOctavesMax = 5;
    [SerializeField] private float ridgePowerMin = 0.8f;             // higher -> sharper ridges
    [SerializeField] private float ridgePowerMax = 2.2f;

    [Header("Materials/Textures")]
    [SerializeField] private Shader shader;                          // default: URP Lit if present else Standard
    [SerializeField] private bool generateNormalMap = false;         // optional (kept simple; tangent space not computed)
    [SerializeField] private bool useSRGBAlbedo = true;
    [SerializeField] private bool randomizeSmoothness = true;
    [SerializeField] private Vector2 smoothnessRange = new Vector2(0.05f, 0.35f);

    [Header("Naming")]
    [SerializeField] private string libraryName = "Rocks";
    [SerializeField] private bool addCollider = true;

    [Header("Repro")]
    [SerializeField] private bool deterministic = true;
    [SerializeField] private int seed = 1337;

    private enum PrimitiveBase { IcoSphere, CubeRounded }

    [MenuItem("Tools/StarConcepts/Procedural Mesh Library Generator")]
    public static void Open()
    {
        var w = GetWindow<ProceduralMeshLibraryGeneratorWindow>();
        w.titleContent = new GUIContent("Mesh Library Gen");
        w.minSize = new Vector2(420, 620);
        w.Show();
    }

    private void OnEnable()
    {
        if (shader == null)
        {
            // Prefer URP Lit if available
            shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Procedural Mesh + Materials + Prefabs", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            outputRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Root Folder", outputRootFolder, typeof(DefaultAsset), false);
            meshesSubfolder = EditorGUILayout.TextField("Meshes Subfolder", meshesSubfolder);
            texturesSubfolder = EditorGUILayout.TextField("Textures Subfolder", texturesSubfolder);
            materialsSubfolder = EditorGUILayout.TextField("Materials Subfolder", materialsSubfolder);
            prefabsSubfolder = EditorGUILayout.TextField("Prefabs Subfolder", prefabsSubfolder);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            meshVariants = Mathf.Clamp(EditorGUILayout.IntField("Mesh Variants", meshVariants), 1, 5000);
            materialVariantsPerMesh = Mathf.Clamp(EditorGUILayout.IntField("Material Variants / Mesh", materialVariantsPerMesh), 1, 64);

            textureResolution = EditorGUILayout.IntPopup("Texture Resolution", textureResolution,
                new[] { "128", "256", "512", "1024" }, new[] { 128, 256, 512, 1024 });

            shader = (Shader)EditorGUILayout.ObjectField("Shader", shader, typeof(Shader), false);
            addCollider = EditorGUILayout.Toggle("Add MeshCollider", addCollider);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            primitiveBase = (PrimitiveBase)EditorGUILayout.EnumPopup("Base Primitive", primitiveBase);
            baseSubdivisions = Mathf.Clamp(EditorGUILayout.IntSlider("Ico Subdivisions", baseSubdivisions, 0, 5), 0, 5);
            planeCuts = Mathf.Clamp(EditorGUILayout.IntSlider("Bottom Slice (cuts)", planeCuts, 0, 6), 0, 6);

            uniformScaleMin = EditorGUILayout.Slider("Scale Min", uniformScaleMin, 0.05f, 3f);
            uniformScaleMax = EditorGUILayout.Slider("Scale Max", uniformScaleMax, uniformScaleMin, 5f);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            displacementStrengthMin = EditorGUILayout.Slider("Displacement Min", displacementStrengthMin, 0f, 1.5f);
            displacementStrengthMax = EditorGUILayout.Slider("Displacement Max", displacementStrengthMax, displacementStrengthMin, 2.5f);

            noiseFrequencyMin = EditorGUILayout.Slider("Noise Freq Min", noiseFrequencyMin, 0.1f, 10f);
            noiseFrequencyMax = EditorGUILayout.Slider("Noise Freq Max", noiseFrequencyMax, noiseFrequencyMin, 20f);

            noiseOctavesMin = Mathf.Clamp(EditorGUILayout.IntSlider("Octaves Min", noiseOctavesMin, 1, 8), 1, 8);
            noiseOctavesMax = Mathf.Clamp(EditorGUILayout.IntSlider("Octaves Max", noiseOctavesMax, noiseOctavesMin, 10), noiseOctavesMin, 10);

            ridgePowerMin = EditorGUILayout.Slider("Ridge Power Min", ridgePowerMin, 0.2f, 4f);
            ridgePowerMax = EditorGUILayout.Slider("Ridge Power Max", ridgePowerMax, ridgePowerMin, 6f);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            generateNormalMap = EditorGUILayout.Toggle("Generate Normal Map (simple)", generateNormalMap);
            useSRGBAlbedo = EditorGUILayout.Toggle("Albedo sRGB", useSRGBAlbedo);
            randomizeSmoothness = EditorGUILayout.Toggle("Randomize Smoothness", randomizeSmoothness);
            smoothnessRange = EditorGUILayout.Vector2Field("Smoothness Range", smoothnessRange);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            libraryName = EditorGUILayout.TextField("Library Name", libraryName);
            deterministic = EditorGUILayout.Toggle("Deterministic", deterministic);
            seed = EditorGUILayout.IntField("Seed", seed);
        }

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(outputRootFolder == null || shader == null))
        {
            if (GUILayout.Button("Generate Library", GUILayout.Height(40)))
            {
                Generate();
            }
        }

        using (new EditorGUI.DisabledScope(outputRootFolder == null))
        {
            if (GUILayout.Button("Open Output Folder"))
            {
                var root = AssetDatabase.GetAssetPath(outputRootFolder);
                EditorUtility.RevealInFinder(Path.GetFullPath(root));
            }
        }
    }

    private void Generate()
    {
        var root = AssetDatabase.GetAssetPath(outputRootFolder);
        if (string.IsNullOrEmpty(root) || !root.StartsWith("Assets", StringComparison.Ordinal))
            throw new Exception("Output root must be inside Assets/");

        string meshesPath = EnsureFolder(root, meshesSubfolder);
        string texPath = EnsureFolder(root, texturesSubfolder);
        string matPath = EnsureFolder(root, materialsSubfolder);
        string prefabPath = EnsureFolder(root, prefabsSubfolder);

        int baseSeed = deterministic ? seed : Environment.TickCount;
        var rng = new System.Random(baseSeed);

        try
        {
            AssetDatabase.StartAssetEditing();

            for (int i = 0; i < meshVariants; i++)
            {
                int s = rng.Next();
                var mesh = GenerateRockMesh(s);

                string meshAssetName = $"{libraryName}_Mesh_{i:0000}.asset";
                string meshAssetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(meshesPath, meshAssetName));
                AssetDatabase.CreateAsset(mesh, meshAssetPath);

                // Materials + textures
                var materials = new Material[materialVariantsPerMesh];

                for (int m = 0; m < materialVariantsPerMesh; m++)
                {
                    int ms = rng.Next();
                    var albedo = GenerateRockAlbedoTexture(ms, textureResolution, useSRGBAlbedo);
                    string texName = $"{libraryName}_Alb_{i:0000}_{m:00}.png";
                    string texAssetPath = Path.Combine(texPath, texName);
                    WriteTexturePngAsset(albedo, texAssetPath, isSRGB: useSRGBAlbedo);

                    Texture2D normal = null;
                    string normalAssetPath = null;
                    if (generateNormalMap)
                    {
                        normal = GenerateFakeNormalFromAlbedo(albedo);
                        string nName = $"{libraryName}_Nrm_{i:0000}_{m:00}.png";
                        normalAssetPath = Path.Combine(texPath, nName);
                        WriteTexturePngAsset(normal, normalAssetPath, isSRGB: false);
                    }

                    var mat = new Material(shader);
                    string matName = $"{libraryName}_Mat_{i:0000}_{m:00}.mat";
                    string matAssetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(matPath, matName));

                    ApplyMaterialTextures(mat, texAssetPath, normalAssetPath);
                    ApplyMaterialParams(mat, rng);

                    AssetDatabase.CreateAsset(mat, matAssetPath);
                    EditorUtility.SetDirty(mat);

                    // sicherstellen, dass das Asset wirklich existiert/geladen werden kann
                    AssetDatabase.ImportAsset(matAssetPath,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                    var matAsset = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);
                    materials[m] = matAsset != null ? matAsset : mat;


                    DestroyImmediate(albedo);
                    if (normal != null) DestroyImmediate(normal);
                }
                AssetDatabase.SaveAssets();

                // Prefabs: one prefab per material variant (keeps variant selection trivial)
                for (int m = 0; m < materials.Length; m++)
                {
                    var go = new GameObject($"{libraryName}_Rock_{i:0000}_V{m:00}");
                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();

                    mf.sharedMesh = mesh;
                    var matToAssign = materials[m];
                    mr.material = matToAssign;
                    EditorUtility.SetDirty(mr);
                    if (addCollider)
                    {
                        var col = go.AddComponent<MeshCollider>();
                        col.sharedMesh = mesh;
                    }

                    string pfName = $"{libraryName}_Prefab_{i:0000}_V{m:00}.prefab";
                    string pfPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(prefabPath, pfName));
                    PrefabUtility.SaveAsPrefabAsset(go, pfPath);
                    DestroyImmediate(go);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    private Mesh GenerateRockMesh(int localSeed)
    {
        var rng = new System.Random(localSeed);

        float scale = Lerp(uniformScaleMin, uniformScaleMax, (float)rng.NextDouble());

        float disp = Lerp(displacementStrengthMin, displacementStrengthMax, (float)rng.NextDouble());
        float freq = Lerp(noiseFrequencyMin, noiseFrequencyMax, (float)rng.NextDouble());
        int oct = rng.Next(noiseOctavesMin, noiseOctavesMax + 1);
        float ridge = Lerp(ridgePowerMin, ridgePowerMax, (float)rng.NextDouble());

        Mesh mesh = primitiveBase switch
        {
            PrimitiveBase.CubeRounded => BuildRoundedCube(subdiv: Mathf.Clamp(baseSubdivisions + 1, 1, 6)),
            _ => BuildIcoSphere(subdivisions: baseSubdivisions),
        };

        var v = mesh.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = v[i];
            Vector3 n = p.normalized;

            float n0 = FractalNoise3(p * freq, oct, rng);
            float ridged = Mathf.Pow(1f - Mathf.Abs(n0 * 2f - 1f), ridge); // ridged-ish
            float d = (n0 * 0.65f + ridged * 0.35f) * disp;

            // Slight anisotropy to look less spherical
            p.x *= (0.85f + 0.35f * (float)rng.NextDouble());
            p.y *= (0.75f + 0.55f * (float)rng.NextDouble());
            p.z *= (0.85f + 0.35f * (float)rng.NextDouble());

            v[i] = (p + n * d) * scale;
        }

        mesh.vertices = v;

        if (planeCuts > 0)
            SliceBottom(mesh, planeCuts);

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.name = $"{libraryName}_RockMesh";
        return mesh;
    }

    private static void SliceBottom(Mesh mesh, int cuts)
    {
        // Simple bottom flattening: progressively clamp vertices below a threshold.
        var b = mesh.bounds;
        float minY = b.min.y;
        float maxY = b.max.y;
        float h = maxY - minY;

        float t = Mathf.Clamp01(cuts / 6f); // 0..1
        float planeY = minY + h * (0.05f + 0.25f * t);

        var v = mesh.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i].y < planeY) v[i].y = planeY;
        }
        mesh.vertices = v;
    }

    private Texture2D GenerateRockAlbedoTexture(int localSeed, int res, bool srgb)
    {
        var rng = new System.Random(localSeed);

        // Base rock palette: gray/brown/greenish
        Color c0 = Color.Lerp(new Color(0.20f, 0.20f, 0.20f, 1f), new Color(0.45f, 0.40f, 0.35f, 1f), (float)rng.NextDouble());
        Color c1 = Color.Lerp(new Color(0.10f, 0.12f, 0.10f, 1f), new Color(0.55f, 0.55f, 0.55f, 1f), (float)rng.NextDouble());

        float freq = Lerp(2.0f, 8.0f, (float)rng.NextDouble());
        int oct = rng.Next(3, 6);

        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        var pixels = new Color32[res * res];

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / res;
                float v = (float)y / res;

                float n = FractalNoise2(new Vector2(u, v) * freq, oct, rng);
                float cracks = Mathf.SmoothStep(0.45f, 0.55f, n);

                Color c = Color.Lerp(c0, c1, cracks);

                // Speckles
                float s = FractalNoise2(new Vector2(u, v) * (freq * 3.5f), 2, rng);
                c = Color.Lerp(c, c * 0.7f, Mathf.SmoothStep(0.7f, 0.95f, s) * 0.35f);

                // Slight color shift
                c.r *= 0.9f + 0.2f * (float)rng.NextDouble();
                c.g *= 0.9f + 0.2f * (float)rng.NextDouble();
                c.b *= 0.9f + 0.2f * (float)rng.NextDouble();

                pixels[y * res + x] = (Color32)c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D GenerateFakeNormalFromAlbedo(Texture2D albedo)
    {
        // Cheap normal: treat luminance as height.
        int w = albedo.width;
        int h = albedo.height;
        var src = albedo.GetPixels32();
        var dst = new Color32[w * h];

        float HeightAt(int x, int y)
        {
            x = (x + w) % w;
            y = (y + h) % h;
            var c = src[y * w + x];
            return (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float hl = HeightAt(x - 1, y);
                float hr = HeightAt(x + 1, y);
                float hd = HeightAt(x, y - 1);
                float hu = HeightAt(x, y + 1);

                Vector3 n = new Vector3(hl - hr, hd - hu, 1f).normalized;
                var c = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                dst[y * w + x] = (Color32)c;
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(dst);
        tex.Apply(false, false);
        return tex;
    }

private void ApplyMaterialTextures(Material mat, string albedoPath, string normalPath)
{
    var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
    if (alb != null)
    {
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", alb); // URP Lit
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", alb); // Standard
    }

    if (!string.IsNullOrEmpty(normalPath))
    {
        var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (nrm != null)
        {
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", nrm);
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1f);
        }
    }
}


    private void ApplyMaterialParams(Material mat, System.Random rng)
    {
        if (randomizeSmoothness)
        {
            float s = Lerp(smoothnessRange.x, smoothnessRange.y, (float)rng.NextDouble());

            // URP Lit: _Smoothness; Built-in Standard: _Glossiness
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", s);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", s);

            // Slight metallic variation for some shaders
            float m = (float)rng.NextDouble() * 0.08f;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", m);
        }
    }

    private static void WriteTexturePngAsset(Texture2D tex, string assetPath, bool isSRGB)
    {
        byte[] png = tex.EncodeToPNG();
        File.WriteAllBytes(assetPath, png);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = isSRGB;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = false;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }
    }


    private static string EnsureFolder(string root, string sub)
    {
        string path = Path.Combine(root, sub).Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(path)) return path;

        string parent = root.Replace("\\", "/");
        foreach (var part in sub.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = $"{parent}/{part}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(parent, part);
            parent = next;
        }
        return parent;
    }

    // ---------- Mesh builders ----------

    private static Mesh BuildIcoSphere(int subdivisions)
    {
        // Minimal icosphere builder suitable for asset generation.
        // Adapted to be self-contained; produces a unit-ish sphere.

        var t = (1f + Mathf.Sqrt(5f)) / 2f;

        var verts = new System.Collections.Generic.List<Vector3>
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
        };

        var tris = new System.Collections.Generic.List<int>
        {
            0,11,5,  0,5,1,  0,1,7,  0,7,10,  0,10,11,
            1,5,9,  5,11,4,  11,10,2,  10,7,6,  7,1,8,
            3,9,4,  3,4,2,  3,2,6,  3,6,8,  3,8,9,
            4,9,5,  2,4,11,  6,2,10,  8,6,7,  9,8,1
        };

        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

        var midCache = new System.Collections.Generic.Dictionary<long, int>();
        int GetMid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (midCache.TryGetValue(key, out int idx)) return idx;

            Vector3 v = (verts[a] + verts[b]) * 0.5f;
            idx = verts.Count;
            verts.Add(v.normalized);
            midCache[key] = idx;
            return idx;
        }

        for (int s = 0; s < subdivisions; s++)
        {
            var newTris = new System.Collections.Generic.List<int>(tris.Count * 4);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int v1 = tris[i];
                int v2 = tris[i + 1];
                int v3 = tris[i + 2];

                int a = GetMid(v1, v2);
                int b = GetMid(v2, v3);
                int c = GetMid(v3, v1);

                newTris.AddRange(new[] { v1, a, c, v2, b, a, v3, c, b, a, b, c });
            }
            tris = newTris;
        }

        var mesh = new Mesh();
        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh BuildRoundedCube(int subdiv)
    {
        // Cheap "rounded cube" by subdividing a cube and normalizing vertices outward.
        subdiv = Mathf.Clamp(subdiv, 1, 10);
        int steps = subdiv;

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i0 = verts.Count; verts.Add(a);
            int i1 = verts.Count; verts.Add(b);
            int i2 = verts.Count; verts.Add(c);
            int i3 = verts.Count; verts.Add(d);

            tris.Add(i0); tris.Add(i1); tris.Add(i2);
            tris.Add(i0); tris.Add(i2); tris.Add(i3);
        }

        // Build each face as grid.
        float step = 2f / steps;

        void Face(Vector3 origin, Vector3 uDir, Vector3 vDir)
        {
            for (int y = 0; y < steps; y++)
            {
                for (int x = 0; x < steps; x++)
                {
                    Vector3 p00 = origin + uDir * (-1f + x * step) + vDir * (-1f + y * step);
                    Vector3 p10 = origin + uDir * (-1f + (x + 1) * step) + vDir * (-1f + y * step);
                    Vector3 p11 = origin + uDir * (-1f + (x + 1) * step) + vDir * (-1f + (y + 1) * step);
                    Vector3 p01 = origin + uDir * (-1f + x * step) + vDir * (-1f + (y + 1) * step);

                    AddQuad(p00, p10, p11, p01);
                }
            }
        }

        Face(new Vector3(0, 0, 1), Vector3.right, Vector3.up);   // +Z
        Face(new Vector3(0, 0, -1), Vector3.left, Vector3.up);  // -Z
        Face(new Vector3(1, 0, 0), Vector3.back, Vector3.up);   // +X
        Face(new Vector3(-1, 0, 0), Vector3.forward, Vector3.up);// -X
        Face(new Vector3(0, 1, 0), Vector3.right, Vector3.back);// +Y
        Face(new Vector3(0, -1, 0), Vector3.right, Vector3.forward);// -Y

        // Round by normalizing to sphere-ish, then lerp back toward cube (keeps some flatness)
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 p = verts[i];
            Vector3 s = p.normalized;
            float round = 0.65f;
            verts[i] = Vector3.Lerp(p, s, round);
        }

        var mesh = new Mesh();
        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    // ---------- Noise ----------

    private static float FractalNoise2(Vector2 p, int octaves, System.Random rng)
    {
        // Deterministic per-call based on p only (no rng state dependency), using PerlinNoise.
        float amp = 1f;
        float freq = 1f;
        float sum = 0f;
        float norm = 0f;

        // Use hashed offsets from p to avoid identical patterns between assets
        float ox = Hash01(p.x * 12.9898f + p.y * 78.233f) * 1000f;
        float oy = Hash01(p.x * 39.3467f + p.y * 11.135f) * 1000f;

        for (int i = 0; i < octaves; i++)
        {
            float n = Mathf.PerlinNoise(p.x * freq + ox, p.y * freq + oy);
            sum += n * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }

        return sum / Mathf.Max(0.0001f, norm);
    }

    private static float FractalNoise3(Vector3 p, int octaves, System.Random rng)
    {
        // Approximate 3D noise by combining 2D Perlin samples.
        float xy = FractalNoise2(new Vector2(p.x, p.y), octaves, rng);
        float yz = FractalNoise2(new Vector2(p.y, p.z), octaves, rng);
        float zx = FractalNoise2(new Vector2(p.z, p.x), octaves, rng);
        return (xy + yz + zx) / 3f;
    }

    private static float Hash01(float x)
    {
        // 0..1 hash
        return Unity.Mathematics.math.frac(Mathf.Sin(x) * 43758.5453f);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);
}
