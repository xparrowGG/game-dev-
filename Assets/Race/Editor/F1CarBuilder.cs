using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Builds a generic open-wheel formula car from primitives (+Z forward, ground at y = 0):
// front/rear wings, halo, sidepods, airbox, driver helmet, exposed wheels with compound stripes.
// Wheel hierarchy matches CarController / RaceAI: Wheels/WheelFL, WheelFR, WheelRL, WheelRR.
public static class F1CarBuilder
{
    const string MatDir = "Assets/Race/Materials";
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    public static GameObject Build(string name, Color livery, Color accent, Color helmet, Color stripe)
    {
        cache.Clear();
        var main = Mat("F1_" + ColorKey(livery), livery, 0.5f, 0.85f);
        var acc = Mat("F1_" + ColorKey(accent), accent, 0.4f, 0.8f);
        var carbon = Mat("F1_Carbon", new Color(0.06f, 0.06f, 0.07f), 0.3f, 0.6f);
        var tire = Mat("F1_Tire", new Color(0.04f, 0.04f, 0.04f), 0f, 0.2f);
        var rim = Mat("F1_Rim", new Color(0.12f, 0.12f, 0.14f), 0.8f, 0.6f);
        var stripeMat = Mat("F1_Stripe_" + ColorKey(stripe), stripe, 0f, 0.4f);
        var helmetMat = Mat("F1_Helmet_" + ColorKey(helmet), helmet, 0.2f, 0.9f);
        var rain = Mat("F1_RainLight", new Color(0.6f, 0f, 0f), 0f, 0.9f, new Color(1f, 0.05f, 0.02f) * 2f);

        var root = new GameObject(name);
        var body = Child("Body", root);

        // Chassis
        Box("FloorPlank", body, new Vector3(0, 0.09f, -0.3f), new Vector3(1.5f, 0.04f, 3.4f), carbon);
        Box("Tub", body, new Vector3(0, 0.4f, 0.5f), new Vector3(0.8f, 0.42f, 1.9f), main);
        Box("Nose", body, new Vector3(0, 0.33f, 1.95f), new Vector3(0.36f, 0.24f, 1.3f), main, new Vector3(4f, 0, 0));
        Box("NoseTip", body, new Vector3(0, 0.26f, 2.7f), new Vector3(0.26f, 0.16f, 0.35f), acc);
        Box("Cockpit", body, new Vector3(0, 0.62f, 0.3f), new Vector3(0.5f, 0.05f, 0.75f), carbon);
        Prim(PrimitiveType.Sphere, "Helmet", body, new Vector3(0, 0.72f, 0.1f), Quaternion.identity, Vector3.one * 0.3f, helmetMat);

        // Halo
        foreach (int s in new[] { -1, 1 })
        {
            Box("HaloBar", body, new Vector3(s * 0.26f, 0.83f, 0.32f), new Vector3(0.05f, 0.05f, 0.65f), carbon);
            Box("HaloMount", body, new Vector3(s * 0.26f, 0.73f, 0.0f), new Vector3(0.05f, 0.2f, 0.05f), carbon);
        }
        Box("HaloFront", body, new Vector3(0, 0.83f, 0.64f), new Vector3(0.57f, 0.05f, 0.05f), carbon);
        Box("HaloStrut", body, new Vector3(0, 0.73f, 0.72f), new Vector3(0.06f, 0.22f, 0.06f), carbon);

        // Sidepods, engine cover, airbox, fin
        foreach (int s in new[] { -1, 1 })
        {
            Box("Sidepod", body, new Vector3(s * 0.62f, 0.32f, -0.15f), new Vector3(0.55f, 0.4f, 1.5f), main);
            Box("SidepodInlet", body, new Vector3(s * 0.62f, 0.34f, 0.61f), new Vector3(0.5f, 0.3f, 0.03f), carbon);
            Box("SidepodRear", body, new Vector3(s * 0.45f, 0.26f, -1.05f), new Vector3(0.35f, 0.28f, 0.8f), main);
            Box("Mirror", body, new Vector3(s * 0.48f, 0.72f, 0.55f), new Vector3(0.18f, 0.08f, 0.04f), carbon);
            Box("MirrorStalk", body, new Vector3(s * 0.43f, 0.64f, 0.55f), new Vector3(0.03f, 0.14f, 0.03f), carbon);
        }
        Box("EngineCover", body, new Vector3(0, 0.6f, -0.85f), new Vector3(0.5f, 0.48f, 1.7f), main);
        Box("Airbox", body, new Vector3(0, 0.95f, -0.25f), new Vector3(0.32f, 0.3f, 0.45f), main);
        Box("AirboxIntake", body, new Vector3(0, 0.95f, -0.02f), new Vector3(0.24f, 0.2f, 0.03f), carbon);
        Box("SharkFin", body, new Vector3(0, 0.95f, -1.2f), new Vector3(0.03f, 0.3f, 1.1f), acc);
        Box("Gearbox", body, new Vector3(0, 0.33f, -1.95f), new Vector3(0.32f, 0.28f, 0.7f), carbon);
        Box("Diffuser", body, new Vector3(0, 0.14f, -2.25f), new Vector3(1.0f, 0.12f, 0.4f), carbon, new Vector3(-12f, 0, 0));
        Box("RainLight", body, new Vector3(0, 0.45f, -2.32f), new Vector3(0.12f, 0.08f, 0.03f), rain);

        // Front wing
        Box("FrontWing", body, new Vector3(0, 0.1f, 2.75f), new Vector3(1.95f, 0.04f, 0.42f), acc);
        Box("FrontWingFlap", body, new Vector3(0, 0.17f, 2.62f), new Vector3(1.8f, 0.03f, 0.25f), main, new Vector3(-12f, 0, 0));
        foreach (int s in new[] { -1, 1 })
            Box("FrontEndplate", body, new Vector3(s * 0.97f, 0.18f, 2.72f), new Vector3(0.03f, 0.24f, 0.55f), carbon);

        // Rear wing
        foreach (int s in new[] { -1, 1 })
            Box("RearEndplate", body, new Vector3(s * 0.52f, 0.72f, -2.4f), new Vector3(0.03f, 0.62f, 0.75f), acc);
        Box("RearWing", body, new Vector3(0, 0.88f, -2.38f), new Vector3(1.04f, 0.05f, 0.42f), main);
        Box("RearFlap", body, new Vector3(0, 1.0f, -2.5f), new Vector3(1.04f, 0.04f, 0.3f), acc, new Vector3(15f, 0, 0));
        Box("RearPylon", body, new Vector3(0, 0.6f, -2.35f), new Vector3(0.06f, 0.45f, 0.1f), carbon);

        // Suspension arms
        foreach (int s in new[] { -1, 1 })
            foreach (float z in new[] { 1.62f, -1.6f })
            {
                Box("WishboneUpper", body, new Vector3(s * 0.6f, 0.42f, z), new Vector3(0.42f, 0.03f, 0.06f), carbon);
                Box("WishboneLower", body, new Vector3(s * 0.6f, 0.26f, z), new Vector3(0.42f, 0.03f, 0.06f), carbon);
            }

        // Wheels (front narrower than rear)
        var wheels = Child("Wheels", root);
        Wheel("WheelFL", wheels, new Vector3(-0.8f, 0.33f, 1.62f), 0.66f, 0.3f, -1, tire, stripeMat, rim, acc);
        Wheel("WheelFR", wheels, new Vector3(0.8f, 0.33f, 1.62f), 0.66f, 0.3f, 1, tire, stripeMat, rim, acc);
        Wheel("WheelRL", wheels, new Vector3(-0.78f, 0.34f, -1.6f), 0.68f, 0.4f, -1, tire, stripeMat, rim, acc);
        Wheel("WheelRR", wheels, new Vector3(0.78f, 0.34f, -1.6f), 0.68f, 0.4f, 1, tire, stripeMat, rim, acc);

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.5f, 0.1f);
        col.size = new Vector3(2.0f, 1.0f, 5.6f);
        return root;
    }

    static void Wheel(string name, GameObject parent, Vector3 pos, float dia, float width, int side,
                      Material tire, Material stripe, Material rim, Material hub)
    {
        var w = Child(name, parent);
        w.transform.localPosition = pos;
        var axle = Quaternion.Euler(0, 0, 90);
        // Cylinder height is 2 units, so y-scale = width / 2. Each layer is slightly wider so it shows on the sidewall.
        Prim(PrimitiveType.Cylinder, "Tire", w, Vector3.zero, axle, new Vector3(dia, width / 2f, dia), tire);
        Prim(PrimitiveType.Cylinder, "Stripe", w, Vector3.zero, axle, new Vector3(dia * 0.84f, width / 2f + 0.003f, dia * 0.84f), stripe);
        Prim(PrimitiveType.Cylinder, "Rim", w, Vector3.zero, axle, new Vector3(dia * 0.7f, width / 2f + 0.006f, dia * 0.7f), rim);
        Prim(PrimitiveType.Cylinder, "Hub", w, Vector3.zero, axle, new Vector3(0.12f, width / 2f + 0.01f, 0.12f), hub);
    }

    // ---------- Helpers ----------

    static string ColorKey(Color c) => ColorUtility.ToHtmlStringRGB(c);

    static Material Mat(string name, Color c, float metal, float smooth, Color? emission = null)
    {
        if (cache.TryGetValue(name, out var m)) return m;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        string path = MatDir + "/" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
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
        cache[name] = m;
        return m;
    }

    static GameObject Child(string n, GameObject parent)
    {
        var g = new GameObject(n);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    static GameObject Box(string n, GameObject parent, Vector3 pos, Vector3 scale, Material m, Vector3? rot = null)
    {
        return Prim(PrimitiveType.Cube, n, parent, pos, rot.HasValue ? Quaternion.Euler(rot.Value) : Quaternion.identity, scale, m);
    }

    static GameObject Prim(PrimitiveType type, string n, GameObject parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = n;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = rot;
        g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }
}
