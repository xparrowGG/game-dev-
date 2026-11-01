using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Runs the race: grid from the menu settings (or the scene as built), team liveries, start-light
// countdown, lap counting with checkpoints, timing, positions with live gaps, the chequered flag
// (everyone finishes on their next pass after the leader), results, pause menu, career scoring,
// race HUD and minimap.
public class RaceManager : MonoBehaviour
{
    public RaceTrack track;
    public VehicleBase player;
    public RaceAI[] ais;
    public int laps = 3;
    [Range(0.7f, 1.3f)] public float aiSkill = 1f;
    public Renderer[] startLights;
    public Material lightOn, lightOff;

    [Header("Session")]
    public bool useSession;              // take laps / grid / teams / assists from the main menu
    public float pitEntryDistance = -1f; // > 0: finished AI cars leave the track this far past the line
    public float pitLaneOffset = 10f;

    public class Racer
    {
        public string name, code;
        public Color color;
        public int team;
        public bool isPlayer, retired;
        public RaceAI ai;
        public Transform tr;
        public float progress, prevProgress, lateral, speed, finishTime;
        public bool finished;
    }
    public readonly List<Racer> racers = new List<Racer>();

    enum State { Countdown, Racing }
    State state = State.Countdown;
    float countdown, goDelay, raceTime, goBannerTimer, winnerBannerTimer;
    Racer playerRacer, winner;
    int crossings, lapsTimed;
    float prevD, lapStartTime, lastLap = -1f, bestLap = -1f, resultsTimer;
    readonly bool[] checkpoints = new bool[3];
    readonly List<Vector3> trail = new List<Vector3>();
    float trailTimer;
    readonly List<Racer> finishOrder = new List<Racer>();
    bool paused, recorded;

    Texture2D tex;
    float mapScale, mapMinX, mapMaxZ, styleScale = -1f;
    GUIStyle big, mid, small, center, button;

    // ---------- Setup ----------

    void Awake()
    {
        if (player is F1Car) Time.fixedDeltaTime = 0.01f;   // stiff suspension and tyres need 100 Hz physics
        Time.timeScale = 1f;
        if (useSession) GameSession.ApplySettings();
    }

    void Start()
    {
        tex = Texture2D.whiteTexture;
        goDelay = 5f + Random.Range(0.4f, 1.4f);
        if (useSession) SetupFromSession();
        else
        {
            playerRacer = new Racer { name = "YOU", code = "YOU", color = Color.white, isPlayer = true, tr = player.transform };
            racers.Add(playerRacer);
            foreach (var ai in ais)
            {
                ai.manager = this;
                racers.Add(new Racer { name = ai.driverName, code = Code(ai.driverName), color = ai.color, ai = ai, tr = ai.transform, progress = ai.progress, prevProgress = ai.progress, lateral = ai.lane });
            }
        }
        player.inputEnabled = false;
        player.onRespawnRequested = RespawnPlayer;
        prevD = track.ClosestDistance(player.transform.position);
        playerRacer.progress = prevD - track.length;
        SetLights(0);

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in track.points)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
        }
        mapMinX = minX; mapMaxZ = maxZ;
        mapScale = 1f / Mathf.Max(maxX - minX, maxZ - minZ);
    }

    void SetupFromSession()
    {
        var setup = GameSession.CareerMode && GameSession.HasCareer ? GameSession.CareerRaceSetup() : GameSession.Race;
        var settings = GameSession.Settings;
        laps = Mathf.Max(1, setup.laps);
        aiSkill = GameSession.DifficultySkill[Mathf.Clamp(setup.difficulty, 0, GameSession.DifficultySkill.Length - 1)];

        var field = GameSession.Grid(setup.team, setup.driverName, setup.driverCode);
        var playerTeam = GameSession.Teams[setup.team];

        // Player car: team livery, team pace, career upgrades, assists
        ApplyLivery(player.gameObject, playerTeam.primary, playerTeam.secondary, new Color(1f, 0.85f, 0.1f));
        if (player is F1Car f1)
        {
            float teamFactor = 1f + (playerTeam.performance - 1f) * 1.5f;
            var c = GameSession.CareerMode ? GameSession.Career : null;
            f1.SetPerformance(teamFactor * (c != null ? GameSession.UpgradeFactor(c.engine, 0.02f) : 1f),
                              teamFactor * (c != null ? GameSession.UpgradeFactor(c.aero, 0.025f) : 1f),
                              c != null ? GameSession.UpgradeFactor(c.chassis, 0.012f) : 1f);
            f1.abs = settings.abs;
            f1.tractionControl = settings.tractionControl;
            f1.steeringAssist = settings.steeringAssist;
            f1.autoGearbox = settings.autoGearbox;
        }
        var cam = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
        if (cam != null) cam.view = (ChaseCamera.View)Mathf.Clamp(settings.defaultCamera, 0, 2);

        playerRacer = new Racer { name = setup.driverName, code = setup.driverCode, color = playerTeam.primary, team = setup.team, isPlayer = true, tr = player.transform };
        racers.Add(playerRacer);

        // Rivals: one car per remaining driver, as many as the opponent setting allows
        var rivals = field.FindAll(d => !d.isPlayer);
        int count = Mathf.Clamp(setup.opponents, 0, Mathf.Min(rivals.Count, ais.Length));
        var scores = new Dictionary<Racer, float>();
        for (int i = 0; i < ais.Length; i++)
        {
            var ai = ais[i];
            if (i >= count) { ai.gameObject.SetActive(false); continue; }
            var d = rivals[i];
            var team = GameSession.Teams[d.team];
            float pace = team.performance * d.skill;
            ai.manager = this;
            ai.driverName = d.name;
            ai.driverCode = d.code;
            ai.team = d.team;
            ai.color = team.primary;
            ai.maxSpeed *= Mathf.Sqrt(pace);
            ai.cornerGrip *= pace * pace;
            ai.aeroGrip *= pace;
            ApplyLivery(ai.gameObject, team.primary, team.secondary, Color.HSVToRGB(Random.value, 0.6f, 0.95f));
            var r = new Racer { name = d.name, code = d.code, color = team.primary, team = d.team, ai = ai, tr = ai.transform };
            racers.Add(r);
            scores[r] = pace * aiSkill + Random.Range(-0.008f, 0.008f);
        }

        // Grid order
        var order = new List<Racer>(scores.Keys);
        order.Sort((a, b) => scores[b].CompareTo(scores[a]));
        int playerSlot;
        switch (setup.grid)
        {
            case 1: playerSlot = Random.Range(0, order.Count + 1); break;
            case 2: playerSlot = order.Count; break;
            case 3:
                var c = GameSession.CareerMode ? GameSession.Career : null;
                float upgrades = c != null ? 1f + 0.004f * (c.engine + c.aero + c.chassis) : 1f;
                float myScore = playerTeam.performance * upgrades + Random.Range(-0.01f, 0.01f);
                playerSlot = order.FindIndex(r => scores[r] < myScore);
                if (playerSlot < 0) playerSlot = order.Count;
                if (playerSlot == 0 && c != null) c.poles++;
                break;
            default: playerSlot = 0; break;
        }
        order.Insert(playerSlot, playerRacer);
        for (int k = 0; k < order.Count; k++)
        {
            var (d, lat) = GridSlot(k);
            if (order[k].isPlayer)
            {
                Vector3 pos = track.PointAt(d) + track.RightAt(d) * lat + Vector3.up * 0.12f;
                player.Teleport(pos, Quaternion.LookRotation(track.TangentAt(d)));
            }
            else
            {
                order[k].ai.SetGrid(d, lat);
                order[k].progress = order[k].prevProgress = d;
                order[k].lateral = lat;
            }
        }
    }

    (float d, float lat) GridSlot(int k)
    {
        float lat = Mathf.Min(2.4f, track.halfWidth - 2.2f) * (k % 2 == 0 ? -1f : 1f);
        return (-(10f + 8f * k), lat);
    }

    static string Code(string name)
    {
        string[] parts = name.Split(' ');
        string s = parts[parts.Length - 1].ToUpperInvariant();
        return s.Length >= 3 ? s.Substring(0, 3) : s;
    }

    // Recolour a car's livery materials (shared assets) with per-car instances.
    public static void ApplyLivery(GameObject car, Color primary, Color secondary, Color helmet)
    {
        var cache = new Dictionary<Material, Material>();
        foreach (var r in car.GetComponentsInChildren<Renderer>(true))
        {
            var src = r.sharedMaterial;
            if (src == null) continue;
            string n = src.name.Replace(" (Instance)", "");
            Color? col = n == "F1_Livery" ? primary : n == "F1_Accent" ? secondary : n == "F1_Helmet" ? helmet
                       : n == "F1_WheelCover" ? Color.Lerp(secondary, Color.black, 0.35f) : (Color?)null;
            if (!col.HasValue) continue;
            if (!cache.TryGetValue(src, out var inst))
            {
                inst = new Material(src);
                if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", col.Value);
                if (inst.HasProperty("_Color")) inst.SetColor("_Color", col.Value);
                cache[src] = inst;
            }
            r.sharedMaterial = inst;
        }
    }

    // ---------- Loop ----------

    void Update()
    {
        bool restart = false, pause = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            restart = (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) && (playerRacer.finished || !useSession);
            pause = k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame;
        }
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame) pause = true;
#else
        restart = (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && (playerRacer.finished || !useSession);
        pause = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);
#endif
        if (restart) { Restart(); return; }
        if (pause && !playerRacer.finished) SetPaused(!paused);
        if (paused) return;

        goBannerTimer -= Time.deltaTime;
        winnerBannerTimer -= Time.deltaTime;
        resultsTimer -= Time.deltaTime;
        trailTimer -= Time.deltaTime;
        if (trailTimer <= 0f)
        {
            trailTimer = 0.25f;
            trail.Add(player.transform.position);
            if (trail.Count > 40) trail.RemoveAt(0);
        }

        if (state == State.Countdown)
        {
            countdown += Time.deltaTime;
            SetLights(Mathf.Min(5, Mathf.FloorToInt(countdown)));
            if (countdown >= goDelay)
            {
                state = State.Racing;
                SetLights(0);
                player.inputEnabled = true;
                goBannerTimer = 1.5f;
            }
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        bool racing = state == State.Racing;
        if (racing) raceTime += dt;

        // Player progress. Laps only count after passing the three checkpoints (no shortcuts).
        float d = track.ClosestDistance(player.transform.position, prevD);
        float lateral = track.LateralOffset(player.transform.position, d);
        if (Mathf.Abs(lateral) < track.halfWidth + 8f)
        {
            for (int k = 0; k < checkpoints.Length; k++)
                if (Mathf.Abs(d - (k + 1) * track.length / 4f) < 15f) checkpoints[k] = true;

            float delta = d - prevD;
            if (delta < -track.length / 2f)
            {
                if (crossings == 0 || System.Array.TrueForAll(checkpoints, c => c))
                {
                    crossings++;
                    System.Array.Clear(checkpoints, 0, checkpoints.Length);
                    OnPlayerCrossedLine();
                }
            }
            else if (delta > track.length / 2f && crossings > 0)
            {
                crossings--;
                for (int k = 0; k < checkpoints.Length; k++) checkpoints[k] = true;
            }
            prevD = d;
        }
        playerRacer.progress = (crossings - 1) * track.length + prevD;
        playerRacer.lateral = lateral;
        playerRacer.speed = player.Speed;

        foreach (var r in racers)
        {
            if (r.isPlayer || !r.ai.gameObject.activeSelf) continue;
            r.prevProgress = r.progress;
            r.ai.Step(dt, racing, r.finished, aiSkill);
            r.progress = r.ai.progress;
            r.lateral = r.ai.lane;
            r.speed = r.ai.Speed;
            if (r.finished) continue;
            bool crossedLine = Mathf.FloorToInt(r.progress / track.length) > Mathf.FloorToInt(r.prevProgress / track.length) && r.progress > 0f;
            if (r.progress >= laps * track.length || (winner != null && crossedLine)) Finish(r);
        }
    }

    void OnPlayerCrossedLine()
    {
        if (crossings < 2 || crossings - 1 <= lapsTimed || playerRacer.finished) return;
        lapsTimed = crossings - 1;
        lastLap = raceTime - lapStartTime;
        if (bestLap < 0 || lastLap < bestLap) bestLap = lastLap;
        lapStartTime = raceTime;
        if (lapsTimed >= laps || winner != null) Finish(playerRacer);
    }

    void Finish(Racer r)
    {
        r.finished = true;
        r.finishTime = raceTime;
        finishOrder.Add(r);
        if (winner == null) { winner = r; winnerBannerTimer = 5f; }
        if (r.isPlayer)
        {
            resultsTimer = 12f;
            player.inputEnabled = false;
        }
        else
        {
            r.ai.parkProgress = pitEntryDistance > 0f
                ? Mathf.Ceil(r.progress / track.length) * track.length + pitEntryDistance
                : laps * track.length + 95f - 11f * (finishOrder.FindAll(f => !f.isPlayer).Count - 1);
            if (pitEntryDistance > 0f) { r.ai.parkLane = pitLaneOffset; r.ai.leaveAfterFinish = true; }
        }
    }

    void RespawnPlayer()
    {
        if (state != State.Racing || playerRacer.finished) return;
        Vector3 pos = track.PointAt(prevD) + Vector3.up * 0.15f;
        player.Teleport(pos, Quaternion.LookRotation(track.TangentAt(prevD)));
    }

    void SetLights(int count)
    {
        for (int i = 0; i < startLights.Length; i++)
            if (startLights[i] != null) startLights[i].sharedMaterial = i < count ? lightOn : lightOff;
    }

    void SetPaused(bool p)
    {
        paused = p;
        Time.timeScale = p ? 0f : 1f;
        AudioListener.pause = p;
    }

    List<Racer> Standings()
    {
        var list = new List<Racer>(finishOrder);
        var running = racers.FindAll(r => !r.finished);
        running.Sort((a, b) => b.progress.CompareTo(a.progress));
        list.AddRange(running);
        return list;
    }

    int LapOf(Racer r) => Mathf.Clamp(Mathf.FloorToInt(r.progress / track.length) + 1, 1, laps);

    // ---------- Scene flow ----------

    void Restart()
    {
        SetPaused(false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void ToMenu()
    {
        SetPaused(false);
        if (Application.CanStreamedLevelBeLoaded(GameSession.MenuScene)) SceneManager.LoadScene(GameSession.MenuScene);
        else Restart();
    }

    void RecordCareer(bool dnf)
    {
        if (recorded || !GameSession.CareerMode || !GameSession.HasCareer) return;
        recorded = true;
        var order = Standings().ConvertAll(r => r.name);
        if (dnf) { order.Remove(playerRacer.name); order.Add(playerRacer.name); }
        GameSession.RecordCareerRace(order, dnf);
        GameSession.OpenCareerHub = true;
    }

    static string FormatTime(float t)
    {
        if (t < 0) return "--:--.---";
        int m = Mathf.FloorToInt(t / 60f);
        float sec = t - m * 60f;
        return string.Format("{0}:{1:00.000}", m, sec);
    }

    string GapText(Racer r, Racer leader)
    {
        if (r == leader) return r.finished ? FormatTime(r.finishTime) : "LEADER";
        if (r.finished && leader.finished)
        {
            int lapsDown = Mathf.RoundToInt((leader.progress - r.progress) / track.length);
            return lapsDown >= 1 ? "+" + lapsDown + (lapsDown == 1 ? " LAP" : " LAPS") : "+" + (r.finishTime - leader.finishTime).ToString("0.000");
        }
        float diff = leader.progress - r.progress;
        if (diff >= track.length) { int n = Mathf.FloorToInt(diff / track.length); return "+" + n + (n == 1 ? " LAP" : " LAPS"); }
        return "+" + (diff / Mathf.Max(r.speed, 25f)).ToString("0.000");
    }

    // ---------- HUD ----------

    void Fill(Rect r, Color c, float radius = 0, float border = 0)
    {
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true, 0, c, border, radius);
    }

    GUIStyle Style(int size, FontStyle fs, TextAnchor a)
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a };
        st.normal.textColor = Color.white;
        return st;
    }

    void OnGUI()
    {
        if (playerRacer == null) return;
        float s = Screen.height / 1080f;
        if (!Mathf.Approximately(s, styleScale))
        {
            styleScale = s;
            big = Style(Mathf.RoundToInt(34 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
            mid = Style(Mathf.RoundToInt(18 * s), FontStyle.Bold, TextAnchor.MiddleLeft);
            small = Style(Mathf.RoundToInt(15 * s), FontStyle.Normal, TextAnchor.MiddleCenter);
            small.normal.textColor = new Color(1, 1, 1, 0.7f);
            center = Style(Mathf.RoundToInt(64 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold };
        }
        var standings = Standings();

        DrawTopPanel(s, standings);
        DrawMinimap(s, standings);
        DrawTower(s, standings);

        if (state == State.Countdown) DrawStartLights(s);
        if (goBannerTimer > 0) Banner(s, "GO!", new Color(0.3f, 1f, 0.4f));

        Vector3 tangent = track.TangentAt(prevD);
        bool onTrack = Mathf.Abs(playerRacer.lateral) < track.halfWidth + 2f;
        if (state == State.Racing && !playerRacer.finished && onTrack && player.Speed > 3f && Vector3.Dot(player.transform.forward, tangent) < -0.3f)
            Banner(s, "WRONG WAY", new Color(1f, 0.3f, 0.25f));
        if (winner != null && !playerRacer.finished && winnerBannerTimer <= 0 && state == State.Racing)
        {
            var st = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter };
            Fill(new Rect(Screen.width / 2f - 200 * s, 128 * s, 400 * s, 34 * s), new Color(0, 0, 0, 0.7f), 8 * s);
            GUI.Label(new Rect(Screen.width / 2f - 200 * s, 128 * s, 400 * s, 34 * s), "CHEQUERED FLAG - finish this lap", st);
        }

        if (playerRacer.finished && resultsTimer > 0) DrawResults(s, standings);
        else if (playerRacer.finished) DrawFinishedStrip(s, standings);
        else if (winner != null && winnerBannerTimer > 0) Banner(s, winner.name.ToUpperInvariant() + " WINS!", new Color(1f, 0.85f, 0.2f));

        if (paused) DrawPause(s);
    }

    void Banner(float s, string text, Color c)
    {
        var st = new GUIStyle(center);
        st.normal.textColor = c;
        Rect r = new Rect(0, Screen.height * 0.3f, Screen.width, 90 * s);
        Fill(new Rect(Screen.width / 2f - 380 * s, r.y, 760 * s, r.height), new Color(0, 0, 0, 0.55f), 16 * s);
        GUI.Label(r, text, st);
    }

    void DrawTopPanel(float s, List<Racer> standings)
    {
        Rect p = new Rect(Screen.width / 2f - 280 * s, 16 * s, 560 * s, 104 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.78f), 14 * s);
        int lap = Mathf.Clamp(crossings, 1, laps);
        int pos = standings.IndexOf(playerRacer) + 1;
        float lapTime = state == State.Racing && !playerRacer.finished ? raceTime - lapStartTime : (playerRacer.finished ? lastLap : 0f);

        float col = p.width / 3f;
        GUI.Label(new Rect(p.x, p.y + 8 * s, col, 20 * s), "LAP", small);
        GUI.Label(new Rect(p.x, p.y + 28 * s, col, 44 * s), lap + "/" + laps, big);
        GUI.Label(new Rect(p.x + col, p.y + 8 * s, col, 20 * s), "POSITION", small);
        GUI.Label(new Rect(p.x + col, p.y + 28 * s, col, 44 * s), "P" + pos + "/" + racers.Count, big);
        GUI.Label(new Rect(p.x + 2 * col, p.y + 8 * s, col, 20 * s), "RACE TIME", small);
        GUI.Label(new Rect(p.x + 2 * col, p.y + 28 * s, col, 44 * s), FormatTime(playerRacer.finished ? playerRacer.finishTime : raceTime), big);
        GUI.Label(new Rect(p.x, p.y + 76 * s, p.width, 22 * s),
            "LAP " + FormatTime(lapTime) + "     LAST " + FormatTime(lastLap) + "     BEST " + FormatTime(bestLap), small);
    }

    void DrawStartLights(float s)
    {
        int lit = Mathf.Min(5, Mathf.FloorToInt(countdown));
        float size = 46 * s, gap = 14 * s;
        float total = 5 * size + 4 * gap;
        Rect panel = new Rect(Screen.width / 2f - total / 2f - 16 * s, 136 * s, total + 32 * s, size + 32 * s);
        Fill(panel, new Color(0.03f, 0.03f, 0.04f, 0.85f), 12 * s);
        for (int i = 0; i < 5; i++)
        {
            Rect r = new Rect(panel.x + 16 * s + i * (size + gap), panel.y + 16 * s, size, size);
            Fill(r, i < lit ? new Color(1f, 0.08f, 0.05f) : new Color(0.2f, 0.05f, 0.05f), size / 2f);
        }
    }

    // Timing tower: position, team colour, driver code, gap to the leader
    void DrawTower(float s, List<Racer> standings)
    {
        float rowH = 21 * s, w = 210 * s;
        Rect p = new Rect(16 * s, 232 * s, w, 34 * s + rowH * standings.Count);
        Fill(p, new Color(0.04f, 0.05f, 0.08f, 0.8f), 10 * s);
        var head = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(p.x + 12 * s, p.y + 4 * s, w, 24 * s), "LAP " + Mathf.Clamp(winner != null ? laps : LapOf(standings[0]), 1, laps) + " / " + laps, head);
        var codeSt = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(14 * s) };
        var gapSt = new GUIStyle(codeSt) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        var posSt = new GUIStyle(codeSt) { alignment = TextAnchor.MiddleCenter };
        Racer leader = standings[0];
        for (int i = 0; i < standings.Count; i++)
        {
            var r = standings[i];
            float y = p.y + 30 * s + i * rowH;
            if (r.isPlayer) Fill(new Rect(p.x + 4 * s, y, w - 8 * s, rowH - 1 * s), new Color(0.15f, 0.4f, 1f, 0.5f), 4 * s);
            GUI.Label(new Rect(p.x + 4 * s, y, 26 * s, rowH), (i + 1).ToString(), posSt);
            Fill(new Rect(p.x + 32 * s, y + 4 * s, 4 * s, rowH - 8 * s), r.color);
            GUI.Label(new Rect(p.x + 42 * s, y, 60 * s, rowH), r.code, codeSt);
            GUI.Label(new Rect(p.x + 90 * s, y, w - 100 * s, rowH), r.finished && r != leader && !leader.finished ? "FIN" : GapText(r, leader), gapSt);
        }
    }

    static readonly Color[] SectorColors = { new Color(1f, 0.3f, 0.35f), new Color(0.25f, 0.8f, 1f), new Color(1f, 0.82f, 0.2f) };
    static readonly Color PlayerBlue = new Color(0.15f, 0.45f, 1f);

    Vector2 MapPoint(Vector3 p, Rect area)
    {
        return new Vector2(area.x + (p.x - mapMinX) * mapScale * area.width, area.y + (mapMaxZ - p.z) * mapScale * area.height);
    }

    // Broadcast-style map: track ribbon with coloured sectors, checkered start line, numbered markers,
    // the player's heading needle, a fading trail and lap progress.
    void DrawMinimap(float s, List<Racer> standings)
    {
        float w = 280 * s, h = 300 * s;
        Rect panel = new Rect(Screen.width - w - 16 * s, 16 * s, w, h);
        Fill(panel, new Color(0.04f, 0.05f, 0.08f, 0.82f), 16 * s);
        Fill(panel, new Color(1f, 1f, 1f, 0.12f), 16 * s, 1.5f * s);

        var header = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(panel.x + 16 * s, panel.y + 8 * s, w - 110 * s, 20 * s), track.trackName.ToUpperInvariant(), header);
        for (int k = 0; k < 3; k++)
        {
            Rect chip = new Rect(panel.xMax - (3 - k) * 30 * s - 10 * s, panel.y + 12 * s, 24 * s, 12 * s);
            Fill(chip, SectorColors[k], 4 * s);
            var cs = new GUIStyle(small) { fontSize = Mathf.RoundToInt(9 * s), fontStyle = FontStyle.Bold };
            cs.normal.textColor = Color.black;
            GUI.Label(chip, "S" + (k + 1), cs);
        }

        Rect area = new Rect(panel.x + 24 * s, panel.y + 40 * s, w - 48 * s, w - 48 * s);
        int step = Mathf.Max(1, track.Count / 450);
        float outer = 11 * s, inner = 4 * s;
        for (int i = 0; i < track.Count; i += step)
        {
            Vector2 m = MapPoint(track.points[i], area);
            Fill(new Rect(m.x - outer / 2, m.y - outer / 2, outer, outer), new Color(0.16f, 0.17f, 0.2f, 1f), outer / 2);
        }
        for (int i = 0; i < track.Count; i += step)
        {
            Vector2 m = MapPoint(track.points[i], area);
            Fill(new Rect(m.x - inner / 2, m.y - inner / 2, inner, inner), SectorColors[Mathf.Min(2, i * 3 / track.Count)], inner / 2);
        }

        Vector2 start = MapPoint(track.points[0], area);
        Vector3 t0 = track.TangentAt(0);
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(t0.x, t0.z) * Mathf.Rad2Deg + 90f, start);
        for (int k = 0; k < 4; k++)
            for (int row = 0; row < 2; row++)
                Fill(new Rect(start.x - 8 * s + k * 4 * s, start.y - 4 * s + row * 4 * s, 4 * s, 4 * s), (k + row) % 2 == 0 ? Color.white : Color.black);
        GUI.matrix = saved;

        for (int i = 0; i < trail.Count; i++)
        {
            Vector2 m = MapPoint(trail[i], area);
            float a = (i + 1f) / trail.Count;
            float d = (2f + 3f * a) * s;
            Fill(new Rect(m.x - d / 2, m.y - d / 2, d, d), new Color(PlayerBlue.r, PlayerBlue.g, PlayerBlue.b, a * 0.8f), d / 2);
        }

        var num = new GUIStyle(small) { fontSize = Mathf.RoundToInt(9 * s), fontStyle = FontStyle.Bold };
        num.normal.textColor = Color.black;
        foreach (var r in racers)
        {
            if (r.isPlayer || !r.ai.gameObject.activeSelf) continue;
            Vector2 m = MapPoint(r.tr.position, area);
            float d = 14 * s;
            Fill(new Rect(m.x - d / 2 - 1.5f * s, m.y - d / 2 - 1.5f * s, d + 3 * s, d + 3 * s), new Color(0, 0, 0, 0.8f), d);
            Fill(new Rect(m.x - d / 2, m.y - d / 2, d, d), r.color, d / 2);
            var ns = new GUIStyle(num);
            ns.normal.textColor = r.color.grayscale > 0.5f ? Color.black : Color.white;
            GUI.Label(new Rect(m.x - d / 2, m.y - d / 2, d, d), (standings.IndexOf(r) + 1).ToString(), ns);
        }

        Vector2 pm = MapPoint(player.transform.position, area);
        float pulse = (22f + 6f * Mathf.Sin(Time.unscaledTime * 6f)) * s;
        Fill(new Rect(pm.x - pulse / 2, pm.y - pulse / 2, pulse, pulse), new Color(1f, 1f, 1f, 0.6f), pulse / 2, 2f * s);
        Vector3 f = player.transform.forward;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, pm);
        Fill(new Rect(pm.x - 2.5f * s, pm.y - 20 * s, 5 * s, 12 * s), Color.white, 2 * s);
        GUI.matrix = saved;
        float pd = 18 * s;
        Fill(new Rect(pm.x - pd / 2 - 2 * s, pm.y - pd / 2 - 2 * s, pd + 4 * s, pd + 4 * s), Color.white, pd);
        Fill(new Rect(pm.x - pd / 2, pm.y - pd / 2, pd, pd), PlayerBlue, pd / 2);
        var pnum = new GUIStyle(num);
        pnum.normal.textColor = Color.white;
        GUI.Label(new Rect(pm.x - pd / 2, pm.y - pd / 2, pd, pd), (standings.IndexOf(playerRacer) + 1).ToString(), pnum);

        float lapFrac = crossings == 0 ? 0f : Mathf.Clamp01(prevD / track.length);
        Rect bar = new Rect(panel.x + 16 * s, panel.yMax - 22 * s, w - 32 * s, 8 * s);
        Fill(bar, new Color(1f, 1f, 1f, 0.12f), 4 * s);
        Fill(new Rect(bar.x, bar.y, bar.width * lapFrac, bar.height), SectorColors[Mathf.Min(2, (int)(lapFrac * 3f))], 4 * s);
        for (int k = 1; k < 3; k++)
            Fill(new Rect(bar.x + bar.width * k / 3f - 1 * s, bar.y - 2 * s, 2 * s, bar.height + 4 * s), new Color(1f, 1f, 1f, 0.5f));
    }

    void DrawFinishedStrip(float s, List<Racer> standings)
    {
        Rect p = new Rect(Screen.width / 2f - 330 * s, 128 * s, 660 * s, 40 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.8f), 10 * s);
        var st = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(1f, 0.85f, 0.2f);
        GUI.Label(p, "WINNER: " + winner.name + "   ·   You finished P" + (finishOrder.IndexOf(playerRacer) + 1) + "   ·   TAB: results", st);
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) resultsTimer = 999f;
#endif
        if (GUI.Button(new Rect(p.xMax + 10 * s, p.y, 120 * s, p.height), "RESULTS", button)) resultsTimer = 999f;
    }

    void DrawResults(float s, List<Racer> standings)
    {
        float rowH = 26 * s;
        int rows = standings.Count;
        Rect p = new Rect(Screen.width / 2f - 330 * s, Screen.height / 2f - (150 * s + rowH * rows) / 2f, 660 * s, 160 * s + rowH * rows);
        Fill(p, new Color(0.03f, 0.04f, 0.07f, 0.94f), 18 * s);

        bool youWon = winner == playerRacer;
        var title = new GUIStyle(big);
        title.normal.textColor = youWon ? new Color(1f, 0.85f, 0.2f) : Color.white;
        GUI.Label(new Rect(p.x, p.y + 10 * s, p.width, 44 * s), youWon ? "YOU WIN!" : "WINNER: " + winner.name.ToUpperInvariant(), title);
        GUI.Label(new Rect(p.x, p.y + 52 * s, p.width, 22 * s), track.trackName.ToUpperInvariant() + "  ·  " + laps + " LAPS  ·  BEST LAP " + FormatTime(bestLap), small);

        var nameSt = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(15 * s) };
        var right = new GUIStyle(nameSt) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        Racer leader = standings[0];
        for (int i = 0; i < rows; i++)
        {
            var r = standings[i];
            float y = p.y + 80 * s + i * rowH;
            if (r.isPlayer) Fill(new Rect(p.x + 16 * s, y, p.width - 32 * s, rowH - 2 * s), new Color(0.15f, 0.4f, 1f, 0.45f), 6 * s);
            Fill(new Rect(p.x + 64 * s, y + 5 * s, 4 * s, rowH - 10 * s), r.color);
            GUI.Label(new Rect(p.x + 24 * s, y, 40 * s, rowH), (i + 1).ToString(), nameSt);
            string team = GameSession.Teams.Length > r.team && useSession ? GameSession.Teams[r.team].name : "";
            GUI.Label(new Rect(p.x + 76 * s, y, 330 * s, rowH), r.name + (team.Length > 0 ? "  —  " + team : ""), nameSt);
            int pts = useSession && i < GameSession.Points.Length ? GameSession.Points[i] : 0;
            GUI.Label(new Rect(p.x + 24 * s, y, p.width - 120 * s, rowH), r.finished ? GapText(r, leader) : "running", right);
            GUI.Label(new Rect(p.x + 24 * s, y, p.width - 48 * s, rowH), pts > 0 ? "+" + pts : "", right);
        }

        float by = p.yMax - 56 * s, bw = 190 * s;
        if (GameSession.CareerMode && useSession)
        {
            if (GUI.Button(new Rect(p.x + p.width / 2f - bw / 2f, by, bw, 42 * s), "CONTINUE", button)) { RecordCareer(false); ToMenu(); }
        }
        else
        {
            if (GUI.Button(new Rect(p.x + p.width / 2f - bw - 8 * s, by, bw, 42 * s), "RACE AGAIN", button)) Restart();
            if (GUI.Button(new Rect(p.x + p.width / 2f + 8 * s, by, bw, 42 * s), "MAIN MENU", button)) ToMenu();
        }
    }

    void DrawPause(float s)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
        Rect p = new Rect(Screen.width / 2f - 200 * s, Screen.height / 2f - 170 * s, 400 * s, 340 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.95f), 18 * s);
        GUI.Label(new Rect(p.x, p.y + 16 * s, p.width, 50 * s), "PAUSED", big);
        float bw = 300 * s, bh = 48 * s, x = p.x + (p.width - bw) / 2f, y = p.y + 86 * s;
        if (GUI.Button(new Rect(x, y, bw, bh), "RESUME", button)) SetPaused(false);
        if (GUI.Button(new Rect(x, y + 60 * s, bw, bh), "RESTART RACE", button)) Restart();
        string quit = GameSession.CareerMode && useSession ? "RETIRE (DNF) & QUIT" : "QUIT TO MAIN MENU";
        if (GUI.Button(new Rect(x, y + 120 * s, bw, bh), quit, button)) { RecordCareer(true); ToMenu(); }
        GUI.Label(new Rect(p.x, p.yMax - 40 * s, p.width, 24 * s), "Esc / P to resume", small);
    }
}
