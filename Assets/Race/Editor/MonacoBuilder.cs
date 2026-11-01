using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

// Tools > Build Monaco Circuit: a street circuit modelled on the Monte Carlo layout (3.337 km):
// start straight, Sainte Dévote, the climb past Beau Rivage, Massenet, Casino Square, Mirabeau,
// the Grand Hotel hairpin, Portier, the tunnel, the harbour chicane, Tabac, the Swimming Pool,
// La Rascasse and Anthony Noghès, with ~40 m of elevation change.
// Built from memory of the corner sequence, so the shape is close but not survey-accurate.
// Scene content: road with collider, kerbs, Armco barriers and catch fences, tunnel with a hotel on top,
// harbour with yachts, textured apartment blocks, a grand casino-style building, swimming pool,
// grandstands with crowds, pit lane and garages, start gantry and lights, palms, 20 cars and the race manager.
public static class MonacoBuilder
{
    const string Dir = "Assets/Race";
    const float HalfWidth = 5f, KerbEnd = 5.8f, BarrierIn = 6.3f, BarrierOut = 6.6f, WalkEnd = 9.5f;
    const float Spacing = 2f, TargetLength = 3337f, StartLineAt = 170f;
    const float PitEntry = 115f, PitLane = 10.5f;
    const int Cars = 20;

    struct Seg
    {
        public string name; public float len, angle, radius, height;
        public static Seg S(float len, float h, string name = null) => new Seg { len = len, height = h, name = name };
        public static Seg L(float deg, float r, float h, string name = null) => new Seg { angle = deg, radius = r, height = h, name = name };
        public static Seg R(float deg, float r, float h, string name = null) => new Seg { angle = -deg, radius = r, height = h, name = name };
    }

    // Driven from the exit of Anthony Noghès; the start line is 170 m along the first straight.
    static readonly Seg[] Layout =
    {
        Seg.S(330, 5, "Start straight"),
        Seg.R(90, 18, 7, "Sainte Devote"),
        Seg.S(110, 14),
        Seg.L(14, 150, 18, "Beau Rivage"),
        Seg.S(250, 36),
        Seg.L(105, 55, 41, "Massenet"),
        Seg.S(35, 42),
        Seg.R(80, 24, 43, "Casino Square"),
        Seg.S(140, 38, "Spelugues"),
        Seg.R(110, 15, 34, "Mirabeau"),
        Seg.S(75, 29),
        Seg.L(180, 9, 27, "Grand Hotel Hairpin"),
        Seg.S(65, 23),
        Seg.R(95, 14, 21, "Mirabeau Bas"),
        Seg.S(55, 17),
        Seg.R(92, 16, 14, "Portier"),
        Seg.S(80, 12),
        Seg.R(52, 380, 10, "Tunnel"),
        Seg.S(130, 6, "Tunnel exit"),
        Seg.L(42, 12, 5, "Nouvelle Chicane"),
        Seg.R(48, 12, 5),
        Seg.S(240, 4, "Quai"),
        Seg.L(72, 45, 4, "Tabac"),
        Seg.S(75, 4),
        Seg.L(32, 40, 4, "Piscine"),
        Seg.R(32, 40, 4),
        Seg.S(85, 4),
        Seg.R(36, 18, 4, "Piscine 2"),
        Seg.L(36, 18, 4),
        Seg.S(100, 4),
        Seg.R(150, 12, 4, "Rascasse"),
        Seg.S(45, 5),
        Seg.R(56, 15, 5, "Anthony Noghes"),
    };

    static Shader lit;
    static Random rng;
    static Dictionary<string, Material> mats;
    static Dictionary<string, Batch> batches;
    static Dictionary<string, float> markers;
    static float tunnelStart, tunnelEnd;
    static RaceTrack track;
    static List<(Vector3 c, float r)> reserved;

    [MenuItem("Tools/Build Monaco Circuit")]
    public static void Build()
    {
        rng = new Random(2026);
        mats = new Dictionary<string, Material>();
        batches = new Dictionary<string, Batch>();
        markers = new Dictionary<string, float>();
        reserved = new List<(Vector3, float)>();
        foreach (var f in new[] { "Materials", "Meshes", "Textures" })
            if (!AssetDatabase.IsValidFolder(Dir + "/" + f)) AssetDatabase.CreateFolder(Dir, f);
        if (!AssetDatabase.IsValidFolder(Dir + "/Meshes/Monaco")) AssetDatabase.CreateFolder(Dir + "/Meshes", "Monaco");
        lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) lit = Shader.Find("Standard");

        var old = GameObject.Find("Monaco");
        if (old != null) Object.DestroyImmediate(old);
        foreach (var name in new[] { "F1_Player" })
        {
            var o = GameObject.Find(name);
            if (o != null) Object.DestroyImmediate(o);
        }

        F1ModelBuilder.Prepare();
        var root = new GameObject("Monaco");
        track = root.AddComponent<RaceTrack>();
        track.points = GenerateCentreline(out float spacing);
        track.spacing = spacing;
        track.length = spacing * track.points.Length;
        track.halfWidth = HalfWidth;
        track.trackName = "Monte Carlo";

        Environment();
        BuildGround(root);
        BuildRoad(root);
        BuildBarriers(root);
        BuildSidewalks(root);
        BuildTunnel(root);
        BuildPits(root);
        BuildStands(root);
        BuildLandmarks(root);
        BuildHarbour(root);
        BuildBuildings(root);
        BuildPalms(root);
        var lights = BuildGantry(root, out var lightOff, out var lightOn);
        BuildGridMarks(root);
        foreach (var b in batches.Values) b.Flush(root.transform);

        var player = BuildPlayer();
        var ais = BuildRivals(root);
        var manager = new GameObject("RaceManager").AddComponent<RaceManager>();
        manager.transform.SetParent(root.transform, false);
        manager.track = track;
        manager.player = player.GetComponent<F1Car>();
        manager.ais = ais;
        manager.laps = 10;
        manager.startLights = lights;
        manager.lightOn = lightOn;
        manager.lightOff = lightOff;
        manager.useSession = true;
        manager.pitEntryDistance = PitEntry;
        manager.pitLaneOffset = PitLane;
        SetupCamera(player);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AddSceneToBuild(EditorSceneManager.GetActiveScene().path, -1);
        Debug.Log($"Monaco built: lap {track.length:0} m, {track.Count} points, min corner radius {MinRadius():0.0} m, " +
                  $"elevation {MinMaxHeight()}, closest approach of separate track sections {SelfClearance():0.0} m.");
    }

    // ---------- Centreline ----------

    // Walk the layout (straights use the given lengths) and return the end point; optionally the path.
    static Vector2 Walk(float[] lens, float ds, List<Vector2> path)
    {
        Vector2 pos = Vector2.zero;
        float heading = 60f * Mathf.Deg2Rad;
        for (int k = 0; k < Layout.Length; k++)
        {
            var seg = Layout[k];
            float length = seg.angle == 0 ? lens[k] : Mathf.Abs(seg.angle) * Mathf.Deg2Rad * seg.radius;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / ds));
            for (int j = 0; j < n; j++)
            {
                path?.Add(pos);
                float step = length / n;
                if (seg.angle != 0) heading += Mathf.Sign(seg.angle) * step / seg.radius;
                pos += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * step;
            }
        }
        return pos;
    }

    static float HeadingBefore(int segIndex)
    {
        float heading = 60f * Mathf.Deg2Rad;
        for (int k = 0; k < segIndex; k++) if (Layout[k].angle != 0) heading += Layout[k].angle * Mathf.Deg2Rad;
        return heading;
    }

    static float Clearance(List<Vector2> path, float ds)
    {
        int n = path.Count, gap = Mathf.RoundToInt(150f / ds);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
            for (int k = i + gap; k < i + n - gap; k++)
                best = Mathf.Min(best, (path[i] - path[k % n]).sqrMagnitude);
        return Mathf.Sqrt(best);
    }

    // Close the loop exactly by changing the lengths of two straights (keeping every corner as designed).
    // Every pair is tried; the one that keeps separate parts of the circuit furthest apart wins.
    static float[] SolveLengths()
    {
        var baseLens = new float[Layout.Length];
        for (int k = 0; k < Layout.Length; k++) baseLens[k] = Layout[k].len;
        Vector2 err = Walk(baseLens, 4f, null);
        float[] best = baseLens;
        float bestScore = float.MinValue;
        for (int a = 1; a < Layout.Length; a++)
        {
            if (Layout[a].angle != 0) continue;
            for (int b = a + 1; b < Layout.Length; b++)
            {
                if (Layout[b].angle != 0) continue;
                float ha = HeadingBefore(a), hb = HeadingBefore(b);
                Vector2 da = new Vector2(Mathf.Cos(ha), Mathf.Sin(ha)), db = new Vector2(Mathf.Cos(hb), Mathf.Sin(hb));
                float det = da.x * db.y - da.y * db.x;
                if (Mathf.Abs(det) < 0.2f) continue;
                // da·x + db·y = -err
                float x = (-err.x * db.y + err.y * db.x) / det;
                float y = (-da.x * err.y + da.y * err.x) / det;
                var lens = (float[])baseLens.Clone();
                lens[a] += x; lens[b] += y;
                if (lens[a] < 30f || lens[b] < 30f || lens[a] > 700f || lens[b] > 700f) continue;
                var path = new List<Vector2>();
                Walk(lens, 4f, path);
                float score = Mathf.Min(Clearance(path, 4f), 30f) - (Mathf.Abs(x) + Mathf.Abs(y)) * 0.01f;
                if (score > bestScore) { bestScore = score; best = lens; }
            }
        }
        return best;
    }

    static Vector3[] GenerateCentreline(out float spacing)
    {
        // Turtle walk through the layout at 0.5 m steps
        float[] lens = SolveLengths();
        var pts = new List<Vector3>();
        var sList = new List<float>();
        var segStart = new List<int>();
        var heights = new List<float>();
        Vector2 pos = Vector2.zero;
        float heading = 60f * Mathf.Deg2Rad, s = 0f, h = Layout[Layout.Length - 1].height;
        const float ds = 0.5f;
        for (int segIdx = 0; segIdx < Layout.Length; segIdx++)
        {
            var seg = Layout[segIdx];
            segStart.Add(pts.Count);
            float length = seg.angle == 0 ? lens[segIdx] : Mathf.Abs(seg.angle) * Mathf.Deg2Rad * seg.radius;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / ds));
            float h0 = h;
            for (int k = 0; k < n; k++)
            {
                pts.Add(new Vector3(pos.x, 0, pos.y));
                sList.Add(s);
                heights.Add(Mathf.Lerp(h0, seg.height, k / (float)n));
                float step = length / n;
                if (seg.angle != 0) heading += Mathf.Sign(seg.angle) * step / seg.radius;
                pos += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * step;
                s += step;
            }
            h = seg.height;
        }

        // Close the loop: spread the end-point error along the lap
        Vector3 err = new Vector3(pos.x, 0, pos.y) - pts[0];
        for (int i = 0; i < pts.Count; i++) pts[i] -= err * (sList[i] / s);

        // Scale to the real lap length
        float len = 0;
        for (int i = 0; i < pts.Count; i++) len += Vector3.Distance(pts[i], pts[(i + 1) % pts.Count]);
        float scale = TargetLength / len;
        for (int i = 0; i < pts.Count; i++) pts[i] *= scale;

        // Smooth the height profile and apply it
        int win = 40;
        for (int i = 0; i < pts.Count; i++)
        {
            float sum = 0;
            for (int k = -win; k <= win; k++) sum += heights[(i + k + pts.Count) % pts.Count];
            pts[i] = new Vector3(pts[i].x, sum / (2 * win + 1), pts[i].z);
        }

        // Resample evenly
        var cum = new float[pts.Count + 1];
        for (int i = 0; i < pts.Count; i++) cum[i + 1] = cum[i] + Flat(pts[(i + 1) % pts.Count] - pts[i]).magnitude;
        float total = cum[pts.Count];
        int count = Mathf.RoundToInt(total / Spacing);
        spacing = total / count;
        var outPts = new Vector3[count];
        int j = 0;
        for (int i = 0; i < count; i++)
        {
            float d = i * spacing;
            while (cum[j + 1] < d) j++;
            outPts[i] = Vector3.Lerp(pts[j], pts[(j + 1) % pts.Count], (d - cum[j]) / Mathf.Max(cum[j + 1] - cum[j], 1e-4f));
        }

        // Start line index, then marker distances relative to it
        int startDense = sList.FindIndex(v => v >= StartLineAt);
        float startD = cum[startDense];
        int startIdx = Mathf.RoundToInt(startD / spacing) % count;
        var rotated = new Vector3[count];
        for (int i = 0; i < count; i++) rotated[i] = outPts[(i + startIdx) % count];
        float lap = spacing * count;
        for (int k = 0; k < Layout.Length; k++)
        {
            float d = Mathf.Repeat(cum[segStart[k]] - startIdx * spacing, lap);
            string key = Layout[k].name ?? ("seg" + k);
            markers[key] = d;
            if (Layout[k].name == "Tunnel") tunnelStart = d - 20f;
            if (Layout[k].name == "Tunnel exit") tunnelEnd = d + lens[k] * scale * 0.6f;
        }
        return rotated;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    static float MinRadius()
    {
        float m = float.MaxValue;
        for (float d = 0; d < track.length; d += 2f) m = Mathf.Min(m, track.RadiusAt(d));
        return m;
    }

    static string MinMaxHeight()
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in track.points) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
        return $"{lo:0.0}..{hi:0.0} m";
    }

    // Smallest distance between points more than 150 m apart along the lap (detects crossings)
    static float SelfClearance()
    {
        int n = track.Count, gap = Mathf.RoundToInt(150f / track.spacing);
        float best = float.MaxValue;
        for (int i = 0; i < n; i += 2)
            for (int k = i + gap; k < i + n - gap; k += 2)
            {
                float d = Flat(track.points[i] - track.points[k % n]).magnitude;
                if (d < best) best = d;
            }
        return best;
    }

    static bool InTunnel(float d) => d >= tunnelStart && d <= tunnelEnd;
    static float D(int i) => i * track.spacing;
    static bool InRange(float d, float a, float b) { d = Mathf.Repeat(d - a, track.length); return d <= Mathf.Repeat(b - a, track.length); }
    static bool PitZone(float d) => InRange(d, track.length - 260f, 60f);
    static bool PitGap(float d) => InRange(d, 95f, 140f);
    static bool Corner(int i) => track.RadiusAt(D(i)) < 90f;

    static Vector3 Right(int i)
    {
        int n = track.Count;
        Vector3 dir = Flat(track.points[(i + 1) % n] - track.points[(i - 1 + n) % n]).normalized;
        return Vector3.Cross(Vector3.up, dir).normalized;
    }

    static Quaternion Facing(float d) { Vector3 t = track.TangentAt(d); t.y = 0; return Quaternion.LookRotation(t.normalized); }

    static float MinDistToTrack(Vector3 p)
    {
        float best = float.MaxValue;
        foreach (var q in track.points) { float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z); if (d < best) best = d; }
        return Mathf.Sqrt(best);
    }

    static bool IsReserved(Vector3 p, float r)
    {
        foreach (var (c, cr) in reserved) if (Flat(c - p).magnitude < cr + r) return true;
        return false;
    }

    // ---------- Environment ----------

    static void Environment()
    {
        var sun = Object.FindFirstObjectByType<Light>();
        if (sun == null || sun.type != LightType.Directional)
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        sun.intensity = 1.35f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 350f;
        RenderSettings.fogEndDistance = 1600f;
        RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.9f);
    }

    static void BuildGround(GameObject root)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "Ground";
        g.transform.SetParent(root.transform, false);
        g.transform.position = new Vector3(0, -0.5f, 0);
        g.transform.localScale = new Vector3(4000, 1, 4000);
        g.GetComponent<MeshRenderer>().sharedMaterial = Mat("MC_Ground", new Color(0.55f, 0.53f, 0.48f), 0f, 0.2f, Tex("Concrete"));
        GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
    }

    // ---------- Road, kerbs, barriers ----------

    static void BuildRoad(GameObject root)
    {
        var parent = Child("Road", root);
        var asphalt = Mat("MC_Asphalt", new Color(0.42f, 0.42f, 0.44f), 0f, 0.3f, Tex("Asphalt"));
        Extrude("RoadSurface", parent, new[] { new Vector2(-BarrierIn, 0.03f), new Vector2(BarrierIn, 0.03f) }, null, i => true, asphalt, 8f, true);
        var white = Mat("MC_LineWhite", new Color(0.95f, 0.95f, 0.93f), 0f, 0.35f);
        Extrude("EdgeLineL", parent, new[] { new Vector2(-HalfWidth, 0.04f), new Vector2(-HalfWidth + 0.15f, 0.04f) }, null, i => true, white, 4f, false);
        Extrude("EdgeLineR", parent, new[] { new Vector2(HalfWidth - 0.15f, 0.04f), new Vector2(HalfWidth, 0.04f) }, null, i => true, white, 4f, false);
        var red = Mat("MC_KerbRed", new Color(0.82f, 0.08f, 0.08f), 0f, 0.45f);
        var kw = Mat("MC_KerbWhite", new Color(0.95f, 0.95f, 0.95f), 0f, 0.45f);
        var kR = new[] { new Vector2(HalfWidth, 0.045f), new Vector2(KerbEnd, 0.075f) };
        var kL = new[] { new Vector2(-KerbEnd, 0.075f), new Vector2(-HalfWidth, 0.045f) };
        Extrude("KerbRedR", parent, kR, null, i => Corner(i) && (i / 2) % 2 == 0, red, 4f, false);
        Extrude("KerbWhiteR", parent, kR, null, i => Corner(i) && (i / 2) % 2 == 1, kw, 4f, false);
        Extrude("KerbRedL", parent, kL, null, i => Corner(i) && (i / 2) % 2 == 0, red, 4f, false);
        Extrude("KerbWhiteL", parent, kL, null, i => Corner(i) && (i / 2) % 2 == 1, kw, 4f, false);
    }

    static void BuildBarriers(GameObject root)
    {
        var parent = Child("Barriers", root);
        var steel = Mat("MC_Armco", new Color(0.72f, 0.74f, 0.76f), 0.75f, 0.55f);
        var red = Mat("MC_BarrierRed", new Color(0.8f, 0.1f, 0.1f), 0.2f, 0.5f);
        var white = Mat("MC_BarrierWhite", new Color(0.93f, 0.93f, 0.93f), 0.2f, 0.5f);
        const float h = 1.05f;
        var right = new[] { new Vector2(BarrierIn, 0), new Vector2(BarrierIn, h), new Vector2(BarrierOut, h), new Vector2(BarrierOut, 0) };
        var left = new[] { new Vector2(-BarrierOut, 0), new Vector2(-BarrierOut, h), new Vector2(-BarrierIn, h), new Vector2(-BarrierIn, 0) };
        // Visual: painted red/white near corners, bare steel elsewhere; a gap at the pit entry on the right
        Extrude("BarrierR_Steel", parent, right, null, i => !Corner(i) && !PitGap(D(i)), steel, 4f, false);
        Extrude("BarrierR_Red", parent, right, null, i => Corner(i) && (i / 3) % 2 == 0 && !PitGap(D(i)), red, 4f, false);
        Extrude("BarrierR_White", parent, right, null, i => Corner(i) && (i / 3) % 2 == 1 && !PitGap(D(i)), white, 4f, false);
        Extrude("BarrierL_Steel", parent, left, null, i => !Corner(i), steel, 4f, false);
        Extrude("BarrierL_Red", parent, left, null, i => Corner(i) && (i / 3) % 2 == 0, red, 4f, false);
        Extrude("BarrierL_White", parent, left, null, i => Corner(i) && (i / 3) % 2 == 1, white, 4f, false);
        // Collision is continuous on both sides (finished AI cars are kinematic and pass the pit gap)
        var colR = Extrude("BarrierR_Collider", parent, right, null, i => true, null, 4f, true);
        var colL = Extrude("BarrierL_Collider", parent, left, null, i => true, null, 4f, true);
        Object.DestroyImmediate(colR.GetComponent<MeshRenderer>());
        Object.DestroyImmediate(colL.GetComponent<MeshRenderer>());

        // Catch fencing: see-through mesh and posts, not in the tunnel
        var fence = TransparentMat("MC_Fence", new Color(0.75f, 0.78f, 0.8f, 0.28f));
        float fx = BarrierOut - 0.05f;
        Extrude("FenceR", parent, new[] { new Vector2(fx, h), new Vector2(fx, 4.2f) }, null, i => !InTunnel(D(i)) && !PitGap(D(i)), fence, 4f, false);
        Extrude("FenceL", parent, new[] { new Vector2(-fx, h), new Vector2(-fx, 4.2f) }, null, i => !InTunnel(D(i)), fence, 4f, false);
        var posts = GetBatch("Posts", Mat("MC_Post", new Color(0.35f, 0.37f, 0.4f), 0.6f, 0.4f));
        for (int i = 0; i < track.Count; i += 2)
        {
            if (InTunnel(D(i))) continue;
            foreach (int sd in new[] { -1, 1 })
            {
                if (sd > 0 && PitGap(D(i))) continue;
                Vector3 p = track.points[i] + Right(i) * (sd * (BarrierOut + 0.05f)) + Vector3.up * 2.6f;
                posts.Box(p, new Vector3(0.08f, 3.2f, 0.08f), Quaternion.identity);
            }
        }
    }

    static void BuildSidewalks(GameObject root)
    {
        var parent = Child("Pavements", root);
        var stone = Mat("MC_Pavement", new Color(0.78f, 0.75f, 0.7f), 0f, 0.25f, Tex("Concrete"));
        var wall = Mat("MC_RetainingWall", new Color(0.86f, 0.8f, 0.68f), 0f, 0.2f, Tex("Concrete"));
        Func<int, bool> rightOk = i => !InTunnel(D(i)) && !PitZone(D(i)) && !PitGap(D(i));
        Func<int, bool> leftOk = i => !InTunnel(D(i));
        Extrude("PavementR", parent, new[] { new Vector2(BarrierOut, 0.15f), new Vector2(WalkEnd, 0.15f) }, null, rightOk, stone, 4f, false);
        Extrude("PavementL", parent, new[] { new Vector2(-WalkEnd, 0.15f), new Vector2(-BarrierOut, 0.15f) }, null, leftOk, stone, 4f, false);
        // Retaining walls from the street down to the ground where the road climbs the hill
        Extrude("WallR", parent, new[] { new Vector2(WalkEnd, 0.15f), new Vector2(WalkEnd, -0.3f) }, new[] { false, true }, rightOk, wall, 6f, false);
        Extrude("WallL", parent, new[] { new Vector2(-WalkEnd, -0.3f), new Vector2(-WalkEnd, 0.15f) }, new[] { true, false }, leftOk, wall, 6f, false);
        Extrude("UnderRoadR", parent, new[] { new Vector2(BarrierOut, 0f), new Vector2(BarrierOut, -0.3f) }, new[] { false, true }, i => !rightOk(i), wall, 6f, false);
    }

    static void BuildTunnel(GameObject root)
    {
        var parent = Child("Tunnel", root);
        var concrete = Mat("MC_TunnelConcrete", new Color(0.62f, 0.6f, 0.56f), 0f, 0.25f, Tex("Concrete"));
        Extrude("TunnelShell", parent, new[] { new Vector2(-7.4f, 0f), new Vector2(-7.4f, 6.5f), new Vector2(7.4f, 6.5f), new Vector2(7.4f, 0f) },
                null, i => InTunnel(D(i)), concrete, 6f, false);
        var lamp = GetBatch("TunnelLights", Mat("MC_TunnelLight", new Color(1f, 0.9f, 0.7f), 0f, 0.5f, null, new Color(1f, 0.85f, 0.6f) * 3f));
        var hotel = GetBatch("Facade_Hotel", Mat("MC_FacadeHotel", new Color(0.95f, 0.92f, 0.86f), 0f, 0.3f, Tex("Facade")), 12f);
        var roof = GetBatch("Roofs", Mat("MC_Roof", new Color(0.45f, 0.42f, 0.4f), 0f, 0.2f));
        for (int i = 0; i < track.Count; i++)
        {
            float d = D(i);
            if (!InTunnel(d)) continue;
            Quaternion rot = Facing(d);
            if (i % 3 == 0)
                foreach (int sd in new[] { -1, 1 })
                    lamp.Box(track.points[i] + Right(i) * (sd * 3.5f) + Vector3.up * 6.4f, new Vector3(0.5f, 0.08f, 1.4f), rot);
            if (i % 8 == 0)
            {
                // The hotel block built over the tunnel
                float hgt = 26f + (float)rng.NextDouble() * 10f;
                Vector3 c = track.points[i] + Right(i) * 6f + Vector3.up * (6.6f + hgt / 2f);
                hotel.Box(c, new Vector3(34f, hgt, 17f), rot);
                roof.Box(c + Vector3.up * (hgt / 2f + 0.3f), new Vector3(34.4f, 0.6f, 17.4f), rot);
                reserved.Add((c, 18f));
            }
        }
    }

    // ---------- Pits, stands, landmarks ----------

    static void BuildPits(GameObject root)
    {
        var parent = Child("PitLane", root);
        var asphalt = Mat("MC_PitAsphalt", new Color(0.36f, 0.36f, 0.38f), 0f, 0.3f, Tex("Asphalt"));
        Extrude("PitLaneSurface", parent, new[] { new Vector2(BarrierOut, 0.03f), new Vector2(15f, 0.03f) }, null,
                i => PitZone(D(i)) || PitGap(D(i)) || InRange(D(i), 60f, 95f), asphalt, 8f, false);
        Extrude("PitLaneEdge", parent, new[] { new Vector2(15f, 0.03f), new Vector2(15f, -0.3f) }, new[] { false, true },
                i => PitZone(D(i)) || PitGap(D(i)) || InRange(D(i), 60f, 95f), asphalt, 8f, false);

        var wall = GetBatch("Facade_Pits", Mat("MC_PitBuilding", new Color(0.92f, 0.93f, 0.95f), 0.1f, 0.5f), 6f);
        var door = GetBatch("GarageDoors", Mat("MC_GarageDoor", new Color(0.12f, 0.13f, 0.16f), 0.4f, 0.6f));
        var glass = GetBatch("PitGlass", Mat("MC_PitGlass", new Color(0.15f, 0.28f, 0.42f), 0.6f, 0.9f));
        int team = 0, step = 0;
        for (float d = -250f; d <= 40f; d += 4f, step++)
        {
            int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
            Quaternion rot = Facing(d);
            Vector3 b = track.points[i] + Right(i) * 20f;
            float top = b.y + 6.2f;
            wall.Box(new Vector3(b.x, (top - 0.3f) / 2f, b.z), new Vector3(10f, top + 0.3f, 4.05f), rot);
            glass.Box(b + Vector3.up * 5.2f + Right(i) * -0.02f, new Vector3(10.06f, 1.4f, 3.6f), rot);
            if (step % 7 == 0 && team < GameSession.Teams.Length)
            {
                var t = GameSession.Teams[team++];
                GetBatch("PitBanner" + team, Mat("MC_TeamBanner" + team, t.primary, 0.2f, 0.6f))
                    .Box(b + Right(i) * -5.1f + Vector3.up * 4.3f, new Vector3(0.1f, 1.2f, 20f), rot);
            }
            door.Box(b + Right(i) * -5.02f + Vector3.up * 1.7f, new Vector3(0.06f, 3.4f, 3.4f), rot);
        }
        reserved.Add((track.PointAt(-105f) + track.RightAt(-105f) * 20f, 150f));
    }

    static void BuildStands(GameObject root)
    {
        (float a, float b, int side)[] zones =
        {
            (-175f, -25f, -1),
            (markers["Sainte Devote"] - 15f, markers["Sainte Devote"] + 45f, -1),
            (markers["Tabac"] - 10f, markers["Tabac"] + 60f, 1),
            (markers["Piscine"] - 20f, markers["Piscine 2"] + 30f, -1),
            (markers["Rascasse"] - 20f, markers["Rascasse"] + 40f, -1),
        };
        var concrete = GetBatch("Stands", Mat("MC_StandConcrete", new Color(0.7f, 0.7f, 0.68f), 0f, 0.25f, Tex("Concrete")), 6f);
        var seats = GetBatch("Seats", Mat("MC_Seats", new Color(0.1f, 0.25f, 0.6f), 0.1f, 0.5f));
        var canopy = GetBatch("StandRoof", Mat("MC_StandRoof", new Color(0.95f, 0.95f, 0.97f), 0.2f, 0.6f));
        Color[] shirts = { Color.red, Color.white, new Color(0.1f, 0.3f, 0.9f), Color.yellow, new Color(0.1f, 0.6f, 0.2f), new Color(1f, 0.5f, 0f), new Color(0.12f, 0.12f, 0.12f), new Color(0.6f, 0.2f, 0.7f) };
        const int tiers = 9;
        foreach (var z in zones)
        {
            for (float d = z.a; d <= z.b; d += 4f)
            {
                int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
                Quaternion rot = Facing(d);
                Vector3 r = Right(i) * z.side;
                Vector3 baseP = track.points[i];
                for (int k = 0; k < tiers; k++)
                {
                    float top = 0.6f + k * 0.75f, off = WalkEnd + 0.8f + k * 1.1f;
                    concrete.Box(baseP + r * off + Vector3.up * (top / 2f), new Vector3(1.1f, top, 4.05f), rot);
                    seats.Box(baseP + r * (off - 0.2f) + Vector3.up * (top + 0.2f), new Vector3(0.5f, 0.4f, 3.9f), rot);
                    for (int f = 0; f < 4; f++)
                    {
                        if (rng.NextDouble() < 0.2) continue;
                        int c = rng.Next(shirts.Length);
                        GetBatch("Fans" + c, Mat("MC_Fan" + c, shirts[c], 0f, 0.4f))
                            .Box(baseP + r * (off + 0.15f) + Vector3.up * (top + 0.45f) + rot * Vector3.forward * (-1.5f + f), new Vector3(0.42f, 0.7f, 0.42f), rot);
                    }
                }
                float back = WalkEnd + 0.8f + tiers * 1.1f;
                concrete.Box(baseP + r * back + Vector3.up * 4f, new Vector3(0.4f, 8f, 4.05f), rot);
                canopy.Box(baseP + r * (back - 5f) + Vector3.up * 9.6f, new Vector3(11f, 0.25f, 4.1f), rot * Quaternion.Euler(0, 0, z.side * 6f));
                // fill under the stand down to the ground
                concrete.Box(new Vector3(0, baseP.y / 2f - 0.15f, 0) + Flat(baseP + r * (WalkEnd + 6f)), new Vector3(11.5f, baseP.y + 0.3f, 4.05f), rot);
                reserved.Add((baseP + r * (WalkEnd + 6f), 8f));
            }
        }
    }

    static void BuildLandmarks(GameObject root)
    {
        // A grand casino-style building with a central dome and twin towers, gardens in front
        float d = markers["Casino Square"] + 10f;
        int i = Mathf.RoundToInt(d / track.spacing) % track.Count;
        Quaternion rot = Facing(d);
        Vector3 c = track.points[i] - Right(i) * (WalkEnd + 26f);
        c.y = track.points[i].y;
        var cream = Mat("MC_Casino", new Color(0.96f, 0.9f, 0.76f), 0.05f, 0.35f, Tex("Facade"));
        var facade = GetBatch("Facade_Casino", cream, 10f);
        var roofMat = Mat("MC_CasinoRoof", new Color(0.35f, 0.5f, 0.48f), 0.4f, 0.5f);
        var roof = GetBatch("CasinoRoof", roofMat);
        // Blocks run from the ground up to their height above the street
        Vector3 ground = new Vector3(c.x, 0f, c.z);
        float mainTop = c.y + 26f, towerTop = c.y + 38f;
        facade.Box(ground + Vector3.up * ((mainTop - 0.3f) / 2f), new Vector3(64f, mainTop + 0.3f, 22f), rot);
        facade.Box(ground + rot * new Vector3(-26f, 0, 0) + Vector3.up * ((towerTop - 0.3f) / 2f), new Vector3(10f, towerTop + 0.3f, 12f), rot);
        facade.Box(ground + rot * new Vector3(26f, 0, 0) + Vector3.up * ((towerTop - 0.3f) / 2f), new Vector3(10f, towerTop + 0.3f, 12f), rot);
        roof.Box(ground + Vector3.up * (mainTop + 0.3f), new Vector3(64.6f, 0.6f, 22.6f), rot);
        foreach (var (x, h, r) in new[] { (0f, mainTop + 1.5f, 9f), (-26f, towerTop + 0.5f, 5f), (26f, towerTop + 0.5f, 5f) })
        {
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(dome.GetComponent<Collider>());
            dome.name = "Dome";
            dome.transform.SetParent(root.transform, false);
            dome.transform.position = ground + rot * new Vector3(x, 0, 0) + Vector3.up * h;
            dome.transform.localScale = new Vector3(r * 2f, r * 1.4f, r * 2f);
            dome.GetComponent<MeshRenderer>().sharedMaterial = roofMat;
        }
        var lawn = GetBatch("Gardens", Mat("MC_Lawn", new Color(0.3f, 0.5f, 0.22f), 0f, 0.2f));
        lawn.Box(track.points[i] - Right(i) * (WalkEnd + 8f) + Vector3.up * 0.1f, new Vector3(14f, 0.3f, 50f), rot);
        reserved.Add((c, 40f));

        // Swimming pool beside the Piscine section
        float pd = markers["Piscine"] + 50f;
        int pi = Mathf.RoundToInt(pd / track.spacing) % track.Count;
        Vector3 pc = track.points[pi] - Right(pi) * (WalkEnd + 30f);
        Quaternion prot = Facing(pd);
        var deck = GetBatch("PoolDeck", Mat("MC_PoolDeck", new Color(0.92f, 0.9f, 0.86f), 0f, 0.3f));
        deck.Box(new Vector3(pc.x, track.points[pi].y / 2f, pc.z), new Vector3(34f, track.points[pi].y + 0.4f, 60f), prot);
        GetBatch("PoolWater", Mat("MC_PoolWater", new Color(0.1f, 0.55f, 0.75f), 0.1f, 0.95f))
            .Box(new Vector3(pc.x, track.points[pi].y + 0.22f, pc.z), new Vector3(22f, 0.1f, 46f), prot);
        reserved.Add((pc, 34f));
    }

    static void BuildHarbour(GameObject root)
    {
        // Water in the bay enclosed by the start straight and the quayside sections
        Vector3 a = track.PointAt(-60f), q = track.PointAt(markers["Piscine"]);
        Vector3 centre = Vector3.Lerp(a, q, 0.5f) + track.RightAt(-60f) * 20f;
        centre.y = 0f;
        Quaternion rot = Facing(-60f);
        var water = GetBatch("Harbour", Mat("MC_Sea", new Color(0.06f, 0.28f, 0.42f), 0.2f, 0.95f));
        var cells = new List<Vector3>();
        for (int x = -16; x <= 16; x++)
            for (int z = -16; z <= 16; z++)
            {
                Vector3 p = centre + rot * new Vector3(x * 10f, 0, z * 10f);
                if (MinDistToTrack(p) < 16f || IsReserved(p, 4f)) continue;
                water.Box(p + Vector3.up * 0.05f, new Vector3(10.02f, 0.1f, 10.02f), rot, true, false);
                cells.Add(p);
                if (MinDistToTrack(p) > 26f) reserved.Add((p, 6f));
            }

        // Yachts moored in the harbour
        var hull = GetBatch("YachtHull", Mat("MC_YachtHull", new Color(0.97f, 0.97f, 0.97f), 0.2f, 0.85f));
        var deckB = GetBatch("YachtDeck", Mat("MC_YachtDeck", new Color(0.6f, 0.43f, 0.28f), 0f, 0.5f));
        var win = GetBatch("YachtWindows", Mat("MC_YachtGlass", new Color(0.05f, 0.08f, 0.12f), 0.6f, 0.95f));
        int placed = 0;
        foreach (var p in cells)
        {
            if (placed >= 34 || rng.NextDouble() < 0.6 || MinDistToTrack(p) < 30f) continue;
            float len = 18f + (float)rng.NextDouble() * 32f;
            Quaternion yr = rot * Quaternion.Euler(0, rng.Next(2) * 90f + (float)(rng.NextDouble() * 10 - 5), 0);
            hull.Box(p + Vector3.up * 1.2f, new Vector3(len * 0.22f, 2.4f, len), yr);
            hull.Box(p + Vector3.up * 1.2f + yr * Vector3.forward * (len * 0.55f), new Vector3(len * 0.12f, 2.2f, len * 0.14f), yr * Quaternion.Euler(0, 45, 0));
            deckB.Box(p + Vector3.up * 2.45f, new Vector3(len * 0.2f, 0.1f, len * 0.9f), yr);
            hull.Box(p + Vector3.up * 3.4f + yr * Vector3.back * (len * 0.1f), new Vector3(len * 0.16f, 1.8f, len * 0.45f), yr);
            win.Box(p + Vector3.up * 3.5f + yr * Vector3.back * (len * 0.1f), new Vector3(len * 0.161f, 0.7f, len * 0.4f), yr);
            if (len > 32f) hull.Box(p + Vector3.up * 5f + yr * Vector3.back * (len * 0.12f), new Vector3(len * 0.12f, 1.4f, len * 0.25f), yr);
            placed++;
        }
    }

    // ---------- City ----------

    static void BuildBuildings(GameObject root)
    {
        Color[] wallColours = { new Color(0.95f, 0.88f, 0.74f), new Color(0.96f, 0.8f, 0.68f), new Color(0.98f, 0.95f, 0.88f),
                                new Color(0.93f, 0.74f, 0.62f), new Color(0.97f, 0.9f, 0.6f), new Color(0.85f, 0.86f, 0.82f) };
        var facades = new Batch[wallColours.Length];
        for (int k = 0; k < wallColours.Length; k++)
            facades[k] = GetBatch("Facade_" + k, Mat("MC_Facade" + k, wallColours[k], 0f, 0.3f, Tex("Facade")), 12f);
        var roof = GetBatch("Roofs", Mat("MC_Roof", new Color(0.45f, 0.42f, 0.4f), 0f, 0.2f));
        var terracotta = GetBatch("RoofsTerracotta", Mat("MC_Terracotta", new Color(0.66f, 0.34f, 0.24f), 0f, 0.3f));
        var balcony = GetBatch("Balconies", Mat("MC_Balcony", new Color(0.97f, 0.97f, 0.95f), 0f, 0.4f));
        Color[] awnings = { new Color(0.75f, 0.12f, 0.12f), new Color(0.1f, 0.35f, 0.25f), new Color(0.12f, 0.2f, 0.5f), new Color(0.85f, 0.6f, 0.15f) };
        var placed = new List<(Vector3 c, float r)>();
        int count = 0;

        for (int i = 0; i < track.Count; i += 6)
        {
            float d = D(i);
            if (InTunnel(d)) continue;
            foreach (int side in new[] { -1, 1 })
            {
                if (rng.NextDouble() < 0.12) continue;
                float depth = 12f + (float)rng.NextDouble() * 14f, width = 14f + (float)rng.NextDouble() * 14f;
                float off = WalkEnd + 2.5f + depth / 2f;
                Vector3 r = Right(i) * side;
                Vector3 c = track.points[i] + r * off;
                float radius = Mathf.Max(depth, width) * 0.5f;
                if (IsReserved(c, radius)) continue;
                if (MinDistToTrack(c) < off - 0.5f || MinDistToTrack(c) - radius * 0.75f < WalkEnd + 1f) continue;
                bool overlap = false;
                foreach (var (pc, pr) in placed) if (Flat(pc - c).magnitude < (pr + radius) * 0.8f) { overlap = true; break; }
                if (overlap) continue;
                placed.Add((c, radius));

                float floors = rng.Next(5, 15);
                float height = floors * 3.2f;
                float baseY = -0.3f, topY = track.points[i].y + height;
                Quaternion rot = Facing(d);
                var fac = facades[rng.Next(facades.Length)];
                Vector3 centre = new Vector3(c.x, (baseY + topY) / 2f, c.z);
                fac.Box(centre, new Vector3(width, topY - baseY, depth), rot, false, false);
                (rng.NextDouble() < 0.4 ? terracotta : roof).Box(new Vector3(c.x, topY + 0.3f, c.z), new Vector3(width + 0.4f, 0.6f, depth + 0.4f), rot);
                if (rng.NextDouble() < 0.5)
                    roof.Box(new Vector3(c.x, topY + 1.5f, c.z) + rot * new Vector3((float)(rng.NextDouble() - 0.5) * width * 0.4f, 0, 0), new Vector3(4f, 2.4f, 4f), rot);

                // Balconies on the street-facing side, from the street level up
                Vector3 faceN = -r;
                float streetY = track.points[i].y + 0.15f;
                for (float y = streetY + 4.4f; y < topY - 1f; y += 3.2f)
                    balcony.Box(new Vector3(c.x, y, c.z) + faceN * (depth / 2f + 0.45f), new Vector3(width * 0.75f, 0.15f, 0.9f), rot);
                // Shop awning at street level
                int ac = rng.Next(awnings.Length);
                GetBatch("Awning" + ac, Mat("MC_Awning" + ac, awnings[ac], 0f, 0.4f))
                    .Box(new Vector3(c.x, streetY + 3f, c.z) + faceN * (depth / 2f + 0.9f), new Vector3(width * 0.8f, 0.12f, 1.8f), rot * Quaternion.Euler(side * 12f, 0, 0));
                count++;
            }
        }

        // A second ring of taller blocks behind, so the city fills the hillside
        for (int i = 3; i < track.Count; i += 10)
        {
            float d = D(i);
            foreach (int side in new[] { -1, 1 })
            {
                float off = 48f + (float)rng.NextDouble() * 30f;
                Vector3 c = track.points[i] + Right(i) * side * off;
                float w = 18f + (float)rng.NextDouble() * 16f, dep = 16f + (float)rng.NextDouble() * 14f;
                float radius = Mathf.Max(w, dep) * 0.55f;
                if (MinDistToTrack(c) < radius + WalkEnd + 3f || IsReserved(c, radius)) continue;
                bool overlap = false;
                foreach (var (pc, pr) in placed) if (Flat(pc - c).magnitude < (pr + radius) * 0.85f) { overlap = true; break; }
                if (overlap) continue;
                placed.Add((c, radius));
                float topY = track.points[i].y + 20f + (float)rng.NextDouble() * 45f;
                Quaternion rot = Facing(d);
                facades[rng.Next(facades.Length)].Box(new Vector3(c.x, (topY - 0.3f) / 2f, c.z), new Vector3(w, topY + 0.3f, dep), rot, false, false);
                roof.Box(new Vector3(c.x, topY + 0.3f, c.z), new Vector3(w + 0.4f, 0.6f, dep + 0.4f), rot);
                count++;
            }
        }
        Debug.Log("Monaco: placed " + count + " buildings.");
    }

    static void BuildPalms(GameObject root)
    {
        var trunk = GetBatch("PalmTrunks", Mat("MC_PalmTrunk", new Color(0.45f, 0.36f, 0.25f), 0f, 0.3f));
        var frond = GetBatch("PalmFronds", Mat("MC_PalmFrond", new Color(0.2f, 0.45f, 0.18f), 0f, 0.35f));
        for (int i = 0; i < track.Count; i += 9)
        {
            float d = D(i);
            if (InTunnel(d) || rng.NextDouble() < 0.45) continue;
            foreach (int side in new[] { -1, 1 })
            {
                if (side > 0 && (PitZone(d) || PitGap(d))) continue;
                if (rng.NextDouble() < 0.5) continue;
                Vector3 p = track.points[i] + Right(i) * side * (WalkEnd - 0.9f);
                if (MinDistToTrack(p) < WalkEnd - 1.2f || IsReserved(p, 0.5f)) continue;
                float h = 6f + (float)rng.NextDouble() * 3f;
                trunk.Box(p + Vector3.up * (0.15f + h / 2f), new Vector3(0.35f, h, 0.35f), Quaternion.Euler(0, (float)rng.NextDouble() * 90f, 0));
                for (int k = 0; k < 7; k++)
                {
                    Quaternion fr = Quaternion.Euler(0, k * 360f / 7f, 0) * Quaternion.Euler(28f, 0, 0);
                    frond.Box(p + Vector3.up * (h + 0.1f) + fr * new Vector3(0, 0, 1.4f), new Vector3(0.6f, 0.05f, 3f), fr);
                }
            }
        }
    }

    // ---------- Start ----------

    static Renderer[] BuildGantry(GameObject root, out Material off, out Material on)
    {
        var g = Child("StartGantry", root);
        g.transform.SetPositionAndRotation(track.PointAt(0), Facing(0));
        var steel = Mat("MC_GantrySteel", new Color(0.22f, 0.24f, 0.28f), 0.6f, 0.5f);
        float x = BarrierOut + 0.6f;
        LocalBox("PillarL", g, new Vector3(-x, 3.6f, 0), new Vector3(0.6f, 7.2f, 0.6f), steel, true);
        LocalBox("PillarR", g, new Vector3(x, 3.6f, 0), new Vector3(0.6f, 7.2f, 0.6f), steel, true);
        LocalBox("Beam", g, new Vector3(0, 7f, 0), new Vector3(2 * x + 0.6f, 1.1f, 0.8f), steel, false);
        LocalBox("LightPanel", g, new Vector3(0, 5.9f, -0.2f), new Vector3(7.8f, 1.3f, 0.5f), Mat("MC_LightPanel", new Color(0.04f, 0.04f, 0.04f), 0f, 0.3f), false);
        off = Mat("MC_StartLightOff", new Color(0.18f, 0.03f, 0.03f), 0f, 0.8f);
        on = Mat("MC_StartLightOn", new Color(1f, 0.05f, 0.03f), 0f, 0.8f, null, new Color(1f, 0.05f, 0.02f) * 4f);
        var lights = new Renderer[5];
        for (int k = 0; k < 5; k++)
        {
            var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(l.GetComponent<Collider>());
            l.name = "StartLight" + (k + 1);
            l.transform.SetParent(g.transform, false);
            l.transform.localPosition = new Vector3(-2.8f + k * 1.4f, 5.9f, -0.5f);
            l.transform.localScale = Vector3.one * 0.8f;
            lights[k] = l.GetComponent<Renderer>();
            lights[k].sharedMaterial = off;
        }
        return lights;
    }

    static void BuildGridMarks(GameObject root)
    {
        var white = GetBatch("GridMarks", Mat("MC_LineWhite", Color.white, 0f, 0.35f));
        var black = GetBatch("Chequer", Mat("MC_CheckBlack", new Color(0.04f, 0.04f, 0.04f), 0f, 0.3f));
        Vector3 p = track.PointAt(0), fwd = Flat(track.TangentAt(0)).normalized, right = track.RightAt(0);
        Quaternion rot = Quaternion.LookRotation(fwd);
        for (int row = 0; row < 2; row++)
            for (int c = 0; c < 10; c++)
                ((row + c) % 2 == 0 ? black : white).Box(p + right * (-4.5f + c) + fwd * (row - 0.5f) + Vector3.up * 0.045f, new Vector3(1f, 0.01f, 1f), rot);
        for (int k = 0; k < Cars; k++)
        {
            float d = -(10f + 8f * k) + 2.6f;
            float lat = Mathf.Min(2.4f, HalfWidth - 2.2f) * (k % 2 == 0 ? -1f : 1f);
            white.Box(track.PointAt(d) + track.RightAt(d) * lat + Vector3.up * 0.045f, new Vector3(2.4f, 0.01f, 0.22f), Facing(d));
        }
    }

    // ---------- Cars ----------

    static GameObject BuildPlayer()
    {
        var car = F1ModelBuilder.Build("F1_Player");
        float d = -10f, lat = -Mathf.Min(2.4f, HalfWidth - 2.2f);
        car.transform.SetPositionAndRotation(track.PointAt(d) + track.RightAt(d) * lat + Vector3.up * 0.05f, Quaternion.LookRotation(track.TangentAt(d)));
        car.AddComponent<Rigidbody>();
        var f1 = car.AddComponent<F1Car>();
        RaceTrackBuilder.CreateSparks(car);
        car.AddComponent<CarHUD>().car = f1;
        var audio = new GameObject("EngineAudio");
        audio.transform.SetParent(car.transform, false);
        audio.AddComponent<AudioSource>();
        audio.AddComponent<EngineAudio>().car = f1;
        return car;
    }

    static RaceAI[] BuildRivals(GameObject root)
    {
        var parent = Child("Field", root);
        var list = new RaceAI[Cars - 1];
        for (int k = 1; k < Cars; k++)
        {
            float d = -(10f + 8f * k), lat = Mathf.Min(2.4f, HalfWidth - 2.2f) * (k % 2 == 0 ? -1f : 1f);
            var car = F1ModelBuilder.Build("Car_" + k);
            car.transform.SetParent(parent.transform, false);
            car.transform.SetPositionAndRotation(track.PointAt(d) + track.RightAt(d) * lat + Vector3.up * 0.03f, Quaternion.LookRotation(track.TangentAt(d)));
            var rb = car.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var ai = car.AddComponent<RaceAI>();
            ai.driverName = "Driver " + k;
            ai.maxSpeed = 84f;
            ai.cornerGrip = 17f;
            ai.aeroGrip = 0.0055f;
            ai.acceleration = 11f;
            ai.braking = 14f;
            ai.aeroBraking = 0.0035f;
            ai.lane = lat;
            ai.progress = d;
            ai.parkLane = PitLane;
            list[k - 1] = ai;
        }
        return list;
    }

    static void SetupCamera(GameObject player)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 2500f;
        var follow = cam.GetComponent<ChaseCamera>();
        if (follow == null) follow = cam.gameObject.AddComponent<ChaseCamera>();
        follow.target = player.transform;
        follow.distance = 7f;
        follow.height = 2.2f;
        follow.tvPodOffset = new Vector3(0, 1.12f, -0.38f);
        follow.tvPodPitch = 7f;
        follow.cockpitOffset = new Vector3(0, 0.86f, 0.12f);
        cam.transform.position = player.transform.TransformPoint(follow.tvPodOffset);
        cam.transform.rotation = player.transform.rotation;
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

    // ---------- Mesh helpers ----------

    // Sweep a cross-section (x = side offset, y = height above the road, or absolute when absY[k]) along the track.
    static GameObject Extrude(string name, GameObject parent, Vector2[] profile, bool[] absY, Func<int, bool> filter, Material mat, float uvScale, bool collider)
    {
        int n = track.Count;
        var right = new Vector3[n];
        for (int i = 0; i < n; i++) right[i] = Right(i);
        var profLen = new float[profile.Length];
        for (int k = 1; k < profile.Length; k++) profLen[k] = profLen[k - 1] + Vector2.Distance(profile[k], profile[k - 1]);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3 P(int i, int k)
        {
            Vector3 p = track.points[i] + right[i] * profile[k].x;
            p.y = absY != null && absY[k] ? profile[k].y : track.points[i].y + profile[k].y;
            return p;
        }
        for (int i = 0; i < n; i++)
        {
            if (!filter(i)) continue;
            int j = (i + 1) % n;
            float v0 = i * track.spacing / uvScale, v1 = (i + 1) * track.spacing / uvScale;
            for (int k = 0; k < profile.Length - 1; k++)
            {
                int b = v.Count;
                v.Add(P(i, k)); v.Add(P(i, k + 1)); v.Add(P(j, k + 1)); v.Add(P(j, k));
                float u0 = profLen[k] / uvScale, u1 = profLen[k + 1] / uvScale;
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u1, v0)); uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u0, v1));
                t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });   // both sides
            }
        }
        var go = Child(name, parent);
        if (v.Count == 0) return go;
        // Duplicate vertices per side so lighting is correct on both faces
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        int half = v.Count;
        var v2 = new List<Vector3>(v); v2.AddRange(v);
        var uv2 = new List<Vector2>(uv); uv2.AddRange(uv);
        var t2 = new List<int>();
        for (int q = 0; q < t.Count; q += 12)
        {
            t2.AddRange(new[] { t[q], t[q + 1], t[q + 2], t[q + 3], t[q + 4], t[q + 5] });
            for (int z = 6; z < 12; z++) t2.Add(t[q + z] + half);
        }
        mesh.SetVertices(v2); mesh.SetUVs(0, uv2); mesh.SetTriangles(t2, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = Dir + "/Meshes/Monaco/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        if (mat != null) mr.sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // Many boxes with world-scaled UVs merged into one mesh per material.
    class Batch
    {
        public string name;
        public Material mat;
        public float uvScale = 4f;
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public void Box(Vector3 c, Vector3 size, Quaternion r, bool top = true, bool bottom = false)
        {
            Vector3 h = size * 0.5f;
            Vector3 ax = r * Vector3.right, ay = r * Vector3.up, az = r * Vector3.forward;
            void Face(Vector3 n, Vector3 u, Vector3 w, float hn, float hu, float hw)
            {
                int b = v.Count;
                Vector3 fc = c + n * hn;
                Vector3[] corners = { fc - u * hu - w * hw, fc + u * hu - w * hw, fc + u * hu + w * hw, fc - u * hu + w * hw };
                foreach (var p in corners)
                {
                    v.Add(p);
                    uv.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w)) / uvScale);
                }
                // winding so the face points along n
                if (Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), n) > 0)
                    t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                else
                    t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            Face(ax, az, ay, h.x, h.z, h.y);
            Face(-ax, az, ay, h.x, h.z, h.y);
            Face(az, ax, ay, h.z, h.x, h.y);
            Face(-az, ax, ay, h.z, h.x, h.y);
            if (top) Face(ay, ax, az, h.y, h.x, h.z);
            if (bottom) Face(-ay, ax, az, h.y, h.x, h.z);
        }

        public void Flush(Transform parent)
        {
            if (v.Count == 0) return;
            const int maxVerts = 200000;
            int part = 0;
            for (int start = 0; start < t.Count; part++)
            {
                // Split on whole boxes worth of triangles (faces use 4 verts, 6 indices)
                int end = Mathf.Min(t.Count, start + maxVerts / 4 * 6);
                int minV = int.MaxValue, maxV = 0;
                for (int i = start; i < end; i++) { minV = Mathf.Min(minV, t[i]); maxV = Mathf.Max(maxV, t[i]); }
                var mesh = new Mesh { name = name + "_" + part, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(v.GetRange(minV, maxV - minV + 1));
                mesh.SetUVs(0, uv.GetRange(minV, maxV - minV + 1));
                var tris = new List<int>(end - start);
                for (int i = start; i < end; i++) tris.Add(t[i] - minV);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                string path = Dir + "/Meshes/Monaco/" + mesh.name + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                start = end;
            }
        }
    }

    static Batch GetBatch(string name, Material mat, float uvScale = 4f)
    {
        if (!batches.TryGetValue(name, out var b)) batches[name] = b = new Batch { name = name, mat = mat, uvScale = uvScale };
        return b;
    }

    static GameObject Child(string n, GameObject parent)
    {
        var g = new GameObject(n);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    static void LocalBox(string n, GameObject parent, Vector3 pos, Vector3 scale, Material m, bool collider)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = n;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    // ---------- Materials & textures ----------

    static Material Mat(string name, Color c, float metal, float smooth, Texture2D tex = null, Color? emission = null)
    {
        if (mats.TryGetValue(name, out var cached)) return cached;
        string path = Dir + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
        m.shader = lit;
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        if (tex != null) { m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); }
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

    static Material TransparentMat(string name, Color c)
    {
        var m = Mat(name, c, 0.3f, 0.5f);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

    static Texture2D Tex(string name)
    {
        if (textures.TryGetValue(name, out var cached) && cached != null) return cached;
        string path = Dir + "/Textures/" + name + ".png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing == null)
        {
            Texture2D t = name == "Facade" ? FacadeTexture() : NoiseTexture(name == "Asphalt" ? 0.5f : 0.85f, name == "Asphalt" ? 0.18f : 0.08f);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.SaveAndReimport();
            existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        textures[name] = existing;
        return existing;
    }

    // Grainy surface: base brightness with speckles and soft blotches
    static Texture2D NoiseTexture(float baseV, float amount)
    {
        const int size = 512;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var r = new Random(7);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float blotch = Mathf.PerlinNoise(x * 0.012f, y * 0.012f) * 0.5f + Mathf.PerlinNoise(x * 0.05f + 50f, y * 0.05f) * 0.3f;
                float grain = (float)r.NextDouble();
                float v = baseV + (blotch - 0.4f) * amount + (grain - 0.5f) * amount * 0.9f;
                if (grain > 0.985f) v += amount * 1.5f;
                px[y * size + x] = new Color(v, v, v * 1.01f, 1f);
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    // 4 x 4 bays (3 m each): windows with frames, shutters, sills and floor lines on a light wall
    static Texture2D FacadeTexture()
    {
        const int size = 512, bay = 128;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var r = new Random(11);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.03f, y * 0.03f) * 0.06f + (float)r.NextDouble() * 0.03f;
                px[y * size + x] = new Color(0.94f + n - 0.04f, 0.93f + n - 0.04f, 0.9f + n - 0.04f);
            }
        void Rect(int x0, int y0, int w, int h, Color c)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    if (x >= 0 && y >= 0 && x < size && y < size) px[y * size + x] = c;
        }
        for (int by = 0; by < 4; by++)
            for (int bx = 0; bx < 4; bx++)
            {
                int ox = bx * bay, oy = by * bay;
                Rect(ox, oy, bay, 3, new Color(0.8f, 0.78f, 0.74f));                 // floor line
                int wx = ox + 34, wy = oy + 26, ww = 60, wh = 76;
                Rect(wx - 4, wy - 6, ww + 8, 6, new Color(0.98f, 0.98f, 0.97f));      // sill
                Rect(wx - 3, wy, ww + 6, wh + 3, new Color(0.97f, 0.97f, 0.96f));     // frame
                for (int y = 0; y < wh - 6; y++)
                {
                    float g = 0.16f + y / (float)wh * 0.12f;
                    Rect(wx + 1, wy + 3 + y, ww - 2, 1, new Color(g * 0.7f, g * 0.85f, g * 1.1f));
                }
                Rect(wx + ww / 2 - 1, wy + 3, 3, wh - 6, new Color(0.95f, 0.95f, 0.94f)); // mullion
                if ((bx + by * 3) % 4 != 0)
                {
                    var sh = (bx + by) % 2 == 0 ? new Color(0.36f, 0.48f, 0.38f) : new Color(0.5f, 0.38f, 0.28f);
                    for (int k = 0; k < 9; k++)
                    {
                        Rect(wx - 30, wy + k * 9, 24, 7, sh);
                        Rect(wx + ww + 6, wy + k * 9, 24, 7, sh);
                    }
                }
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}
