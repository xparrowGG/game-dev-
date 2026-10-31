using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

// Tools > Build Race Track: replaces the city with a ~700 m circuit (kerbs, gravel traps,
// barriers, start gantry with lights, grid, grandstand, pit building), puts the SportsSedan
// on the grid with 4 AI rivals and sets up the RaceManager.
public static class RaceTrackBuilder
{
    const string Dir = "Assets/Race";
    const float HalfWidth = 7f, KerbWidth = 1.2f, WallOffset = 12f, WallThickness = 0.6f, TargetSpacing = 2f;
    const float TrackScale = 0.7f;   // shrinks the whole circuit (1 = ~690 m lap)
    static readonly Vector3 StartLinePos = new Vector3(0, 0, -80f * TrackScale);

    // Circuit shape (x, z) before scaling, driven in this order. The start straight runs along z = -80.
    static readonly Vector2[] Ctrl =
    {
        new Vector2(-60, -80), new Vector2(40, -80), new Vector2(100, -70), new Vector2(125, -25),
        new Vector2(110, 25), new Vector2(70, 40), new Vector2(40, 75), new Vector2(-10, 95),
        new Vector2(-60, 80), new Vector2(-75, 35), new Vector2(-110, 10), new Vector2(-120, -40),
        new Vector2(-100, -75)
    };

    static readonly (string name, Color color, float speed, float grip)[] Rivals =
    {
        ("A. Moreau",    new Color(0.85f, 0.1f, 0.1f),  54f, 33f),
        ("K. Lindqvist", new Color(0.95f, 0.8f, 0.1f),  52f, 31f),
        ("D. Okafor",    new Color(0.1f, 0.65f, 0.3f),  51f, 30f),
        ("S. Nakamura",  new Color(1f, 0.5f, 0.05f),    50f, 29f),
    };

    static readonly Color[] RivalAccents = { Color.white, new Color(0.08f, 0.08f, 0.1f), Color.white, new Color(0.1f, 0.15f, 0.35f) };
    static readonly Color[] RivalHelmets = { Color.white, new Color(0.9f, 0.2f, 0.2f), new Color(1f, 0.85f, 0.1f), Color.white };
    static readonly Color[] RivalStripes = { new Color(0.9f, 0.1f, 0.1f), new Color(1f, 0.85f, 0.1f), Color.white, new Color(0.9f, 0.1f, 0.1f) };

    static Shader shader;
    static System.Random rng;
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("Tools/Build Race Track")]
    public static void Build()
    {
        rng = new System.Random(77);
        mats.Clear();
        foreach (var f in new[] { "Materials", "Meshes" })
            if (!AssetDatabase.IsValidFolder(Dir + "/" + f)) AssetDatabase.CreateFolder(Dir, f);
        shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        foreach (var name in new[] { "City", "RaceTrack" })
        {
            var old = GameObject.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }

        var root = new GameObject("RaceTrack");
        var track = root.AddComponent<RaceTrack>();
        track.points = SampleCentreline(out float spacing);
        track.spacing = spacing;
        track.length = spacing * track.points.Length;
        track.halfWidth = HalfWidth;

        Box("Ground", root, new Vector3(0, -0.5f, 0), Quaternion.identity, new Vector3(600, 1, 600), Mat("Grass", new Color(0.3f, 0.5f, 0.22f)), true);
        BuildSurfaces(root, track);
        BuildBarriers(root, track);
        BuildBounds(root);
        BuildStartLine(root, track);
        var lights = BuildGantry(root, track, out var lightOff);
        BuildGrandstand(root, track);
        BuildPits(root, track);
        BuildTrees(root, track);

        var player = SetupPlayer(track);
        var ais = BuildRivals(root, track, player);

        var manager = new GameObject("RaceManager").AddComponent<RaceManager>();
        manager.transform.SetParent(root.transform, false);
        manager.track = track;
        manager.laps = 1;
        manager.player = player.GetComponent<CarController>();
        manager.ais = ais;
        manager.startLights = lights;
        manager.lightOff = lightOff;
        manager.lightOn = Mat("StartLightOn", new Color(1f, 0.05f, 0.03f), 0f, 0.8f, new Color(1f, 0.05f, 0.02f) * 4f);

        SetupCamera(player);
        Undo.RegisterCreatedObjectUndo(root, "Build Race Track");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AddActiveSceneToBuild();

        float minR = float.MaxValue;
        for (float d = 0; d < track.length; d += 2f) minR = Mathf.Min(minR, track.RadiusAt(d));
        Debug.Log($"Race track built: {track.length:0} m lap, tightest corner radius {minR:0} m.");
    }

    // ---------- Centreline ----------

    static Vector3[] SampleCentreline(out float spacing)
    {
        int n = Ctrl.Length;
        var dense = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = Ctrl[(i - 1 + n) % n] * TrackScale, p1 = Ctrl[i] * TrackScale,
                    p2 = Ctrl[(i + 1) % n] * TrackScale, p3 = Ctrl[(i + 2) % n] * TrackScale;
            for (int k = 0; k < 80; k++)
            {
                float t = k / 80f, t2 = t * t, t3 = t2 * t;
                Vector2 p = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                dense.Add(new Vector3(p.x, 0, p.y));
            }
        }
        var cum = new float[dense.Count + 1];
        for (int i = 0; i < dense.Count; i++) cum[i + 1] = cum[i] + Vector3.Distance(dense[i], dense[(i + 1) % dense.Count]);
        float total = cum[dense.Count];
        int count = Mathf.RoundToInt(total / TargetSpacing);
        spacing = total / count;

        var pts = new Vector3[count];
        int seg = 0;
        for (int i = 0; i < count; i++)
        {
            float d = i * spacing;
            while (cum[seg + 1] < d) seg++;
            float t = (d - cum[seg]) / (cum[seg + 1] - cum[seg]);
            pts[i] = Vector3.Lerp(dense[seg], dense[(seg + 1) % dense.Count], t);
        }

        // Rotate so index 0 is the start/finish line
        int start = 0; float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            float dd = (pts[i] - StartLinePos).sqrMagnitude;
            if (dd < best) { best = dd; start = i; }
        }
        var rotated = new Vector3[count];
        for (int i = 0; i < count; i++) rotated[i] = pts[(i + start) % count];
        return rotated;
    }

    // ---------- Track surfaces ----------

    static void BuildSurfaces(GameObject root, RaceTrack t)
    {
        var parent = Child("Surface", root);
        Func<int, bool> all = i => true;
        Func<int, bool> corner = i => t.RadiusAt(i * t.spacing) < 110f;
        Func<int, float> turn = i => Vector3.Cross(t.TangentAt(i * t.spacing - 6f), t.TangentAt(i * t.spacing + 6f)).y;

        MeshObj("Asphalt", parent, t, new[] { new Vector2(-HalfWidth, 0.03f), new Vector2(HalfWidth, 0.03f) }, all,
            Mat("TrackAsphalt", new Color(0.2f, 0.2f, 0.22f), 0f, 0.3f), false);
        var white = Mat("LineWhite", new Color(0.95f, 0.95f, 0.95f), 0f, 0.3f);
        MeshObj("EdgeLineL", parent, t, new[] { new Vector2(-6.8f, 0.04f), new Vector2(-6.5f, 0.04f) }, all, white, false);
        MeshObj("EdgeLineR", parent, t, new[] { new Vector2(6.5f, 0.04f), new Vector2(6.8f, 0.04f) }, all, white, false);

        // Red/white kerbs on corners, both sides
        var red = Mat("KerbRed", new Color(0.85f, 0.08f, 0.08f), 0f, 0.4f);
        var kerbW = Mat("KerbWhite", new Color(0.95f, 0.95f, 0.95f), 0f, 0.4f);
        var kerbR = new[] { new Vector2(HalfWidth, 0.04f), new Vector2(HalfWidth + KerbWidth, 0.08f) };
        var kerbL = new[] { new Vector2(-HalfWidth - KerbWidth, 0.08f), new Vector2(-HalfWidth, 0.04f) };
        MeshObj("KerbRedR", parent, t, kerbR, i => corner(i) && (i / 2) % 2 == 0, red, false);
        MeshObj("KerbWhiteR", parent, t, kerbR, i => corner(i) && (i / 2) % 2 == 1, kerbW, false);
        MeshObj("KerbRedL", parent, t, kerbL, i => corner(i) && (i / 2) % 2 == 0, red, false);
        MeshObj("KerbWhiteL", parent, t, kerbL, i => corner(i) && (i / 2) % 2 == 1, kerbW, false);

        // Gravel traps on the outside of corners
        var gravel = Mat("Gravel", new Color(0.78f, 0.7f, 0.52f));
        float g0 = HalfWidth + KerbWidth, g1 = WallOffset - 0.1f;
        MeshObj("GravelR", parent, t, new[] { new Vector2(g0, 0.02f), new Vector2(g1, 0.02f) }, i => corner(i) && turn(i) < 0, gravel, false);
        MeshObj("GravelL", parent, t, new[] { new Vector2(-g1, 0.02f), new Vector2(-g0, 0.02f) }, i => corner(i) && turn(i) > 0, gravel, false);
    }

    // Invisible walls at the edge of the map; inside them you can drive anywhere.
    static void BuildBounds(GameObject root)
    {
        var parent = Child("MapBounds", root);
        const float e = 285f;
        foreach (var (pos, size) in new[] {
            (new Vector3(0, 3, e), new Vector3(2 * e, 6, 1)), (new Vector3(0, 3, -e), new Vector3(2 * e, 6, 1)),
            (new Vector3(e, 3, 0), new Vector3(1, 6, 2 * e)), (new Vector3(-e, 3, 0), new Vector3(1, 6, 2 * e)) })
        {
            var w = Child("Wall", parent);
            w.transform.localPosition = pos;
            w.AddComponent<BoxCollider>().size = size;
        }
    }

    // RaceManager restarts by reloading the scene, which needs it in the build list.
    static void AddActiveSceneToBuild()
    {
        string path = EditorSceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(path)) return;
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // Red/white barrier walls on both sides of the circuit (solid mesh colliders).
    static void BuildBarriers(GameObject root, RaceTrack t)
    {
        var parent = Child("Barriers", root);
        float a = WallOffset, b = WallOffset + WallThickness, h = 1.1f;
        var right = new[] { new Vector2(a, 0), new Vector2(a, h), new Vector2(b, h), new Vector2(b, 0) };
        var left = new[] { new Vector2(-b, 0), new Vector2(-b, h), new Vector2(-a, h), new Vector2(-a, 0) };
        var red = Mat("BarrierRed", new Color(0.8f, 0.1f, 0.1f), 0f, 0.35f);
        var white = Mat("BarrierWhite", new Color(0.92f, 0.92f, 0.92f), 0f, 0.35f);
        foreach (var (name, prof) in new[] { ("R", right), ("L", left) })
        {
            MeshObj("Barrier" + name + "_Red", parent, t, prof, i => (i / 5) % 2 == 0, red, false);
            MeshObj("Barrier" + name + "_White", parent, t, prof, i => (i / 5) % 2 == 1, white, false);
            var col = MeshObj("Barrier" + name + "_Collider", parent, t, prof, i => true, null, true);
            Object.DestroyImmediate(col.GetComponent<MeshRenderer>());
        }
    }

    static void BuildStartLine(GameObject root, RaceTrack t)
    {
        var parent = Child("StartFinish", root);
        var black = Mat("CheckBlack", new Color(0.05f, 0.05f, 0.05f));
        var white = Mat("LineWhite", Color.white);
        Vector3 p = t.PointAt(0), fwd = t.TangentAt(0), right = t.RightAt(0);
        var rot = Quaternion.LookRotation(fwd);
        for (int row = 0; row < 2; row++)
            for (int c = 0; c < 14; c++)
            {
                Vector3 pos = p + right * (-6.5f + c) + fwd * (row - 0.5f) + Vector3.up * 0.045f;
                Box("Check", parent, pos, rot, new Vector3(1f, 0.01f, 1f), (row + c) % 2 == 0 ? black : white);
            }
        // Grid boxes
        for (int k = 0; k < 5; k++)
        {
            float d = -(10f + 8f * k) + 2.6f;
            float lat = k % 2 == 0 ? -3f : 3f;
            Box("GridSlot" + (k + 1), parent, t.PointAt(d) + t.RightAt(d) * lat + Vector3.up * 0.045f, Quaternion.LookRotation(t.TangentAt(d)),
                new Vector3(2.6f, 0.01f, 0.25f), white);
        }
    }

    static Renderer[] BuildGantry(GameObject root, RaceTrack t, out Material lightOff)
    {
        var g = Child("StartGantry", root);
        g.transform.SetPositionAndRotation(t.PointAt(0), Quaternion.LookRotation(t.TangentAt(0)));
        var steel = Mat("GantrySteel", new Color(0.25f, 0.27f, 0.3f), 0.6f, 0.5f);
        float x = WallOffset + 1.2f;
        Box("PillarL", g, new Vector3(-x, 3.5f, 0), Quaternion.identity, new Vector3(0.6f, 7f, 0.6f), steel, true);
        Box("PillarR", g, new Vector3(x, 3.5f, 0), Quaternion.identity, new Vector3(0.6f, 7f, 0.6f), steel, true);
        Box("Beam", g, new Vector3(0, 6.8f, 0), Quaternion.identity, new Vector3(2 * x + 0.6f, 1.2f, 0.8f), steel);
        Box("LightPanel", g, new Vector3(0, 5.6f, -0.2f), Quaternion.identity, new Vector3(7.8f, 1.3f, 0.5f), Mat("LightPanel", new Color(0.05f, 0.05f, 0.05f)));

        lightOff = Mat("StartLightOff", new Color(0.18f, 0.03f, 0.03f), 0f, 0.8f);
        var lights = new Renderer[5];
        for (int i = 0; i < 5; i++)
        {
            var l = Prim(PrimitiveType.Sphere, "StartLight" + (i + 1), g, new Vector3(-2.8f + i * 1.4f, 5.6f, -0.5f), Quaternion.identity, Vector3.one * 0.8f, lightOff, false, false);
            lights[i] = l.GetComponent<Renderer>();
        }
        return lights;
    }

    static void BuildGrandstand(GameObject root, RaceTrack t)
    {
        var g = Child("Grandstand", root);
        g.transform.SetPositionAndRotation(t.PointAt(0) + t.RightAt(0) * 15f, Quaternion.LookRotation(t.TangentAt(0)));
        var concrete = Mat("Concrete", new Color(0.7f, 0.7f, 0.68f));
        var roof = Mat("StandRoof", new Color(0.12f, 0.3f, 0.6f), 0.3f, 0.6f);
        Color[] shirts = { Color.red, Color.white, new Color(0.1f, 0.3f, 0.9f), Color.yellow, new Color(0.1f, 0.6f, 0.2f),
                           new Color(1f, 0.5f, 0f), new Color(0.1f, 0.1f, 0.1f), new Color(0.6f, 0.2f, 0.7f) };
        const int tiers = 7;
        const float len = 60f;
        for (int k = 0; k < tiers; k++)
        {
            float h = 0.6f + k * 0.8f;
            Box("Tier" + k, g, new Vector3(2f + k * 1.2f, h / 2f, 0), Quaternion.identity, new Vector3(1.2f, h, len), concrete, k == 0);
            for (float z = -len / 2 + 1f; z < len / 2 - 1f; z += 1.1f)
            {
                if (rng.NextDouble() < 0.3) continue;
                int c = rng.Next(shirts.Length);
                Box("Fan", g, new Vector3(2f + k * 1.2f, h + 0.35f, z), Quaternion.identity, new Vector3(0.45f, 0.7f, 0.45f), Mat("Fan" + c, shirts[c]));
            }
        }
        float back = 2f + tiers * 1.2f;
        for (float z = -len / 2; z <= len / 2; z += 15f)
            Box("RoofPost", g, new Vector3(back, 4.5f, z), Quaternion.identity, new Vector3(0.4f, 9f, 0.4f), concrete);
        Box("Roof", g, new Vector3(back / 2f + 0.8f, 9.1f, 0), Quaternion.Euler(0, 0, -6f), new Vector3(back + 2f, 0.25f, len + 2f), roof);
        Box("BackWall", g, new Vector3(back + 0.3f, 3f, 0), Quaternion.identity, new Vector3(0.3f, 6f, len), concrete);
    }

    static void BuildPits(GameObject root, RaceTrack t)
    {
        var g = Child("PitBuilding", root);
        float d = -5f;
        g.transform.SetPositionAndRotation(t.PointAt(d) + t.RightAt(d) * -(WallOffset + 9f), Quaternion.LookRotation(t.TangentAt(d)));
        var wall = Mat("PitWall", new Color(0.88f, 0.88f, 0.9f), 0.1f, 0.5f);
        var door = Mat("GarageDoor", new Color(0.15f, 0.16f, 0.2f), 0.4f, 0.6f);
        var glass = Mat("PitGlass", new Color(0.15f, 0.3f, 0.45f), 0.6f, 0.9f);
        Box("Building", g, new Vector3(-4f, 3f, 0), Quaternion.identity, new Vector3(8f, 6f, 48f), wall, true);
        Box("Windows", g, new Vector3(-4f, 4.9f, 0), Quaternion.identity, new Vector3(8.05f, 1.1f, 44f), glass);
        for (float z = -19f; z <= 19f; z += 7.6f)
            Box("Garage", g, new Vector3(0.03f, 1.8f, z), Quaternion.identity, new Vector3(0.1f, 3.4f, 6f), door);
        Box("PitLane", g, new Vector3(4.3f, 0.02f, 0), Quaternion.identity, new Vector3(8.6f, 0.04f, 48f), Mat("TrackAsphalt", Color.gray));
        Box("ControlTower", g, new Vector3(-4f, 8f, 27f), Quaternion.identity, new Vector3(6f, 16f, 6f), wall, true);
        Box("TowerGlass", g, new Vector3(-4f, 14f, 27f), Quaternion.identity, new Vector3(6.1f, 2.5f, 6.1f), glass);
        Color[] banners = { new Color(0.85f, 0.1f, 0.1f), new Color(0.1f, 0.3f, 0.8f), new Color(0.95f, 0.75f, 0.1f), new Color(0.1f, 0.6f, 0.3f) };
        for (int i = 0; i < 6; i++)
            Box("Banner", g, new Vector3(0.1f, 6.6f, -19f + i * 7.6f), Quaternion.identity, new Vector3(0.1f, 1.2f, 6f), Mat("Banner" + i % 4, banners[i % 4]));
    }

    static void BuildTrees(GameObject root, RaceTrack t)
    {
        var parent = Child("Trees", root);
        var bark = Mat("Bark", new Color(0.36f, 0.25f, 0.16f));
        Color[] leaves = { new Color(0.18f, 0.42f, 0.16f), new Color(0.25f, 0.5f, 0.2f), new Color(0.14f, 0.35f, 0.18f) };
        int placed = 0, tries = 0;
        while (placed < 170 && tries++ < 5000)
        {
            float x = R(-240f, 240f), z = R(-240f, 240f);
            var p = new Vector3(x, 0, z);
            Vector3 c = t.PointAt(t.ClosestDistance(p)); c.y = 0;
            if (Vector3.Distance(p, c) < 26f) continue;
            Vector3 s = StartLinePos;
            if (Mathf.Abs(x - s.x) < 50f && Mathf.Abs(z - s.z) < 45f) continue;   // keep grandstand and pits clear
            var tree = Child("Tree", parent);
            tree.transform.position = p;
            float size = R(3f, 5.5f);
            int li = rng.Next(leaves.Length);
            Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0, 1.5f, 0), Quaternion.identity, new Vector3(0.45f, 1.5f, 0.45f), bark, true, true);
            Prim(PrimitiveType.Sphere, "Canopy", tree, new Vector3(0, 3f + size * 0.4f, 0), Quaternion.identity, Vector3.one * size, Mat("Leaves" + li, leaves[li]), false, true);
            placed++;
        }
    }

    // ---------- Cars ----------

    static (Vector3 pos, Quaternion rot, float d, float lat) GridSlot(RaceTrack t, int k)
    {
        float d = -(10f + 8f * k);
        float lat = k % 2 == 0 ? -3f : 3f;
        return (t.PointAt(d) + t.RightAt(d) * lat + Vector3.up * 0.02f, Quaternion.LookRotation(t.TangentAt(d)), d, lat);
    }

    static GameObject SetupPlayer(RaceTrack t)
    {
        // Replace the old road car (and any previous player F1 car) with a fresh F1 car
        foreach (var name in new[] { "SportsSedan", "F1_Player" })
        {
            var old = GameObject.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }
        var car = F1CarBuilder.Build("F1_Player", new Color(0.08f, 0.22f, 0.75f), Color.white, new Color(1f, 0.85f, 0.1f), new Color(1f, 0.85f, 0.1f));
        Undo.RegisterCreatedObjectUndo(car, "Create player car");
        var slot = GridSlot(t, 0);   // pole position
        car.transform.SetPositionAndRotation(slot.pos, slot.rot);

        var rb = car.AddComponent<Rigidbody>();
        rb.mass = 800f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/City/Materials/CarBody.asset");
        if (pm != null) car.GetComponent<BoxCollider>().sharedMaterial = pm;
        CreateSparks(car);
        car.AddComponent<CarController>().wheelRadius = 0.33f;
        car.AddComponent<CarHUD>();
        return car;
    }

    // Spark burst emitted by CarController at impact points (world-space, additive streaks).
    static void CreateSparks(GameObject car)
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

    static Material SparkMaterial()
    {
        const string path = Dir + "/Materials/Sparks.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        // Transparent, additive blending so sparks glow
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

    static RaceAI[] BuildRivals(GameObject root, RaceTrack t, GameObject player)
    {
        var parent = Child("Rivals", root);
        var result = new RaceAI[Rivals.Length];
        int[] slots = { 1, 2, 3, 4 };   // player starts on pole (slot 0)
        for (int i = 0; i < Rivals.Length; i++)
        {
            var info = Rivals[i];
            var slot = GridSlot(t, slots[i]);
            var car = F1CarBuilder.Build("Rival_" + info.name, info.color, RivalAccents[i], RivalHelmets[i], RivalStripes[i]);
            car.transform.SetParent(parent.transform, false);
            car.transform.SetPositionAndRotation(slot.pos, slot.rot);
            // Rivals are solid kinematic bodies; CarController.OnCollisionEnter shares the impact with them
            var rb = car.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var ai = car.AddComponent<RaceAI>();
            ai.driverName = info.name;
            ai.color = info.color;
            ai.maxSpeed = info.speed;
            ai.cornerGrip = info.grip;
            ai.lane = slot.lat;
            ai.progress = slot.d;
            result[i] = ai;
        }
        return result;
    }

    static void SetupCamera(GameObject player)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }
        cam.farClipPlane = 800f;
        var follow = cam.GetComponent<ChaseCamera>();
        if (follow == null) follow = Undo.AddComponent<ChaseCamera>(cam.gameObject);
        follow.target = player.transform;
        follow.distance = 6.5f;
        follow.height = 2.1f;
        follow.cockpitOffset = new Vector3(0, 0.95f, 0.2f);
        cam.nearClipPlane = 0.2f;
        cam.transform.position = player.transform.position - player.transform.forward * 7f + Vector3.up * 2.6f;
        cam.transform.LookAt(player.transform.position + Vector3.up * 1.2f);
    }

    // ---------- Mesh extrusion ----------

    // Sweeps a cross-section profile (x = sideways offset, y = height) along the track,
    // emitting only the segments the filter accepts. Faces are double-sided.
    static GameObject MeshObj(string name, GameObject parent, RaceTrack t, Vector2[] profile, Func<int, bool> filter, Material mat, bool collider)
    {
        int n = t.Count;
        var right = new Vector3[n];
        for (int i = 0; i < n; i++)
            right[i] = Vector3.Cross(Vector3.up, (t.points[(i + 1) % n] - t.points[(i - 1 + n) % n]).normalized).normalized;

        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (!filter(i)) continue;
            int j = (i + 1) % n;
            for (int k = 0; k < profile.Length - 1; k++)
            {
                Vector3 a = t.points[i] + right[i] * profile[k].x + Vector3.up * profile[k].y;
                Vector3 b = t.points[i] + right[i] * profile[k + 1].x + Vector3.up * profile[k + 1].y;
                Vector3 c = t.points[j] + right[j] * profile[k + 1].x + Vector3.up * profile[k + 1].y;
                Vector3 d = t.points[j] + right[j] * profile[k].x + Vector3.up * profile[k].y;
                int v = verts.Count;
                verts.AddRange(new[] { a, b, c, d, a, b, c, d });
                tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
                tris.AddRange(new[] { v + 4, v + 5, v + 6, v + 4, v + 6, v + 7 });
            }
        }
        var mesh = new UnityEngine.Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string path = Dir + "/Meshes/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        var go = Child(name, parent);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        if (mat != null) mr.sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // ---------- Helpers ----------

    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    static GameObject Child(string n, GameObject parent)
    {
        var g = new GameObject(n);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    static Material Mat(string name, Color c, float metal = 0f, float smooth = 0.3f, Color? emission = null)
    {
        if (mats.TryGetValue(name, out var cached)) return cached;
        var path = Dir + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission.Value);
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    // Positions are local to the parent (parents sitting at the origin use world coordinates).
    static GameObject Box(string n, GameObject parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m, bool collider = false)
    {
        return Prim(PrimitiveType.Cube, n, parent, pos, rot, scale, m, collider, true);
    }

    static GameObject Prim(PrimitiveType type, string n, GameObject parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m, bool collider, bool isStatic)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = n;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = rot;
        g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (isStatic) GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
        return g;
    }
}
