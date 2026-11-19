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
    const string CarPrefab = "Assets/F1Assets/Car/F1_2026.prefab";
    const string LiveryDir = "Assets/Resources/F1Liveries";
    const string GenDir = "Assets/F1Assets/Generated";
    const string SceneDir = "Assets/Scenes";
    public const int Cars = 22;

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
        public float[] centre, pit, startFinish, pitEntry, pitExit, asphaltMin, asphaltMax;
        public DrsJson[] drs;
    }

    // ---------- Menu ----------

    [MenuItem("Tools/XPARROW/1 Build Car Liveries + Prefab")]
    public static void BuildCarAssets()
    {
        BuildLiveries();
        BuildCarPrefab();
    }

    [MenuItem("Tools/XPARROW/2 Build Circuit/Monaco")] static void BMonaco() => BuildCircuitScene("Monaco");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Baku")] static void BBaku() => BuildCircuitScene("Baku");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Monza")] static void BMonza() => BuildCircuitScene("Monza");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Spa")] static void BSpa() => BuildCircuitScene("Spa");
    [MenuItem("Tools/XPARROW/2 Build Circuit/Suzuka")] static void BSuzuka() => BuildCircuitScene("Suzuka");

    [MenuItem("Tools/XPARROW/3 Set Build Scenes (menu + 5 circuits)")]
    public static void SetBuildScenes()
    {
        var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(SceneDir + "/Main Menu.unity", true) };
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

    public static void BuildLiveries()
    {
        EnsureFolder(LiveryDir);
        EnsureFolder(GenDir);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        string texDir = "Assets/F1Assets/Car/Textures";
        var normal = LoadTex(texDir + "/shared/T_F1_2026_Normal.png");
        var ao = LoadTex(texDir + "/shared/T_F1_2026_AO.png");
        var emission = LoadTex(texDir + "/shared/T_F1_2026_Emission.png");
        for (int i = 0; i < LiveryNames.Length; i++)
        {
            string n = LiveryNames[i];
            var baseCol = LoadTex($"{texDir}/teams/T_{n}_BaseColor.png");
            string mgPath = $"{GenDir}/T_{n}_MetallicGloss.png";
            if (!File.Exists(mgPath))
            {
                var met = LoadTex($"{texDir}/teams/T_{n}_Metallic.png");
                var rough = LoadTex($"{texDir}/teams/T_{n}_Roughness.png");
                if (met != null && rough != null)
                {
                    var mp = met.GetPixels(); var rp = rough.GetPixels();
                    int w = met.width, h = met.height;
                    if (rough.width != w || rough.height != h) rp = Resize(rough, w, h);
                    var outp = new Color[mp.Length];
                    for (int k = 0; k < mp.Length; k++) outp[k] = new Color(mp[k].r, mp[k].r, mp[k].r, 1f - rp[k].r);
                    var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                    t.SetPixels(outp); t.Apply();
                    File.WriteAllBytes(mgPath, t.EncodeToPNG());
                    Object.DestroyImmediate(t);
                    AssetDatabase.ImportAsset(mgPath);
                    var ti = (TextureImporter)AssetImporter.GetAtPath(mgPath);
                    ti.sRGBTexture = false; ti.maxTextureSize = 2048; ti.SaveAndReimport();
                }
            }
            string matPath = $"{LiveryDir}/Livery_{i:00}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, matPath); }
            m.shader = lit;
            m.SetTexture("_BaseMap", baseCol);
            m.SetColor("_BaseColor", Color.white);
            var mg = LoadTex(mgPath);
            if (mg != null) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            if (ao != null) { m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (emission != null)
            {
                m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", Color.white * 1.5f);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Liveries built: " + LiveryNames.Length);
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
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(CarFbx);
        if (fbx == null) { Debug.LogError("Car FBX missing: " + CarFbx); return; }
        var root = new GameObject("F1_2026");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Body";
        model.transform.SetParent(root.transform, false);
        foreach (var g in model.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(g);

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

        // Wheel pivots in the layout F1Car / RaceAI expect: Wheels/WheelFL ... (spin about X, steer about Y)
        var wheels = new GameObject("Wheels").transform;
        wheels.SetParent(root.transform, false);
        string[] keys = { "FL", "FR", "RL", "RR" };
        var pivots = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            var spin = F("Spin_" + keys[i]);
            var w = new GameObject("Wheel" + keys[i]).transform;
            w.SetParent(wheels, false);
            w.position = C(keys[i]);
            w.rotation = root.transform.rotation;
            pivots[i] = w.localPosition;
            var move = new List<Transform>();
            foreach (Transform c in spin) move.Add(c);
            var steer = F("Steer_" + keys[i]);
            if (steer != null) foreach (Transform c in steer) if (c != spin) move.Add(c);
            foreach (var c in move) c.SetParent(w, true);
        }
        foreach (var k in keys) { var s = F("Steer_" + k); if (s != null) Object.DestroyImmediate(s.gameObject); }
        foreach (var k in keys) { var s = F("Spin_" + k); if (s != null) Object.DestroyImmediate(s.gameObject); }

        // Body collider from the pack's collision hull
        var hull = F("Collider_Body");
        var box = root.AddComponent<BoxCollider>();
        if (hull != null && hull.GetComponent<Renderer>() != null)
        {
            var b = hull.GetComponent<Renderer>().bounds;
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = b.size;
            Object.DestroyImmediate(hull.gameObject);
        }
        else { box.center = new Vector3(0, 0.5f, 0); box.size = new Vector3(1.9f, 0.95f, 5.4f); }
        // Keep the box off the ground so the floor never scrapes on kerbs
        float bottom = box.center.y - box.size.y / 2f;
        if (bottom < 0.06f) { box.size -= new Vector3(0, 0.06f - bottom, 0); box.center += new Vector3(0, (0.06f - bottom) / 2f, 0); }

        // One livery material for every part (swapped per team at race start), LOD groups by suffix
        var livery = AssetDatabase.LoadAssetAtPath<Material>(LiveryDir + "/Livery_00.mat");
        var lods = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (livery != null) r.sharedMaterial = livery;
            r.shadowCastingMode = ShadowCastingMode.On;
            for (int l = 0; l < 3; l++) if (r.name.EndsWith("_LOD" + l)) lods[l].Add(r);
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

        string boxInfo = box.size.ToString("F2");
        PrefabUtility.SaveAsPrefabAsset(root, CarPrefab);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"Car prefab built: front axle {info.frontAxleZ:0.00}, rear {info.rearAxleZ:0.00}, track {info.frontTrack:0.00}/{info.rearTrack:0.00}, " +
                  $"tyre radius {radius:0.000}, collider {boxInfo}, LODs {lods[0].Count}/{lods[1].Count}/{lods[2].Count}");
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
        var asphalt = FindDeep(map.transform, "Col_Track_Asphalt");
        // The pack's meshes are already Y-up; the importer leaves an extra axis rotation on each object.
        // Cancel it at the root so the circuit lies flat (asphalt ends up with no rotation).
        if (asphalt != null)
        {
            map.transform.rotation = Quaternion.Inverse(asphalt.rotation) * map.transform.rotation;
            foreach (var mc in map.GetComponentsInChildren<MeshCollider>(true)) { var m0 = mc.sharedMesh; mc.sharedMesh = null; mc.sharedMesh = m0; }
        }
        Physics.SyncTransforms();
        Bounds ub = BoundsOf(asphalt != null ? asphalt.gameObject : map);
        if (ub.size.y > ub.size.x * 0.5f) Debug.LogWarning(id + ": circuit still not flat after axis fix, bounds " + ub.size);
        float err = Calibrate(data, ub);

        // Centreline
        track = root.AddComponent<RaceTrack>();
        var raw = ToPoints(data.centre);
        bool? cw = Clockwise.TryGetValue(id, out var v) ? v : null;
        if (cw.HasValue && IsClockwiseBlender(data.centre) != cw.Value) raw.Reverse();
        var pts = Resample(raw, 2f, true);
        // Start the lap at the start/finish line
        Vector3 sf = data.startFinish != null && data.startFinish.Length == 3 ? B2U(data.startFinish, 0) : pts[0];
        int start = Nearest(pts, sf);
        var rot = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++) rot.Add(pts[(start + i) % pts.Count]);
        track.points = rot.ToArray();
        track.spacing = 2f;
        track.length = track.spacing * track.points.Length;
        track.halfWidth = id == "Monaco" ? 4.6f : Mathf.Clamp(data.halfWidth, 4.5f, 8f);
        track.trackName = info != null ? info.name : id;

        int verge = BuildVerges(root, map, id);
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
        manager.ais = BuildRivals(root, manager);

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
        string scenePath = SceneDir + "/" + (info != null ? info.scene : "Circuit " + id) + ".unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AddSceneToBuild(scenePath, -1);
        AssetDatabase.SaveAssets();
        Debug.Log($"{id} built: lap {track.length:0} m ({track.points.Length} pts, {data.centreSource}), half width {track.halfWidth:0.0}, " +
                  $"axis fit error {err:0.00} m, {colliders} colliders, embankment fill {verge} m², DRS zones {manager.drsZones.Length}, {pitNote}. Saved {scenePath}");
    }

    // The pack's roads sit on embankments: off the edge the terrain can be 3-4 m lower, so a car that runs wide
    // drops off a ledge. Fill both sides with a grass slope (1:3) from the road edge down to whatever surface is
    // there. Where the existing run-off / gravel / grass is already high enough the fill stays just below it (hidden).
    static int BuildVerges(GameObject root, GameObject map, string id)
    {
        float[] offs = id == "Monaco" ? new[] { 0f, 1f, 2.5f, 4f, 6f } : new[] { 0f, 1.5f, 3f, 5f, 8f, 12f, 17f, 24f, 32f };
        float edge = track.halfWidth + 1.2f, slope = 1f / 3f, step = 4f;
        var grassPhys = SurfaceMat("Grass", 0.6f);
        Material grassMat = null;
        foreach (var r in map.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && (m.name.Contains("Terrain_Grass") || m.name.Contains("Verge_Grass") || m.name.Contains("Lawn"))) { grassMat = m; break; }
        if (grassMat == null) grassMat = Mat("Verge_Grass", new Color(0.28f, 0.42f, 0.2f), 0f, 0.08f);
        var hits = new RaycastHit[16];
        int n = Mathf.CeilToInt(track.length / step);
        float area = 0f;
        var parent = new GameObject("Embankments");
        parent.transform.SetParent(root.transform, false);
        foreach (float side in new[] { -1f, 1f })
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int cols = offs.Length;
            for (int i = 0; i <= n; i++)
            {
                float d = i * step;
                Vector3 c = track.PointAt(d), right = track.RightAt(d);
                float roadY = c.y;
                for (int k = 0; k < cols; k++)
                {
                    Vector3 p = c + right * side * (edge + offs[k]);
                    float fill = roadY - 0.08f - offs[k] * slope;
                    // Highest ground-like surface near road level (ignore walls, roofs, cars)
                    int hc = Physics.RaycastNonAlloc(new Vector3(p.x, roadY + 1.5f, p.z), Vector3.down, hits, 60f, ~0, QueryTriggerInteraction.Ignore);
                    float top = float.MinValue;
                    for (int h = 0; h < hc; h++)
                    {
                        var hit = hits[h];
                        if (hit.collider.attachedRigidbody != null || hit.normal.y < 0.6f || hit.collider.name.StartsWith("Barrier")) continue;
                        if (hit.collider.transform.IsChildOf(parent.transform)) continue;
                        top = Mathf.Max(top, hit.point.y);
                    }
                    float y = top > fill ? top - 0.06f : fill;
                    if (top > fill && k > 0) area += 0f; else if (k > 0) area += step * (offs[k] - offs[k - 1]);
                    verts.Add(new Vector3(p.x, y, p.z));
                    uvs.Add(new Vector2((edge + offs[k]) / 5f, d / 5f));
                }
            }
            for (int i = 0; i < n; i++)
                for (int k = 0; k < cols - 1; k++)
                {
                    int a = i * cols + k, b = a + 1, c2 = a + cols, d2 = c2 + 1;
                    if (side > 0) { tris.Add(a); tris.Add(c2); tris.Add(b); tris.Add(b); tris.Add(c2); tris.Add(d2); }
                    else { tris.Add(a); tris.Add(b); tris.Add(c2); tris.Add(b); tris.Add(d2); tris.Add(c2); }
                }
            var mesh = new UnityEngine.Mesh { name = id + "_Embankment_" + (side < 0 ? "L" : "R"), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string meshPath = GenDir + "/" + id + "/" + mesh.name + ".asset";
            EnsureFolder(GenDir + "/" + id);
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            var go = new GameObject("Embankment_" + (side < 0 ? "L" : "R"));
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = grassMat;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.sharedMaterial = grassPhys;
        }
        Physics.SyncTransforms();
        return Mathf.RoundToInt(area);
    }

    // Colliders on the Col_* surfaces, helper objects hidden, imported lights/cameras removed
    static int SetupMap(GameObject map)
    {
        int count = 0;
        var asphalt = SurfaceMat("Asphalt", 1f);
        var kerb = SurfaceMat("Kerb", 0.9f);
        var grass = SurfaceMat("Grass", 0.6f);
        var gravel = SurfaceMat("Gravel", 0.5f);
        var wall = SurfaceMat("Barrier", 0.2f);
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
                || n.StartsWith("Cam_") || n.StartsWith("GRP_Cameras") || n.StartsWith("GRP_Lighting"))
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
            bool hasTex = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null;
            if (n.Contains("Asphalt")) { if (!hasTex) tex = T("Track_Asphalt_Dark_2k", "Asphalt_Albedo"); smooth = 0.22f; }
            else if (n.Contains("Gravel")) { tex = T("Gravel_Albedo", "Concrete_PBR_Albedo"); tint = new Color(0.86f, 0.78f, 0.62f); smooth = 0.05f; }
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
            if (tex == null && smooth < 0f && metal < 0f) { cache[src] = src; return src; }
            string path = outDir + "/" + n.Replace("/", "_") + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(src);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                var c = tint ?? (src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white);
                // Keep the authored colour as a gentle tint over the texture
                m.SetColor("_BaseColor", Color.Lerp(Color.white, c, 0.5f));
            }
            if (smooth >= 0f) m.SetFloat("_Smoothness", smooth);
            if (metal >= 0f) m.SetFloat("_Metallic", metal);
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
        m.bounciness = 0f; m.bounceCombine = PhysicsMaterialCombine.Minimum;
        return m;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
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

    // Catmull-Rom densify, then resample at a fixed arc-length spacing
    static List<Vector3> Resample(List<Vector3> src, float step, bool closed)
    {
        var dense = new List<Vector3>();
        int n = src.Count;
        for (int i = 0; i < (closed ? n : n - 1); i++)
        {
            Vector3 p0 = src[closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)], p1 = src[i], p2 = src[(i + 1) % n], p3 = src[closed ? (i + 2) % n : Mathf.Min(i + 2, n - 1)];
            int sub = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(p1, p2) / 0.5f), 1, 64);
            for (int k = 0; k < sub; k++)
            {
                float t = k / (float)sub, t2 = t * t, t3 = t2 * t;
                dense.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        if (closed) dense.Add(dense[0]); else dense.Add(src[n - 1]);
        float total = 0f;
        for (int i = 1; i < dense.Count; i++) total += Vector3.Distance(dense[i - 1], dense[i]);
        int count = Mathf.Max(2, Mathf.RoundToInt(total / step));
        float ds = total / count;
        var outp = new List<Vector3> { dense[0] };
        float acc = 0f, next = ds; int idx = 1;
        while (outp.Count < (closed ? count : count + 1) && idx < dense.Count)
        {
            float seg = Vector3.Distance(dense[idx - 1], dense[idx]);
            if (acc + seg >= next) { outp.Add(Vector3.Lerp(dense[idx - 1], dense[idx], (next - acc) / Mathf.Max(seg, 1e-5f))); next += ds; }
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
        }        // Side and typical offset from the middle half of the lane
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
        // Team boxes spread along the straight part of the lane
        float a0 = (inIdx * 2f) + 25f, a1 = (outIdx * 2f) - 25f;
        if (a1 < a0) { a0 = inIdx * 2f; a1 = outIdx * 2f; }
        int teams = GameSession.Teams.Length;
        m.boxDistances = new float[teams];
        for (int t = 0; t < teams; t++) m.boxDistances[t] = Mathf.Repeat(startD + Mathf.Lerp(a0, a1, (t + 0.5f) / teams), L);
        return $"pit lane {span:0} m, offset {off:0.0} m, entry {m.pitEntryStart:0}-{m.pitEntryEnd:0}, exit {m.pitExitStart:0}-{m.pitExitEnd:0}";
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
        sun.intensity = 1.6f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
        RenderSettings.sun = sun;

        var sky = AssetDatabase.LoadAssetAtPath<Material>(GenDir + "/SkyPanorama.mat");
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(sky, GenDir + "/SkyPanorama.mat"); }
        sky.shader = Shader.Find("Skybox/Panoramic");
        sky.SetTexture("_MainTex", SkyTexture(-sun.transform.forward));
        sky.SetFloat("_Mapping", 1f);
        sky.SetFloat("_ImageType", 0f);
        sky.SetFloat("_Exposure", 1.05f);
        sky.SetFloat("_Rotation", 0f);
        EditorUtility.SetDirty(sky);        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.05f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.68f, 0.76f, 0.86f);
        RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.9f);
        RenderSettings.fogStartDistance = 250f;
        RenderSettings.fogEndDistance = 3200f;
        DynamicGI.UpdateEnvironment();

        // Post-processing: filmic tonemapping, gentle bloom, a little warmth and contrast
        var volGo = new GameObject("PostProcess");
        volGo.transform.SetParent(root.transform, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        string profPath = GenDir + "/CircuitPost.asset";
        var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
        if (prof == null)
        {
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, profPath);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bl = prof.Add<Bloom>(true); bl.intensity.Override(0.35f); bl.threshold.Override(1.1f); bl.scatter.Override(0.6f);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.15f); ca.contrast.Override(12f); ca.saturation.Override(8f);
            var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(6f);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.2f); vg.smoothness.Override(0.45f);
            foreach (var c in prof.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, prof); }
            AssetDatabase.SaveAssets();
        }
        vol.sharedProfile = prof;
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
        RaceTrackBuilder.CreateSparks(car);
        car.AddComponent<CarHUD>().car = f1;
        var audio = new GameObject("EngineAudio");
        audio.transform.SetParent(car.transform, false);
        audio.AddComponent<AudioSource>();
        audio.AddComponent<EngineAudio>().car = f1;
        return car;
    }

    static RaceAI[] BuildRivals(GameObject root, RaceManager m)
    {
        var parent = new GameObject("Field");
        parent.transform.SetParent(root.transform, false);
        var list = new RaceAI[Cars - 1];
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
            car.GetComponent<BoxCollider>().isTrigger = true;   // contact with the player is resolved by F1Car
            var ai = car.AddComponent<RaceAI>();
            ai.driverName = "Driver " + k;
            ai.maxSpeed = 88f;
            ai.cornerGrip = 17f;
            ai.aeroGrip = 0.0055f;
            ai.acceleration = 11f;
            ai.braking = 14f;
            ai.aeroBraking = 0.0035f;
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
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 4000f;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var follow = go.AddComponent<ChaseCamera>();
        follow.target = player.transform;
        // TV pod above the airbox, cockpit at the driver's eyes (2026 car proportions)
        var geo = player.GetComponent<CarGeometry>();
        float headZ = geo != null ? geo.headZ : -0.2f, headY = geo != null ? geo.headY : 0.95f;
        follow.tvPodOffset = new Vector3(0, headY + 0.42f, headZ - 0.2f);
        follow.tvPodPitch = 8f;
        follow.cockpitOffset = new Vector3(0, headY + 0.03f, headZ + 0.12f);
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
}
