using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// Tools > Build City: generates a 5x5-block city with roads, houses, downtown towers,
// a central park, AI traffic, and sets up the SportsSedan as the drivable player car.
public static class CityBuilder
{
    const string MatDir = "Assets/City/Materials";
    static readonly float[] Roads = { -100f, -60f, -20f, 20f, 60f, 100f };
    const float Lane = 2.5f;
    static readonly Vector3 PlayerSpawn = new Vector3(22.5f, 0.02f, -88f);

    static Shader shader;
    static System.Random rng;
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("Tools/Build City")]
    public static void Build()
    {
        rng = new System.Random(1234);
        mats.Clear();
        if (!AssetDatabase.IsValidFolder("Assets/City")) AssetDatabase.CreateFolder("Assets", "City");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/City", "Materials");
        shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var old = GameObject.Find("City");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var city = new GameObject("City");

        BuildGround(city);
        BuildRoads(city);
        BuildBlocks(city);
        BuildOuterTrees(city);
        BuildBounds(city);
        var player = SetupPlayer();
        BuildTraffic(city);
        SetupCamera(player);

        Undo.RegisterCreatedObjectUndo(city, "Build City");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("City built. Press Play and drive with WASD / arrows.");
    }

    // ---------- Layout ----------

    static void BuildGround(GameObject city)
    {
        Box("Ground", city, new Vector3(0, -0.5f, 0), new Vector3(320, 1, 320), Mat("Grass", new Color(0.33f, 0.52f, 0.25f)), true);
    }

    static void BuildRoads(GameObject city)
    {
        var roads = Child("Roads", city);
        var asphalt = Mat("Asphalt", new Color(0.16f, 0.16f, 0.17f), 0f, 0.25f);
        var paint = Mat("LaneMarking", new Color(0.95f, 0.95f, 0.9f), 0f, 0.3f);
        foreach (float r in Roads)
        {
            Box("Road_X" + r, roads, new Vector3(r, 0.01f, 0), new Vector3(10, 0.02f, 210), asphalt);
            Box("Road_Z" + r, roads, new Vector3(0, 0.011f, r), new Vector3(210, 0.02f, 10), asphalt);
            for (float t = -102f; t < 103f; t += 6f)
            {
                bool nearJunction = false;
                foreach (float rr in Roads) if (Mathf.Abs(t - rr) < 7f) nearJunction = true;
                if (nearJunction) continue;
                Box("Dash", roads, new Vector3(r, 0.025f, t), new Vector3(0.18f, 0.01f, 3f), paint);
                Box("Dash", roads, new Vector3(t, 0.025f, r), new Vector3(3f, 0.01f, 0.18f), paint);
            }
        }
    }

    static void BuildBlocks(GameObject city)
    {
        var blocks = Child("Blocks", city);
        var sidewalk = Mat("Sidewalk", new Color(0.62f, 0.62f, 0.6f), 0f, 0.2f);
        var lawn = Mat("Lawn", new Color(0.38f, 0.58f, 0.28f));
        for (int i = 0; i < Roads.Length - 1; i++)
        for (int j = 0; j < Roads.Length - 1; j++)
        {
            float cx = Roads[i] + 20f, cz = Roads[j] + 20f;
            var block = Child("Block_" + i + "_" + j, blocks);
            block.transform.position = new Vector3(cx, 0, cz);
            Box("Sidewalk", block, new Vector3(0, 0.03f, 0), new Vector3(30, 0.06f, 30), sidewalk);
            Box("Lot", block, new Vector3(0, 0.031f, 0), new Vector3(26, 0.062f, 26), lawn);

            bool downtown = Mathf.Abs(cx) <= 40f && Mathf.Abs(cz) <= 40f;
            if (cx == 0 && cz == 0) { BuildPark(block); continue; }

            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                var lot = new Vector3(a * 6.5f, 0, b * 6.5f);
                if (downtown) Tower(block, lot); else House(block, lot);
                if (!downtown && rng.NextDouble() < 0.6) Tree(block, new Vector3(a * 11.6f, 0, b * 11.6f));
            }
            // Street trees on the sidewalk edges
            if (!downtown)
                for (int k = -1; k <= 1; k += 2)
                {
                    Tree(block, new Vector3(k * 14f, 0, 0));
                    Tree(block, new Vector3(0, 0, k * 14f));
                }
        }
    }

    static void BuildPark(GameObject block)
    {
        Box("Pond", block, new Vector3(0, 0.07f, 0), new Vector3(10, 0.02f, 8), Mat("Water", new Color(0.15f, 0.35f, 0.55f), 0.1f, 0.95f));
        var path = Mat("Path", new Color(0.72f, 0.65f, 0.5f));
        Box("PathNS", block, new Vector3(-7.5f, 0.07f, 0), new Vector3(2, 0.02f, 26), path);
        Box("PathEW", block, new Vector3(0, 0.071f, 7.5f), new Vector3(26, 0.02f, 2), path);
        for (int n = 0; n < 16; n++)
        {
            float ang = n / 16f * Mathf.PI * 2f + R(-0.15f, 0.15f);
            float rad = R(8.5f, 11.5f);
            Tree(block, new Vector3(Mathf.Cos(ang) * rad, 0, Mathf.Sin(ang) * rad));
        }
        var wood = Mat("Wood", new Color(0.45f, 0.3f, 0.18f));
        for (int k = -1; k <= 1; k += 2)
            Box("Bench", block, new Vector3(k * 6f, 0.25f, 4.8f), new Vector3(1.8f, 0.5f, 0.5f), wood, true);
    }

    static void House(GameObject parent, Vector3 lotPos)
    {
        Color[] walls = { new Color(0.93f, 0.89f, 0.8f), new Color(0.85f, 0.75f, 0.62f), new Color(0.75f, 0.82f, 0.88f),
                          new Color(0.92f, 0.82f, 0.72f), new Color(0.8f, 0.86f, 0.74f), new Color(0.95f, 0.95f, 0.95f),
                          new Color(0.72f, 0.45f, 0.38f) };
        Color[] roofs = { new Color(0.55f, 0.2f, 0.15f), new Color(0.28f, 0.28f, 0.32f), new Color(0.4f, 0.26f, 0.18f), new Color(0.2f, 0.3f, 0.4f) };
        int wi = rng.Next(walls.Length), ri = rng.Next(roofs.Length);
        var wallMat = Mat("HouseWall" + wi, walls[wi]);
        var roofMat = Mat("Roof" + ri, roofs[ri], 0f, 0.35f);
        var win = Mat("Window", new Color(0.12f, 0.18f, 0.25f), 0.3f, 0.9f);
        var door = Mat("Door", new Color(0.35f, 0.22f, 0.14f));

        float w = R(7f, 10f), d = R(7f, 10f);
        int floors = rng.Next(1, 3);
        float h = floors * 3f + 0.3f;

        var house = Child("House", parent);
        house.transform.localPosition = lotPos;
        house.transform.localRotation = Quaternion.Euler(0, 90 * rng.Next(4), 0);

        Box("Walls", house, new Vector3(0, h / 2, 0), new Vector3(w, h, d), wallMat, true);
        Box("Door", house, new Vector3(0, 1.1f, 0), new Vector3(1.1f, 2.2f, d + 0.04f), door);
        for (int f = 0; f < floors; f++)
        {
            float y = f * 3f + 1.7f;
            for (int k = -1; k <= 1; k += 2)
            {
                Box("Window", house, new Vector3(k * w * 0.28f, y, 0), new Vector3(1.2f, 1.1f, d + 0.04f), win);
                Box("Window", house, new Vector3(0, y, k * d * 0.28f), new Vector3(w + 0.04f, 1.1f, 1.2f), win);
            }
        }

        // Gabled roof: a 45-degree-rotated cube squashed vertically by its parent
        var roof = Child("Roof", house);
        roof.transform.localPosition = new Vector3(0, h, 0);
        roof.transform.localScale = new Vector3(1, 0.55f, 1);
        if (w <= d)
        {
            float s = (w + 0.6f) / Mathf.Sqrt(2f);
            Box("RoofPanel", roof, Vector3.zero, new Vector3(s, s, d + 0.6f), roofMat, false, new Vector3(0, 0, 45));
        }
        else
        {
            float s = (d + 0.6f) / Mathf.Sqrt(2f);
            Box("RoofPanel", roof, Vector3.zero, new Vector3(w + 0.6f, s, s), roofMat, false, new Vector3(45, 0, 0));
        }
        if (rng.NextDouble() < 0.5)
            Box("Chimney", house, new Vector3(w * 0.25f, h + 1.2f, d * 0.15f), new Vector3(0.6f, 2f, 0.6f), Mat("Brick", new Color(0.55f, 0.3f, 0.25f)));
    }

    static void Tower(GameObject parent, Vector3 lotPos)
    {
        Color[] walls = { new Color(0.7f, 0.72f, 0.75f), new Color(0.55f, 0.58f, 0.62f), new Color(0.82f, 0.78f, 0.7f),
                          new Color(0.38f, 0.42f, 0.48f), new Color(0.75f, 0.68f, 0.6f) };
        int wi = rng.Next(walls.Length);
        var wallMat = Mat("TowerWall" + wi, walls[wi], 0.1f, 0.4f);
        var glass = Mat("TowerGlass", new Color(0.2f, 0.32f, 0.45f), 0.6f, 0.92f);
        var cap = Mat("TowerCap", new Color(0.2f, 0.2f, 0.22f));

        float w = R(9f, 12f), d = R(9f, 12f);
        int floors = rng.Next(5, 13);
        float h = floors * 3.2f;

        var tower = Child("Tower", parent);
        tower.transform.localPosition = lotPos;
        Box("Walls", tower, new Vector3(0, h / 2, 0), new Vector3(w, h, d), wallMat, true);
        for (int f = 0; f < floors; f++)
        {
            float y = f * 3.2f + 1.8f;
            Box("Glass", tower, new Vector3(0, y, 0), new Vector3(w + 0.04f, 1.5f, d - 1.2f), glass);
            Box("Glass", tower, new Vector3(0, y, 0), new Vector3(w - 1.2f, 1.5f, d + 0.04f), glass);
        }
        Box("RoofCap", tower, new Vector3(0, h + 0.15f, 0), new Vector3(w + 0.3f, 0.3f, d + 0.3f), cap);
        Box("RoofUnit", tower, new Vector3(R(-2f, 2f), h + 1f, R(-2f, 2f)), new Vector3(R(2f, 4f), 1.4f, R(2f, 4f)), cap);
    }

    static void Tree(GameObject parent, Vector3 localPos)
    {
        var bark = Mat("Bark", new Color(0.36f, 0.25f, 0.16f));
        Color[] leaves = { new Color(0.2f, 0.45f, 0.18f), new Color(0.27f, 0.52f, 0.2f), new Color(0.16f, 0.38f, 0.2f) };
        int li = rng.Next(leaves.Length);
        var tree = Child("Tree", parent);
        tree.transform.localPosition = localPos;
        float size = R(2.2f, 3.4f);
        var trunk = Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0, 1f, 0), new Vector3(0.3f, 1f, 0.3f), bark, true, Quaternion.identity, true);
        Prim(PrimitiveType.Sphere, "Canopy", tree, new Vector3(0, 2f + size * 0.4f, 0), Vector3.one * size, Mat("Leaves" + li, leaves[li]), false, Quaternion.identity, true);
    }

    static void BuildOuterTrees(GameObject city)
    {
        var nature = Child("OuterTrees", city);
        for (int n = 0; n < 110; n++)
        {
            float x = R(-150f, 150f), z = R(-150f, 150f);
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 112f) { n--; continue; }
            Tree(nature, new Vector3(x, 0, z));
        }
    }

    static void BuildBounds(GameObject city)
    {
        var bounds = Child("Bounds", city);
        const float e = 107f;
        Wall(bounds, new Vector3(0, 2.5f, e + 0.5f), new Vector3(2 * e + 2, 5, 1));
        Wall(bounds, new Vector3(0, 2.5f, -e - 0.5f), new Vector3(2 * e + 2, 5, 1));
        Wall(bounds, new Vector3(e + 0.5f, 2.5f, 0), new Vector3(1, 5, 2 * e + 2));
        Wall(bounds, new Vector3(-e - 0.5f, 2.5f, 0), new Vector3(1, 5, 2 * e + 2));
    }

    static void Wall(GameObject parent, Vector3 pos, Vector3 size)
    {
        var g = Child("InvisibleWall", parent);
        g.transform.localPosition = pos;
        g.AddComponent<BoxCollider>().size = size;
    }

    // ---------- Player ----------

    static GameObject SetupPlayer()
    {
        var car = GameObject.Find("SportsSedan");
        if (car == null) { SportsSedanBuilder.Build(); car = GameObject.Find("SportsSedan"); }
        Undo.RecordObject(car.transform, "Place player car");
        car.transform.SetPositionAndRotation(PlayerSpawn, Quaternion.identity);

        var rb = car.GetComponent<Rigidbody>();
        if (rb == null) rb = Undo.AddComponent<Rigidbody>(car);
        rb.mass = 1400f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        const string pmPath = "Assets/City/Materials/CarBody.asset";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
        if (pm == null)
        {
            pm = new PhysicsMaterial("CarBody") { dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum };
            AssetDatabase.CreateAsset(pm, pmPath);
        }
        car.GetComponent<BoxCollider>().sharedMaterial = pm;

        if (car.GetComponent<CarController>() == null) Undo.AddComponent<CarController>(car);
        if (car.GetComponent<CarHUD>() == null) Undo.AddComponent<CarHUD>(car);
        return car;
    }

    static void SetupCamera(GameObject player)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            Undo.RegisterCreatedObjectUndo(go, "Create camera");
        }
        Undo.RecordObject(cam, "Camera setup");
        cam.farClipPlane = 600f;
        var follow = cam.GetComponent<ChaseCamera>();
        if (follow == null) follow = Undo.AddComponent<ChaseCamera>(cam.gameObject);
        follow.target = player.transform;
        cam.transform.position = player.transform.position - player.transform.forward * 7f + Vector3.up * 2.6f;
        cam.transform.LookAt(player.transform.position + Vector3.up * 1.2f);
    }

    // ---------- Traffic ----------

    static void BuildTraffic(GameObject city)
    {
        var traffic = Child("Traffic", city);
        Color[] paints = { new Color(0.8f, 0.1f, 0.1f), new Color(0.95f, 0.95f, 0.95f), new Color(0.1f, 0.1f, 0.12f),
                           new Color(0.85f, 0.7f, 0.1f), new Color(0.2f, 0.5f, 0.25f), new Color(0.55f, 0.57f, 0.6f),
                           new Color(0.9f, 0.45f, 0.1f), new Color(0.3f, 0.2f, 0.5f) };
        var spawned = new List<Vector3>();
        for (int n = 0; n < 24; n++)
        {
            int ix0 = rng.Next(0, 5), iz0 = rng.Next(0, 5);
            int ix1 = Mathf.Min(5, ix0 + 1 + rng.Next(0, 2)), iz1 = Mathf.Min(5, iz0 + 1 + rng.Next(0, 2));
            float xa = Roads[ix0] + Lane, xb = Roads[ix1] - Lane, za = Roads[iz0] + Lane, zb = Roads[iz1] - Lane;
            // Clockwise loop, always in the right-hand lane
            var wps = new[] { new Vector3(xa, 0, za), new Vector3(xa, 0, zb), new Vector3(xb, 0, zb), new Vector3(xb, 0, za) };

            Vector3 pos = Vector3.zero; int seg = 0; bool ok = false;
            for (int attempt = 0; attempt < 12 && !ok; attempt++)
            {
                seg = rng.Next(4);
                pos = Vector3.Lerp(wps[seg], wps[(seg + 1) % 4], R(0.2f, 0.8f));
                ok = Vector3.Distance(pos, PlayerSpawn) > 25f;
                foreach (var p in spawned) if (Vector3.Distance(p, pos) < 12f) ok = false;
            }
            if (!ok) continue;
            spawned.Add(pos);

            Vector3 dir = (wps[(seg + 1) % 4] - wps[seg]).normalized;
            int pi = rng.Next(paints.Length);
            var npc = NpcModel("NpcCar_" + n, traffic, pos, Quaternion.LookRotation(dir), Mat("NpcPaint" + pi, paints[pi], 0.6f, 0.8f), rng.Next(3));
            var ai = npc.AddComponent<NpcCar>();
            ai.waypoints = wps;
            ai.targetIndex = (seg + 1) % 4;
            ai.cruiseSpeed = R(9f, 14f);
            ai.halfLength = npc.GetComponent<BoxCollider>().size.z / 2f;
        }
    }

    static GameObject NpcModel(string name, GameObject parent, Vector3 pos, Quaternion rot, Material paint, int type)
    {
        // type 0 sedan, 1 SUV, 2 compact
        float len = type == 2 ? 3.7f : (type == 1 ? 4.5f : 4.3f);
        float bodyH = type == 1 ? 0.8f : 0.6f;
        float cabinLen = type == 2 ? 2.1f : (type == 1 ? 2.8f : 2.2f);
        var glass = Mat("NpcGlass", new Color(0.06f, 0.08f, 0.1f), 0.2f, 0.95f);
        var tire = Mat("NpcTire", new Color(0.05f, 0.05f, 0.05f), 0f, 0.15f);
        var rim = Mat("NpcRim", new Color(0.6f, 0.6f, 0.62f), 0.8f, 0.6f);
        var head = AssetDatabase.LoadAssetAtPath<Material>("Assets/SportsSedan/Headlight.mat");
        var tail = AssetDatabase.LoadAssetAtPath<Material>("Assets/SportsSedan/Taillight.mat");

        var car = new GameObject(name);
        car.transform.SetParent(parent.transform, false);
        car.transform.SetPositionAndRotation(pos, rot);
        float bodyBottom = 0.32f;
        float cabinY = bodyBottom + bodyH + 0.25f;
        var col = car.AddComponent<BoxCollider>();
        col.center = new Vector3(0, (cabinY + 0.3f) / 2f, 0);
        col.size = new Vector3(1.8f, cabinY + 0.3f, len);
        var rb = car.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        Box("Body", car, new Vector3(0, bodyBottom + bodyH / 2, 0), new Vector3(1.8f, bodyH, len), paint, false, null, false);
        Box("Cabin", car, new Vector3(0, cabinY, type == 1 ? -0.4f : -0.2f), new Vector3(1.55f, 0.5f, cabinLen), glass, false, null, false);
        Box("Roof", car, new Vector3(0, cabinY + 0.27f, type == 1 ? -0.4f : -0.2f), new Vector3(1.5f, 0.06f, cabinLen - 0.15f), paint, false, null, false);
        for (int k = -1; k <= 1; k += 2)
        {
            if (head != null) Box("Headlight", car, new Vector3(k * 0.6f, bodyBottom + bodyH * 0.7f, len / 2), new Vector3(0.35f, 0.12f, 0.04f), head, false, null, false);
            if (tail != null) Box("Taillight", car, new Vector3(k * 0.6f, bodyBottom + bodyH * 0.7f, -len / 2), new Vector3(0.35f, 0.12f, 0.04f), tail, false, null, false);
        }
        float wz = len / 2 - 0.8f;
        string[] names = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        Vector3[] wpos = { new Vector3(-0.82f, 0.33f, wz), new Vector3(0.82f, 0.33f, wz), new Vector3(-0.82f, 0.33f, -wz), new Vector3(0.82f, 0.33f, -wz) };
        for (int i = 0; i < 4; i++)
        {
            var w = Child(names[i], car);
            w.transform.localPosition = wpos[i];
            var axle = Quaternion.Euler(0, 0, 90);
            Prim(PrimitiveType.Cylinder, "Tire", w, Vector3.zero, new Vector3(0.66f, 0.12f, 0.66f), tire, false, axle, false);
            Prim(PrimitiveType.Cylinder, "Rim", w, new Vector3(Mathf.Sign(wpos[i].x) * 0.01f, 0, 0), new Vector3(0.4f, 0.12f, 0.4f), rim, false, axle, false);
        }
        return car;
    }

    // ---------- Helpers ----------

    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    static GameObject Child(string n, GameObject parent)
    {
        var g = new GameObject(n);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    static Material Mat(string name, Color c, float metal = 0f, float smooth = 0.3f)
    {
        if (mats.TryGetValue(name, out var cached)) return cached;
        var path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static GameObject Box(string n, GameObject parent, Vector3 p, Vector3 s, Material m, bool collider = false, Vector3? rot = null, bool isStatic = true)
    {
        return Prim(PrimitiveType.Cube, n, parent, p, s, m, collider, rot.HasValue ? Quaternion.Euler(rot.Value) : Quaternion.identity, isStatic);
    }

    static GameObject Prim(PrimitiveType t, string n, GameObject parent, Vector3 p, Vector3 s, Material m, bool collider, Quaternion r, bool isStatic)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = n;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = p;
        g.transform.localRotation = r;
        g.transform.localScale = s;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (isStatic) GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
        return g;
    }
}
