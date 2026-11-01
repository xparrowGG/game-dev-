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
    const float PitLane = 10.5f;
    const float StartHeading = 50f;   // start straight runs north-east (compass bearing ~40°)
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
        // Turn angles follow the real compass headings of each section (start straight heading NE,
        // Beau Rivage climbing ENE, the Casino plateau, the descent through Mirabeau and the hairpin,
        // the seafront tunnel heading WSW, the harbour, and the final right-handers back onto the straight).
        Seg.S(330, 5, "Start straight"),
        Seg.R(70, 16, 8, "Sainte Devote"),
        Seg.S(80, 14),
        Seg.L(20, 150, 20, "Beau Rivage"),
        Seg.S(300, 38),
        Seg.L(90, 45, 42, "Massenet"),
        Seg.S(30, 43),
        Seg.R(110, 22, 44, "Casino Square"),
        Seg.S(120, 40, "Spelugues"),
        Seg.R(100, 14, 37, "Mirabeau"),
        Seg.S(60, 32),
        Seg.L(180, 9, 29, "Grand Hotel Hairpin"),
        Seg.S(40, 27),
        Seg.R(90, 15, 24, "Mirabeau Bas"),
        Seg.S(60, 18),
        Seg.R(100, 15, 13, "Portier"),
        Seg.S(50, 12),
        Seg.R(40, 400, 9, "Tunnel"),
        Seg.S(200, 6, "Tunnel exit"),
        Seg.L(35, 12, 5, "Nouvelle Chicane"),
        Seg.R(35, 12, 5),
        Seg.S(280, 4, "Quai"),
        Seg.L(50, 40, 4, "Tabac"),
        Seg.S(110, 4),
        Seg.L(25, 35, 4, "Piscine"),
        Seg.R(25, 35, 4),
        Seg.S(70, 4),
        Seg.R(30, 18, 4, "Piscine 2"),
        Seg.L(30, 18, 4),
        Seg.S(100, 4),
        Seg.R(160, 12, 4, "Rascasse"),
        Seg.S(40, 5),
        Seg.R(30, 15, 5, "Anthony Noghes"),
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

        DefineZones();
        Environment();
        BuildGround(root);
        BuildAtmosphere(root);
        BuildRoad(root);
        BuildBarriers(root);
        BuildSidewalks(root);
        BuildTunnel(root);
        var pitBoxes = BuildPits(root);
        BuildPaddock(root);
        BuildStands(root);
        var marshals = BuildMarshals(root);
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
        manager.pitEntryDistance = PitExitStart + 13f;   // finished cars leave through the pit exit opening
        manager.pitLaneOffset = PitLane;
        manager.pitEntryStart = PitEntryStart;
        manager.pitEntryEnd = PitEntryEnd;
        manager.pitExitStart = PitExitStart;
        manager.pitExitEnd = PitExitEnd;
        manager.pitSpeed = 80f / 3.6f;
        manager.boxDistances = new float[GameSession.Teams.Length];
        for (int t = 0; t < GameSession.Teams.Length; t++) manager.boxDistances[t] = Mathf.Repeat(BoxD(t), track.length);
        manager.pitBoxes = pitBoxes;
        manager.marshalPosts = marshals;
        manager.lineGreen = Mat("MC_MarkerGreen", new Color(0.1f, 0.8f, 0.3f), 0f, 0.2f, null, new Color(0.1f, 0.9f, 0.3f) * 0.6f);
        manager.lineYellow = Mat("MC_MarkerYellow", new Color(1f, 0.85f, 0.1f), 0f, 0.2f, null, new Color(1f, 0.8f, 0.05f) * 0.7f);
        manager.lineRed = Mat("MC_MarkerRed", new Color(0.95f, 0.1f, 0.1f), 0f, 0.2f, null, new Color(1f, 0.08f, 0.05f) * 0.8f);

        // Audio: a 3D engine voice that follows the nearest rival, and the crowd/city ambience
        var rivalGo = Child("RivalEngineAudio", root);
        rivalGo.AddComponent<AudioSource>();
        var rivalAudio = rivalGo.AddComponent<EngineAudio>();
        rivalAudio.rival = true;
        manager.rivalAudio = rivalAudio;
        var ambGo = Child("Ambience", root);
        ambGo.AddComponent<AudioSource>();
        manager.ambient = ambGo.AddComponent<AmbientAudio>();
        SetupCamera(player);

        PlayerSettings.productName = GameSession.GameTitle;
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
        float heading = StartHeading * Mathf.Deg2Rad;
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
        float heading = StartHeading * Mathf.Deg2Rad;
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
        float heading = StartHeading * Mathf.Deg2Rad, s = 0f, h = Layout[Layout.Length - 1].height;
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
    static bool Corner(int i) => track.RadiusAt(D(i)) < 90f;

    // ---------- Pit lane layout (distances along the lap, start line = 0) ----------
    const float PitEntryStart = -168f, PitEntryEnd = -140f;   // gap in the pit wall to turn in
    const float PitExitStart = 112f, PitExitEnd = 142f;       // gap to rejoin before Sainte Dévote
    const float PitZoneStart = -176f, PitZoneEnd = 150f;
    const float PitOuter = 15.2f;                             // garage fronts / outer edge of the pit lane
    static float BoxD(int team) => -112f + 12f * team;
    static bool PitZone(float d) => InRange(d, PitZoneStart, PitZoneEnd);
    static bool PitWindow(float d) => InRange(d, PitEntryStart, PitEntryEnd) || InRange(d, PitExitStart, PitExitEnd);

    // ---------- Run-off areas ----------
    struct Zone { public float a, b, width; public int side; public bool gravel, tyres; }
    static List<Zone> zones = new List<Zone>();

    static void DefineZones()
    {
        zones = new List<Zone>();
        AddZone("Sainte Devote", -60f, 50f, -1, 18f, false, true);
        AddZone("Mirabeau", -50f, 30f, -1, 13f, false, true);
        AddZone("Portier", -30f, 30f, -1, 10f, false, true);
        AddZone("Nouvelle Chicane", -40f, 60f, 1, 16f, false, false);
        AddZone("Tabac", -30f, 50f, 1, 14f, true, true);
        AddZone("Piscine", -20f, 60f, 1, 12f, true, false);
        AddZone("Rascasse", -30f, 40f, -1, 12f, false, true);
    }

    // A run-off zone may only be as wide as the space before another part of the circuit allows.
    static void AddZone(string marker, float from, float to, int side, float width, bool gravel, bool tyres)
    {
        float m = markers[marker];
        var z = new Zone { a = m + from, b = m + to, side = side, width = width, gravel = gravel, tyres = tyres };
        for (float d = z.a; d <= z.b; d += 4f)
        {
            int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
            while (z.width > BarrierIn + 1f)
            {
                Vector3 outer = track.points[i] + Right(i) * side * (z.width + 3.5f);
                bool clash = false;
                for (int k = 0; k < track.Count; k += 2)
                {
                    float along = Mathf.Abs(Mathf.DeltaAngle(i * 360f / track.Count, k * 360f / track.Count)) / 360f * track.length;
                    if (along < 80f) continue;
                    if (Flat(track.points[k] - outer).magnitude < BarrierOut + 4f) { clash = true; break; }
                }
                if (!clash) break;
                z.width -= 1f;
            }
        }
        if (z.width > BarrierIn + 1f) zones.Add(z);
    }

    static float ZoneBlend(Zone z, float d)
    {
        float len = Mathf.Repeat(z.b - z.a, track.length), u = Mathf.Repeat(d - z.a, track.length);
        if (u > len) return 0f;
        float t = Mathf.Min(1f, Mathf.Min(u, len - u) / Mathf.Min(25f, len / 3f));
        return t * t * (3f - 2f * t);
    }

    // Inner face of the barrier on a side (1 = right, -1 = left), widened smoothly through run-off zones
    static float Off(int i, int side)
    {
        float d = D(i), off = BarrierIn;
        foreach (var z in zones)
            if (z.side == side) off = Mathf.Max(off, Mathf.Lerp(BarrierIn, z.width, ZoneBlend(z, d)));
        return off;
    }

    static bool ZoneFlag(int i, int side, bool gravel)
    {
        float d = D(i);
        foreach (var z in zones)
            if (z.side == side && ZoneBlend(z, d) > 0.05f && (gravel ? z.gravel : z.tyres)) return true;
        return false;
    }

    static bool GravelAt(int i, int side) => ZoneFlag(i, side, true) && Off(i, side) > BarrierIn + 0.5f;
    static bool TyreWallAt(int i, int side) => ZoneFlag(i, side, false) && Off(i, side) > BarrierIn + 1.5f;
    static float WalkOut(int i, int side) => Off(i, side) + 3.2f;   // outer edge of the pavement

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

    static Terrain terrain;
    const float SeaLevel = 0.3f;

    // Ground height under any point (terrain surface)
    static float GroundAt(Vector3 p) => terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;

    // Hillside terrain that follows the circuit: the ground sits just under the road and pavements,
    // rises toward Casino Square, and drops into the harbour basin and the sea off the seafront.
    static void BuildGround(GameObject root)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in track.points) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
        const float margin = 320f, baseY = -12f, range = 140f;
        const int res = 513;
        float size = Mathf.Max(maxX - minX, maxZ - minZ) + margin * 2f;
        Vector3 origin = new Vector3((minX + maxX) / 2f - size / 2f, baseY, (minZ + maxZ) / 2f - size / 2f);

        // Spatial bins for nearest-track lookups
        const float bin = 25f;
        var bins = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < track.Count; i++)
        {
            var key = (Mathf.FloorToInt(track.points[i].x / bin), Mathf.FloorToInt(track.points[i].z / bin));
            if (!bins.TryGetValue(key, out var l)) bins[key] = l = new List<int>();
            l.Add(i);
        }
        // Smooth hillside field from the track heights (coarse grid, inverse-distance weighted)
        const int coarse = 40;
        var hill = new float[coarse + 1, coarse + 1];
        for (int gz = 0; gz <= coarse; gz++)
            for (int gx = 0; gx <= coarse; gx++)
            {
                Vector3 p = origin + new Vector3(gx * size / coarse, 0, gz * size / coarse);
                float ws = 0, hs = 0;
                for (int i = 0; i < track.Count; i += 4)
                {
                    float d2 = Flat(track.points[i] - p).sqrMagnitude + 900f;
                    float w = 1f / (d2 * d2);
                    ws += w; hs += w * track.points[i].y;
                }
                hill[gx, gz] = hs / ws;
            }

        float seaFrom = markers["Portier"], seaTo = markers["Tabac"];
        Vector3 harbour = HarbourCentre();
        var heights = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                Vector3 p = origin + new Vector3(x * size / (res - 1), 0, z * size / (res - 1));
                // nearest track point
                int bx = Mathf.FloorToInt(p.x / bin), bz = Mathf.FloorToInt(p.z / bin);
                int near = -1; float dmin = float.MaxValue;
                for (int ox = -3; ox <= 3; ox++)
                    for (int oz = -3; oz <= 3; oz++)
                        if (bins.TryGetValue((bx + ox, bz + oz), out var l))
                            foreach (int i in l)
                            {
                                float d = Flat(track.points[i] - p).magnitude;
                                if (d < dmin) { dmin = d; near = i; }
                            }
                float gxf = (p.x - origin.x) / size * coarse, gzf = (p.z - origin.z) / size * coarse;
                int gx0 = Mathf.Clamp(Mathf.FloorToInt(gxf), 0, coarse - 1), gz0 = Mathf.Clamp(Mathf.FloorToInt(gzf), 0, coarse - 1);
                float tx = gxf - gx0, tz = gzf - gz0;
                float field = Mathf.Lerp(Mathf.Lerp(hill[gx0, gz0], hill[gx0 + 1, gz0], tx), Mathf.Lerp(hill[gx0, gz0 + 1], hill[gx0 + 1, gz0 + 1], tx), tz);
                float h;
                if (near >= 0)
                {
                    float road = track.points[near].y - 0.35f;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(13f, 80f, dmin));
                    h = Mathf.Lerp(road, field - 0.5f, t);
                    // Sea off the seafront: left of the tunnel / chicane / quay sections
                    float dn = D(near);
                    if (dmin > 22f && InRange(dn, seaFrom, seaTo) && Vector3.Dot(p - track.points[near], Right(near)) < 0f)
                        h = Mathf.Lerp(h, -4f, Mathf.InverseLerp(22f, 40f, dmin));
                }
                else h = field - 0.5f;
                // Keep the ground well below the road and pavements so it never shows through as bumps
                if (near >= 0 && dmin < 18f)
                    h = Mathf.Min(h, track.points[near].y - Mathf.Lerp(1.6f, 0.4f, Mathf.InverseLerp(10f, 18f, dmin)));
                // Harbour basin
                float hd = Flat(p - harbour).magnitude;
                if (hd < 150f && dmin > 18f) h = Mathf.Min(h, Mathf.Lerp(-4f, h, Mathf.InverseLerp(110f, 150f, hd)));
                heights[z, x] = Mathf.Clamp01((h - baseY) / range);
            }

        var data = new TerrainData { heightmapResolution = res, size = new Vector3(size, range, size) };
        data.SetHeights(0, 0, heights);
        var layer = new TerrainLayer { diffuseTexture = Tex("Ground"), tileSize = new Vector2(18f, 18f) };
        AssetDatabase.DeleteAsset(Dir + "/Meshes/Monaco/GroundLayer.terrainlayer");
        AssetDatabase.CreateAsset(layer, Dir + "/Meshes/Monaco/GroundLayer.terrainlayer");
        data.terrainLayers = new[] { layer };
        AssetDatabase.DeleteAsset(Dir + "/Meshes/Monaco/Terrain.asset");
        AssetDatabase.CreateAsset(data, Dir + "/Meshes/Monaco/Terrain.asset");
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root.transform, false);
        go.transform.position = origin;
        terrain = go.GetComponent<Terrain>();
        var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (terrainShader != null)
        {
            var tm = new Material(terrainShader);
            AssetDatabase.DeleteAsset(Dir + "/Materials/MC_Terrain.mat");
            AssetDatabase.CreateAsset(tm, Dir + "/Materials/MC_Terrain.mat");
            terrain.materialTemplate = tm;
        }
        terrain.heightmapPixelError = 3f;
        terrain.Flush();

        // Sea surface across the whole map (shows wherever the ground dips below sea level)
        GetBatch("Sea", Mat("MC_Sea", new Color(0.05f, 0.27f, 0.42f), 0.2f, 0.95f)).Box(
            new Vector3(origin.x + size / 2f, SeaLevel - 0.05f, origin.z + size / 2f), new Vector3(size * 1.6f, 0.1f, size * 1.6f), Quaternion.identity, true, false);
    }

    static Vector3 HarbourCentre()
    {
        Vector3 a = track.PointAt(-60f), q = track.PointAt(markers["Piscine"]);
        Vector3 c = Vector3.Lerp(a, q, 0.5f) + track.RightAt(-60f) * 20f;
        c.y = 0f;
        return c;
    }

    // Post-processing and sky: ACES tonemapping, bloom, colour grading, vignette, procedural sky
    static void BuildAtmosphere(GameObject root)
    {
        const string path = Dir + "/Materials/MC_PostProfile.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        var tone = profile.Add<UnityEngine.Rendering.Universal.Tonemapping>(true);
        tone.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.ACES);
        var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        bloom.intensity.Override(0.35f);
        bloom.threshold.Override(1.05f);
        var colour = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
        colour.postExposure.Override(0.35f);
        colour.contrast.Override(14f);
        colour.saturation.Override(10f);
        var white = profile.Add<UnityEngine.Rendering.Universal.WhiteBalance>(true);
        white.temperature.Override(6f);
        var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
        vignette.intensity.Override(0.2f);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        EditorUtility.SetDirty(profile);

        var volGo = Child("PostProcessing", root);
        var vol = volGo.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        var sky = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/MC_Sky.mat");
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, Dir + "/Materials/MC_Sky.mat");
        }
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_AtmosphereThickness", 0.9f);
        sky.SetColor("_SkyTint", new Color(0.45f, 0.6f, 0.85f));
        sky.SetFloat("_Exposure", 1.25f);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
    }

    // ---------- Road, kerbs, barriers ----------

    static void BuildRoad(GameObject root)
    {
        var parent = Child("Road", root);
        var asphalt = Mat("MC_Asphalt", new Color(0.42f, 0.42f, 0.44f), 0f, 0.3f, Tex("Asphalt"));
        Extrude("RoadSurface", parent, new[] { new Vector2(-BarrierIn, 0.03f), new Vector2(BarrierIn, 0.03f) }, null, i => true, asphalt, 8f, false);

        // Drivable collision: the road plus paved run-off out to the barriers (gravel has its own collider)
        var col = ExtrudeVar("RoadCollider", parent, i => new[] {
                new Vector2(GravelAt(i, -1) ? -BarrierIn : -Off(i, -1), 0.03f),
                new Vector2(GravelAt(i, 1) ? BarrierIn : Off(i, 1), 0.03f) }, null, i => true, null, 8f, true);
        Object.DestroyImmediate(col.GetComponent<MeshRenderer>());

        // Run-off areas: painted paved run-off or gravel traps beyond the kerbs
        var runoff = Mat("MC_Runoff", new Color(0.32f, 0.36f, 0.42f), 0f, 0.3f, Tex("Asphalt"));
        var gravelMat = Mat("MC_Gravel", new Color(0.86f, 0.77f, 0.58f), 0f, 0.15f, Tex("Gravel"));
        var gravelSurface = GravelSurface();
        foreach (int side in new[] { -1, 1 })
        {
            string sn = side < 0 ? "L" : "R";
            Func<int, Vector2[]> band = i => side > 0
                ? new[] { new Vector2(BarrierIn, 0.028f), new Vector2(Off(i, 1), 0.028f) }
                : new[] { new Vector2(-Off(i, -1), 0.028f), new Vector2(-BarrierIn, 0.028f) };
            ExtrudeVar("Runoff" + sn, parent, band, null, i => Off(i, side) > BarrierIn + 0.2f && !GravelAt(i, side), runoff, 6f, false);
            ExtrudeVar("Gravel" + sn, parent, i => side > 0
                    ? new[] { new Vector2(BarrierIn, 0.05f), new Vector2(Off(i, 1), 0.05f) }
                    : new[] { new Vector2(-Off(i, -1), 0.05f), new Vector2(-BarrierIn, 0.05f) },
                null, i => GravelAt(i, side), gravelMat, 3f, true, gravelSurface);
            // Painted stripes on paved run-off (green, as at many circuits)
            ExtrudeVar("RunoffStripe" + sn, parent, i => side > 0
                    ? new[] { new Vector2(BarrierIn + 0.3f, 0.032f), new Vector2(BarrierIn + 1.3f, 0.032f) }
                    : new[] { new Vector2(-BarrierIn - 1.3f, 0.032f), new Vector2(-BarrierIn - 0.3f, 0.032f) },
                null, i => Off(i, side) > BarrierIn + 3f && !GravelAt(i, side), Mat("MC_RunoffGreen", new Color(0.15f, 0.55f, 0.3f), 0f, 0.3f), 4f, false);
        }
        var white = Mat("MC_LineWhite", new Color(0.95f, 0.95f, 0.93f), 0f, 0.35f);
        Extrude("EdgeLineL", parent, new[] { new Vector2(-HalfWidth, 0.04f), new Vector2(-HalfWidth + 0.15f, 0.04f) }, null, i => true, white, 4f, false);
        Extrude("EdgeLineR", parent, new[] { new Vector2(HalfWidth - 0.15f, 0.04f), new Vector2(HalfWidth, 0.04f) }, null, i => true, white, 4f, false);
        var red = Mat("MC_KerbRed", new Color(0.82f, 0.08f, 0.08f), 0f, 0.45f);
        var kw = Mat("MC_KerbWhite", new Color(0.95f, 0.95f, 0.95f), 0f, 0.45f);
        // Flat painted kerbs, only a couple of centimetres proud of the asphalt
        var kR = new[] { new Vector2(HalfWidth, 0.036f), new Vector2(KerbEnd, 0.05f) };
        var kL = new[] { new Vector2(-KerbEnd, 0.05f), new Vector2(-HalfWidth, 0.036f) };
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
        const float h = 1.05f, th = BarrierOut - BarrierIn;
        Func<int, int, float, Vector2[]> wall = (i, side, inset) =>
        {
            float o = Off(i, side) - inset;
            return side > 0
                ? new[] { new Vector2(o, 0), new Vector2(o, h), new Vector2(o + th, h), new Vector2(o + th, 0) }
                : new[] { new Vector2(-o - th, 0), new Vector2(-o - th, h), new Vector2(-o, h), new Vector2(-o, 0) };
        };
        foreach (int side in new[] { -1, 1 })
        {
            string sn = side < 0 ? "L" : "R";
            // Right-hand pit wall has openings at the pit entry and exit
            Func<int, bool> present = i => side < 0 || !PitWindow(D(i));
            // Visual: painted red/white near corners, bare steel elsewhere
            ExtrudeVar("Barrier" + sn + "_Steel", parent, i => wall(i, side, 0f), null, i => present(i) && !Corner(i), steel, 4f, false);
            ExtrudeVar("Barrier" + sn + "_Red", parent, i => wall(i, side, 0f), null, i => present(i) && Corner(i) && (i / 3) % 2 == 0, red, 4f, false);
            ExtrudeVar("Barrier" + sn + "_White", parent, i => wall(i, side, 0f), null, i => present(i) && Corner(i) && (i / 3) % 2 == 1, white, 4f, false);
            // Collision sits in front of any tyre wall, so cars hit the tyres rather than pass into them
            // The collision wall is 3 m tall (taller than the visible barrier) so a car can never ride up over its top edge
            Func<int, Vector2[]> tall = i =>
            {
                var p = wall(i, side, TyreWallAt(i, side) ? 0.9f : 0f);
                p[1].y = 3f; p[2].y = 3f;
                return p;
            };
            var col = ExtrudeVar("Barrier" + sn + "_Collider", parent, tall, null, present, null, 4f, true);
            Object.DestroyImmediate(col.GetComponent<MeshRenderer>());
        }

        // Catch fencing: see-through mesh and posts, not in the tunnel
        // Catch fencing: steel cables strung between the posts (you can see through it, as on TV)
        var cable = Mat("MC_FenceCable", new Color(0.45f, 0.47f, 0.5f), 0.7f, 0.5f);
        foreach (int side in new[] { -1, 1 })
            for (int w = 0; w < 6; w++)
            {
                float y = 1.5f + w * 0.55f, sz = w == 5 ? 0.06f : 0.025f;
                Func<int, Vector2[]> wire = i =>
                {
                    float x = side * (Off(i, side) + th - 0.02f);
                    return new[] { new Vector2(x, y), new Vector2(x, y + sz), new Vector2(x + side * sz, y + sz), new Vector2(x + side * sz, y) };
                };
                ExtrudeVar("FenceWire" + (side < 0 ? "L" : "R") + w, parent, wire, null, i => !InTunnel(D(i)) && (side < 0 || !PitWindow(D(i))), cable, 4f, false);
            }
        var posts = GetBatch("Posts", Mat("MC_Post", new Color(0.35f, 0.37f, 0.4f), 0.6f, 0.4f));
        for (int i = 0; i < track.Count; i += 2)
        {
            if (InTunnel(D(i))) continue;
            foreach (int sd in new[] { -1, 1 })
            {
                if (sd > 0 && PitWindow(D(i))) continue;
                Vector3 p = track.points[i] + Right(i) * (sd * (Off(i, sd) + th + 0.05f)) + Vector3.up * 2.6f;
                posts.Box(p, new Vector3(0.08f, 3.2f, 0.08f), Quaternion.identity);
            }
        }
        BuildSafety();
    }

    // Tyre walls (stacked, belted tyres) in front of the barriers at run-off corners, and
    // energy-absorbing block barriers where the run-off narrows back to the street.
    static void BuildSafety()
    {
        var tyre = GetBatch("TyreWalls", Mat("MC_TyreStack", new Color(0.06f, 0.06f, 0.07f), 0f, 0.3f));
        var belt = GetBatch("TyreBelts", Mat("MC_TyreBelt", new Color(0.9f, 0.9f, 0.92f), 0f, 0.4f));
        var blockA = GetBatch("TecBlocksRed", Mat("MC_TecRed", new Color(0.8f, 0.12f, 0.12f), 0.1f, 0.5f));
        var blockB = GetBatch("TecBlocksBlue", Mat("MC_TecBlue", new Color(0.12f, 0.25f, 0.7f), 0.1f, 0.5f));
        foreach (int side in new[] { -1, 1 })
        {
            for (int i = 0; i < track.Count; i++)
            {
                if (!TyreWallAt(i, side)) continue;
                // Two staggered rows of tyre stacks along the barrier, every metre
                for (int k = 0; k < 2; k++)
                {
                    float along = k * 1f;
                    Vector3 p = track.PointAt(D(i) + along) + track.RightAt(D(i) + along) * side * (Off(i, side) - 0.38f);
                    tyre.Cylinder(p + Vector3.up * 0.55f, 0.34f, 1.1f);
                    belt.Cylinder(p + Vector3.up * 0.75f, 0.345f, 0.08f);
                    Vector3 q = p - track.RightAt(D(i) + along) * side * 0.55f + Flat(track.TangentAt(D(i))).normalized * 0.5f;
                    tyre.Cylinder(q + Vector3.up * 0.55f, 0.34f, 1.1f);
                }
            }
            // Block barriers at both ends of every run-off zone on this side
            foreach (var z in zones)
            {
                if (z.side != side) continue;
                foreach (float d in new[] { z.a + 6f, z.b - 6f })
                {
                    int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
                    for (int k = 0; k < 6; k++)
                    {
                        float dd = d + k * 1.7f - 4f;
                        Vector3 p = track.PointAt(dd) + track.RightAt(dd) * side * (Off(i, side) - 0.55f) + Vector3.up * 0.55f;
                        (k % 2 == 0 ? blockA : blockB).Box(p, new Vector3(1f, 1.1f, 1.6f), Facing(dd));
                    }
                }
            }
        }
    }

    static PhysicsMaterial GravelSurface()
    {
        const string path = Dir + "/Materials/Gravel.asset";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (pm == null)
        {
            pm = new PhysicsMaterial("Gravel") { dynamicFriction = 0.6f, staticFriction = 0.6f };
            AssetDatabase.CreateAsset(pm, path);
        }
        pm.name = "Gravel";
        return pm;
    }

    static void BuildSidewalks(GameObject root)
    {
        var parent = Child("Pavements", root);
        var stone = Mat("MC_Pavement", new Color(0.78f, 0.75f, 0.7f), 0f, 0.25f, Tex("Concrete"));
        var wall = Mat("MC_RetainingWall", new Color(0.86f, 0.8f, 0.68f), 0f, 0.2f, Tex("Concrete"));
        Func<int, bool> rightOk = i => !InTunnel(D(i)) && !PitZone(D(i));
        Func<int, bool> leftOk = i => !InTunnel(D(i));
        float th = BarrierOut - BarrierIn;
        ExtrudeVar("PavementR", parent, i => new[] { new Vector2(Off(i, 1) + th, 0.15f), new Vector2(WalkOut(i, 1), 0.15f) }, null, rightOk, stone, 4f, false);
        ExtrudeVar("PavementL", parent, i => new[] { new Vector2(-WalkOut(i, -1), 0.15f), new Vector2(-Off(i, -1) - th, 0.15f) }, null, leftOk, stone, 4f, false);
        // Retaining walls from the street down to the ground where the road climbs the hill
        // Retaining walls reach well below ground; the terrain hides them except where the hillside drops away
        ExtrudeVar("WallR", parent, i => new[] { new Vector2(WalkOut(i, 1), 0.15f), new Vector2(WalkOut(i, 1), -14f) }, new[] { false, true }, rightOk, wall, 6f, false);
        ExtrudeVar("WallL", parent, i => new[] { new Vector2(-WalkOut(i, -1), -14f), new Vector2(-WalkOut(i, -1), 0.15f) }, new[] { true, false }, leftOk, wall, 6f, false);
        Extrude("UnderRoadR", parent, new[] { new Vector2(BarrierOut, 0f), new Vector2(BarrierOut, -14f) }, new[] { false, true }, i => !rightOk(i) && !PitZone(D(i)), wall, 6f, false);
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

    // Pit lane alongside the start straight: entry after Anthony Noghès, a speed-limited lane past the
    // team garages and pit boxes, exit before Sainte Dévote.
    static PitBox[] BuildPits(GameObject root)
    {
        var parent = Child("PitLane", root);
        var asphalt = Mat("MC_PitAsphalt", new Color(0.36f, 0.36f, 0.38f), 0f, 0.3f, Tex("Asphalt"));
        Func<int, bool> zone = i => PitZone(D(i));
        Extrude("PitLaneSurface", parent, new[] { new Vector2(BarrierIn - 0.1f, 0.03f), new Vector2(PitOuter, 0.03f) }, null, zone, asphalt, 8f, true);
        Extrude("PitLaneEdge", parent, new[] { new Vector2(PitOuter, 0.03f), new Vector2(PitOuter, -14f) }, new[] { false, true }, zone, asphalt, 8f, false);
        var outer = Extrude("PitLaneOuterWall", parent, new[] { new Vector2(PitOuter, 0f), new Vector2(PitOuter, 2.5f), new Vector2(PitOuter + 0.3f, 2.5f), new Vector2(PitOuter + 0.3f, 0f) }, null, zone, null, 4f, true);
        Object.DestroyImmediate(outer.GetComponent<MeshRenderer>());
        foreach (float d in new[] { PitZoneStart, PitZoneEnd })
        {
            var end = Child("PitLaneEndWall", parent);
            end.transform.SetPositionAndRotation(track.PointAt(d) + track.RightAt(d) * ((BarrierOut + PitOuter) / 2f) + Vector3.up, Facing(d));
            end.AddComponent<BoxCollider>().size = new Vector3(PitOuter - BarrierOut, 2f, 0.4f);
        }

        // Markings: fast-lane line, yellow speed-limit lines, team pit boxes
        var white = GetBatch("PitLines", Mat("MC_LineWhite", new Color(0.95f, 0.95f, 0.93f), 0f, 0.35f));
        var yellow = GetBatch("PitLimitLines", Mat("MC_LineYellow", new Color(1f, 0.82f, 0.1f), 0f, 0.35f));
        for (float d = PitEntryEnd; InRange(d, PitEntryEnd, PitExitStart); d += 3f)
            white.Box(track.PointAt(d) + track.RightAt(d) * 7.6f + Vector3.up * 0.045f, new Vector3(0.15f, 0.01f, 2f), Facing(d));
        foreach (float d in new[] { PitEntryEnd, PitExitStart })
            for (float x = BarrierOut + 0.5f; x < PitOuter; x += 1.2f)
                yellow.Box(track.PointAt(d) + track.RightAt(d) * x + Vector3.up * 0.045f, new Vector3(1.1f, 0.01f, 0.4f), Facing(d));

        // Garages, pit boxes and crews, one per team
        var wallMat = Mat("MC_PitBuilding", new Color(0.92f, 0.93f, 0.95f), 0.1f, 0.5f);
        var wall = GetBatch("Facade_Pits", wallMat, 6f);
        var door = GetBatch("GarageDoors", Mat("MC_GarageDoor", new Color(0.12f, 0.13f, 0.16f), 0.4f, 0.6f));
        var glass = GetBatch("PitGlass", Mat("MC_PitGlass", new Color(0.15f, 0.28f, 0.42f), 0.6f, 0.9f));
        for (float d = PitZoneStart; InRange(d, PitZoneStart, PitZoneEnd); d += 4f)
        {
            int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
            Vector3 b = track.points[i] + Right(i) * (PitOuter + 5.5f);
            float top = track.points[i].y + 7f;
            float foot = Mathf.Min(GroundAt(b), track.points[i].y) - 1f;
            wall.Box(new Vector3(b.x, (top + foot) / 2f, b.z), new Vector3(11f, top - foot, 4.05f), Facing(d));
            glass.Box(new Vector3(b.x, track.points[i].y + 5.6f, b.z) - Right(i) * 0.03f, new Vector3(11.06f, 1.3f, 3.7f), Facing(d));
        }
        var boxes = new PitBox[GameSession.Teams.Length];
        var red = Mat("MC_ReleaseRed", new Color(1f, 0.05f, 0.03f), 0f, 0.8f, null, new Color(1f, 0.05f, 0.03f) * 3f);
        var green = Mat("MC_ReleaseGreen", new Color(0.1f, 1f, 0.2f), 0f, 0.8f, null, new Color(0.1f, 1f, 0.2f) * 3f);
        var offMat = Mat("MC_ReleaseOff", new Color(0.1f, 0.1f, 0.1f), 0f, 0.5f);
        var helmet = Mat("MC_CrewHelmet", new Color(0.08f, 0.08f, 0.1f), 0.4f, 0.8f);
        var legs = Mat("MC_CrewLegs", new Color(0.12f, 0.12f, 0.14f), 0f, 0.3f);
        for (int team = 0; team < GameSession.Teams.Length; team++)
        {
            var t = GameSession.Teams[team];
            float d = BoxD(team);
            int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
            Quaternion rot = Facing(d);
            Vector3 right = Right(i);
            var teamMat = Mat("MC_Team" + team, t.primary, 0.2f, 0.6f);
            var teamBox = GetBatch("TeamColour" + team, teamMat);
            // Garage front: door, colour band and the team name
            Vector3 front = track.points[i] + right * (PitOuter + 0.04f);
            door.Box(front + Vector3.up * 2f, new Vector3(0.06f, 4f, 9.5f), rot);
            teamBox.Box(front + Vector3.up * 4.6f - right * 0.02f, new Vector3(0.08f, 0.8f, 11.8f), rot);
            Sign(parent, t.name.ToUpperInvariant(), front + Vector3.up * 4.6f - right * 0.08f, Quaternion.LookRotation(right), 0.07f, Color.white);
            // Pit box outline on the ground
            Vector3 c = track.points[i] + right * PitLane + Vector3.up * 0.045f;
            teamBox.Box(c + right * 1.7f, new Vector3(0.15f, 0.01f, 6.6f), rot);
            teamBox.Box(c - right * 1.7f, new Vector3(0.15f, 0.01f, 6.6f), rot);
            teamBox.Box(c + rot * Vector3.forward * 3.3f, new Vector3(3.4f, 0.01f, 0.15f), rot);
            teamBox.Box(c - rot * Vector3.forward * 3.3f, new Vector3(3.4f, 0.01f, 0.15f), rot);

            // Pit box object: crew and release light
            var box = Child("PitBox_" + t.name, parent);
            box.transform.SetPositionAndRotation(track.points[i] + right * PitLane, rot);
            var pb = box.AddComponent<PitBox>();
            pb.team = team;
            pb.trackDistance = Mathf.Repeat(d, track.length);
            Vector3[] work =
            {
                new Vector3(-1.45f, 0, 1.8f), new Vector3(-1.45f, 0, 1.15f), new Vector3(1.45f, 0, 1.8f), new Vector3(1.45f, 0, 1.15f),
                new Vector3(-1.45f, 0, -1.8f), new Vector3(-1.45f, 0, -2.45f), new Vector3(1.45f, 0, -1.8f), new Vector3(1.45f, 0, -2.45f),
                new Vector3(0, 0, 3.6f), new Vector3(0, 0, -3.3f), new Vector3(-1.9f, 0, 0.2f), new Vector3(1.9f, 0, 0.2f)
            };
            var crew = new Transform[work.Length];
            var offsets = new Vector3[work.Length];
            for (int k = 0; k < work.Length; k++)
            {
                Vector3 home = new Vector3(3.2f + (k % 3) * 0.6f, 0, -2.5f + (k / 3) * 1.6f);
                var m = Child("Crew" + k, box);
                m.transform.localPosition = home;
                CrewPart(m, PrimitiveType.Cube, new Vector3(0, 0.45f, 0), new Vector3(0.36f, 0.9f, 0.26f), legs);
                CrewPart(m, PrimitiveType.Cube, new Vector3(0, 1.2f, 0), new Vector3(0.5f, 0.65f, 0.32f), teamMat);
                CrewPart(m, PrimitiveType.Sphere, new Vector3(0, 1.68f, 0), Vector3.one * 0.3f, helmet);
                crew[k] = m.transform;
                offsets[k] = work[k] - home;
            }
            pb.crew = crew;
            pb.workOffsets = offsets;
            // Release light on a pole at the front of the box
            var pole = Child("ReleasePole", box);
            pole.transform.localPosition = new Vector3(2.4f, 0, 4.2f);
            CrewPart(pole, PrimitiveType.Cylinder, new Vector3(0, 1.4f, 0), new Vector3(0.08f, 1.4f, 0.08f), legs);
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.name = "ReleaseLight";
            lamp.transform.SetParent(pole.transform, false);
            lamp.transform.localPosition = new Vector3(-0.3f, 2.9f, 0);
            lamp.transform.localScale = new Vector3(0.7f, 0.35f, 0.12f);
            lamp.GetComponent<Renderer>().sharedMaterial = offMat;
            pb.releaseLight = lamp.GetComponent<Renderer>();
            pb.lightRed = red; pb.lightGreen = green; pb.lightOff = offMat;
            boxes[team] = pb;
        }

        // Signs for drivers
        Vector3 entry = track.PointAt(PitEntryStart - 60f) + track.RightAt(PitEntryStart - 60f) * (BarrierOut + 1.2f) + Vector3.up * 2.4f;
        Sign(parent, "PIT IN  >", entry, Quaternion.LookRotation(Flat(track.TangentAt(PitEntryStart - 60f))), 0.12f, Color.white);
        Sign(parent, "80", track.PointAt(PitEntryEnd) + track.RightAt(PitEntryEnd) * (PitOuter - 0.4f) + Vector3.up * 2.4f,
             Quaternion.LookRotation(Flat(track.TangentAt(PitEntryEnd))), 0.16f, new Color(1f, 0.2f, 0.2f));
        Sign(parent, "PIT EXIT", track.PointAt(PitExitStart) + track.RightAt(PitExitStart) * (PitOuter - 0.4f) + Vector3.up * 2.4f,
             Quaternion.LookRotation(Flat(track.TangentAt(PitExitStart))), 0.1f, Color.white);

        for (float d = PitZoneStart; InRange(d, PitZoneStart, PitZoneEnd); d += 20f)
            reserved.Add((track.PointAt(d) + track.RightAt(d) * 22f, 16f));
        return boxes;
    }

    // Paddock behind the garages: team motorhomes, transporters and hospitality tents on a paved apron
    static void BuildPaddock(GameObject root)
    {
        var slab = GetBatch("PaddockGround", Mat("MC_Paddock", new Color(0.62f, 0.62f, 0.6f), 0f, 0.25f, Tex("Concrete")), 6f);
        var white = GetBatch("PaddockWhite", Mat("MC_PaddockWhite", new Color(0.96f, 0.96f, 0.97f), 0.1f, 0.6f));
        var glass = GetBatch("PaddockGlass", Mat("MC_PaddockGlass", new Color(0.08f, 0.12f, 0.18f), 0.7f, 0.95f));
        var truck = GetBatch("Transporters", Mat("MC_Truck", new Color(0.85f, 0.86f, 0.88f), 0.5f, 0.7f));
        for (float d = PitZoneStart; InRange(d, PitZoneStart, 60f); d += 4f)
        {
            int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
            Vector3 p = track.points[i] + Right(i) * 48f;
            float top = track.points[i].y + 0.1f;
            float foot = Mathf.Min(GroundAt(p), top) - 1f;
            slab.Box(new Vector3(p.x, (top + foot) / 2f, p.z), new Vector3(46f, top - foot, 4.1f), Facing(d));
            reserved.Add((p, 26f));
        }
        for (int team = 0; team < GameSession.Teams.Length; team++)
        {
            float d = BoxD(team);
            int i = Mathf.RoundToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
            Quaternion rot = Facing(d);
            Vector3 r = Right(i), basePos = track.points[i] + Vector3.up * 0.1f;
            var colour = GetBatch("TeamColour" + team, Mat("MC_Team" + team, GameSession.Teams[team].primary, 0.2f, 0.6f));
            // Motorhome: glass ground floor, team-colour upper floor, white roof terrace
            Vector3 mh = basePos + r * 38f;
            glass.Box(mh + Vector3.up * 1.6f, new Vector3(7f, 3.2f, 10.5f), rot);
            colour.Box(mh + Vector3.up * 4.4f, new Vector3(7.2f, 2.4f, 10.7f), rot);
            white.Box(mh + Vector3.up * 5.75f, new Vector3(7.6f, 0.3f, 11f), rot);
            // Transporter
            Vector3 tr = basePos + r * 50f;
            truck.Box(tr + Vector3.up * 2.1f, new Vector3(2.6f, 3.6f, 9.5f), rot);
            colour.Box(tr + Vector3.up * 2.1f + rot * Vector3.forward * 5.6f, new Vector3(2.5f, 3f, 1.8f), rot);
            // Hospitality tent with a pitched roof
            Vector3 tent = basePos + r * 62f;
            white.Box(tent + Vector3.up * 1.4f, new Vector3(8f, 2.8f, 8f), rot);
            white.Box(tent + Vector3.up * 3.1f, new Vector3(5.8f, 5.8f, 8.2f), rot * Quaternion.Euler(0, 0, 45f));
        }
    }

    // Marshal posts every ~180 m, alternating sides, with flags the race manager can turn yellow
    static MarshalPost[] BuildMarshals(GameObject root)
    {
        var parent = Child("MarshalPosts", root);
        var hut = Mat("MC_MarshalHut", new Color(0.95f, 0.95f, 0.95f), 0f, 0.4f);
        var roof = Mat("MC_MarshalRoof", new Color(1f, 0.45f, 0.05f), 0f, 0.4f);
        var orange = Mat("MC_MarshalSuit", new Color(1f, 0.5f, 0.05f), 0f, 0.4f);
        var greenFlag = Mat("MC_FlagGreen", new Color(0.1f, 0.75f, 0.25f), 0f, 0.4f);
        var yellowFlag = Mat("MC_FlagYellow", new Color(1f, 0.86f, 0.05f), 0f, 0.4f, null, new Color(1f, 0.8f, 0f) * 0.4f);
        var posts = new List<MarshalPost>();
        int side = -1;
        for (float d = 60f; d < track.length - 20f; d += 180f)
        {
            side = -side;
            int i = Mathf.RoundToInt(d / track.spacing) % track.Count;
            if (InTunnel(d) || (side > 0 && PitZone(d))) side = -side;
            if (InTunnel(d)) continue;
            Vector3 r = Right(i) * side;
            Vector3 p = track.points[i] + r * (Off(i, side) + 1.6f) + Vector3.up * 0.15f;
            if (IsReserved(p, 1f)) { side = -side; r = -r; p = track.points[i] + r * (Off(i, side) + 1.6f) + Vector3.up * 0.15f; }
            var post = Child("MarshalPost", parent);
            post.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-r));
            CrewPart(post, PrimitiveType.Cube, new Vector3(0, 1.15f, 0.6f), new Vector3(1.6f, 2.3f, 1.4f), hut);
            CrewPart(post, PrimitiveType.Cube, new Vector3(0, 2.4f, 0.6f), new Vector3(1.9f, 0.2f, 1.7f), roof);
            CrewPart(post, PrimitiveType.Cube, new Vector3(0.7f, 0.5f, -0.4f), new Vector3(0.36f, 1f, 0.26f), orange);
            CrewPart(post, PrimitiveType.Sphere, new Vector3(0.7f, 1.2f, -0.4f), Vector3.one * 0.28f, orange);
            CrewPart(post, PrimitiveType.Cylinder, new Vector3(0.95f, 1.6f, -0.4f), new Vector3(0.04f, 0.8f, 0.04f), hut);
            var pivot = Child("FlagPivot", post);
            pivot.transform.localPosition = new Vector3(0.95f, 2.35f, -0.4f);
            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(flag.GetComponent<Collider>());
            flag.name = "Flag";
            flag.transform.SetParent(pivot.transform, false);
            flag.transform.localPosition = new Vector3(0.45f, -0.25f, 0);
            flag.transform.localScale = new Vector3(0.9f, 0.55f, 0.02f);
            flag.GetComponent<Renderer>().sharedMaterial = greenFlag;
            var mp = post.AddComponent<MarshalPost>();
            mp.trackDistance = d;
            mp.flag = flag.GetComponent<Renderer>();
            mp.flagPivot = pivot.transform;
            mp.green = greenFlag;
            mp.yellow = yellowFlag;
            posts.Add(mp);
            reserved.Add((p, 2f));
        }
        return posts.ToArray();
    }

    static void CrewPart(GameObject parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    static void Sign(GameObject parent, string text, Vector3 pos, Quaternion rot, float size, Color colour)
    {
        var go = Child("Sign_" + text, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = colour;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
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
                float baseOff = WalkOut(i, z.side);
                for (int k = 0; k < tiers; k++)
                {
                    float top = 0.6f + k * 0.75f, off = baseOff + 0.8f + k * 1.1f;
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
                float back = baseOff + 0.8f + tiers * 1.1f;
                concrete.Box(baseP + r * back + Vector3.up * 4f, new Vector3(0.4f, 8f, 4.05f), rot);
                canopy.Box(baseP + r * (back - 5f) + Vector3.up * 9.6f, new Vector3(11f, 0.25f, 4.1f), rot * Quaternion.Euler(0, 0, z.side * 6f));
                // fill under the stand down to the ground
                concrete.Box(new Vector3(0, baseP.y / 2f - 0.15f, 0) + Flat(baseP + r * (baseOff + 6f)), new Vector3(11.5f, baseP.y + 0.3f, 4.05f), rot);
                reserved.Add((baseP + r * (baseOff + 6f), 8f));
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
        float foot = Mathf.Min(GroundAt(c), c.y) - 1f;
        float mainTop = c.y + 26f, towerTop = c.y + 38f;
        facade.Box(ground + Vector3.up * ((mainTop + foot) / 2f), new Vector3(64f, mainTop - foot, 22f), rot);
        facade.Box(ground + rot * new Vector3(-26f, 0, 0) + Vector3.up * ((towerTop + foot) / 2f), new Vector3(10f, towerTop - foot, 12f), rot);
        facade.Box(ground + rot * new Vector3(26f, 0, 0) + Vector3.up * ((towerTop + foot) / 2f), new Vector3(10f, towerTop - foot, 12f), rot);
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
        // The harbour basin is carved into the terrain; the sea surface fills it.
        // Keep buildings out of the water and collect mooring spots for yachts.
        Vector3 centre = HarbourCentre();
        Quaternion rot = Facing(-60f);
        var cells = new List<Vector3>();
        for (int x = -16; x <= 16; x++)
            for (int z = -16; z <= 16; z++)
            {
                Vector3 p = centre + rot * new Vector3(x * 10f, 0, z * 10f);
                if (GroundAt(p) > SeaLevel - 1.5f) continue;
                reserved.Add((p, 6f));
                if (MinDistToTrack(p) > 20f && !IsReserved(p + Vector3.up * 1000f, 0f)) cells.Add(new Vector3(p.x, SeaLevel - 1.2f, p.z));
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
                float off = WalkOut(i, side) + 2.5f + depth / 2f;
                if (side > 0 && PitZone(d)) continue;
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
                float ground = GroundAt(c);
                if (ground < SeaLevel + 0.5f) { placed.RemoveAt(placed.Count - 1); continue; }   // no buildings in the water
                // Founded on the lowest ground under the footprint, so nothing floats on the hillside
                float baseY = Mathf.Min(ground, track.points[i].y) - 1.5f;
                // Box frame: local X points across the street (depth), local Z runs along it (width)
                foreach (var corner in new[] { new Vector3(depth, 0, width), new Vector3(-depth, 0, width), new Vector3(depth, 0, -width), new Vector3(-depth, 0, -width) })
                    baseY = Mathf.Min(baseY, GroundAt(c + Facing(d) * corner * 0.5f) - 1.5f);
                float topY = Mathf.Max(track.points[i].y, ground) + height;
                Quaternion rot = Facing(d);
                var fac = facades[rng.Next(facades.Length)];
                Vector3 centre = new Vector3(c.x, (baseY + topY) / 2f, c.z);
                fac.Box(centre, new Vector3(depth, topY - baseY, width), rot, false, false);
                (rng.NextDouble() < 0.4 ? terracotta : roof).Box(new Vector3(c.x, topY + 0.3f, c.z), new Vector3(depth + 0.4f, 0.6f, width + 0.4f), rot);
                if (rng.NextDouble() < 0.5)
                    roof.Box(new Vector3(c.x, topY + 1.5f, c.z) + rot * new Vector3(0, 0, (float)(rng.NextDouble() - 0.5) * width * 0.4f), new Vector3(4f, 2.4f, 4f), rot);

                // Balconies on the street-facing wall, from street level up (attached to the facade)
                Vector3 faceN = -r;
                float streetY = track.points[i].y + 0.15f;
                for (float y = Mathf.Max(streetY, baseY) + 4.4f; y < topY - 1f; y += 3.2f)
                    balcony.Box(new Vector3(c.x, y, c.z) + faceN * (depth / 2f + 0.45f), new Vector3(0.9f, 0.15f, width * 0.75f), rot);
                // Shop awning at street level, sloping down toward the street
                int ac = rng.Next(awnings.Length);
                GetBatch("Awning" + ac, Mat("MC_Awning" + ac, awnings[ac], 0f, 0.4f))
                    .Box(new Vector3(c.x, streetY + 3f, c.z) + faceN * (depth / 2f + 0.9f), new Vector3(1.8f, 0.12f, width * 0.8f), rot * Quaternion.Euler(0, 0, -side * 12f));
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
                float g = GroundAt(c);
                if (g < SeaLevel + 0.5f) continue;
                placed.Add((c, radius));
                float foot = g - 3f;
                float topY = g + 18f + (float)rng.NextDouble() * 35f;
                Quaternion rot = Facing(d);
                facades[rng.Next(facades.Length)].Box(new Vector3(c.x, (topY + foot) / 2f, c.z), new Vector3(w, topY - foot, dep), rot, false, false);
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
                if (side > 0 && PitZone(d)) continue;
                if (rng.NextDouble() < 0.5) continue;
                float off = WalkOut(i, side) - 0.9f;
                Vector3 p = track.points[i] + Right(i) * side * off;
                if (MinDistToTrack(p) < off - 0.3f || IsReserved(p, 0.5f)) continue;
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
        // Spans the track and the pit lane: left pillar behind the barrier, right pillar beyond the pit lane
        float xl = BarrierOut + 0.6f, xr = PitOuter + 0.7f;
        LocalBox("PillarL", g, new Vector3(-xl, 3.6f, 0), new Vector3(0.6f, 7.2f, 0.6f), steel, true);
        LocalBox("PillarR", g, new Vector3(xr, 3.6f, 0), new Vector3(0.6f, 7.2f, 0.6f), steel, true);
        LocalBox("Beam", g, new Vector3((xr - xl) / 2f, 7f, 0), new Vector3(xl + xr + 0.6f, 1.1f, 0.8f), steel, false);
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
            // Contact with the player is resolved by F1Car (horizontal, momentum-shared) instead of the solver
            car.GetComponent<BoxCollider>().isTrigger = true;
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
        var camData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
        camData.renderPostProcessing = true;
        camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
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
        => ExtrudeVar(name, parent, i => profile, absY, filter, mat, uvScale, collider);

    // As Extrude, but the cross-section may change along the track (same number of points everywhere).
    static GameObject ExtrudeVar(string name, GameObject parent, Func<int, Vector2[]> profileAt, bool[] absY, Func<int, bool> filter,
                                 Material mat, float uvScale, bool collider, PhysicsMaterial surface = null)
    {
        int n = track.Count;
        var right = new Vector3[n];
        for (int i = 0; i < n; i++) right[i] = Right(i);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3 P(int i, Vector2[] prof, int k)
        {
            Vector3 p = track.points[i] + right[i] * prof[k].x;
            p.y = absY != null && absY[k] ? prof[k].y : track.points[i].y + prof[k].y;
            return p;
        }
        for (int i = 0; i < n; i++)
        {
            if (!filter(i)) continue;
            int j = (i + 1) % n;
            Vector2[] pi = profileAt(i), pj = profileAt(j);
            float v0 = i * track.spacing / uvScale, v1 = (i + 1) * track.spacing / uvScale;
            float acc = 0f;
            for (int k = 0; k < pi.Length - 1; k++)
            {
                int b = v.Count;
                v.Add(P(i, pi, k)); v.Add(P(i, pi, k + 1)); v.Add(P(j, pj, k + 1)); v.Add(P(j, pj, k));
                float seg = Vector2.Distance(pi[k], pi[k + 1]);
                float u0 = acc / uvScale, u1 = (acc + seg) / uvScale;
                acc += seg;
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
        if (collider)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            if (surface != null) mc.sharedMaterial = surface;
        }
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

        // Upright cylinder (used for tyre-wall stacks), flat-shaded sides plus a top cap
        public void Cylinder(Vector3 c, float radius, float height, int segs = 10)
        {
            for (int s = 0; s < segs; s++)
            {
                float a0 = s / (float)segs * Mathf.PI * 2f, a1 = (s + 1) / (float)segs * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius, d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Vector3 b0 = c + d0 - Vector3.up * height / 2f, b1 = c + d1 - Vector3.up * height / 2f;
                Vector3 t0 = b0 + Vector3.up * height, t1 = b1 + Vector3.up * height;
                float u0 = s / (float)segs * 2f, u1 = (s + 1) / (float)segs * 2f;
                // Both windings with separate vertices, so the side is lit correctly whichever way it faces
                for (int side = 0; side < 2; side++)
                {
                    int b = v.Count;
                    v.AddRange(new[] { b0, b1, t1, t0 });
                    uv.AddRange(new[] { new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, height), new Vector2(u0, height) });
                    if (side == 0) t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                    else t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    int ct = v.Count;
                    v.AddRange(new[] { c + Vector3.up * height / 2f, t0, t1 });
                    uv.AddRange(new[] { Vector2.zero, Vector2.zero, Vector2.zero });
                    if (side == 0) t.AddRange(new[] { ct, ct + 2, ct + 1 }); else t.AddRange(new[] { ct, ct + 1, ct + 2 });
                }
            }
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
            Texture2D t = name == "Facade" ? FacadeTexture()
                        : name == "Ground" ? GroundTexture()
                        : name == "Asphalt" ? NoiseTexture(0.5f, 0.18f)
                        : name == "Gravel" ? NoiseTexture(0.8f, 0.45f)
                        : NoiseTexture(0.85f, 0.08f);
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

    // Mediterranean ground: warm stone and paving with patches of dry green
    static Texture2D GroundTexture()
    {
        const int size = 512;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var r = new Random(3);
        var px = new Color[size * size];
        Color stone = new Color(0.72f, 0.68f, 0.6f), green = new Color(0.36f, 0.45f, 0.25f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float patch = Mathf.PerlinNoise(x * 0.008f + 10f, y * 0.008f) * 0.7f + Mathf.PerlinNoise(x * 0.03f, y * 0.03f + 20f) * 0.3f;
                float g = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.65f, patch));
                float grain = ((float)r.NextDouble() - 0.5f) * 0.08f;
                Color c = Color.Lerp(stone, green, g) * (0.92f + grain);
                c.a = 1f;
                px[y * size + x] = c;
            }
        t.SetPixels(px);
        t.Apply();
        return t;
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
