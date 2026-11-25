using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Assembles playable race scenes from the real circuit asset pack (Assets/F1Assets/Tracks/<Name>) and the
// 2026 F1 car model (Assets/F1Assets/Car). Each circuit gets its own scene "Circuit <Name>":
//  - the circuit FBX with mesh colliders on its Col_* surfaces (asphalt, kerbs, grass, gravel, barriers)
//  - a RaceTrack centreline from the pack's track limits / racing line (exported from Blender to <Name>.track.json)
//  - the real pit lane path, DRS zones, start gantry, sun, sky and post-processing
//  - the player car plus 21 AI cars built from the 2026 model with the eleven team liveries
// Tools > XPARROW > ... menu items.
public static class CircuitBuilder
{
    const string TrackRoot = "Assets/F1Assets/Tracks";
    const string CarFbx = "Assets/F1Assets/Car/F1_2026_Car.fbx";
    const string PitPropsFbx = "Assets/F1Assets/Car/F1_PitProps.fbx";
    const string CarPrefab = "Assets/F1Assets/Car/F1_2026.prefab";
    const string LiveryDir = "Assets/Resources/F1Liveries";
    const string TyreDir = "Assets/Resources/F1Tyres";
    const string GenDir = "Assets/F1Assets/Generated";
    const string SceneDir = "Assets/Scenes/Final";
    public const int Cars = 22;
    static bool skyCubeBuiltThisSession;

    static readonly string[] LiveryNames =
        { "00_Concept2026", "01_Ferrari", "02_RedBull", "03_McLaren", "04_Mercedes", "05_AstonMartin",
          "06_Alpine", "07_Williams", "08_RacingBulls", "09_Haas", "10_Audi", "11_Cadillac" };

    // Real-world lap direction seen from above (true = clockwise). Suzuka is a figure of eight: trust the data.
    static readonly Dictionary<string, bool?> Clockwise = new Dictionary<string, bool?>
        { { "Monaco", true }, { "Monza", true }, { "Spa", true }, { "Baku", false }, { "Suzuka", null } };

    [Serializable] class DrsJson { public string name; public float[] start, end; }
    [Serializable] class TrackJson
    {
        public string centreSource;
        public float halfWidth;
        public float[] centre, halfWidths, pit, garages, startFinish, pitEntry, pitExit, asphaltMin, asphaltMax;
        public DrsJson[] drs;
    }

    // ---------- Menu ----------

    [MenuItem("Tools/XPARROW/Build FINAL Game (All Assets + Menu + 5 Circuits + Verify)")]
    public static void BuildFinalGameAll()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("Stop Play mode first."); return; }
        skyCubeBuiltThisSession = false;
        BuildCarAssets();

        if (!EngineAudio.RunAudioSelfTest(out string audioReport))
        {
            Debug.LogError("EngineAudio self-test FAILED:\n" + audioReport);
            if (Application.isBatchMode) EditorApplication.Exit(2);
            return;
        }
        Debug.Log("EngineAudio self-test PASSED:\n" + audioReport);

        F1PhysicsTest.Run();
        MenuSceneBuilder.Build(false);
        foreach (var t in GameSession.Tracks) BuildCircuitScene(t.id);
        SetBuildScenes();

        int passed = 0;
        var report = new System.Text.StringBuilder("FINAL BUILD VERIFICATION:\n");
        foreach (var t in GameSession.Tracks)
        {
            string p = SceneDir + "/" + t.scene + ".unity";
            bool ok = CircuitDriveTest.RunForScene(p, out string summary);
            if (ok) passed++;
            report.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {t.scene}: {summary.Replace('\n', ' ')}");
        }
        Debug.Log($"FINAL BUILD SUMMARY: {passed}/{GameSession.Tracks.Length} circuits passed\n{report}");
        if (Application.isBatchMode && passed < GameSession.Tracks.Length)
            Debug.LogError($"Batch build verification failed: {passed}/{GameSession.Tracks.Length} circuits passed");

        VerifyBarrierContainment();
        PitLaneVerifyTest();
        RenderUnityVerificationScreenshots();

        string menuPath = SceneDir + "/Final Main Menu.unity";
        if (File.Exists(menuPath)) EditorSceneManager.OpenScene(menuPath, OpenSceneMode.Single);
    }

    [MenuItem("Tools/XPARROW/1 Build Car Liveries + Tyres + Prefab")]
    public static void BuildCarAssets()
    {
        BuildLiveries();
        BuildTyreMaterials();
        BuildPitPropPrefabs();
        BuildCarPrefab();
    }

    [MenuItem("Tools/XPARROW/2 Build Circuit/Monaco")] static void BMonaco() => BuildCircuitScene("Monaco");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Baku")] static void BBaku() => BuildCircuitScene("Baku");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Monza")] static void BMonza() => BuildCircuitScene("Monza");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Spa")] static void BSpa() => BuildCircuitScene("Spa");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Suzuka")] static void BSuzuka() => BuildCircuitScene("Suzuka");

    [MenuItem("Tools/XPARROW/3 Set Build Scenes (Final menu + 5 circuits)")]
    public static void SetBuildScenes()
    {
        var list = new List<EditorBuildSettingsScene>();
        string menuPath = SceneDir + "/Final Main Menu.unity";
        if (File.Exists(menuPath)) list.Add(new EditorBuildSettingsScene(menuPath, true));
        foreach (var t in GameSession.Tracks)
        {
            string p = SceneDir + "/" + t.scene + ".unity";
            if (File.Exists(p)) list.Add(new EditorBuildSettingsScene(p, true));
        }
        EditorBuildSettings.scenes = list.ToArray();
        PlayerSettings.productName = GameSession.GameTitle;
        Debug.Log("Build scenes: " + string.Join(", ", list.ConvertAll(s => Path.GetFileNameWithoutExtension(s.path))));
    }

    public static void AddSceneToBuild(string path, int index)
    {
        if (string.IsNullOrEmpty(path)) return;
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        var entry = new EditorBuildSettingsScene(path, true);
        if (index >= 0 && index <= scenes.Count) scenes.Insert(index, entry); else scenes.Add(entry);
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ---------- Liveries ----------

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Texture2D LoadTex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static Texture2D LoadOrPackMetallicGloss(string metPath, string roughPath, string outPath)
    {
        bool needPack = !File.Exists(outPath)
                     || (File.Exists(metPath) && File.GetLastWriteTimeUtc(metPath) > File.GetLastWriteTimeUtc(outPath))
                     || (File.Exists(roughPath) && File.GetLastWriteTimeUtc(roughPath) > File.GetLastWriteTimeUtc(outPath));
        if (needPack)
        {
            EnsureReadableNonColor(metPath);
            EnsureReadableNonColor(roughPath);
            var met = LoadTex(metPath);
            var rough = LoadTex(roughPath);
            if (met != null && rough != null)
            {
                var mp = met.GetPixels(); var rp = rough.GetPixels();
                int w = met.width, h = met.height;
                if (rough.width != w || rough.height != h) rp = Resize(rough, w, h);
                var outp = new Color[mp.Length];
                for (int k = 0; k < mp.Length; k++) outp[k] = new Color(mp[k].r, mp[k].r, mp[k].r, 1f - rp[k].r);
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                t.SetPixels(outp); t.Apply();
                File.WriteAllBytes(outPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
                var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
                if (ti != null) { ti.sRGBTexture = false; ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            }
        }
        return LoadTex(outPath);
    }

    static void EnsureReadableNonColor(string texPath)
    {
        var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (ti != null && (!ti.isReadable || ti.sRGBTexture || ti.maxTextureSize < 2048))
        {
            ti.isReadable = true;
            ti.sRGBTexture = false;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }
    }

    public static void BuildLiveries()
    {
        EnsureFolder(LiveryDir);
        EnsureFolder(GenDir);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        string texDir = "Assets/F1Assets/Car/Textures";
        var normal = LoadTex(texDir + "/shared/T_F1_2026_Normal.png");
        var emission = LoadTex(texDir + "/shared/T_F1_2026_Emission.png");
        for (int i = 0; i < LiveryNames.Length; i++)
        {
            string n = LiveryNames[i];
            string basePath = $"{texDir}/teams/T_{n}_BaseColor.png";
            var bti = AssetImporter.GetAtPath(basePath) as TextureImporter;
            if (bti != null && (bti.maxTextureSize < 4096 || bti.textureCompression != TextureImporterCompression.CompressedHQ))
            {
                bti.maxTextureSize = 4096;
                bti.textureCompression = TextureImporterCompression.CompressedHQ;
                bti.SaveAndReimport();
            }
            var baseCol = LoadTex(basePath);
            var mg = LoadOrPackMetallicGloss(
                $"{texDir}/teams/T_{n}_Metallic.png",
                $"{texDir}/teams/T_{n}_Roughness.png",
                $"{GenDir}/T_{n}_MetallicGloss.png");
            string matPath = $"{LiveryDir}/Livery_{i:00}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, matPath); }
            m.shader = lit;
            m.SetTexture("_BaseMap", baseCol);
            m.SetColor("_BaseColor", Color.white);
            if (mg != null) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            // Strip legacy UV1 AO map so URP Lit never multiplies car albedo by 0
            m.SetTexture("_OcclusionMap", null);
            m.SetFloat("_OcclusionStrength", 0f);
            m.DisableKeyword("_OCCLUSIONMAP");
            if (emission != null)
            {
                m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", Color.white * 2.2f);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
        BuildCarDetailMaterials(lit, texDir, normal);
        AssetDatabase.SaveAssets();
        Debug.Log("Liveries built (4K, AO stripped): " + LiveryNames.Length);
    }

    static void BuildCarDetailMaterials(Shader lit, string texDir, Texture2D normal)
    {
        // 1. Steering Wheel Body + Gloves (with dedicated height-derived NormalMap)
        string swTexPath = texDir + "/shared/T_SteeringWheel_Body_BaseColor.png";
        var swTi = AssetImporter.GetAtPath(swTexPath) as TextureImporter;
        if (swTi != null && (!swTi.sRGBTexture || swTi.maxTextureSize < 2048))
        {
            swTi.sRGBTexture = true;
            swTi.maxTextureSize = 2048;
            swTi.SaveAndReimport();
        }
        string swNormPath = texDir + "/shared/T_SteeringWheel_Body_Normal.png";
        var swNormTi = AssetImporter.GetAtPath(swNormPath) as TextureImporter;
        if (swNormTi != null && (swNormTi.textureType != TextureImporterType.NormalMap || swNormTi.maxTextureSize < 2048))
        {
            swNormTi.textureType = TextureImporterType.NormalMap;
            swNormTi.sRGBTexture = false;
            swNormTi.maxTextureSize = 2048;
            swNormTi.SaveAndReimport();
        }
        var swBase = LoadTex(swTexPath);
        var swNorm = LoadTex(swNormPath) ?? normal;
        string swMatPath = $"{LiveryDir}/SteeringWheel_Body.mat";
        var swMat = AssetDatabase.LoadAssetAtPath<Material>(swMatPath);
        if (swMat == null) { swMat = new Material(lit); AssetDatabase.CreateAsset(swMat, swMatPath); }
        swMat.shader = lit;
        if (swBase != null) swMat.SetTexture("_BaseMap", swBase);
        swMat.SetColor("_BaseColor", Color.white);
        swMat.SetFloat("_Metallic", 0.14f);
        swMat.SetFloat("_Smoothness", 0.44f);
        if (swNorm != null) { swMat.SetTexture("_BumpMap", swNorm); swMat.SetFloat("_BumpScale", 0.85f); swMat.EnableKeyword("_NORMALMAP"); }
        swMat.SetTexture("_OcclusionMap", null);
        swMat.SetFloat("_OcclusionStrength", 0f);
        swMat.DisableKeyword("_OCCLUSIONMAP");
        EditorUtility.SetDirty(swMat);

        // 1b. Sealed Monocoque Carbon Tub & Steered Front Wake Deflectors
        string carbBasePath = texDir + "/shared/T_Cockpit_Carbon_BaseColor.png";
        var cbTi = AssetImporter.GetAtPath(carbBasePath) as TextureImporter;
        if (cbTi != null && (!cbTi.sRGBTexture || cbTi.maxTextureSize < 1024))
        {
            cbTi.sRGBTexture = true;
            cbTi.maxTextureSize = 1024;
            cbTi.SaveAndReimport();
        }
        string carbNormPath = texDir + "/shared/T_Cockpit_Carbon_Normal.png";
        var cnTi = AssetImporter.GetAtPath(carbNormPath) as TextureImporter;
        if (cnTi != null && (cnTi.textureType != TextureImporterType.NormalMap || cnTi.maxTextureSize < 1024))
        {
            cnTi.textureType = TextureImporterType.NormalMap;
            cnTi.sRGBTexture = false;
            cnTi.maxTextureSize = 1024;
            cnTi.SaveAndReimport();
        }
        var carbBase = LoadTex(carbBasePath);
        var carbNorm = LoadTex(carbNormPath) ?? normal;
        string carbMatPath = $"{LiveryDir}/Cockpit_Carbon.mat";
        var carbMat = AssetDatabase.LoadAssetAtPath<Material>(carbMatPath);
        if (carbMat == null) { carbMat = new Material(lit); AssetDatabase.CreateAsset(carbMat, carbMatPath); }
        carbMat.shader = lit;
        if (carbBase != null) carbMat.SetTexture("_BaseMap", carbBase);
        carbMat.SetColor("_BaseColor", Color.white);
        carbMat.SetFloat("_Metallic", 0.18f);
        carbMat.SetFloat("_Smoothness", 0.56f);
        if (carbNorm != null) { carbMat.SetTexture("_BumpMap", carbNorm); carbMat.SetFloat("_BumpScale", 0.75f); carbMat.EnableKeyword("_NORMALMAP"); }
        carbMat.SetTexture("_OcclusionMap", null);
        carbMat.SetFloat("_OcclusionStrength", 0f);
        carbMat.DisableKeyword("_OCCLUSIONMAP");
        EditorUtility.SetDirty(carbMat);

        // 2. Steering Wheel Unlit OLED Screen
        var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        string scrPath = $"{LiveryDir}/SteeringWheel_Screen.mat";
        var scrMat = AssetDatabase.LoadAssetAtPath<Material>(scrPath);
        if (scrMat == null) { scrMat = new Material(unlit); AssetDatabase.CreateAsset(scrMat, scrPath); }
        scrMat.shader = unlit;
        if (scrMat.HasProperty("_BaseColor")) scrMat.SetColor("_BaseColor", Color.white);
        if (scrMat.HasProperty("_Color")) scrMat.SetColor("_Color", Color.white);
        EditorUtility.SetDirty(scrMat);

        // 3. Mirror Chrome Glass
        string mirPath = $"{LiveryDir}/Mirror_Chrome.mat";
        var mirMat = AssetDatabase.LoadAssetAtPath<Material>(mirPath);
        if (mirMat == null) { mirMat = new Material(lit); AssetDatabase.CreateAsset(mirMat, mirPath); }
        mirMat.shader = lit;
        mirMat.SetColor("_BaseColor", new Color(0.90f, 0.94f, 0.98f));
        mirMat.SetFloat("_Metallic", 0.96f);
        mirMat.SetFloat("_Smoothness", 0.98f);
        mirMat.SetTexture("_OcclusionMap", null);
        mirMat.SetFloat("_OcclusionStrength", 0f);
        mirMat.DisableKeyword("_OCCLUSIONMAP");
        EditorUtility.SetDirty(mirMat);
    }

    public static void BuildTyreMaterials()
    {
        EnsureFolder(TyreDir);
        EnsureFolder(GenDir);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        string texDir = "Assets/F1Assets/Car/Textures/tyres";
        (string key, string fileKey)[] specs =
        {
            ("Soft", "01_Soft_Red"),
            ("Medium", "02_Medium_Yellow"),
            ("Hard", "03_Hard_White"),
            ("Inter", "04_Intermediate_Green"),
            ("Wet", "05_FullWet_Blue"),
        };
        foreach (var (key, fileKey) in specs)
        {
            var baseCol = LoadTex($"{texDir}/T_Tyre_{fileKey}_BaseColor.png");
            var normal = LoadTex($"{texDir}/T_Tyre_{fileKey}_Normal.png");
            var mg = LoadOrPackMetallicGloss(
                $"{texDir}/T_Tyre_{fileKey}_Metallic.png",
                $"{texDir}/T_Tyre_{fileKey}_Roughness.png",
                $"{GenDir}/T_Tyre_{fileKey}_MetallicGloss.png");
            string matPath = $"{TyreDir}/Tyre_{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, matPath); }
            m.shader = lit;
            if (baseCol != null) m.SetTexture("_BaseMap", baseCol);
            m.SetColor("_BaseColor", Color.white);
            if (mg != null) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetTexture("_OcclusionMap", null);
            m.SetFloat("_OcclusionStrength", 0f);
            m.DisableKeyword("_OCCLUSIONMAP");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Copyright-safe HD tyre materials built: " + specs.Length);
    }

    public static void BuildPitPropPrefabs()
    {
        EnsureFolder(TyreDir);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(PitPropsFbx);
        if (fbx == null) { Debug.LogWarning("PitProps FBX missing: " + PitPropsFbx); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var matMed = AssetDatabase.LoadAssetAtPath<Material>(TyreDir + "/Tyre_Medium.mat");
        var matSoft = AssetDatabase.LoadAssetAtPath<Material>(TyreDir + "/Tyre_Soft.mat");

        void SaveCenteredTyrePrefab(string srcName, string outName)
        {
            var src = FindDeep(inst.transform, srcName);
            if (src == null) return;
            var root = new GameObject(outName);
            src.SetParent(root.transform, true);
            foreach (var c in src.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var r = src.GetComponentInChildren<Renderer>(true);
            if (r != null)
            {
                if (matMed != null) r.sharedMaterial = matMed;
                if (r.bounds.size.x > r.bounds.size.y && r.bounds.size.z < r.bounds.size.y)
                    src.Rotate(0f, 90f, 0f, Space.World);
                src.position -= r.bounds.center - root.transform.position;
            }
            PrefabUtility.SaveAsPrefabAsset(root, $"{TyreDir}/{outName}.prefab");
            Object.DestroyImmediate(root);
        }

        SaveCenteredTyrePrefab("Tyre_Front_Prop", "PitTyre_Front");
        SaveCenteredTyrePrefab("Tyre_Rear_Prop", "PitTyre_Rear");

        var cluster = new GameObject("PitProps_Cluster");
        string[] propNames = { "Prop_Pit_Tyre_Trolley", "Prop_Pit_Tyre_Stack_4x", "Prop_Pit_Tyre_Blanket_Pair" };
        float[] zOffsets = { -1.6f, 0.2f, 1.4f };
        for (int i = 0; i < propNames.Length; i++)
        {
            var p = FindDeep(inst.transform, propNames[i]);
            if (p == null) continue;
            p.SetParent(cluster.transform, true);
            foreach (var c in p.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                string mn = r.sharedMaterial != null ? r.sharedMaterial.name : "";
                if (mn.Contains("Soft") && matSoft != null) r.sharedMaterial = matSoft;
                else if (mn.Contains("Medium") && matMed != null) r.sharedMaterial = matMed;
            }
            Bounds b = BoundsOf(p.gameObject);
            Vector3 offset = new Vector3(0f, 0f, zOffsets[i]) - new Vector3(b.center.x, b.min.y, b.center.z);
            p.position += offset;
        }
        PrefabUtility.SaveAsPrefabAsset(cluster, $"{TyreDir}/PitProps_Cluster.prefab");
        Object.DestroyImmediate(cluster);
        Object.DestroyImmediate(inst);
        AssetDatabase.SaveAssets();
        Debug.Log("Pit prop prefabs built in " + TyreDir);
    }

    static Color[] Resize(Texture2D src, int w, int h)
    {
        var o = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) o[y * w + x] = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
        return o;
    }

    // ---------- Car prefab ----------

    public static void BuildCarPrefab()
    {
        var mi = AssetImporter.GetAtPath(CarFbx) as ModelImporter;
        if (mi != null && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); }
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(CarFbx);
        if (fbx == null) { Debug.LogError("Car FBX missing: " + CarFbx); return; }
        var root = new GameObject("F1_2026");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Body";
        model.transform.SetParent(root.transform, false);
        foreach (var g in model.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(g);
        var stray = new List<GameObject>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t != model.transform && (t.name.StartsWith("Studio_") || t.name.IndexOf("Backdrop", StringComparison.OrdinalIgnoreCase) >= 0))
                stray.Add(t.gameObject);
        foreach (var go in stray) if (go != null) Object.DestroyImmediate(go);

        Transform F(string n) => FindDeep(model.transform, n);
        var fl = F("Spin_FL"); var fr = F("Spin_FR"); var rl = F("Spin_RL"); var rr = F("Spin_RR");
        if (fl == null || fr == null || rl == null || rr == null) { Debug.LogError("Car wheel pivots not found"); Object.DestroyImmediate(root); return; }

        // Wheel centres from the tyre meshes themselves (more reliable than the pivot empties)
        Vector3 C(string k) { var m = F("F1_Wheel_" + k + "_LOD0"); return m != null ? m.GetComponent<Renderer>().bounds.center : F("Spin_" + k).position; }
        // Orient the model: nose along +Z
        Vector3 front = (C("FL") + C("FR")) / 2f, rear = (C("RL") + C("RR")) / 2f;
        Vector3 fwd = front - rear; fwd.y = 0f;
        model.transform.rotation = Quaternion.Euler(0f, Vector3.SignedAngle(fwd.normalized, Vector3.forward, Vector3.up), 0f) * model.transform.rotation;
        if (C("FL").x > C("FR").x) Debug.LogWarning("Car model looks mirrored (FL is right of FR)");
        // Centre the wheelbase on the origin, wheel centres at tyre radius above the ground
        front = (C("FL") + C("FR")) / 2f; rear = (C("RL") + C("RR")) / 2f;
        float wheelY = (C("FL").y + C("RL").y) / 2f;
        var mf = F("F1_Wheel_FL_LOD0").GetComponent<Renderer>().bounds;
        float radius = mf.extents.y;
        model.transform.position -= new Vector3((front.x + rear.x) / 2f, wheelY - radius, (front.z + rear.z) / 2f);

        // Wheel pivots: front Wheels/SteerFL -> F1_Hub_* + SpinFL -> F1_Wheel_*; rear Wheels/SpinRL -> F1_Wheel_*
        var wheels = new GameObject("Wheels").transform;
        wheels.SetParent(root.transform, false);
        string[] keys = { "FL", "FR", "RL", "RR" };
        var pivots = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            var spinSrc = F("Spin_" + keys[i]);
            var steerSrc = F("Steer_" + keys[i]);
            Transform parentForSpin = wheels;
            if (i < 2)
            {
                var steerPivot = new GameObject("Steer" + keys[i]).transform;
                steerPivot.SetParent(wheels, false);
                steerPivot.position = C(keys[i]);
                steerPivot.rotation = root.transform.rotation;
                pivots[i] = steerPivot.localPosition;
                if (steerSrc != null)
                {
                    var hubChildren = new List<Transform>();
                    foreach (Transform c in steerSrc) if (c != spinSrc) hubChildren.Add(c);
                    foreach (var c in hubChildren) c.SetParent(steerPivot, true);
                }
                parentForSpin = steerPivot;
            }
            var spinPivot = new GameObject("Spin" + keys[i]).transform;
            spinPivot.SetParent(parentForSpin, false);
            if (i < 2) { spinPivot.localPosition = Vector3.zero; spinPivot.localRotation = Quaternion.identity; }
            else { spinPivot.position = C(keys[i]); spinPivot.rotation = root.transform.rotation; pivots[i] = spinPivot.localPosition; }
            var tyreChildren = new List<Transform>();
            foreach (Transform c in spinSrc) tyreChildren.Add(c);
            foreach (var c in tyreChildren) c.SetParent(spinPivot, true);
        }
        foreach (var k in keys) { var s = F("Steer_" + k); if (s != null) Object.DestroyImmediate(s.gameObject); }
        foreach (var k in keys) { var s = F("Spin_" + k); if (s != null) Object.DestroyImmediate(s.gameObject); }

        // Body collider from the pack's convex hull (with fallback BoxCollider)
        var hull = F("Collider_Body");
        string colInfo;
        var hullMf = hull != null ? hull.GetComponent<MeshFilter>() : null;
        if (hullMf != null && hullMf.sharedMesh != null)
        {
            var srcMesh = hullMf.sharedMesh;
            var colMesh = new Mesh { name = "F1_ColliderBody" };
            var srcVerts = srcMesh.vertices;
            var dstVerts = new Vector3[srcVerts.Length];
            for (int v = 0; v < srcVerts.Length; v++)
            {
                Vector3 p = root.transform.InverseTransformPoint(hull.TransformPoint(srcVerts[v]));
                p.y = Mathf.Max(p.y, 0.08f);
                dstVerts[v] = p;
            }
            colMesh.vertices = dstVerts;
            colMesh.triangles = srcMesh.triangles;
            colMesh.RecalculateNormals();
            colMesh.RecalculateBounds();
            EnsureFolder(GenDir);
            string meshPath = GenDir + "/F1_ColliderBody.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(colMesh, meshPath);
            var mc = root.AddComponent<MeshCollider>();
            mc.sharedMesh = colMesh;
            mc.convex = true;
            colInfo = $"Mesh({colMesh.vertexCount}v, {colMesh.bounds.size:F2})";
            Object.DestroyImmediate(hull.gameObject);
        }
        else
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.54f, 0);
            box.size = new Vector3(1.9f, 0.92f, 5.4f);
            colInfo = box.size.ToString("F2");
            if (hull != null) Object.DestroyImmediate(hull.gameObject);
        }

        // Livery on chassis/wings/hubs, dedicated materials on wheel/screen/mirrors/cockpit carbon/tyres; LOD groups by suffix
        var livery = AssetDatabase.LoadAssetAtPath<Material>(LiveryDir + "/Livery_00.mat");
        var tyreMat = AssetDatabase.LoadAssetAtPath<Material>(TyreDir + "/Tyre_Medium.mat");
        var swBodyMat = AssetDatabase.LoadAssetAtPath<Material>(LiveryDir + "/SteeringWheel_Body.mat");
        var swScreenMat = AssetDatabase.LoadAssetAtPath<Material>(LiveryDir + "/SteeringWheel_Screen.mat");
        var mirrorMat = AssetDatabase.LoadAssetAtPath<Material>(LiveryDir + "/Mirror_Chrome.mat");
        var cockpitCarbonMat = AssetDatabase.LoadAssetAtPath<Material>(LiveryDir + "/Cockpit_Carbon.mat");
        var lods = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.StartsWith("F1_Wheel") && tyreMat != null) r.sharedMaterial = tyreMat;
            else if (r.name == "SteeringWheel_Screen" && swScreenMat != null) r.sharedMaterial = swScreenMat;
            else if (r.name.StartsWith("F1_SteeringWheel") && swBodyMat != null) r.sharedMaterial = swBodyMat;
            else if (r.name.StartsWith("Mirror_Glass") && mirrorMat != null) r.sharedMaterial = mirrorMat;
            else if ((r.name.StartsWith("F1_Cockpit_Interior") || r.name.StartsWith("F1_Cockpit_Deflector")) && cockpitCarbonMat != null) r.sharedMaterial = cockpitCarbonMat;
            else if (livery != null) r.sharedMaterial = livery;
            r.shadowCastingMode = r.name == "SteeringWheel_Screen" ? ShadowCastingMode.Off : ShadowCastingMode.On;
            for (int l = 0; l < 3; l++) if (r.name.EndsWith("_LOD" + l)) lods[l].Add(r);
            if (r.name == "SteeringWheel_Screen") lods[0].Add(r);
        }
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[]
        {
            new LOD(0.22f, lods[0].ToArray()),
            new LOD(0.07f, lods[1].ToArray()),
            new LOD(0.006f, lods[2].ToArray()),
        });
        group.RecalculateBounds();

        var info = root.AddComponent<CarGeometry>();
        info.frontAxleZ = (pivots[0].z + pivots[1].z) / 2f;
        info.rearAxleZ = (pivots[2].z + pivots[3].z) / 2f;
        info.frontTrack = (Mathf.Abs(pivots[0].x) + Mathf.Abs(pivots[1].x)) / 2f;
        info.rearTrack = (Mathf.Abs(pivots[2].x) + Mathf.Abs(pivots[3].x)) / 2f;
        info.tyreRadius = radius;
        var driver = FindDeep(root.transform, "F1_Cockpit_Driver_LOD0");
        if (driver != null)
        {
            var db = driver.GetComponent<Renderer>().bounds;
            info.headZ = db.min.z + 0.3f;
            info.headY = db.max.y - 0.1f;
        }
        var swPivot = FindDeep(root.transform, "SteeringWheel_Pivot");
        if (swPivot != null)
            info.steeringWheelLocalPos = root.transform.InverseTransformPoint(swPivot.position);

        PrefabUtility.SaveAsPrefabAsset(root, CarPrefab);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"Car prefab built: front axle {info.frontAxleZ:0.00}, rear {info.rearAxleZ:0.00}, track {info.frontTrack:0.00}/{info.rearTrack:0.00}, " +
                  $"tyre radius {radius:0.000}, swPivot {info.steeringWheelLocalPos:F3}, collider {colInfo}, LODs {lods[0].Count}/{lods[1].Count}/{lods[2].Count}");
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }

    public static GameObject InstantiateCar(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab);
        if (prefab == null) { BuildCarAssets(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab); }
        var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        car.name = name;
        return car;
    }

    static void ConfigureF1(F1Car f1, CarGeometry g)
    {
        f1.frontAxleZ = g.frontAxleZ; f1.rearAxleZ = g.rearAxleZ;
        f1.frontTrack = g.frontTrack; f1.rearTrack = g.rearTrack;
        f1.tyreRadius = g.tyreRadius;
    }

    // ---------- Circuit scene ----------

    static Matrix4x4 blenderToUnity;
    static RaceTrack track;
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    public static void BuildCircuitScene(string id)
    {
        if (EditorApplication.isPlaying) { Debug.LogError("Stop Play mode first."); return; }
        var info = Array.Find(GameSession.Tracks, t => t.id == id);
        string dir = TrackRoot + "/" + id;
        string jsonPath = dir + "/" + id + ".track.json";
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + id + "_GP.fbx");
        if (fbx == null || !File.Exists(jsonPath)) { Debug.LogError("Circuit assets missing for " + id); return; }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab) == null) BuildCarAssets();
        var data = JsonUtility.FromJson<TrackJson>(File.ReadAllText(jsonPath));
        mats.Clear();
        EnsureFolder(GenDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject(id + " Circuit");

        // Circuit model
        var map = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(map, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        map.transform.SetParent(root.transform, true);
        map.name = id + "_GP";
        int colliders = SetupMap(map);
        EnhanceMaterials(map, id, dir + "/textures");

        // Blender -> Unity coordinates, found by matching the asphalt bounds
        Physics.SyncTransforms();
        var asphalt = FindDeep(map.transform, "Col_Track_Asphalt");
        Bounds ub = BoundsOf(asphalt != null ? asphalt.gameObject : map);
        // Only adjust root rotation if the imported circuit is NOT already flat (Y = vertical elevation)
        if (asphalt != null && ub.size.y > Mathf.Min(ub.size.x, ub.size.z) * 0.5f)
        {
            map.transform.rotation = Quaternion.Inverse(asphalt.rotation) * map.transform.rotation;
            Physics.SyncTransforms();
            ub = BoundsOf(asphalt.gameObject);
            if (ub.size.z < ub.size.y * 0.5f)
            {
                map.transform.rotation = Quaternion.Euler(-90f, 0f, 0f) * map.transform.rotation;
                Physics.SyncTransforms();
                ub = BoundsOf(asphalt.gameObject);
            }
            foreach (var mc in map.GetComponentsInChildren<MeshCollider>(true)) { var m0 = mc.sharedMesh; mc.sharedMesh = null; mc.sharedMesh = m0; }
            Physics.SyncTransforms();
        }
        if (ub.size.y > ub.size.x * 0.5f) Debug.LogWarning(id + ": circuit still not flat after axis fix, bounds " + ub.size);
        if (asphalt != null)
        {
            var amf = asphalt.GetComponent<MeshFilter>();
            if (amf != null && amf.sharedMesh != null && amf.sharedMesh.normals.Length > 0)
            {
                float sumY = 0f;
                var ns = amf.sharedMesh.normals;
                int stepN = Mathf.Max(1, ns.Length / 200);
                int cnt = 0;
                for (int k = 0; k < ns.Length; k += stepN) { sumY += asphalt.TransformDirection(ns[k]).normalized.y; cnt++; }
                float avgY = sumY / Mathf.Max(1, cnt);
                if (avgY < 0.5f) Debug.LogWarning($"{id}: Col_Track_Asphalt average world normal Y = {avgY:0.00} (expected ~+1.0)");
            }
        }
        float err = Calibrate(data, ub);
        var roadVis = FindDeep(map.transform, "Road_Surface") ?? FindDeep(map.transform, "Road_Asphalt");
        if (asphalt != null && roadVis != null)
        {
            float rotDiff = Quaternion.Angle(roadVis.rotation, asphalt.rotation);
            float cDiff = Vector3.Distance(BoundsOf(roadVis.gameObject).center, ub.center);
            if (rotDiff > 1f || cDiff > 10f)
                Debug.LogWarning($"{id}: visual vs collider mismatch (rot {rotDiff:0.0}°, center diff {cDiff:0.0} m)");
        }

        // Centreline (+ per-point halfWidths from Blender track.json)
        track = root.AddComponent<RaceTrack>();
        var raw = ToPoints(data.centre);
        List<float> rawW = null;
        if (data.halfWidths != null && data.halfWidths.Length == raw.Count)
            rawW = new List<float>(data.halfWidths);
        bool? cw = Clockwise.TryGetValue(id, out var v) ? v : null;
        if (cw.HasValue && IsClockwiseBlender(data.centre) != cw.Value)
        {
            raw.Reverse();
            rawW?.Reverse();
        }
        var pts = ResampleWithWidths(raw, rawW, 2f, true, out var resW);
        // Start the lap at the start/finish line
        Vector3 sf = data.startFinish != null && data.startFinish.Length == 3 ? B2U(data.startFinish, 0) : pts[0];
        int start = Nearest(pts, sf);
        var rot = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++) rot.Add(pts[(start + i) % pts.Count]);
        track.points = rot.ToArray();
        if (resW != null && resW.Count == pts.Count)
        {
            var rotW = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++) rotW[i] = Mathf.Clamp(resW[(start + i) % pts.Count], 3.8f, 12f);
            track.halfWidths = rotW;
        }
        track.spacing = 2f;
        track.length = track.spacing * track.points.Length;
        track.halfWidth = id == "Monaco" ? 4.6f : Mathf.Clamp(data.halfWidth, 4.5f, 8f);
        track.trackName = info != null ? info.name : id;

        Environment(root, track.PointAt(0));

        // Race manager + pit lane + DRS
        var manager = new GameObject("RaceManager").AddComponent<RaceManager>();
        manager.transform.SetParent(root.transform, false);
        manager.track = track;
        manager.laps = 10;
        manager.useSession = true;
        manager.snapToGround = true;
        string pitNote = SetupPitLane(manager, data);
        manager.drsZones = DrsZones(data);
        var lights = BuildGantry(root, out var on, out var off);
        manager.startLights = lights; manager.lightOn = on; manager.lightOff = off;
        manager.lineGreen = Mat("Marker_Green", new Color(0.1f, 0.8f, 0.3f), 0f, 0.2f, new Color(0.1f, 0.9f, 0.3f) * 0.6f);
        manager.lineYellow = Mat("Marker_Yellow", new Color(1f, 0.85f, 0.1f), 0f, 0.2f, new Color(1f, 0.8f, 0.05f) * 0.7f);
        manager.lineRed = Mat("Marker_Red", new Color(0.95f, 0.1f, 0.1f), 0f, 0.2f, new Color(1f, 0.08f, 0.05f) * 0.8f);

        // Cars
        var player = BuildPlayer();
        manager.player = player.GetComponent<F1Car>();
        var wheelDisp = player.GetComponentInChildren<SteeringWheelDisplay>(true);
        if (wheelDisp != null) wheelDisp.manager = manager;
        manager.ais = BuildRivals(root, manager, id);

        var rivalGo = new GameObject("RivalEngineAudio");
        rivalGo.transform.SetParent(root.transform, false);
        rivalGo.AddComponent<AudioSource>();
        var rivalAudio = rivalGo.AddComponent<EngineAudio>();
        rivalAudio.rival = true;
        manager.rivalAudio = rivalAudio;
        var ambGo = new GameObject("Ambience");
        ambGo.transform.SetParent(root.transform, false);
        ambGo.AddComponent<AudioSource>();
        manager.ambient = ambGo.AddComponent<AmbientAudio>();
        SetupCamera(player);

        var credit = new GameObject("MapCredit").AddComponent<MapCredit>();
        credit.transform.SetParent(root.transform, false);

        EnsureFolder(SceneDir);
        string scenePath = SceneDir + "/" + (info != null ? info.scene : "Final Circuit " + id) + ".unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AddSceneToBuild(scenePath, -1);
        AssetDatabase.SaveAssets();
        Debug.Log($"{id} built: lap {track.length:0} m ({track.points.Length} pts, {data.centreSource}), half width {track.halfWidth:0.0}, " +
                  $"axis fit error {err:0.00} m, {colliders} colliders, DRS zones {manager.drsZones.Length}, {pitNote}. Saved {scenePath}");
    }

    // Colliders on the Col_* surfaces, helper objects hidden, imported lights/cameras removed
    static int SetupMap(GameObject map)
    {
        int count = 0;
        var asphalt = SurfaceMat("Asphalt", 1f);
        var kerb = SurfaceMat("Kerb", 0.9f);
        var grass = SurfaceMat("Grass", 0.6f);
        var gravel = SurfaceMat("Gravel", 0.5f);
        var wall = SurfaceMat("Barrier", 0.12f);
        var all = map.GetComponentsInChildren<Transform>(true);
        var crowd1 = new HashSet<string>();
        foreach (var t in all) if (t.name.EndsWith("Crowd_LOD1")) crowd1.Add(t.name.Substring(0, t.name.Length - 1));
        foreach (var t in all)
        {
            if (t == null) continue;
            string n = t.name;
            if (t.GetComponent<Light>() != null || t.GetComponent<Camera>() != null) { Object.DestroyImmediate(t.gameObject); continue; }
            if (n.StartsWith("Col_") || n == "Collider")
            {
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                string l = n.ToLowerInvariant();
                if (l.Contains("asphalt")) mc.sharedMaterial = asphalt;
                else if (l.Contains("kerb")) mc.sharedMaterial = kerb;
                else if (l.Contains("gravel")) mc.sharedMaterial = gravel;
                else if (l.Contains("grass") || l.Contains("terrain")) mc.sharedMaterial = grass;
                else if (l.Contains("barrier") || l.Contains("wall"))
                {
                    mc.sharedMaterial = wall;
                    t.name = "Barrier_" + n;      // F1Car's wheel rays ignore barrier tops
                }
                else mc.sharedMaterial = wall;
                count++;
                continue;
            }
            if (n.StartsWith("Checkpoint_") || n.StartsWith("Trigger_") || n.StartsWith("DRS_Zone") || n.StartsWith("Reset_Point")
                || n.StartsWith("Track_Limit") || n.StartsWith("AI_") || n.StartsWith("Marker_") || n.StartsWith("Grid_Slot")
                || n.StartsWith("Cam_") || n.StartsWith("GRP_Cameras") || n.StartsWith("GRP_Lighting")
                || n.StartsWith("Pit_Crew") || n.StartsWith("Pit_Stop_Equipment") || n.StartsWith("Start_Gantry")
                || n.StartsWith("Track_Lane_Markings") || n.StartsWith("Side_Roads"))
            {
                t.gameObject.SetActive(false);
                continue;
            }
            // The full-detail crowd is >1M triangles: use the lighter version
            if (n.EndsWith("Crowd_LOD0") && crowd1.Contains(n.Substring(0, n.Length - 1))) t.gameObject.SetActive(false);
            var r = t.GetComponent<MeshRenderer>();
            if (r != null) r.shadowCastingMode = n.Contains("Tree") || n.Contains("Crowd") ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }
        return count;
    }

    // The importer only keeps textures wired straight into the shader; give the plain materials their
    // matching texture from the pack and sensible smoothness (dull asphalt, shiny steel, wet water).
    static void EnhanceMaterials(GameObject map, string id, string texDir)
    {
        string outDir = GenDir + "/" + id;
        EnsureFolder(outDir);
        Texture2D T(params string[] names)
        {
            foreach (var n in names)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "/" + n + ".png");
                if (tex != null) return tex;
            }
            return null;
        }
        var cache = new Dictionary<Material, Material>();
        Material Fix(Material src)
        {
            if (src == null) return null;
            if (cache.TryGetValue(src, out var done)) return done;
            string n = src.name;
            Texture2D tex = null; float smooth = -1f, metal = -1f; Color? tint = null;
            bool alphaClip = false;
            bool hasTex = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null;
            if (n.Contains("Asphalt")) { if (!hasTex) tex = T("Track_Asphalt_Dark_2k", "Asphalt_Albedo"); smooth = 0.22f; }
            else if (n.Contains("Fence") || n.Contains("Chainlink"))
            {
                tex = T("Chainlink_RGBA_1k") ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/F1Assets/Tracks/Monaco/textures/Chainlink_RGBA_1k.png");
                smooth = 0.4f; metal = 0.6f; alphaClip = true;
            }
            else if (n.Contains("Gravel")) { tex = T("Gravel_Albedo", "Concrete_PBR_Albedo"); tint = new Color(0.86f, 0.78f, 0.62f); smooth = 0.05f; }
            else if (n.Contains("Tunnel_Wall") || n.Contains("Tunnel_Ceiling")) { tex = T("Concrete_PBR_Albedo"); tint = n.Contains("Ceiling") ? new Color(0.28f, 0.30f, 0.34f) : new Color(0.46f, 0.48f, 0.52f); smooth = 0.16f; }
            else if (n.Contains("Tunnel_Light")) { smooth = 0.8f; tint = new Color(1f, 0.96f, 0.86f); }
            else if (n.Contains("Monaco_White")) { tex = T("Concrete_PBR_Albedo"); tint = new Color(0.86f, 0.87f, 0.89f); smooth = 0.18f; }
            else if (n.Contains("Concrete") || n.Contains("Pit_Wall") || n.Contains("Bridge")) { tex = T("Concrete_PBR_Albedo"); smooth = 0.15f; }
            else if (n.Contains("Armco") || n.Contains("Steel")) { tex = T("Armco_Albedo_1k", "Steel_Albedo_1k"); smooth = 0.55f; metal = 0.8f; }
            else if (n.Contains("Stucco")) { tex = T("Stucco_Wall_Albedo"); smooth = 0.1f; }
            else if (n.Contains("Terracotta")) { tex = T("Terracotta_Tile_Albedo"); smooth = 0.15f; }
            else if (n.Contains("Roof")) { tex = T("Roof_Corrugated_Albedo_1k"); smooth = 0.35f; metal = 0.5f; }
            else if (n.Contains("Bark")) { tex = T("Bark_Albedo_1k"); smooth = 0.05f; }
            else if (n.Contains("Leaves")) { tex = T("Leaves_Albedo_1k"); smooth = 0.1f; }
            else if (n.Contains("Grass") || n.Contains("Lawn")) { if (!hasTex) tex = T("Lawn_Green_Albedo_2k", "Grass_Albedo"); smooth = 0.08f; }
            else if (n.Contains("Water")) { smooth = 0.92f; }
            else if (n.Contains("Kerb")) { smooth = 0.3f; }
            else if (n.Contains("Glass")) { smooth = 0.95f; metal = 0.2f; }
            if (tex == null && smooth < 0f && metal < 0f && !alphaClip) { cache[src] = src; return src; }
            string path = outDir + "/" + n.Replace("/", "_") + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(src);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                var c = tint ?? (src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white);
                // Keep the authored colour as a gentle tint over the texture
                m.SetColor("_BaseColor", alphaClip ? Color.white : Color.Lerp(Color.white, c, 0.5f));
            }
            if (n.Contains("Tunnel_Light"))
            {
                m.SetColor("_BaseColor", new Color(1f, 0.96f, 0.86f));
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(1f, 0.94f, 0.80f) * 4.5f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (smooth >= 0f) m.SetFloat("_Smoothness", smooth);
            if (metal >= 0f) m.SetFloat("_Metallic", metal);
            if (alphaClip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            cache[src] = m;
            return m;
        }
        foreach (var r in map.GetComponentsInChildren<Renderer>(true))
        {
            var ms = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < ms.Length; i++) { var f = Fix(ms[i]); if (f != ms[i]) { ms[i] = f; changed = true; } }
            if (changed) r.sharedMaterials = ms;
        }
        AssetDatabase.SaveAssets();
    }

    static PhysicsMaterial SurfaceMat(string name, float friction)
    {
        string p = GenDir + "/" + name + ".asset";
        var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(p);
        if (m == null) { m = new PhysicsMaterial(name); AssetDatabase.CreateAsset(m, p); }
        m.dynamicFriction = friction; m.staticFriction = friction;
        m.frictionCombine = name == "Barrier" ? PhysicsMaterialCombine.Minimum : PhysicsMaterialCombine.Average;
        m.bounciness = 0f; m.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var mfs = go.GetComponentsInChildren<MeshFilter>(true);
        Bounds b = default;
        bool has = false;
        foreach (var mf in mfs)
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 wp = m.MultiplyPoint3x4(c + new Vector3(e.x * sx, e.y * sy, e.z * sz));
                        if (!has) { b = new Bounds(wp, Vector3.zero); has = true; }
                        else b.Encapsulate(wp);
                    }
        }
        if (has) return b;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Try every axis permutation / sign and keep the one that maps the Blender asphalt bounds onto Unity's
    static float Calibrate(TrackJson d, Bounds ub)
    {
        int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        float best = float.MaxValue;
        foreach (var p in perms)
            for (int s = 0; s < 8; s++)
            {
                var m = Matrix4x4.zero;
                for (int r = 0; r < 3; r++) m[r, p[r]] = ((s >> r) & 1) == 0 ? 1f : -1f;
                m[3, 3] = 1f;
                Vector3 a = m.MultiplyPoint3x4(new Vector3(d.asphaltMin[0], d.asphaltMin[1], d.asphaltMin[2]));
                Vector3 b = m.MultiplyPoint3x4(new Vector3(d.asphaltMax[0], d.asphaltMax[1], d.asphaltMax[2]));
                Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b);
                float e = (mn - ub.min).magnitude + (mx - ub.max).magnitude;
                if (e < best) { best = e; blenderToUnity = m; }
            }
        return best;
    }

    static Vector3 B2U(float[] a, int i) => blenderToUnity.MultiplyPoint3x4(new Vector3(a[i], a[i + 1], a[i + 2]));

    static List<Vector3> ToPoints(float[] flat)
    {
        var l = new List<Vector3>();
        if (flat == null) return l;
        for (int i = 0; i + 2 < flat.Length; i += 3) l.Add(B2U(flat, i));
        return l;
    }

    static bool IsClockwiseBlender(float[] flat)
    {
        double area = 0; int n = flat.Length / 3;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += flat[i * 3] * flat[j * 3 + 1] - flat[j * 3] * flat[i * 3 + 1];
        }
        return area < 0;   // Blender: x east, y north
    }

    // Catmull-Rom densify, then resample at a fixed arc-length spacing (with optional parallel width array)
    static List<Vector3> Resample(List<Vector3> src, float step, bool closed)
        => ResampleWithWidths(src, null, step, closed, out _);

    static List<Vector3> ResampleWithWidths(List<Vector3> src, List<float> widths, float step, bool closed, out List<float> outWidths)
    {
        bool hasW = widths != null && widths.Count == src.Count;
        var dense = new List<Vector3>();
        var denseW = hasW ? new List<float>() : null;
        int n = src.Count;
        for (int i = 0; i < (closed ? n : n - 1); i++)
        {
            Vector3 p0 = src[closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)], p1 = src[i], p2 = src[(i + 1) % n], p3 = src[closed ? (i + 2) % n : Mathf.Min(i + 2, n - 1)];
            float w1 = hasW ? widths[i] : 7f, w2 = hasW ? widths[(i + 1) % n] : 7f;
            int sub = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(p1, p2) / 0.5f), 1, 64);
            for (int k = 0; k < sub; k++)
            {
                float t = k / (float)sub, t2 = t * t, t3 = t2 * t;
                dense.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                if (hasW) denseW.Add(Mathf.Lerp(w1, w2, t));
            }
        }
        if (closed) { dense.Add(dense[0]); if (hasW) denseW.Add(denseW[0]); }
        else { dense.Add(src[n - 1]); if (hasW) denseW.Add(widths[n - 1]); }
        float total = 0f;
        for (int i = 1; i < dense.Count; i++) total += Vector3.Distance(dense[i - 1], dense[i]);
        int count = Mathf.Max(2, Mathf.RoundToInt(total / step));
        float ds = total / count;
        var outp = new List<Vector3> { dense[0] };
        outWidths = hasW ? new List<float> { denseW[0] } : null;
        float acc = 0f, next = ds; int idx = 1;
        while (outp.Count < (closed ? count : count + 1) && idx < dense.Count)
        {
            float seg = Vector3.Distance(dense[idx - 1], dense[idx]);
            if (acc + seg >= next)
            {
                float alpha = (next - acc) / Mathf.Max(seg, 1e-5f);
                outp.Add(Vector3.Lerp(dense[idx - 1], dense[idx], alpha));
                if (hasW) outWidths.Add(Mathf.Lerp(denseW[idx - 1], denseW[idx], alpha));
                next += ds;
            }
            else { acc += seg; idx++; }
        }
        return outp;
    }

    static int Nearest(List<Vector3> pts, Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < pts.Count; i++) { float d = (pts[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
        return best;
    }

    // Pit lane: lateral profile of the real pit lane along the track, entry / exit windows and team boxes
    static string SetupPitLane(RaceManager m, TrackJson d)
    {
        var pit = ToPoints(d.pit);
        if (pit.Count < 4) { m.boxDistances = new float[0]; return "no pit lane"; }
        pit = Resample(pit, 2f, false);
        float L = track.length;
        float d0 = track.ClosestDistance(pit[0]);
        var ds = new List<float>(); var lats = new List<float>();
        float hint = d0;
        foreach (var p in pit)
        {
            float dd = track.ClosestDistance(p, hint, 40);
            hint = dd;
            ds.Add(dd); lats.Add(track.LateralOffset(p, dd));
        }
        // Pit lane must run in the lap direction
        float span = Mathf.Repeat(ds[ds.Count - 1] - ds[0], L);
        if (span > L / 2f)
        {
            pit.Reverse(); ds.Clear(); lats.Clear();
            hint = track.ClosestDistance(pit[0]);
            foreach (var p in pit) { float dd = track.ClosestDistance(p, hint, 40); hint = dd; ds.Add(dd); lats.Add(track.LateralOffset(p, dd)); }
            span = Mathf.Repeat(ds[ds.Count - 1] - ds[0], L);
        }
        float startD = ds[0];
        // Distances relative to the first point, made monotonic (small backward jitter would stall the scan)
        var rel = new float[ds.Count];
        for (int i = 0; i < ds.Count; i++)
        {
            float r = Mathf.Repeat(ds[i] - startD + L * 0.25f, L) - L * 0.25f;
            rel[i] = i == 0 ? 0f : Mathf.Max(rel[i - 1], r);
        }
        span = rel[rel.Length - 1];
        int steps = Mathf.Max(2, Mathf.CeilToInt(span / 2f) + 1);
        var prof = new float[steps];
        int j = 0;
        for (int s = 0; s < steps; s++)
        {
            float u = s * 2f;
            while (j < rel.Length - 2 && rel[j + 1] < u) j++;
            float a = rel[j], b = rel[j + 1];
            prof[s] = Mathf.Lerp(lats[j], lats[j + 1], b > a ? Mathf.Clamp01((u - a) / (b - a)) : 1f);
        }

        // Guarantee physical barrier clearance & narrow-chute midpoint centering at every pit-lane sample
        Physics.SyncTransforms();
        var pitColHits = new Collider[8];
        var pitRayHits = new RaycastHit[8];
        int nudged = 0;
        bool HitsBarrier(float sd, float latTest, float rad)
        {
            Vector3 wp = track.PointAt(sd) + track.RightAt(sd) * latTest;
            float gy = RaceManager.GroundAt(wp, out float y) ? y : wp.y;
            wp.y = gy + 0.65f;
            int hc = Physics.OverlapSphereNonAlloc(wp, rad, pitColHits, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < hc; k++)
                if (pitColHits[k].name.StartsWith("Barrier")) return true;
            return false;
        }
        bool RayBarrierDist(Vector3 origin, Vector3 dir, out float dist)
        {
            dist = 5f;
            int hc = Physics.RaycastNonAlloc(origin, dir, pitRayHits, 5f, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int k = 0; k < hc; k++)
            {
                var h = pitRayHits[k];
                if (h.collider != null && h.collider.name.StartsWith("Barrier") && h.distance < dist)
                { dist = h.distance; found = true; }
            }
            return found;
        }
        float[] radii = { 1.30f, 1.05f, 0.90f };
        float[] testDeltas = { 0.35f, -0.35f, 0.70f, -0.70f, 1.05f, -1.05f, 1.45f, -1.45f, 1.90f, -1.90f, 2.40f, -2.40f, 3.00f, -3.00f };
        for (int s = 0; s < steps; s++)
        {
            float sd = Mathf.Repeat(startD + s * 2f, L);
            float orig = prof[s];
            if (HitsBarrier(sd, orig, 1.30f))
            {
                float centerDir = -Mathf.Sign(Mathf.Abs(orig) > 0.1f ? orig : 1f);
                bool fixedSample = false;
                foreach (float rad in radii)
                {
                    foreach (float dAbs in testDeltas)
                    {
                        float cand = orig + (dAbs > 0f ? Mathf.Abs(dAbs) * centerDir : -Mathf.Abs(dAbs) * centerDir);
                        if (!HitsBarrier(sd, cand, rad)) { prof[s] = cand; nudged++; fixedSample = true; break; }
                    }
                    if (fixedSample) break;
                }
            }
            // Bilateral midpoint centering whenever enclosed in a narrow pit chute
            Vector3 right = track.RightAt(sd);
            Vector3 wpNow = track.PointAt(sd) + right * prof[s];
            wpNow.y = (RaceManager.GroundAt(wpNow, out float gy2) ? gy2 : wpNow.y) + 0.65f;
            if (RayBarrierDist(wpNow, -right, out float dL) && RayBarrierDist(wpNow, right, out float dR)
                && dL < 3.5f && dR < 3.5f && Mathf.Min(dL, dR) < 1.25f)
            {
                float centered = prof[s] + (dR - dL) * 0.5f;
                if (!HitsBarrier(sd, centered, 0.88f) && Mathf.Abs(centered - prof[s]) > 0.05f)
                { prof[s] = centered; nudged++; }
            }
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var sm = (float[])prof.Clone();
            for (int s = 1; s < steps - 1; s++)
            {
                float blended = prof[s - 1] * 0.25f + prof[s] * 0.50f + prof[s + 1] * 0.25f;
                float sd = Mathf.Repeat(startD + s * 2f, L);
                if (!HitsBarrier(sd, blended, 0.95f)) sm[s] = blended;
            }
            prof = sm;
        }

        // Side and typical offset from the middle half of the lane
        var mid = new List<float>();
        for (int s = steps / 4; s < steps * 3 / 4; s++) mid.Add(prof[s]);
        mid.Sort((x, y) => Mathf.Abs(x).CompareTo(Mathf.Abs(y)));
        float off = mid.Count > 0 ? mid[mid.Count / 2] : prof[steps / 2];
        float edge = track.halfWidth + 2.5f;
        int inIdx = Array.FindIndex(prof, x => Mathf.Abs(x) > edge && Mathf.Sign(x) == Mathf.Sign(off));
        int outIdx = Array.FindLastIndex(prof, x => Mathf.Abs(x) > edge && Mathf.Sign(x) == Mathf.Sign(off));
        if (inIdx < 0) { inIdx = steps / 6; outIdx = steps * 5 / 6; }
        m.pitProfile = prof;
        m.pitProfileStart = startD;
        m.pitProfileStep = 2f;
        m.pitLaneOffset = off;
        m.pitEntryStart = startD;
        m.pitEntryEnd = Mathf.Repeat(startD + inIdx * 2f, L);
        m.pitExitStart = Mathf.Repeat(startD + outIdx * 2f, L);
        m.pitExitEnd = Mathf.Repeat(startD + span, L);
        m.pitSpeed = 80f / 3.6f;
        m.pitEntryDistance = m.pitEntryEnd;
        int teams = GameSession.Teams.Length;
        m.boxDistances = new float[teams];
        m.boxOffsets = new float[teams];

        if (d.garages != null && d.garages.Length >= teams * 3)
        {
            float sumSide = 0f;
            var rawGLats = new float[teams];
            float uMin = Mathf.Max(0f, inIdx * 2f - 4f);
            float uMax = Mathf.Min(span, outIdx * 2f + 4f);
            for (int t = 0; t < teams; t++)
            {
                Vector3 gw = B2U(d.garages, t * 3);
                float uBest = uMin, bestSq = float.MaxValue;
                for (float u = uMin; u <= uMax; u += 1f)
                {
                    float sd = Mathf.Repeat(startD + u, L);
                    Vector3 pf = track.PointAt(sd) + track.RightAt(sd) * m.PitLat(sd);
                    float dx = gw.x - pf.x, dz = gw.z - pf.z;
                    float dSq = dx * dx + dz * dz;
                    if (dSq < bestSq) { bestSq = dSq; uBest = u; }
                }
                float db = Mathf.Repeat(startD + uBest, L);
                m.boxDistances[t] = db;
                rawGLats[t] = track.LateralOffset(gw, db);
                sumSide += rawGLats[t] - m.PitLat(db);
            }
            m.garageSide = sumSide >= 0f ? 1f : -1f;
            float[] tryOffsets = { 2.35f, 2.10f, 1.85f, 1.60f };
            var colHits = new Collider[8];
            for (int t = 0; t < teams; t++)
            {
                float db = m.boxDistances[t];
                float fLat = m.PitLat(db);
                float maxShift = Mathf.Clamp(Mathf.Abs(rawGLats[t] - fLat) * 0.62f, 1.85f, 2.40f);
                float chosen = fLat + m.garageSide * Mathf.Min(1.85f, maxShift);
                foreach (float offTry in tryOffsets)
                {
                    float cLat = fLat + m.garageSide * Mathf.Min(offTry, maxShift);
                    Vector3 wp = track.PointAt(db) + track.RightAt(db) * cLat;
                    if (!RaceManager.GroundAt(wp, out float gy)) continue;
                    wp.y = gy + 0.65f;
                    int hc = Physics.OverlapSphereNonAlloc(wp, 0.90f, colHits, ~0, QueryTriggerInteraction.Ignore);
                    bool hitWall = false;
                    for (int k = 0; k < hc; k++) if (colHits[k].name.StartsWith("Barrier")) { hitWall = true; break; }
                    if (!hitWall) { chosen = cLat; break; }
                }
                m.boxOffsets[t] = chosen;
            }
            return $"pit lane {span:0} m (nudged {nudged}), offset {off:0.0} m, entry {m.pitEntryStart:0}-{m.pitEntryEnd:0}, exit {m.pitExitStart:0}-{m.pitExitEnd:0}, 11 garages aligned (side {m.garageSide:+0;-0})";
        }
        else
        {
            float a0 = (inIdx * 2f) + 25f, a1 = (outIdx * 2f) - 25f;
            if (a1 < a0) { a0 = inIdx * 2f; a1 = outIdx * 2f; }
            m.garageSide = off >= 0f ? 1f : -1f;
            for (int t = 0; t < teams; t++)
            {
                float db = Mathf.Repeat(startD + Mathf.Lerp(a0, a1, (t + 0.5f) / teams), L);
                m.boxDistances[t] = db;
                m.boxOffsets[t] = m.PitLat(db);
            }
            return $"pit lane {span:0} m, offset {off:0.0} m, entry {m.pitEntryStart:0}-{m.pitEntryEnd:0}, exit {m.pitExitStart:0}-{m.pitExitEnd:0}";
        }
    }

    // DRS zones from the pack where given, otherwise the two longest straights
    static Vector2[] DrsZones(TrackJson d)
    {
        var zones = new List<Vector2>();
        if (d.drs != null)
            foreach (var z in d.drs)
                if (z.start != null && z.start.Length == 3 && z.end != null && z.end.Length == 3)
                    zones.Add(new Vector2(track.ClosestDistance(B2U(z.start, 0)), track.ClosestDistance(B2U(z.end, 0))));
        if (zones.Count > 0) return zones.ToArray();
        for (float minLen = 350f; minLen >= 150f && zones.Count == 0; minLen -= 100f) zones.AddRange(StraightZones(minLen));
        return zones.ToArray();
    }

    static List<Vector2> StraightZones(float minLen)
    {
        var zones = new List<Vector2>();
        int n = track.points.Length;
        var straight = new bool[n];
        for (int i = 0; i < n; i++) straight[i] = track.RadiusAt(i * track.spacing) > 350f;
        var runs = new List<Vector2>();
        int first = Array.FindIndex(straight, s => !s);
        if (first < 0) { zones.Add(new Vector2(0, track.length * 0.25f)); return zones; }
        int runStart = -1;
        for (int k = 1; k <= n; k++)
        {
            int i = (first + k) % n;
            if (straight[i] && runStart < 0) runStart = first + k;
            if ((!straight[i] || k == n) && runStart >= 0)
            {
                float a = runStart * track.spacing, b = (first + k) * track.spacing;
                if (b - a > minLen) runs.Add(new Vector2(a, b));
                runStart = -1;
            }
        }
        runs.Sort((x, y) => (y.y - y.x).CompareTo(x.y - x.x));
        for (int i = 0; i < Mathf.Min(2, runs.Count); i++)
            zones.Add(new Vector2(Mathf.Repeat(runs[i].x + 40f, track.length), Mathf.Repeat(runs[i].y - 60f, track.length)));
        return zones;
    }

    // ---------- Scene dressing ----------

    static Material Mat(string name, Color c, float metal, float smooth, Color? emission = null)
    {
        if (mats.TryGetValue(name, out var cached)) return cached;
        string p = GenDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static void Environment(GameObject root, Vector3 focus)
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 2.4f;
        sun.color = new Color(1f, 0.97f, 0.91f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.72f;
        sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
        RenderSettings.sun = sun;

        var sky = AssetDatabase.LoadAssetAtPath<Material>(GenDir + "/SkyPanorama.mat");
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(sky, GenDir + "/SkyPanorama.mat"); }
        sky.shader = Shader.Find("Skybox/Panoramic");
        sky.SetTexture("_MainTex", SkyTexture(-sun.transform.forward));
        sky.SetFloat("_Mapping", 1f);
        sky.SetFloat("_ImageType", 0f);
        sky.SetFloat("_Exposure", 1.1f);
        sky.SetFloat("_Rotation", 0f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.74f, 0.92f);
        RenderSettings.ambientEquatorColor = new Color(0.54f, 0.57f, 0.62f);
        RenderSettings.ambientGroundColor = new Color(0.28f, 0.29f, 0.31f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = GetOrBuildSkyReflectionCube(-sun.transform.forward);
        RenderSettings.reflectionIntensity = 1.25f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.9f);
        RenderSettings.fogStartDistance = 250f;
        RenderSettings.fogEndDistance = 3200f;
        DynamicGI.UpdateEnvironment();

        // Post-processing: ACES filmic + lifted exposure for bright daytime reflections
        var volGo = new GameObject("PostProcess");
        volGo.transform.SetParent(root.transform, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        string profPath = GenDir + "/CircuitPost.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
        var bl = prof.Add<Bloom>(true); bl.intensity.Override(0.25f); bl.threshold.Override(1.15f); bl.scatter.Override(0.55f);
        var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.30f); ca.contrast.Override(14f); ca.saturation.Override(10f);
        var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(4f);
        var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.14f); vg.smoothness.Override(0.50f);
        foreach (var c in prof.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, prof); }
        AssetDatabase.SaveAssets();
        vol.sharedProfile = prof;
    }

    public static Cubemap GetOrBuildSkyReflectionCube(Vector3 sunDir)
    {
        EnsureFolder(GenDir);
        string cubePath = GenDir + "/SkyReflectionCube.asset";
        if (skyCubeBuiltThisSession)
        {
            var cached = AssetDatabase.LoadAssetAtPath<Cubemap>(cubePath);
            if (cached != null) return cached;
        }
        AssetDatabase.DeleteAsset(cubePath);

        int size = 128;
        var cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "SkyReflectionCube" };
        sunDir = sunDir.sqrMagnitude > 0.01f ? sunDir.normalized : new Vector3(0.4f, 0.7f, -0.4f).normalized;
        Color zenith = new Color(0.22f, 0.48f, 0.90f);
        Color horizon = new Color(0.76f, 0.85f, 0.96f);
        Color ground = new Color(0.32f, 0.34f, 0.36f);
        Color sunCol = new Color(1.0f, 0.92f, 0.74f);

        Color SampleDir(Vector3 dir)
        {
            dir.Normalize();
            if (dir.y < 0f) return Color.Lerp(horizon * 0.72f, ground, Mathf.Clamp01(-dir.y * 3.5f));
            Color c = Color.Lerp(horizon, zenith, Mathf.Pow(dir.y, 0.45f));
            float sd = Mathf.Max(0f, Vector3.Dot(dir, sunDir));
            c += sunCol * (Mathf.Pow(sd, 10f) * 0.35f + Mathf.Pow(sd, 140f) * 2.2f);
            float cloudBand = Mathf.SmoothStep(0.05f, 0.35f, dir.y) * (0.5f + 0.5f * Mathf.Sin(dir.x * 5f + dir.z * 3.7f));
            c = Color.Lerp(c, new Color(0.95f, 0.96f, 0.98f), cloudBand * 0.22f);
            return c;
        }

        foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
        {
            if (face == CubemapFace.Unknown) continue;
            var cols = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = 1f - 2f * ((y + 0.5f) / size);
                for (int x = 0; x < size; x++)
                {
                    float u = 2f * ((x + 0.5f) / size) - 1f;
                    Vector3 d;
                    switch (face)
                    {
                        case CubemapFace.PositiveX: d = new Vector3(1f, v, -u); break;
                        case CubemapFace.NegativeX: d = new Vector3(-1f, v, u); break;
                        case CubemapFace.PositiveY: d = new Vector3(u, 1f, -v); break;
                        case CubemapFace.NegativeY: d = new Vector3(u, -1f, v); break;
                        case CubemapFace.PositiveZ: d = new Vector3(u, v, 1f); break;
                        default:                    d = new Vector3(-u, v, -1f); break;
                    }
                    Color col = SampleDir(d);
                    col.a = 1f;
                    cols[y * size + x] = col;
                }
            }
            cube.SetPixels(cols, face);
        }
        cube.Apply(true, false);
        AssetDatabase.CreateAsset(cube, cubePath);
        AssetDatabase.SaveAssets();
        skyCubeBuiltThisSession = true;
        return cube;
    }

    // Panoramic daytime sky: deep blue zenith, pale hazy horizon, warm glow round the sun, fbm cumulus clouds
    // projected on a cloud layer (thin towards the horizon). Generated once and saved as a texture asset.
    static Texture2D SkyTexture(Vector3 sunDir)
    {
        string path = GenDir + "/SkyPanorama.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        int w = 2048, h = 1024;
        var px = new Color[w * h];
        var rnd = new System.Random(7);
        var perm = new int[512]; for (int i = 0; i < 256; i++) perm[i] = i;
        for (int i = 255; i > 0; i--) { int j = rnd.Next(i + 1); int tmp = perm[i]; perm[i] = perm[j]; perm[j] = tmp; }
        for (int i = 0; i < 256; i++) perm[256 + i] = perm[i];
        float Hash(int x, int y) => perm[(perm[x & 255] + y) & 511] / 255f;
        float VNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
        }
        float Fbm(float x, float y) { float s = 0, a = 0.5f; for (int o = 0; o < 6; o++) { s += a * VNoise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; } return s; }
        Color zenith = new Color(0.16f, 0.36f, 0.72f), horizon = new Color(0.72f, 0.82f, 0.93f), ground = new Color(0.42f, 0.45f, 0.44f);
        for (int y = 0; y < h; y++)
        {
            float lat = (y + 0.5f) / h * Mathf.PI - Mathf.PI / 2f;    // -90 (down) .. +90 (up)
            for (int x = 0; x < w; x++)
            {
                float lon = (x + 0.5f) / w * 2f * Mathf.PI - Mathf.PI;
                Vector3 dir = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
                Color c;
                if (dir.y < 0f) c = Color.Lerp(horizon * 0.95f, ground, Mathf.Clamp01(-dir.y * 4f));
                else
                {
                    c = Color.Lerp(horizon, zenith, Mathf.Pow(dir.y, 0.45f));
                    float sunDot = Mathf.Max(0f, Vector3.Dot(dir, sunDir));
                    c += new Color(1f, 0.85f, 0.6f) * (Mathf.Pow(sunDot, 8f) * 0.25f + Mathf.Pow(sunDot, 200f) * 0.8f);
                    // Cloud layer
                    float py = Mathf.Max(dir.y, 0.02f);
                    float u = dir.x / py * 1.6f, v = dir.z / py * 1.6f;
                    float n = Fbm(u + 40f, v + 17f);
                    float cover = Mathf.SmoothStep(0.48f, 0.72f, n) * Mathf.SmoothStep(0.02f, 0.18f, dir.y);
                    float shade = Mathf.Lerp(0.78f, 1.05f, Mathf.SmoothStep(0.5f, 0.85f, Fbm(u * 1.7f + 3f, v * 1.7f + 9f)));
                    Color cloud = new Color(1f, 0.99f, 0.97f) * shade + new Color(1f, 0.9f, 0.7f) * Mathf.Pow(sunDot, 6f) * 0.25f;
                    c = Color.Lerp(c, cloud, cover * 0.92f);
                    // Haze near the horizon
                    c = Color.Lerp(c, horizon, Mathf.Pow(1f - dir.y, 10f) * 0.6f);
                }
                c.a = 1f;
                px[y * w + x] = c;
            }
        }
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
        ti.maxTextureSize = 2048; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Start lights on a gantry over the start/finish line
    static Renderer[] BuildGantry(GameObject root, out Material on, out Material off)
    {
        var g = new GameObject("StartGantry");
        g.transform.SetParent(root.transform, false);
        Vector3 p = track.PointAt(-6f);
        Vector3 t = track.TangentAt(-6f); t.y = 0;
        g.transform.SetPositionAndRotation(p, Quaternion.LookRotation(t.normalized));
        var steel = Mat("Gantry_Steel", new Color(0.2f, 0.22f, 0.25f), 0.6f, 0.5f);
        float x = track.halfWidth + 1.6f;
        Box("PillarL", g, new Vector3(-x, 3.8f, 0), new Vector3(0.5f, 7.6f, 0.5f), steel, true);
        Box("PillarR", g, new Vector3(x, 3.8f, 0), new Vector3(0.5f, 7.6f, 0.5f), steel, true);
        Box("Beam", g, new Vector3(0, 7.3f, 0), new Vector3(2f * x + 0.5f, 0.8f, 0.7f), steel, false);
        Box("Panel", g, new Vector3(0, 6.2f, -0.2f), new Vector3(6.6f, 1.2f, 0.4f), Mat("Gantry_Panel", new Color(0.03f, 0.03f, 0.03f), 0f, 0.3f), false);
        off = Mat("StartLight_Off", new Color(0.18f, 0.03f, 0.03f), 0f, 0.8f);
        on = Mat("StartLight_On", new Color(1f, 0.05f, 0.03f), 0f, 0.8f, new Color(1f, 0.05f, 0.02f) * 4f);
        var lights = new Renderer[5];
        for (int k = 0; k < 5; k++)
        {
            var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(l.GetComponent<Collider>());
            l.name = "StartLight" + (k + 1);
            l.transform.SetParent(g.transform, false);
            l.transform.localPosition = new Vector3(-2.4f + k * 1.2f, 6.2f, -0.45f);
            l.transform.localScale = Vector3.one * 0.7f;
            lights[k] = l.GetComponent<Renderer>();
            lights[k].sharedMaterial = off;
        }
        // Ground the pillars on the real surface
        foreach (Transform c in g.transform)
            if (c.name.StartsWith("Pillar") && Physics.Raycast(c.position + Vector3.up * 20f, Vector3.down, out var hit, 60f))
                c.position = new Vector3(c.position.x, hit.point.y + 3.8f, c.position.z);
        return lights;
    }

    static void Box(string n, GameObject parent, Vector3 pos, Vector3 scale, Material m, bool collider)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = n;
        b.transform.SetParent(parent.transform, false);
        b.transform.localPosition = pos;
        b.transform.localScale = scale;
        b.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(b.GetComponent<Collider>());
        else b.name = "Barrier_" + n;
    }

    static float SurfaceY(Vector3 p)
    {
        return RaceManager.GroundAt(p, out float y) ? y : p.y;
    }

    static GameObject BuildPlayer()
    {
        var car = InstantiateCar("F1_Player");
        float d = -10f, lat = -Mathf.Min(2.4f, track.halfWidth - 2.2f);
        Vector3 pos = track.PointAt(d) + track.RightAt(d) * lat;
        pos.y = SurfaceY(pos) + 0.08f;
        car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(track.TangentAt(d)));
        car.AddComponent<Rigidbody>();
        var f1 = car.AddComponent<F1Car>();
        ConfigureF1(f1, car.GetComponent<CarGeometry>());
        CreateSparks(car);
        car.AddComponent<CarHUD>().car = f1;
        var swScreen = FindDeep(car.transform, "SteeringWheel_Screen");
        if (swScreen != null && swScreen.GetComponent<SteeringWheelDisplay>() == null)
        {
            var disp = swScreen.gameObject.AddComponent<SteeringWheelDisplay>();
            disp.car = f1;
        }
        var audio = new GameObject("EngineAudio");
        audio.transform.SetParent(car.transform, false);
        audio.AddComponent<AudioSource>();
        audio.AddComponent<EngineAudio>().car = f1;
        return car;
    }

    static RaceAI[] BuildRivals(GameObject root, RaceManager m, string id)
    {
        var parent = new GameObject("Field");
        parent.transform.SetParent(root.transform, false);
        var list = new RaceAI[Cars - 1];
        bool monaco = id == "Monaco";
        for (int k = 1; k < Cars; k++)
        {
            float d = -(10f + 8f * k), lat = Mathf.Min(2.4f, track.halfWidth - 2.2f) * (k % 2 == 0 ? -1f : 1f);
            var car = InstantiateCar("Car_" + k);
            car.transform.SetParent(parent.transform, false);
            Vector3 pos = track.PointAt(d) + track.RightAt(d) * lat;
            pos.y = SurfaceY(pos) + 0.03f;
            car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(track.TangentAt(d)));
            var rb = car.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var col = car.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;   // contact with the player is resolved by F1Car
            var ai = car.AddComponent<RaceAI>();
            ai.driverName = "Driver " + k;
            ai.maxSpeed = 84f;
            ai.cornerGrip = monaco ? 12.6f : 14.5f;
            ai.aeroGrip = monaco ? 0.0036f : 0.0044f;
            ai.acceleration = 10.2f;
            ai.braking = 13.5f;
            ai.aeroBraking = 0.0032f;
            ai.lane = lat;
            ai.progress = d;
            ai.parkLane = m.pitLaneOffset;
            list[k - 1] = ai;
        }
        return list;
    }

    static void SetupCamera(GameObject player)
    {
        var go = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = go.AddComponent<Camera>();
        go.AddComponent<AudioListener>();
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 4000f;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var follow = go.AddComponent<ChaseCamera>();
        follow.target = player.transform;
        // TV pod above the airbox, cockpit geometrically aligned behind SteeringWheel_Pivot
        var geo = player.GetComponent<CarGeometry>();
        float headZ = geo != null ? geo.headZ : -0.2f, headY = geo != null ? geo.headY : 0.95f;
        follow.tvPodOffset = new Vector3(0, headY + 0.42f, headZ - 0.2f);
        follow.tvPodPitch = 8f;
        var swPivot = FindDeep(player.transform, "SteeringWheel_Pivot");
        Vector3 sw = swPivot != null ? player.transform.InverseTransformPoint(swPivot.position)
                   : (geo != null ? geo.steeringWheelLocalPos : new Vector3(0f, 0.650f, 0.585f));
        follow.cockpitOffset = new Vector3(0f, sw.y + 0.098f, sw.z - 0.305f);
        follow.cockpitPitch = 2.8f;
        follow.distance = 5.6f;
        follow.height = 1.45f;
        follow.view = ChaseCamera.View.Chase;
        go.transform.position = player.transform.TransformPoint(follow.tvPodOffset);
        go.transform.rotation = player.transform.rotation;
    }

    // ---------- Old map removal ----------

    // Editor testing: play the open circuit scene directly (skips the redirect to the main menu)
    [MenuItem("Tools/XPARROW/Play Open Circuit (test)")]
    public static void PlayOpenCircuit()
    {
        var id = Array.Find(GameSession.Tracks, t => t.scene == EditorSceneManager.GetActiveScene().name);
        if (id != null) GameSession.Race.track = Array.IndexOf(GameSession.Tracks, id);
        SessionState.SetBool("xparrow_direct_play", true);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/XPARROW/4 Remove Old Procedural Monaco")]
    public static void RemoveOldMonaco()
    {
        var removed = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("\"Monaco GP\" t:Scene"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == "Monaco GP" && AssetDatabase.MoveAssetToTrash(p)) removed.Add(p);
        }
        foreach (var folder in new[] { "Assets/Race/Meshes/Monaco", "Assets/Race/Generated/Meshes/Monaco" })
            if (AssetDatabase.IsValidFolder(folder) && AssetDatabase.MoveAssetToTrash(folder)) removed.Add(folder);
        SetBuildScenes();
        Debug.Log("Removed (moved to the Recycle Bin): " + (removed.Count > 0 ? string.Join(", ", removed) : "nothing found"));
    }

    public static void CreateSparks(GameObject car)
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(car.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.55f, 0.1f));
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = grad;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 1.5f;
        r.sharedMaterial = SparkMaterial();
    }

    public static void RenderUnityVerificationScreenshots()
    {
        const string bakuPath = "Assets/Scenes/Final/Final Circuit Baku.unity";
        if (!File.Exists(bakuPath)) return;
        EditorSceneManager.OpenScene(bakuPath, OpenSceneMode.Single);

        var player = GameObject.Find("F1_Player");
        var cam = Camera.main;
        if (player == null || cam == null) return;

        var lod = player.GetComponent<LODGroup>();
        if (lod != null) lod.ForceLOD(0);

        // Apply copyright-safe 4K ORBITAL livery (02) + Soft tyres + live telemetry onto OLED display
        RaceManager.ApplyLivery(player, Color.white, Color.white, Color.yellow, 2);
        var softTyre = Resources.Load<Material>("F1Tyres/Tyre_Soft");
        if (softTyre != null)
            foreach (var mr in player.GetComponentsInChildren<Renderer>(true))
                if (mr.name.StartsWith("F1_Wheel")) mr.sharedMaterial = softTyre;

        var disp = player.GetComponentInChildren<SteeringWheelDisplay>(true);
        Material origScreenMat = null, tmpScreenMat = null;
        Renderer dispRend = disp != null ? disp.GetComponent<Renderer>() : null;
        if (dispRend != null && dispRend.sharedMaterial != null)
        {
            origScreenMat = dispRend.sharedMaterial;
            tmpScreenMat = new Material(origScreenMat);
            dispRend.sharedMaterial = tmpScreenMat;
        }
        if (disp != null)
            disp.RenderTelemetry(4, 248, 0.84f, 0.82f, true, true, 3, 1, 93f);

        var follow = cam.GetComponent<ChaseCamera>();
        string outDir = @"C:\Users\USERAS\.gemini\antigravity\brain\9cf7120c-afb5-4b52-a65d-90ae4fdbb68f\scratch";
        Directory.CreateDirectory(outDir);

        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;

        void Snap(string fileName)
        {
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, fileName), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // 1. Cockpit View (hide F1_Cockpit_Driver only if desired, verify straight + 45 deg steered with gloves visible)
        var hidden = new List<Renderer>();
        foreach (var mr in player.GetComponentsInChildren<Renderer>(true))
            if (mr.name.StartsWith("F1_Cockpit_Driver") && mr.enabled) { mr.enabled = false; hidden.Add(mr); }

        Vector3 cockOff = follow != null ? follow.cockpitOffset : new Vector3(0f, 0.748f, 0.28f);
        float cockPitch = follow != null ? follow.cockpitPitch : 2.8f;
        cam.transform.position = player.transform.TransformPoint(cockOff);
        cam.transform.rotation = player.transform.rotation * Quaternion.Euler(cockPitch, 0f, 0f);
        cam.fieldOfView = 55f;
        Snap("unity_verify_cockpit.png");

        var swPivotSnap = FindDeep(player.transform, "SteeringWheel_Pivot");
        if (swPivotSnap != null)
        {
            Quaternion origSwRot = swPivotSnap.localRotation;
            swPivotSnap.localRotation = origSwRot * Quaternion.Euler(0f, 0f, 45f);
            Snap("unity_verify_cockpit_steered.png");
            swPivotSnap.localRotation = origSwRot;
        }
        foreach (var mr in hidden) mr.enabled = true;

        // 2. Chase View (rear ORBITAL DRS flap, exhaust, rain light, glossy reflections)
        cam.transform.position = player.transform.TransformPoint(new Vector3(0.4f, 1.55f, -4.6f));
        cam.transform.LookAt(player.transform.TransformPoint(new Vector3(0f, 0.58f, 0.5f)));
        cam.fieldOfView = 58f;
        Snap("unity_verify_chase.png");

        // 3. Front-Left Hero View (bright daytime lighting, 4K ORBITAL livery, glossy car + XPARROW Soft tyres)
        cam.transform.position = player.transform.TransformPoint(new Vector3(-3.2f, 1.32f, 3.85f));
        cam.transform.LookAt(player.transform.TransformPoint(new Vector3(0f, 0.48f, 0.1f)));
        cam.fieldOfView = 42f;
        Snap("unity_verify_front.png");

        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        if (dispRend != null && origScreenMat != null)
        {
            dispRend.sharedMaterial = origScreenMat;
            Object.DestroyImmediate(tmpScreenMat);
        }
        Debug.Log("Rendered 3 Unity URP verification screenshots cleanly to scratch/");
    }

    public static void VerifyBarrierContainment()
    {
        var sb = new System.Text.StringBuilder("BARRIER CONTAINMENT VERIFICATION:\n");
        bool allOk = true;
        foreach (var t in GameSession.Tracks)
        {
            string p = SceneDir + "/" + t.scene + ".unity";
            if (!File.Exists(p)) { allOk = false; sb.AppendLine($"  [FAIL] {t.id}: scene missing"); continue; }
            EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var rm = Object.FindFirstObjectByType<RaceManager>();
            RaceTrack tr = rm != null ? rm.track : null;
            if (tr == null || tr.length < 10f)
            {
                allOk = false;
                sb.AppendLine($"  [FAIL] {t.id}: missing RaceTrack");
                continue;
            }
            int steps = 0, missL = 0, missR = 0;
            for (float d = 0f; d < tr.length; d += 4f)
            {
                steps++;
                Vector3 pt = tr.PointAt(d);
                Vector3 right = tr.RightAt(d);
                float gy = SurfaceY(pt);
                for (int s = 0; s < 2; s++)
                {
                    Vector3 dir = s == 0 ? -right : right;
                    bool sideOk = true;
                    foreach (float h in new[] { 0.55f, 2.0f })
                    {
                        Vector3 origin = new Vector3(pt.x, gy + h, pt.z);
                        var hits = Physics.RaycastAll(origin, dir, 120f, ~0, QueryTriggerInteraction.Ignore);
                        bool wallHit = false;
                        for (int i = 0; i < hits.Length; i++)
                        {
                            var hit = hits[i];
                            if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                            if (hit.distance > 1.5f && Mathf.Abs(hit.normal.y) < 0.65f) { wallHit = true; break; }
                        }
                        if (!wallHit) { sideOk = false; break; }
                    }
                    if (!sideOk) { if (s == 0) missL++; else missR++; }
                }
            }
            bool ok = missL == 0 && missR == 0;
            if (!ok) allOk = false;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] BarrierContainment {t.id}: missL={missL}/{steps} missR={missR}/{steps}");
            if (!ok) Debug.LogError($"BarrierContainment FAILED on {t.id}: missL={missL}/{steps} missR={missR}/{steps}");
        }
        Debug.Log(sb.ToString());
        if (!allOk) Debug.LogError("VerifyBarrierContainment detected barrier gaps!");
    }

    public static void PitLaneVerifyTest()
    {
        var sb = new System.Text.StringBuilder("PIT LANE GARAGE & STOP/EXIT VERIFICATION:\n");
        bool allOk = true;
        foreach (var t in GameSession.Tracks)
        {
            string p = SceneDir + "/" + t.scene + ".unity";
            if (!File.Exists(p)) { allOk = false; sb.AppendLine($"  [FAIL] {t.id}: scene missing"); continue; }
            EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
            var rm = Object.FindFirstObjectByType<RaceManager>();
            if (rm == null || rm.boxDistances == null || rm.boxDistances.Length < 11 || rm.boxOffsets == null || rm.boxOffsets.Length < 11)
            {
                allOk = false;
                sb.AppendLine($"  [FAIL] {t.id}: missing boxDistances/boxOffsets");
                continue;
            }
            int grounded = 0;
            for (int i = 0; i < 11; i++)
            {
                Vector3 bp = rm.track.PointAt(rm.boxDistances[i]) + rm.track.RightAt(rm.boxDistances[i]) * rm.boxOffsets[i];
                if (RaceManager.GroundAt(bp, out _)) grounded++;
            }
            bool ok = grounded == 11;
            if (!ok) allOk = false;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {t.id}: 11 boxes aligned (side {rm.garageSide:+0;-0}, bay0={rm.boxDistances[0]:0}m@{rm.boxOffsets[0]:0.0}m, bay10={rm.boxDistances[10]:0}m@{rm.boxOffsets[10]:0.0}m, grounded={grounded}/11)");
        }

        // Full Pit Entry -> Red Bull Garage Stop + Wing Repair -> Pit Exit Simulation & Render on Baku + Monaco clearance test
        foreach (string verifyId in new[] { "Baku", "Monaco" })
        {
            string cPath = SceneDir + $"/Final Circuit {verifyId}.unity";
            EditorSceneManager.OpenScene(cPath, OpenSceneMode.Single);
            var manager = Object.FindFirstObjectByType<RaceManager>();
            var player = GameObject.Find("F1_Player");
            var f1 = player != null ? player.GetComponent<F1Car>() : null;
            var cam = Camera.main;
            if (manager != null && player != null && f1 != null && cam != null)
            {
                var lod = player.GetComponent<LODGroup>();
                if (lod != null) lod.ForceLOD(0);
                RaceManager.ApplyLivery(player, GameSession.Teams[1].primary, GameSession.Teams[1].secondary, Color.yellow, GameSession.Teams[1].livery);
                f1.Init();
                f1.ForceBreakFrontWingForTest();
                bool brokeWing = f1.FrontWingLost;

                float entryD = Mathf.Repeat(manager.pitEntryStart + 4f, manager.track.length);
                manager.BuildPitCrews();
                manager.StartTestPitStop(1, entryD, manager.PitLat(entryD), false);

                string outDir = @"C:\Users\USERAS\.gemini\antigravity\brain\9cf7120c-afb5-4b52-a65d-90ae4fdbb68f\scratch";
                Directory.CreateDirectory(outDir);
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                string snapPrefix = verifyId == "Monaco" ? "unity_verify_monaco_" : "unity_verify_";
                void Snap(string fn)
                {
                    if (rt == null) return;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, fn), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }

                bool pitStopSnapped = false, pitExitSnapped = false;
                int barrierHitsDuringPit = 0;
                var hitCols = new Collider[8];
                for (int step = 0; step < 2500 && manager.PlayerInPit; step++)
                {
                    manager.StepTestPitFixed(0.02f);

                    Vector3 probe = player.transform.position + Vector3.up * 0.65f;
                    int hc = Physics.OverlapSphereNonAlloc(probe, 0.85f, hitCols, ~0, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < hc; k++)
                    {
                        if (!hitCols[k].name.StartsWith("Barrier")) continue;
                        barrierHitsDuringPit++;
                        if (barrierHitsDuringPit <= 5)
                            Debug.Log($"[PitBarrier:{verifyId}] step={step} {manager.PitDebugState} col={hitCols[k].name} pos={player.transform.position}");
                    }

                    if (!pitStopSnapped && f1.SpeedKmh < 0.5f && step > 100)
                    {
                        pitStopSnapped = true;
                        var box = manager.pitBoxes[1];
                        box.PreviewServiceAt(1.45f, 4.7f, true);
                        Vector3 gVec = player.transform.right * RaceManager.PitCameraSide;
                        cam.transform.position = player.transform.position - player.transform.forward * 4.6f + gVec * 2.85f + Vector3.up * 2.35f;
                        cam.transform.LookAt(player.transform.position + Vector3.up * 0.62f + player.transform.forward * 0.45f);
                        cam.fieldOfView = 52f;
                        Snap(snapPrefix + (verifyId == "Monaco" ? "pitbox.png" : "pitstop.png"));
                    }

                    if (pitStopSnapped && !pitExitSnapped && f1.SpeedKmh > 68f && !f1.FrontWingLost)
                    {
                        pitExitSnapped = true;
                        cam.transform.position = player.transform.TransformPoint(new Vector3(0.8f, 2.2f, -6.2f));
                        cam.transform.LookAt(player.transform.TransformPoint(new Vector3(0f, 0.7f, 4.5f)));
                        cam.fieldOfView = 56f;
                        Snap(snapPrefix + "pitexit.png");
                    }
                }

                if (rt != null)
                {
                    cam.targetTexture = null;
                    RenderTexture.active = null;
                    Object.DestroyImmediate(rt);
                }

                var rb = player.GetComponent<Rigidbody>();
                bool handoffOk = !manager.PlayerInPit && !rb.isKinematic && !f1.FrontWingLost && brokeWing && f1.SpeedKmh > 80f && f1.Gear >= 2 && barrierHitsDuringPit == 0;
                if (!handoffOk) allOk = false;
                sb.AppendLine($"  [{(handoffOk ? "PASS" : "FAIL")}] {verifyId} full pit stop + wing repair + exit: broke={brokeWing}, repaired={!f1.FrontWingLost}, handoffSpeed={f1.SpeedKmh:0}km/h (gear {f1.Gear}), barrierHits={barrierHitsDuringPit}");
            }
            else { allOk = false; sb.AppendLine($"  [FAIL] Missing {verifyId} objects for PitLaneVerifyTest"); }
        }

        if (allOk) Debug.Log("PitLaneVerifyTest PASSED:\n" + sb);
        else Debug.LogError("PitLaneVerifyTest FAILED:\n" + sb);
    }

    static Material SparkMaterial()
    {
        EnsureFolder(GenDir);
        const string path = GenDir + "/Sparks.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
