using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Generates a smooth single-seater model from lofted cross-sections instead of boxes:
// tapered nose and monocoque, sculpted sidepods, airbox with camera pod, shark fin, cambered wing
// profiles, tubular halo and suspension, rounded slick tyres with sidewall stripes and wheel covers.
// Meshes are generated once into Assets/Race/Meshes/F1 and shared by every car.
// Livery materials (F1_Livery, F1_Accent, F1_Helmet, F1_WheelCover) are recoloured per car at runtime.
// Frame: +Z forward, ground at y = 0, wheel centres at y = 0.36, axles at z = ±1.8.
public static class F1ModelBuilder
{
    const string MeshDir = "Assets/Race/Meshes/F1";
    const string MatDir = "Assets/Race/Materials/F1";
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    struct Sec
    {
        public float z, hw, hh, cy, cx, n;
        public Sec(float z, float hw, float hh, float cy, float n, float cx = 0f) { this.z = z; this.hw = hw; this.hh = hh; this.cy = cy; this.n = n; this.cx = cx; }
    }

    // ---------- Public API ----------

    public static void Prepare()
    {
        EnsureFolder("Assets/Race", "Meshes"); EnsureFolder("Assets/Race/Meshes", "F1");
        EnsureFolder("Assets/Race", "Materials"); EnsureFolder("Assets/Race/Materials", "F1");
        meshes.Clear();
        mats.Clear();

        // Monocoque from nose tip to gearbox
        Save("Monocoque", Loft(new[] {
            new Sec(3.02f, .05f, .04f, .20f, 2.2f), new Sec(2.85f, .10f, .07f, .21f, 2.5f), new Sec(2.5f, .15f, .10f, .25f, 2.8f),
            new Sec(2.0f, .20f, .14f, .31f, 3f), new Sec(1.5f, .26f, .18f, .37f, 3.2f), new Sec(1.0f, .33f, .22f, .42f, 3.5f),
            new Sec(0.55f, .39f, .25f, .45f, 3.5f), new Sec(0.0f, .42f, .26f, .46f, 3.5f), new Sec(-0.45f, .42f, .30f, .50f, 3.2f),
            new Sec(-0.9f, .36f, .30f, .50f, 3f), new Sec(-1.4f, .27f, .24f, .45f, 2.8f), new Sec(-1.9f, .18f, .18f, .38f, 2.5f),
            new Sec(-2.3f, .12f, .12f, .33f, 2.2f), new Sec(-2.45f, .06f, .06f, .32f, 2f) }, 28));

        // Engine cover rising into the airbox behind the driver
        Save("Airbox", Loft(new[] {
            new Sec(0.06f, .12f, .09f, .80f, 2.4f), new Sec(-0.2f, .16f, .14f, .80f, 2.6f), new Sec(-0.6f, .15f, .13f, .74f, 2.6f),
            new Sec(-1.1f, .10f, .10f, .66f, 2.4f), new Sec(-1.6f, .05f, .06f, .58f, 2.2f), new Sec(-2.0f, .02f, .03f, .50f, 2f) }, 24));
        Save("SharkFin", Loft(new[] {
            new Sec(-0.35f, .01f, .03f, .94f, 2f), new Sec(-0.8f, .012f, .14f, .84f, 2f), new Sec(-1.3f, .012f, .17f, .74f, 2f),
            new Sec(-1.8f, .01f, .10f, .62f, 2f) }, 12));
        Save("CameraPod", Loft(new[] {
            new Sec(0.02f, .03f, .025f, .97f, 2f), new Sec(-0.08f, .07f, .035f, .97f, 2.5f), new Sec(-0.25f, .07f, .035f, .96f, 2.5f),
            new Sec(-0.33f, .03f, .02f, .95f, 2f) }, 16));

        foreach (int s in new[] { -1, 1 })
        {
            string side = s < 0 ? "L" : "R";
            Save("Sidepod" + side, Loft(new[] {
                new Sec(0.85f, .18f, .17f, .36f, 2.6f, s * .58f), new Sec(0.6f, .27f, .21f, .37f, 3f, s * .60f),
                new Sec(0.0f, .30f, .22f, .37f, 3f, s * .60f), new Sec(-0.6f, .28f, .19f, .34f, 3f, s * .56f),
                new Sec(-1.2f, .18f, .13f, .29f, 2.8f, s * .45f), new Sec(-1.6f, .08f, .07f, .25f, 2.4f, s * .32f) }, 24));
            Save("Mirror" + side, Loft(new[] {
                new Sec(0.62f, .02f, .015f, .72f, 2f, s * .47f), new Sec(0.58f, .09f, .04f, .72f, 3f, s * .47f),
                new Sec(0.52f, .09f, .04f, .72f, 3f, s * .47f), new Sec(0.49f, .06f, .03f, .72f, 2.5f, s * .47f) }, 16));
            Save("FrontEndplate" + side, Loft(new[] {
                new Sec(3.12f, .012f, .05f, .16f, 4f, s * .98f), new Sec(3.0f, .012f, .13f, .18f, 4f, s * .98f),
                new Sec(2.62f, .012f, .14f, .19f, 4f, s * .98f), new Sec(2.5f, .012f, .08f, .16f, 4f, s * .98f) }, 8));
            Save("RearEndplate" + side, Loft(new[] {
                new Sec(-2.12f, .012f, .15f, .62f, 4f, s * .51f), new Sec(-2.3f, .012f, .32f, .70f, 5f, s * .51f),
                new Sec(-2.75f, .012f, .32f, .74f, 5f, s * .51f), new Sec(-2.85f, .012f, .22f, .80f, 4f, s * .51f) }, 8));
        }

        Save("Floor", Loft(new[] {
            new Sec(1.0f, .45f, .015f, .07f, 6f), new Sec(0.7f, .75f, .015f, .07f, 8f), new Sec(-1.3f, .78f, .015f, .07f, 8f),
            new Sec(-1.75f, .55f, .015f, .08f, 6f) }, 16));
        Save("Diffuser", Loft(new[] { new Sec(-1.7f, .5f, .03f, .09f, 6f), new Sec(-2.35f, .52f, .12f, .20f, 6f) }, 16));
        Save("Cockpit", Loft(new[] {
            new Sec(0.78f, .05f, .02f, .71f, 2f), new Sec(0.6f, .22f, .03f, .71f, 2.5f), new Sec(0.1f, .25f, .03f, .73f, 2.5f),
            new Sec(-0.15f, .2f, .03f, .74f, 2.5f), new Sec(-0.22f, .05f, .02f, .74f, 2f) }, 20));

        // Wings: cambered aerofoil sections extruded across the span
        Save("FrontWingMain", Wing(1.95f, 0.30f, 0.14f, 0.05f));
        Save("FrontWingFlap", Wing(1.90f, 0.20f, 0.14f, 0.06f));
        Save("FrontWingFlap2", Wing(1.84f, 0.14f, 0.15f, 0.06f));
        Save("RearWingMain", Wing(1.0f, 0.38f, 0.13f, 0.06f));
        Save("RearWingFlap", Wing(1.0f, 0.22f, 0.13f, 0.06f));
        Save("BeamWing", Wing(0.9f, 0.16f, 0.15f, 0.04f));

        // Halo, suspension and rear wing pylon
        Save("Halo", Tube(new[] {
            new Vector3(-.27f, .68f, -.05f), new Vector3(-.28f, .82f, .2f), new Vector3(-.22f, .87f, .48f), new Vector3(-.1f, .88f, .62f),
            new Vector3(0, .875f, .66f), new Vector3(.1f, .88f, .62f), new Vector3(.22f, .87f, .48f), new Vector3(.28f, .82f, .2f),
            new Vector3(.27f, .68f, -.05f) }, 0.034f, 10));
        Save("HaloStrut", Tube(new[] { new Vector3(0, .875f, .66f), new Vector3(0, .80f, .78f), new Vector3(0, .66f, .86f) }, 0.03f, 8));
        Save("RearPylon", Tube(new[] { new Vector3(0, .35f, -2.15f), new Vector3(0, .62f, -2.35f), new Vector3(0, .84f, -2.42f) }, 0.035f, 8));
        var arms = new List<Vector3[]>();
        foreach (int s in new[] { -1, 1 })
        {
            foreach (float z in new[] { 1.8f, -1.8f })
            {
                float cx = z > 0 ? .30f : .26f, wx = z > 0 ? .66f : .62f;
                arms.Add(new[] { new Vector3(s * cx, .48f, z + .22f), new Vector3(s * wx, .47f, z) });
                arms.Add(new[] { new Vector3(s * cx, .48f, z - .22f), new Vector3(s * wx, .47f, z) });
                arms.Add(new[] { new Vector3(s * cx, .22f, z + .25f), new Vector3(s * wx, .22f, z) });
                arms.Add(new[] { new Vector3(s * cx, .22f, z - .25f), new Vector3(s * wx, .22f, z) });
                arms.Add(new[] { new Vector3(s * wx, .24f, z), new Vector3(s * (cx - .05f), .55f, z - .05f) });
            }
        }
        Save("Suspension", Tubes(arms, 0.017f, 6));

        // Tyres (rounded tread and shoulders), sidewall stripes, wheel covers
        Save("TyreFront", Lathe(TyreProfile(0.30f), 32));
        Save("TyreRear", Lathe(TyreProfile(0.40f), 32));
        Save("StripeFront", Lathe(new[] { new Vector2(.162f, .275f), new Vector2(.162f, .305f), new Vector2(-.162f, .305f), new Vector2(-.162f, .275f) }, 32, false));
        Save("StripeRear", Lathe(new[] { new Vector2(.212f, .275f), new Vector2(.212f, .305f), new Vector2(-.212f, .305f), new Vector2(-.212f, .275f) }, 32, false));
        Save("CoverFront", Lathe(new[] { new Vector2(.15f, .232f), new Vector2(.156f, .14f), new Vector2(.158f, .0f), new Vector2(-.158f, 0f), new Vector2(-.156f, .14f), new Vector2(-.15f, .232f) }, 32));
        Save("CoverRear", Lathe(new[] { new Vector2(.20f, .232f), new Vector2(.206f, .14f), new Vector2(.208f, .0f), new Vector2(-.208f, 0f), new Vector2(-.206f, .14f), new Vector2(-.20f, .232f) }, 32));

        AssetDatabase.SaveAssets();
    }

    public static GameObject Build(string name)
    {
        if (meshes.Count == 0) Prepare();
        var livery = Mat("F1_Livery", new Color(0.1f, 0.25f, 0.8f), 0.45f, 0.88f);
        var accent = Mat("F1_Accent", Color.white, 0.3f, 0.8f);
        var carbon = Mat("F1_Carbon", new Color(0.07f, 0.07f, 0.08f), 0.35f, 0.6f);
        var tyre = Mat("F1_Tyre", new Color(0.045f, 0.045f, 0.05f), 0f, 0.28f);
        var stripe = Mat("F1_Stripe", new Color(1f, 0.85f, 0.1f), 0f, 0.4f);
        var cover = Mat("F1_WheelCover", new Color(0.15f, 0.15f, 0.17f), 0.6f, 0.7f);
        var helmet = Mat("F1_Helmet", Color.white, 0.2f, 0.92f);
        var visor = Mat("F1_Visor", new Color(0.03f, 0.03f, 0.05f), 0.7f, 0.97f);
        var interior = Mat("F1_Interior", new Color(0.02f, 0.02f, 0.025f), 0f, 0.2f);
        var rain = Mat("F1_RainLight", new Color(0.6f, 0f, 0f), 0f, 0.9f, new Color(1f, 0.05f, 0.02f) * 2f);

        var root = new GameObject(name);
        var body = Child("Body", root);
        Part("Monocoque", body, livery);
        Part("Airbox", body, livery);
        Part("SharkFin", body, accent);
        Part("CameraPod", body, accent);
        Part("SidepodL", body, livery); Part("SidepodR", body, livery);
        Part("MirrorL", body, livery); Part("MirrorR", body, livery);
        Part("Floor", body, carbon);
        Part("Diffuser", body, carbon);
        Part("Cockpit", body, interior);
        Part("Halo", body, carbon);
        Part("HaloStrut", body, carbon);
        Part("Suspension", body, carbon);
        Part("RearPylon", body, carbon);

        // Sidepod inlets and airbox intake (dark openings)
        foreach (int s in new[] { -1, 1 })
            Prim(PrimitiveType.Sphere, "SidepodInlet", body, new Vector3(s * .58f, .36f, .855f), Quaternion.identity, new Vector3(.3f, .27f, .02f), interior);
        Prim(PrimitiveType.Sphere, "AirboxIntake", body, new Vector3(0, .80f, .065f), Quaternion.identity, new Vector3(.2f, .15f, .02f), interior);
        Prim(PrimitiveType.Cube, "RainLight", body, new Vector3(0, .42f, -2.47f), Quaternion.identity, new Vector3(.1f, .07f, .03f), rain);

        // Driver
        Prim(PrimitiveType.Sphere, "Helmet", body, new Vector3(0, .79f, .1f), Quaternion.identity, new Vector3(.25f, .26f, .28f), helmet);
        Prim(PrimitiveType.Sphere, "Visor", body, new Vector3(0, .80f, .2f), Quaternion.identity, new Vector3(.19f, .07f, .1f), visor);

        // Front wing: three elements and endplates (names start with FrontWing / FrontEndplate so damage can detach them)
        Part("FrontWingMain", body, carbon, new Vector3(0, .09f, 2.88f), Quaternion.Euler(-3f, 0, 0));
        Part("FrontWingFlap", body, livery, new Vector3(0, .15f, 2.70f), Quaternion.Euler(-16f, 0, 0), "FrontWingFlap");
        Part("FrontWingFlap2", body, accent, new Vector3(0, .21f, 2.60f), Quaternion.Euler(-28f, 0, 0), "FrontWingFlap2");
        Part("FrontEndplateL", body, livery);
        Part("FrontEndplateR", body, livery);

        // Rear wing and beam wing
        Part("RearWingMain", body, livery, new Vector3(0, .86f, -2.42f), Quaternion.Euler(-8f, 0, 0));
        Part("RearWingFlap", body, accent, new Vector3(0, .98f, -2.62f), Quaternion.Euler(-26f, 0, 0));
        Part("BeamWing", body, carbon, new Vector3(0, .42f, -2.32f), Quaternion.Euler(-10f, 0, 0));
        Part("RearEndplateL", body, livery);
        Part("RearEndplateR", body, livery);

        // Wheels: pivot at the wheel centre, spin about local X
        var wheelsRoot = Child("Wheels", root);
        foreach (var (wname, pos, front) in new[] {
            ("WheelFL", new Vector3(-.8f, .36f, 1.8f), true), ("WheelFR", new Vector3(.8f, .36f, 1.8f), true),
            ("WheelRL", new Vector3(-.78f, .36f, -1.8f), false), ("WheelRR", new Vector3(.78f, .36f, -1.8f), false) })
        {
            var w = Child(wname, wheelsRoot);
            w.transform.localPosition = pos;
            Part(front ? "TyreFront" : "TyreRear", w, tyre);
            Part(front ? "StripeFront" : "StripeRear", w, stripe);
            Part(front ? "CoverFront" : "CoverRear", w, cover);
        }

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, .45f, .2f);
        col.size = new Vector3(1.9f, .6f, 5.6f);
        return root;
    }

    // ---------- Mesh generators ----------

    // Superellipse cross-sections along Z, joined into a smooth closed hull with end caps.
    static Mesh Loft(Sec[] secs, int ring)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        foreach (var s in secs)
        {
            for (int i = 0; i < ring; i++)
            {
                float a = i / (float)ring * Mathf.PI * 2f;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                float x = s.hw * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / s.n);
                float y = s.hh * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 2f / s.n);
                v.Add(new Vector3(s.cx + x, s.cy + y, s.z));
            }
        }
        for (int k = 0; k < secs.Length - 1; k++)
        {
            int a0 = k * ring, b0 = (k + 1) * ring;
            for (int i = 0; i < ring; i++)
            {
                int i1 = (i + 1) % ring;
                // sections run front (+z) to back, so this winding faces outward
                t.AddRange(new[] { a0 + i, a0 + i1, b0 + i, b0 + i, a0 + i1, b0 + i1 });
            }
        }
        // Caps
        int fc = v.Count; v.Add(new Vector3(secs[0].cx, secs[0].cy, secs[0].z + 0.005f));
        for (int i = 0; i < ring; i++) t.AddRange(new[] { fc, (i + 1) % ring, i });
        int last = (secs.Length - 1) * ring;
        var ls = secs[secs.Length - 1];
        int bc = v.Count; v.Add(new Vector3(ls.cx, ls.cy, ls.z - 0.005f));
        for (int i = 0; i < ring; i++) t.AddRange(new[] { bc, last + i, last + (i + 1) % ring });
        return Finish(v, t);
    }

    // Cambered NACA-style aerofoil (leading edge at z = 0, chord running toward -Z) extruded along X.
    // thickness and camber are fractions of the chord.
    static Mesh Wing(float span, float chord, float thickness, float camber)
    {
        const int n = 14;
        var profile = new List<Vector2>();   // (z, y) loop: upper surface to the trailing edge, then lower surface back
        for (int i = 0; i <= n; i++) profile.Add(AerofoilPoint(i / (float)n, chord, thickness, camber, 1f));
        for (int i = n - 1; i >= 1; i--) profile.Add(AerofoilPoint(i / (float)n, chord, thickness, camber, -1f));
        var v = new List<Vector3>();
        var t = new List<int>();
        int m = profile.Count;
        foreach (float xs in new[] { -span / 2f, span / 2f })
            foreach (var p in profile) v.Add(new Vector3(xs, p.y, p.x));
        for (int i = 0; i < m; i++)
        {
            int i1 = (i + 1) % m;
            t.AddRange(new[] { i, m + i, i1, i1, m + i, m + i1 });
        }
        // End caps
        for (int i = 1; i < m - 1; i++)
        {
            t.AddRange(new[] { 0, i, i + 1 });
            t.AddRange(new[] { m, m + i + 1, m + i });
        }
        return Finish(v, t);
    }

    static Vector2 AerofoilPoint(float u, float chord, float thickness, float camber, float side)
    {
        float x = (1f - Mathf.Cos(u * Mathf.PI)) * 0.5f;      // cosine spacing: denser at the edges
        float yt = 5f * thickness * (0.2969f * Mathf.Sqrt(x) - 0.126f * x - 0.3516f * x * x + 0.2843f * x * x * x - 0.1015f * x * x * x * x);
        float yc = camber * 4f * x * (1f - x);
        return new Vector2(-x * chord, (yc + side * yt) * chord);
    }

    // Tube of the given radius following a polyline.
    static Mesh Tube(Vector3[] path, float radius, int sides)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        AddTube(v, t, path, radius, sides);
        return Finish(v, t);
    }

    static Mesh Tubes(List<Vector3[]> paths, float radius, int sides)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        foreach (var p in paths) AddTube(v, t, p, radius, sides);
        return Finish(v, t);
    }

    static void AddTube(List<Vector3> v, List<int> t, Vector3[] path, float radius, int sides)
    {
        int start = v.Count;
        Vector3 prevNormal = Vector3.zero;
        for (int k = 0; k < path.Length; k++)
        {
            Vector3 dir = (k < path.Length - 1 ? path[k + 1] - path[k] : path[k] - path[k - 1]).normalized;
            if (k > 0 && k < path.Length - 1) dir = ((path[k + 1] - path[k]).normalized + (path[k] - path[k - 1]).normalized).normalized;
            Vector3 n = prevNormal == Vector3.zero ? Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                                                   : Vector3.ProjectOnPlane(prevNormal, dir).normalized;
            prevNormal = n;
            Vector3 b = Vector3.Cross(dir, n);
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                v.Add(path[k] + (n * Mathf.Cos(a) + b * Mathf.Sin(a)) * radius);
            }
        }
        for (int k = 0; k < path.Length - 1; k++)
            for (int i = 0; i < sides; i++)
            {
                int a0 = start + k * sides + i, a1 = start + k * sides + (i + 1) % sides;
                int b0 = a0 + sides, b1 = a1 + sides;
                t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
            }
    }

    // Revolve a (axial x, radius) profile about the local X axis.
    static Mesh Lathe(Vector2[] profile, int segs, bool closeLoop = true)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        int m = profile.Length;
        for (int s = 0; s < segs; s++)
        {
            float a = s / (float)segs * Mathf.PI * 2f;
            float c = Mathf.Cos(a), sn = Mathf.Sin(a);
            foreach (var p in profile) v.Add(new Vector3(p.x, p.y * c, p.y * sn));
        }
        int edges = closeLoop ? m : m - 1;
        for (int s = 0; s < segs; s++)
        {
            int s1 = (s + 1) % segs;
            for (int i = 0; i < edges; i++)
            {
                int i1 = (i + 1) % m;
                int a0 = s * m + i, a1 = s * m + i1, b0 = s1 * m + i, b1 = s1 * m + i1;
                t.AddRange(new[] { a0, a1, b0, b0, a1, b1 });
            }
        }
        return Finish(v, t);
    }

    static Vector2[] TyreProfile(float width)
    {
        float h = width / 2f;
        return new[] {
            new Vector2(-h + .005f, .232f), new Vector2(-h, .27f), new Vector2(-h - .008f, .31f), new Vector2(-h - .004f, .34f),
            new Vector2(-h + .015f, .355f), new Vector2(-h + .045f, .3625f), new Vector2(0, .364f), new Vector2(h - .045f, .3625f),
            new Vector2(h - .015f, .355f), new Vector2(h + .004f, .34f), new Vector2(h + .008f, .31f), new Vector2(h, .27f),
            new Vector2(h - .005f, .232f) };
    }

    static Mesh Finish(List<Vector3> v, List<int> t)
    {
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Every part is rendered from both sides, so winding direction never shows a hole.
    static void MakeDoubleSided(Mesh m)
    {
        var v = new List<Vector3>(m.vertices);
        var tris = new List<int>(m.triangles);
        int count = v.Count, tc = tris.Count;
        v.AddRange(m.vertices);
        for (int i = 0; i < tc; i += 3) tris.AddRange(new[] { tris[i] + count, tris[i + 2] + count, tris[i + 1] + count });
        m.SetVertices(v);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
    }

    // ---------- Assets & helpers ----------

    static void Save(string name, Mesh mesh)
    {
        MakeDoubleSided(mesh);
        mesh.name = name;
        string path = MeshDir + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        meshes[name] = mesh;
    }

    static GameObject Part(string meshName, GameObject parent, Material mat, Vector3? pos = null, Quaternion? rot = null, string objName = null)
    {
        var g = new GameObject(objName ?? meshName);
        g.transform.SetParent(parent.transform, false);
        if (pos.HasValue) g.transform.localPosition = pos.Value;
        if (rot.HasValue) g.transform.localRotation = rot.Value;
        g.AddComponent<MeshFilter>().sharedMesh = meshes[meshName];
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
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

    static GameObject Child(string n, GameObject parent)
    {
        var g = new GameObject(n);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    public static Material Mat(string name, Color c, float metal, float smooth, Color? emission = null)
    {
        if (mats.TryGetValue(name, out var cached)) return cached;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission.Value);
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
    }
}
