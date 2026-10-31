using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class SportsSedanBuilder
{
    const string Dir = "Assets/SportsSedan";
    static Shader shader;

    [MenuItem("Tools/Build Sports Sedan")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "SportsSedan");
        shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var paint  = Mat("Paint",  new Color(0.08f, 0.22f, 0.62f), 0.7f, 0.85f);
        var glass  = Mat("Glass",  new Color(0.04f, 0.06f, 0.08f), 0.2f, 0.95f);
        var black  = Mat("Trim",   new Color(0.03f, 0.03f, 0.03f), 0f, 0.4f);
        var chrome = Mat("Chrome", new Color(0.85f, 0.85f, 0.88f), 1f, 0.9f);
        var tire   = Mat("Tire",   new Color(0.05f, 0.05f, 0.05f), 0f, 0.15f);
        var rim    = Mat("Rim",    new Color(0.6f, 0.6f, 0.62f), 0.9f, 0.7f);
        var head   = Mat("Headlight", Color.white, 0f, 0.9f, new Color(1f, 1f, 0.95f) * 2f);
        var tail   = Mat("Taillight", new Color(0.6f, 0f, 0f), 0f, 0.9f, new Color(1f, 0.05f, 0.02f) * 1.5f);
        var plate  = Mat("Plate",  new Color(0.95f, 0.95f, 0.95f), 0f, 0.3f);

        var root = new GameObject("SportsSedan");
        var body = Child("Body", root);
        var wheels = Child("Wheels", root);
        var lights = Child("Lights", root);

        // Main body (+Z is forward)
        Box("LowerBody", body, new Vector3(0, 0.63f, 0), new Vector3(1.8f, 0.5f, 4.7f), paint);
        Box("Hood", body, new Vector3(0, 0.9f, 1.62f), new Vector3(1.76f, 0.06f, 1.4f), paint, new Vector3(3f, 0, 0));
        Box("TrunkLid", body, new Vector3(0, 0.91f, -1.98f), new Vector3(1.76f, 0.06f, 0.65f), paint);
        Box("SideSkirtL", body, new Vector3(-0.91f, 0.42f, 0), new Vector3(0.04f, 0.08f, 2.1f), black);
        Box("SideSkirtR", body, new Vector3(0.91f, 0.42f, 0), new Vector3(0.04f, 0.08f, 2.1f), black);
        Box("BeltLineL", body, new Vector3(-0.905f, 0.78f, -0.1f), new Vector3(0.02f, 0.015f, 3.6f), chrome);
        Box("BeltLineR", body, new Vector3(0.905f, 0.78f, -0.1f), new Vector3(0.02f, 0.015f, 3.6f), chrome);

        // Greenhouse (tapered glass cabin) + roof
        var cabin = Child("Greenhouse", body);
        cabin.AddComponent<MeshFilter>().sharedMesh = CabinMesh();
        cabin.AddComponent<MeshRenderer>().sharedMaterial = glass;
        Box("Roof", body, new Vector3(0, 1.425f, -0.32f), new Vector3(1.44f, 0.04f, 1.5f), paint);
        Box("BPillarL", body, new Vector3(-0.77f, 1.15f, -0.28f), new Vector3(0.03f, 0.52f, 0.12f), black, new Vector3(0, 0, -12f));
        Box("BPillarR", body, new Vector3(0.77f, 1.15f, -0.28f), new Vector3(0.03f, 0.52f, 0.12f), black, new Vector3(0, 0, 12f));
        Box("MirrorL", body, new Vector3(-0.98f, 1.0f, 0.62f), new Vector3(0.2f, 0.1f, 0.12f), paint);
        Box("MirrorR", body, new Vector3(0.98f, 1.0f, 0.62f), new Vector3(0.2f, 0.1f, 0.12f), paint);
        Box("DoorHandleFL", body, new Vector3(-0.905f, 0.84f, 0.15f), new Vector3(0.02f, 0.03f, 0.16f), chrome);
        Box("DoorHandleFR", body, new Vector3(0.905f, 0.84f, 0.15f), new Vector3(0.02f, 0.03f, 0.16f), chrome);
        Box("DoorHandleRL", body, new Vector3(-0.905f, 0.84f, -0.75f), new Vector3(0.02f, 0.03f, 0.16f), chrome);
        Box("DoorHandleRR", body, new Vector3(0.905f, 0.84f, -0.75f), new Vector3(0.02f, 0.03f, 0.16f), chrome);

        // Front: twin-kidney grille, lower intake, plates, rear diffuser
        for (int s = -1; s <= 1; s += 2)
        {
            Box(s < 0 ? "GrilleFrameL" : "GrilleFrameR", body, new Vector3(s * 0.17f, 0.72f, 2.355f), new Vector3(0.28f, 0.24f, 0.02f), chrome);
            Box(s < 0 ? "GrilleL" : "GrilleR", body, new Vector3(s * 0.17f, 0.72f, 2.362f), new Vector3(0.24f, 0.2f, 0.02f), black);
        }
        Box("LowerIntake", body, new Vector3(0, 0.48f, 2.355f), new Vector3(1.3f, 0.13f, 0.02f), black);
        Box("FrontPlate", body, new Vector3(0, 0.58f, 2.37f), new Vector3(0.52f, 0.11f, 0.01f), plate);
        Box("RearPlate", body, new Vector3(0, 0.66f, -2.36f), new Vector3(0.52f, 0.11f, 0.01f), plate);
        Box("RearDiffuser", body, new Vector3(0, 0.44f, -2.355f), new Vector3(1.4f, 0.1f, 0.02f), black);
        Box("ExhaustL", body, new Vector3(-0.55f, 0.42f, -2.37f), new Vector3(0.12f, 0.07f, 0.03f), chrome);
        Box("ExhaustR", body, new Vector3(0.55f, 0.42f, -2.37f), new Vector3(0.12f, 0.07f, 0.03f), chrome);

        // Lights
        Box("HeadlightL", lights, new Vector3(-0.6f, 0.77f, 2.355f), new Vector3(0.48f, 0.1f, 0.02f), head);
        Box("HeadlightR", lights, new Vector3(0.6f, 0.77f, 2.355f), new Vector3(0.48f, 0.1f, 0.02f), head);
        Box("FogLightL", lights, new Vector3(-0.72f, 0.48f, 2.36f), new Vector3(0.14f, 0.05f, 0.02f), head);
        Box("FogLightR", lights, new Vector3(0.72f, 0.48f, 2.36f), new Vector3(0.14f, 0.05f, 0.02f), head);
        Box("TaillightL", lights, new Vector3(-0.62f, 0.8f, -2.355f), new Vector3(0.5f, 0.12f, 0.02f), tail);
        Box("TaillightR", lights, new Vector3(0.62f, 0.8f, -2.355f), new Vector3(0.5f, 0.12f, 0.02f), tail);

        // Wheels: tire + rim + hub + spokes
        string[] names = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        Vector3[] pos = { new Vector3(-0.84f, 0.34f, 1.42f), new Vector3(0.84f, 0.34f, 1.42f),
                          new Vector3(-0.84f, 0.34f, -1.42f), new Vector3(0.84f, 0.34f, -1.42f) };
        for (int i = 0; i < 4; i++)
        {
            var w = Child(names[i], wheels);
            w.transform.localPosition = pos[i];
            float side = Mathf.Sign(pos[i].x);
            Cyl("Tire", w, Vector3.zero, new Vector3(0.68f, 0.13f, 0.68f), tire);
            Cyl("Rim", w, new Vector3(side * 0.01f, 0, 0), new Vector3(0.46f, 0.125f, 0.46f), rim);
            Cyl("Hub", w, new Vector3(side * 0.02f, 0, 0), new Vector3(0.12f, 0.125f, 0.12f), chrome);
            for (int k = 0; k < 5; k++)
                Box("Spoke" + k, w, new Vector3(side * 0.135f, 0, 0), new Vector3(0.02f, 0.4f, 0.05f), chrome, new Vector3(k * 72f, 0, 0));
        }

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.72f, 0);
        col.size = new Vector3(1.9f, 1.44f, 4.75f);

        AssetDatabase.SaveAssets();
        Undo.RegisterCreatedObjectUndo(root, "Build Sports Sedan");
        Selection.activeGameObject = root;
        Debug.Log("SportsSedan built with shader " + shader.name);
    }

    static GameObject Child(string n, GameObject parent)
    {
        var g = new GameObject(n);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    static Material Mat(string name, Color c, float metal, float smooth, Color? emission = null)
    {
        var path = Dir + "/" + name + ".mat";
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
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", emission.Value);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Box(string n, GameObject parent, Vector3 p, Vector3 s, Material m, Vector3? rot = null)
    {
        return Prim(PrimitiveType.Cube, n, parent, p, s, m, rot.HasValue ? Quaternion.Euler(rot.Value) : Quaternion.identity);
    }

    static GameObject Cyl(string n, GameObject parent, Vector3 p, Vector3 s, Material m)
    {
        return Prim(PrimitiveType.Cylinder, n, parent, p, s, m, Quaternion.Euler(0, 0, 90));
    }

    static GameObject Prim(PrimitiveType t, string n, GameObject parent, Vector3 p, Vector3 s, Material m, Quaternion r)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = n;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = p;
        g.transform.localRotation = r;
        g.transform.localScale = s;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }

    static UnityEngine.Mesh CabinMesh()
    {
        // Tapered frustum: wide base on the body, narrower roof; raked windshield and rear glass
        float yb = 0.86f, yt = 1.41f;
        float wb = 0.86f, wt = 0.71f;
        float zbF = 0.95f, zbR = -1.65f, ztF = 0.42f, ztR = -1.06f;
        Vector3[] c = {
            new Vector3(-wb, yb, zbF), new Vector3(wb, yb, zbF), new Vector3(wb, yb, zbR), new Vector3(-wb, yb, zbR),
            new Vector3(-wt, yt, ztF), new Vector3(wt, yt, ztF), new Vector3(wt, yt, ztR), new Vector3(-wt, yt, ztR) };
        int[][] faces = { new[]{0,1,2,3}, new[]{4,5,6,7}, new[]{0,1,5,4}, new[]{1,2,6,5}, new[]{2,3,7,6}, new[]{3,0,4,7} };
        Vector3 center = Vector3.zero; foreach (var v in c) center += v; center /= 8f;
        var verts = new List<Vector3>(); var tris = new List<int>();
        foreach (var f in faces)
        {
            int b = verts.Count;
            Vector3 a = c[f[0]], bb = c[f[1]], cc = c[f[2]], d = c[f[3]];
            Vector3 n = Vector3.Cross(bb - a, cc - a);
            bool flip = Vector3.Dot(n, (a + bb + cc + d) / 4f - center) < 0;
            verts.Add(a); verts.Add(bb); verts.Add(cc); verts.Add(d);
            if (!flip) tris.AddRange(new[]{ b, b+1, b+2, b, b+2, b+3 });
            else tris.AddRange(new[]{ b, b+2, b+1, b, b+3, b+2 });
        }
        var mesh = new UnityEngine.Mesh { name = "GreenhouseMesh" };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var path = Dir + "/GreenhouseMesh.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
