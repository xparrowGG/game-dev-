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

    [Header("Pit lane (Monaco)")]
    public float pitEntryStart = -168f, pitEntryEnd = -140f;   // gap in the pit wall where cars turn in
    public float pitExitStart = 112f, pitExitEnd = 142f;       // gap where cars rejoin
    public float pitSpeed = 22.2f;                             // 80 km/h limiter
    public float[] boxDistances;                               // where each team's car stops, along the track
    public float[] boxOffsets;                                 // lateral pit-apron offset per team in front of its garage bay
    public float garageSide = 1f;                              // +1 right of pit fast lane, -1 left of pit fast lane
    public PitBox[] pitBoxes;
    public MarshalPost[] marshalPosts;

    [Header("Audio")]
    public EngineAudio rivalAudio;       // voices the nearest rival car (3D, with Doppler)
    public AmbientAudio ambient;
    EngineAudio playerAudio;

    [Header("Real circuit data")]
    public bool snapToGround;                 // AI cars follow the scanned surface (camber, banking, kerbs)
    public float pitProfileStart;             // track distance of pitProfile[0]
    public float pitProfileStep = 2f;
    public float[] pitProfile;                // lateral offset of the real pit lane, sampled along the track
    public Vector2[] drsZones;                // (start, end) track distances where DRS can be opened

    public float PitSide => pitLaneOffset >= 0f ? 1f : -1f;
    public float PitLimit => Mathf.Abs(pitLaneOffset) + (pitProfile != null && pitProfile.Length > 0 ? 6f : 0.5f);

    // Lateral position of the pit lane at a track distance (follows the real pit lane where it is known).
    public float PitLat(float d)
    {
        if (pitProfile == null || pitProfile.Length < 2 || track == null) return pitLaneOffset;
        float u = Mathf.Repeat(d - pitProfileStart, track.length) / pitProfileStep;
        if (u > pitProfile.Length - 1) return pitProfile[pitProfile.Length - 1];
        int i = Mathf.Clamp((int)u, 0, pitProfile.Length - 2);
        return Mathf.Lerp(pitProfile[i], pitProfile[i + 1], u - i);
    }

    public bool InDrsZone(float d)
    {
        if (drsZones == null || track == null) return false;
        foreach (var z in drsZones) if (InWindow(d, z.x, z.y, track.length)) return true;
        return false;
    }

    public bool HasPitLane => boxDistances != null && boxDistances.Length > 0;
    public bool MandatoryStop => HasPitLane && laps >= 3 && !(useSession && GameSession.Race.timeTrial && !GameSession.CareerMode);
    public int PlayerLap { get; private set; } = 1;
    public int PlayerPosition { get; private set; } = 1;
    public float BoxDistance(int team) => boxDistances[Mathf.Clamp(team, 0, boxDistances.Length - 1)];
    public float BoxLat(int team) => boxOffsets != null && boxOffsets.Length > 0 ? boxOffsets[Mathf.Clamp(team, 0, boxOffsets.Length - 1)] : PitLat(BoxDistance(team));
    public bool PlayerInPit => pitPhase != PitPhase.None;
    public static bool PitServiceActive { get; private set; }
    public static float PitCameraSide { get; private set; } = -1f;
    public string PitDebugState => $"{pitPhase} s={pitS:0.0} lat={pitLat:0.0}";
    public static bool InWindow(float d, float a, float b, float length) => Mathf.Repeat(d - a, length) <= Mathf.Repeat(b - a, length);

    public class Racer
    {
        public string name, code;
        public Color color;
        public int team;
        public bool isPlayer, retired, tyresHidden;
        public RaceAI ai;
        public Transform tr;
        public float progress, prevProgress, lateral, speed, finishTime, penalty;
        public bool finished;
        public int currentSector;
        public float sectorStartTime;
    }

    // Player tyres and pit stop
    enum PitPhase { None, Approach, BoxEntry, Stopping, Service, BoxExit, Release, Exit }
    PitPhase pitPhase;
    Compound playerCompound = Compound.Medium, playerNext = Compound.Hard;
    float playerWear, pitS, pitLat, pitLatVel, pitV, pitTimer, serviceTime, pitJackBlend, pitExitStartLat;
    Quaternion pitHeading = Quaternion.identity;
    readonly bool[] compoundsUsed = new bool[3];
    bool pitRequested, testAutoPit, tyresSwapped, pitNeedWingRepair, pitWingRepaired;
    int playerPitStops;
    float incidentD, incidentTime = -100f, lastIncidentSeen = -1f;
    string penaltyNote = "";
    public readonly List<Racer> racers = new List<Racer>();

    enum State { Countdown, Racing }
    State state = State.Countdown;
    float countdown, goDelay, raceTime, goBannerTimer, winnerBannerTimer;
    Racer playerRacer, winner;
    int crossings, lapsTimed;
    float prevD, lapStartTime, lastLap = -1f, bestLap = -1f, resultsTimer;
    int currentSector, trackLimitWarnings;
    float sectorStartTime, offTrackCooldown, trackLimitBannerTimer;
    string trackLimitMsg = "";
    readonly float[] lastSectors = { -1f, -1f, -1f };
    readonly float[] bestSectors = { -1f, -1f, -1f };
    readonly float[] overallBestSectors = { -1f, -1f, -1f };
    readonly int[] sectorStatus = new int[3];
    readonly bool[] checkpoints = new bool[3];
    readonly List<Vector3> trail = new List<Vector3>();
    float trailTimer;
    readonly List<Racer> finishOrder = new List<Racer>();
    bool paused, recorded;

    Texture2D tex;
    float mapScale, mapMinX, mapMaxZ, styleScale = -1f;
    GUIStyle big, mid, small, center, button;
    GUIStyle midCenter, bannerStyle, towerHead, towerCode, towerGap, towerPos;
    GUIStyle miniBadge, miniPit, mapHeader, mapNumBlack, mapNumWhite;
    GUIStyle drsStyle, tyreLetter, smallLeft, statusBold, pitChoiceStyle;
    GUIStyle resultsName, resultsRight, radioHead, radioBody, tagPos, tagCode, sectorStyle;

    // ---------- Setup ----------

    bool redirecting;

    void Awake()
    {
        // Opened directly (e.g. Play in this scene)? Start the game at the main menu like a real game.
        bool directTest = false;
#if UNITY_EDITOR
        directTest = UnityEditor.SessionState.GetBool("xparrow_direct_play", false);
        UnityEditor.SessionState.SetBool("xparrow_direct_play", false);
#endif
        if (useSession && !GameSession.StartedFromMenu && !directTest && Application.CanStreamedLevelBeLoaded(GameSession.MenuScene))
        {
            redirecting = true;
            SceneManager.LoadScene(GameSession.MenuScene);
            return;
        }
        if (player is F1Car) Time.fixedDeltaTime = 0.01f;   // stiff suspension and tyres need 100 Hz physics
        Time.timeScale = 1f;
        if (useSession) GameSession.ApplySettings();
    }

    void Start()
    {
        if (redirecting) { enabled = false; return; }
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
        if (HasPitLane && (pitBoxes == null || pitBoxes.Length == 0)) BuildPitCrews();
        testAutoPit = false;
        player.inputEnabled = false;
        player.onRespawnRequested = RespawnPlayer;
        playerAudio = player.GetComponentInChildren<EngineAudio>();
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

        BuildSpeedProfile();
        if (useSession && GameSession.Settings.brakingLine && lineGreen != null) BuildBrakingMarkers();
    }

    // ---------- Team radio: the race engineer's short messages ----------

    string radioText = "";
    float radioTimer, lastRadioTime = -100f, posTimer, lastPosMsgTime = -100f, lastGravelMsg = -100f, lastYellowMsg = -100f;
    int lastPos = -1;
    bool tyreWarned, stopWarned, lastLapSaid;

    void Radio(string message, bool priority = false)
    {
        if (useSession && !GameSession.Settings.teamRadio) return;
        if (!priority && Time.time - lastRadioTime < 5f) return;
        radioText = message;
        radioTimer = 6f;
        lastRadioTime = Time.time;
        if (playerAudio != null) playerAudio.PlayRadio();
    }

    string GapAheadText(List<Racer> standings)
    {
        int i = standings.IndexOf(playerRacer);
        if (i <= 0) return "you're leading";
        var ahead = standings[i - 1];
        float refSpeed = Mathf.Clamp(track.length / 76f, 44f, 68f);
        float gap = Mathf.Max(0f, ahead.progress - playerRacer.progress) / refSpeed;
        return "gap to " + ahead.code + " is " + gap.ToString("0.0") + " seconds";
    }

    void UpdateRadio(List<Racer> standings)
    {
        radioTimer -= Time.deltaTime;
        if (state != State.Racing || playerRacer.finished) return;
        int pos = standings.IndexOf(playerRacer) + 1;
        posTimer -= Time.deltaTime;
        if (posTimer <= 0f)
        {
            posTimer = 1f;
            if (lastPos > 0 && pos != lastPos && Time.time - lastPosMsgTime > 8f && raceTime > 8f)
            {
                lastPosMsgTime = Time.time;
                Radio(pos < lastPos ? "Great move! You're P" + pos + ". Keep it tidy." : "Lost a place, P" + pos + " now. Stay calm, we'll get it back.");
            }
            lastPos = pos;
        }
        if (HasPitLane)
        {
            if (!tyreWarned && playerWear > 0.6f) { tyreWarned = true; Radio("Tyres are going off. Box this lap if you can, press B.", true); }
            int lap = Mathf.Clamp(crossings, 1, laps);
            if (!stopWarned && MandatoryStop && CompoundsUsedCount() < 2 && laps - lap <= 2 && laps >= 4)
            { stopWarned = true; Radio("We still need a pit stop for the second compound. Box, box!", true); }
            if (player is F1Car f1 && f1.OnGravel && Time.time - lastGravelMsg > 15f) { lastGravelMsg = Time.time; Radio("Off track. Rejoin safely and watch the limits."); }
            if (Time.time - incidentTime < 2f && Time.time - lastYellowMsg > 35f && InWindow(prevD, incidentD - 260f, incidentD - 15f, track.length))
            { lastYellowMsg = Time.time; Radio("Yellow flag ahead, careful. No overtaking."); }
        }
        if (!lastLapSaid && crossings == laps && laps > 1) { lastLapSaid = true; Radio("Last lap! Bring it home.", true); }
    }

    void DrawRadio(float s)
    {
        if (radioTimer <= 0f || radioText.Length == 0) return;
        float alpha = Mathf.Clamp01(radioTimer * 2f);
        float radioY = ChaseCamera.ActiveView == ChaseCamera.View.Cockpit ? 196f * s : Screen.height - 330f * s;
        Rect p = new Rect(Screen.width / 2f - 340 * s, radioY, 680 * s, 70 * s);
        Fill(p, new Color(0.03f, 0.04f, 0.07f, 0.88f * alpha), 12 * s);
        Fill(new Rect(p.x, p.y, 8 * s, p.height), new Color(playerRacer.color.r, playerRacer.color.g, playerRacer.color.b, alpha), 4 * s);
        // little animated voice meter
        for (int k = 0; k < 5; k++)
        {
            float h = (10f + 14f * Mathf.Abs(Mathf.Sin(Time.time * (9f + k * 2.3f) + k))) * s;
            Fill(new Rect(p.x + 22 * s + k * 7 * s, p.y + p.height / 2f - h / 2f, 4 * s, h), new Color(0.4f, 1f, 0.6f, alpha), 2 * s);
        }
        var head = radioHead;
        head.normal.textColor = new Color(0.4f, 1f, 0.6f, alpha);
        GUI.Label(new Rect(p.x + 70 * s, p.y + 6 * s, p.width, 20 * s), "TEAM RADIO  ·  RACE ENGINEER", head);
        var body = radioBody;
        body.normal.textColor = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(p.x + 70 * s, p.y + 28 * s, p.width - 80 * s, 34 * s), "\"" + radioText + "\"", body);
    }

    readonly List<(Racer r, Vector3 sp, float dist)> visibleTags = new List<(Racer, Vector3, float)>();
    readonly List<Rect> placedTagRects = new List<Rect>();

    // F1-style name plates above nearby visible cars: occluded by buildings/walls, distance faded, overlap culled
    void DrawNameTags(float s, List<Racer> standings)
    {
        if (useSession && !GameSession.Settings.nameTags) return;
        var cam = Camera.main;
        if (cam == null) return;
        visibleTags.Clear();
        placedTagRects.Clear();
        Vector3 camPos = cam.transform.position;
        foreach (var r in racers)
        {
            if (r.isPlayer || !r.ai.gameObject.activeSelf) continue;
            Vector3 world = r.tr.position + Vector3.up * 1.5f;
            float dist = Vector3.Distance(camPos, world);
            if (dist > 55f) continue;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) continue;
            if (Physics.Linecast(camPos, world, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && hit.collider.attachedRigidbody == null && !hit.collider.transform.IsChildOf(r.tr))
                    continue;
            visibleTags.Add((r, sp, dist));
        }
        visibleTags.Sort((a, b) => a.dist.CompareTo(b.dist));
        int drawn = 0;
        for (int idx = 0; idx < visibleTags.Count && drawn < 6; idx++)
        {
            var (r, sp, dist) = visibleTags[idx];
            float alpha = Mathf.InverseLerp(55f, 35f, dist);
            if (alpha <= 0.02f) continue;
            float k = Mathf.Lerp(1.05f, 0.72f, dist / 55f) * s;
            float w = 96 * k, h = 24 * k;
            Rect p = new Rect(sp.x - w / 2f, Screen.height - sp.y - h, w, h);
            bool overlaps = false;
            foreach (var prev in placedTagRects)
                if (p.Overlaps(prev)) { overlaps = true; break; }
            if (overlaps) continue;
            placedTagRects.Add(p);
            drawn++;

            Color savedCol = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Fill(p, new Color(0.03f, 0.04f, 0.07f, 0.8f), 4 * k);
            Fill(new Rect(p.x, p.y, 26 * k, h), new Color(1f, 1f, 1f, 0.95f), 4 * k);
            int fs = Mathf.RoundToInt(13 * k);
            tagPos.fontSize = fs;
            GUI.Label(new Rect(p.x, p.y, 26 * k, h), (standings.IndexOf(r) + 1).ToString(), tagPos);
            Fill(new Rect(p.x + 28 * k, p.y + 4 * k, 4 * k, h - 8 * k), r.color);
            tagCode.fontSize = fs;
            GUI.Label(new Rect(p.x + 36 * k, p.y, w - 36 * k, h), r.code, tagCode);
            GUI.color = savedCol;
        }
    }

    // ---------- Braking markers (green = full throttle, yellow = get ready, red = brake now) ----------

    [Header("Braking markers")]
    public Material lineGreen, lineYellow, lineRed;
    float[] targetSpeed;
    readonly List<Renderer> markers = new List<Renderer>();
    const float MarkerSpacing = 4f, MarkerStart = 8f;
    const int MarkerCount = 40;
    const float AssumedBraking = 24f;      // upgraded carbon-carbon braking markers plan for (m/s², plus downforce below)

    // The fastest safe speed at every track point: corner grip with downforce, then braking backward from each corner
    void BuildSpeedProfile()
    {
        int n = track.Count;
        targetSpeed = new float[n];
        for (int i = 0; i < n; i++)
        {
            float r = track.RadiusAt(i * track.spacing);
            float a0 = 15.5f, k = 0.0050f;
            float denom = 1f - k * r;
            targetSpeed[i] = denom <= 0.05f ? 95f : Mathf.Min(95f, Mathf.Sqrt(a0 * r / denom));
        }
        for (int pass = 0; pass < 2; pass++)
            for (int i = n - 1; i >= 0; i--)
            {
                float next = targetSpeed[(i + 1) % n];
                float decel = AssumedBraking + 0.0028f * next * next;
                targetSpeed[i] = Mathf.Min(targetSpeed[i], Mathf.Sqrt(next * next + 2f * decel * track.spacing));
            }
    }

    float TargetSpeedAt(float d)
    {
        int i = Mathf.FloorToInt(Mathf.Repeat(d, track.length) / track.spacing) % track.Count;
        return targetSpeed[i];
    }

    void BuildBrakingMarkers()
    {
        var parent = new GameObject("BrakingMarkers").transform;
        for (int k = 0; k < MarkerCount; k++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.name = "Marker";
            g.transform.SetParent(parent, false);
            g.transform.localScale = new Vector3(0.55f, 0.01f, 1.8f);
            var r = g.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial = lineGreen;
            markers.Add(r);
        }
    }

    void UpdateBrakingMarkers()
    {
        if (markers.Count == 0) return;
        bool show = state != State.Countdown && !playerRacer.finished && pitPhase == PitPhase.None;
        float v = Mathf.Max(player.Speed, 0f);
        for (int k = 0; k < markers.Count; k++)
        {
            var r = markers[k];
            r.enabled = show;
            if (!show) continue;
            float ahead = MarkerStart + k * MarkerSpacing;
            float d = prevD + ahead;
            r.transform.SetPositionAndRotation(track.PointAt(d) + Vector3.up * 0.045f, Quaternion.LookRotation(track.TangentAt(d)));
            // Distance needed to slow from the current speed to the safe speed at this marker
            float vt = TargetSpeedAt(d);
            float need = v > vt ? (v * v - vt * vt) / (2f * (AssumedBraking + 0.0028f * v * v)) : 0f;
            Material m = need >= ahead - 6f ? lineRed : need >= ahead - 50f ? lineYellow : lineGreen;
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }
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
        ApplyLivery(player.gameObject, playerTeam.primary, playerTeam.secondary, new Color(1f, 0.85f, 0.1f), playerTeam.livery);
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
        if (cam != null)
        {
            cam.view = (ChaseCamera.View)Mathf.Clamp(settings.defaultCamera, 0, 3);
            cam.baseFov = settings.fieldOfView;
            cam.shake = settings.cameraShake;
        }

        playerRacer = new Racer { name = setup.driverName, code = setup.driverCode, color = playerTeam.primary, team = setup.team, isPlayer = true, tr = player.transform };
        racers.Add(playerRacer);

        // Rivals: one car per remaining driver, as many as the opponent setting allows
        var rivals = field.FindAll(d => !d.isPlayer);
        int count = setup.timeTrial && !GameSession.CareerMode ? 0 : Mathf.Clamp(setup.opponents, 0, Mathf.Min(rivals.Count, ais.Length));
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
            ApplyLivery(ai.gameObject, team.primary, team.secondary, Color.HSVToRGB(Random.value, 0.6f, 0.95f), team.livery);
            var r = new Racer { name = d.name, code = d.code, color = team.primary, team = d.team, ai = ai, tr = ai.transform };
            racers.Add(r);
            scores[r] = pace * aiSkill + Random.Range(-0.008f, 0.008f);
        }

        // Tyres: the player's starting set, each rival's strategy
        playerCompound = (Compound)Mathf.Clamp(setup.startCompound, 0, 2);
        compoundsUsed[(int)playerCompound] = true;
        playerNext = DefaultNextCompound();
        Tyres.Paint(player.gameObject, playerCompound);
        foreach (var r in scores.Keys) AssignStrategy(r.ai);

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

    // One-stop strategy: start on Soft or Medium, switch to a different compound around mid-race.
    public void AssignStrategy(RaceAI ai)
    {
        if (!HasPitLane || laps < 3) { ai.pitLap = -1; return; }
        bool soft = Random.value < 0.4f;
        ai.compound = soft ? Compound.Soft : Compound.Medium;
        ai.nextCompound = soft ? (Random.value < 0.5f ? Compound.Medium : Compound.Hard) : (Random.value < 0.75f ? Compound.Hard : Compound.Soft);
        float frac = soft ? Random.Range(0.3f, 0.45f) : Random.Range(0.45f, 0.65f);
        ai.pitLap = Mathf.Clamp(Mathf.RoundToInt(laps * frac), 1, laps - 1);
        ai.tyreWear = 0f;
        Tyres.Paint(ai.gameObject, ai.compound);
    }

    Compound DefaultNextCompound()
    {
        switch (playerCompound)
        {
            case Compound.Soft: return Compound.Medium;
            case Compound.Medium: return Compound.Hard;
            default: return Compound.Medium;
        }
    }

    int CompoundsUsedCount() { int n = 0; foreach (bool b in compoundsUsed) if (b) n++; return n; }

    public void OnAiPitStop(RaceAI ai, bool arriving)
    {
        if (pitBoxes == null || ai.team < 0 || ai.team >= pitBoxes.Length || pitBoxes[ai.team] == null) return;
        if (arriving)
        {
            pitBoxes[ai.team].SetCompounds(ai.compound, ai.nextCompound);
            pitBoxes[ai.team].Begin(ai.StopTime);
        }
        else { pitBoxes[ai.team].SetWorking(false); pitBoxes[ai.team].ShowGreen(); }
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
    public static void ApplyLivery(GameObject car, Color primary, Color secondary, Color helmet, int livery = -1)
    {
        var cache = new Dictionary<Material, Material>();
        // The 2026 car model carries the whole livery in one texture set per team
        Material teamMat = livery >= 0 ? Resources.Load<Material>("F1Liveries/Livery_" + livery.ToString("00")) : null;
        foreach (var r in car.GetComponentsInChildren<Renderer>(true))
        {
            var src = r.sharedMaterial;
            if (src == null) continue;
            string n = src.name.Replace(" (Instance)", "");
            if (n.StartsWith("Livery_") || n.StartsWith("Mat_Team_"))
            {
                if (teamMat != null) r.sharedMaterial = teamMat;
                continue;
            }
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

        if (HasPitLane)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.bKey.wasPressedThisFrame && pitPhase == PitPhase.None && state == State.Racing)
                {
                    pitRequested = !pitRequested;
                    if (pitRequested && pitBoxes != null && playerRacer != null && playerRacer.team < pitBoxes.Length && pitBoxes[playerRacer.team] != null)
                        pitBoxes[playerRacer.team].Prepare();
                }
                bool choosing = pitPhase == PitPhase.None || pitPhase == PitPhase.Approach || pitPhase == PitPhase.BoxEntry || pitPhase == PitPhase.Stopping;
                if (choosing && kb.digit1Key.wasPressedThisFrame) playerNext = Compound.Soft;
                if (choosing && kb.digit2Key.wasPressedThisFrame) playerNext = Compound.Medium;
                if (choosing && kb.digit3Key.wasPressedThisFrame) playerNext = Compound.Hard;
            }
#endif
            // Incidents: only major impacts trigger yellow flags, with a cooldown to prevent spam
            if (player.LastImpactTime > lastIncidentSeen + 0.5f && player.LastImpactStrength > 15f)
            {
                lastIncidentSeen = player.LastImpactTime;
                if (Time.time - incidentTime > 12f)
                {
                    incidentD = prevD;
                    incidentTime = Time.time;
                }
            }
            bool active = Time.time - incidentTime < 5.5f;
            if (marshalPosts != null)
                foreach (var post in marshalPosts)
                    if (post != null) post.SetYellow(active && InWindow(post.trackDistance, incidentD - 260f, incidentD - 15f, track.length));
        }

        goBannerTimer -= Time.deltaTime;
        winnerBannerTimer -= Time.deltaTime;
        resultsTimer -= Time.deltaTime;
        trackLimitBannerTimer -= Time.deltaTime;
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
                lapStartTime = raceTime;
                sectorStartTime = raceTime;
                currentSector = 0;
                if (ambient != null) ambient.Cheer();
                string turn1 = useSession && !string.IsNullOrEmpty(GameSession.CurrentTrack.turn1Name) ? GameSession.CurrentTrack.turn1Name : "Turn 1";
                Radio("Lights out! Clean launch, careful into " + turn1 + ".", true);
            }
        }

        UpdateBrakingMarkers();
        UpdateRadio(Standings());

        // The nearest rival is voiced by a 3D engine sound that follows it
        if (rivalAudio != null)
        {
            Racer nearest = null;
            float best = 220f * 220f;
            foreach (var r in racers)
            {
                if (r.isPlayer || !r.ai.gameObject.activeSelf) continue;
                float dd = (r.tr.position - player.transform.position).sqrMagnitude;
                if (dd < best) { best = dd; nearest = r; }
            }
            rivalAudio.gameObject.SetActive(nearest != null);
            if (nearest != null)
            {
                rivalAudio.transform.position = nearest.tr.position;
                rivalAudio.externalSpeed = nearest.speed;
                rivalAudio.externalRpm = Mathf.Clamp(6500f + Mathf.Repeat(nearest.speed * 3.6f, 45f) * 120f, 6000f, 12200f);
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
            if (racing && !playerRacer.finished && crossings >= 1)
            {
                if (currentSector == 0 && prevD >= track.length / 3f && prevD < track.length * 0.6f)
                {
                    RecordPlayerSector(0, raceTime - sectorStartTime);
                    currentSector = 1;
                    sectorStartTime = raceTime;
                }
                else if (currentSector == 1 && prevD >= 2f * track.length / 3f && prevD < track.length * 0.9f)
                {
                    RecordPlayerSector(1, raceTime - sectorStartTime);
                    currentSector = 2;
                    sectorStartTime = raceTime;
                }
            }
        }
        playerRacer.progress = (crossings - 1) * track.length + prevD;
        playerRacer.lateral = lateral;
        playerRacer.speed = player.Speed;

        if (racing && pitPhase == PitPhase.None && !playerRacer.finished)
        {
            offTrackCooldown -= dt;
            bool inPitWin = HasPitLane && InWindow(prevD, pitEntryStart, pitExitEnd, track.length)
                         && (lateral * PitSide > track.HalfWidthAt(prevD) - 1.5f || Mathf.Abs(lateral - PitLat(prevD)) < 7.5f);
            float limit = track.HalfWidthAt(prevD) + 1.4f;
            if (!inPitWin && Mathf.Abs(lateral) > limit && player.Speed > 15f && offTrackCooldown <= 0f)
            {
                offTrackCooldown = 4.5f;
                trackLimitWarnings++;
                if (trackLimitWarnings % 3 == 0)
                {
                    playerRacer.penalty += 3f;
                    penaltyNote = "+" + Mathf.RoundToInt(playerRacer.penalty) + " s time penalty";
                    trackLimitMsg = "TRACK LIMITS: +3s PENALTY (" + trackLimitWarnings + " warnings)";
                    Radio("That's track limits again - 3 second time penalty added.", true);
                }
                else
                {
                    trackLimitMsg = "TRACK LIMITS WARNING " + (trackLimitWarnings % 3) + "/3";
                    Radio("Watch track limits, stay inside the white lines.");
                }
                trackLimitBannerTimer = 3.2f;
            }
        }

        if (HasPitLane && player is F1Car f1)
        {
            // Tyre wear: distance driven, more when cornering hard
            if (racing && pitPhase == PitPhase.None && !playerRacer.finished)
                playerWear += Mathf.Abs(player.Speed) * dt / Tyres.LifeMeters[(int)playerCompound] * (1f + 0.25f * Mathf.Abs(f1.LateralG));
            f1.tyreGripScale = Tyres.GripFactor(playerCompound, playerWear);

            bool physicallyInPitEntry = InWindow(prevD, pitEntryStart, pitExitStart, track.length)
                && (Mathf.Abs(lateral - PitLat(prevD)) < 5.2f || lateral * PitSide > Mathf.Max(6.5f, track.HalfWidthAt(prevD) + 0.65f));
            if (pitPhase == PitPhase.None && racing && !playerRacer.finished && player.Speed > 2f && physicallyInPitEntry)
                StartPlayerPit(lateral);
            UpdatePlayerPit(dt);
        }

        // DRS & Slipstream
        if (player is F1Car drsCar)
        {
            bool zone = drsZones != null && drsZones.Length > 0 && racing && pitPhase == PitPhase.None && !playerRacer.finished && InDrsZone(prevD);
            bool eligible = racers.Count <= 1 || crossings >= 2 && GapAheadSeconds() < 1f;
            drsCar.drsAvailable = zone && eligible;
            drsCar.drsAuto = useSession && GameSession.Settings.drsAssist;
            DrsZoneNow = zone;

            float tow = 0f;
            if (racing && pitPhase == PitPhase.None && player.Speed > 25f)
            {
                foreach (var r in racers)
                {
                    if (r.isPlayer || r.retired || (r.ai != null && (!r.ai.gameObject.activeSelf || r.ai.Pit != RaceAI.PitState.None))) continue;
                    float dp = r.progress - playerRacer.progress;
                    if (dp > 3f && dp < 28f && Mathf.Abs(r.lateral - playerRacer.lateral) < 2.6f)
                        tow = Mathf.Max(tow, Mathf.InverseLerp(28f, 6f, dp));
                }
            }
            drsCar.slipstreamFactor = tow;
        }

        foreach (var r in racers)
        {
            if (r.isPlayer || !r.ai.gameObject.activeSelf) continue;
            r.prevProgress = r.progress;
            r.ai.Step(dt, racing, r.finished, aiSkill);
            if ((r.ai.Pit == RaceAI.PitState.Entering || r.ai.Pit == RaceAI.PitState.InLane) && pitBoxes != null && r.team >= 0 && r.team < pitBoxes.Length && pitBoxes[r.team] != null)
                pitBoxes[r.team].Prepare();
            r.progress = r.ai.progress;
            r.lateral = r.ai.lane;
            r.speed = r.ai.Speed;
            if (r.ai.TyresChanged) { r.ai.TyresChanged = false; Tyres.Paint(r.ai.gameObject, r.ai.compound); }
            if (r.ai.TyresOff != r.tyresHidden) { r.tyresHidden = r.ai.TyresOff; Tyres.SetTyresVisible(r.ai.gameObject, !r.tyresHidden); }
            if (r.finished) continue;
            if (racing && r.progress > 0f)
            {
                float dMod = Mathf.Repeat(r.progress, track.length);
                if (r.currentSector == 0 && dMod >= track.length / 3f && dMod < track.length * 0.6f)
                {
                    RecordAiSector(r, 0, raceTime - r.sectorStartTime);
                    r.currentSector = 1;
                    r.sectorStartTime = raceTime;
                }
                else if (r.currentSector == 1 && dMod >= 2f * track.length / 3f && dMod < track.length * 0.9f)
                {
                    RecordAiSector(r, 1, raceTime - r.sectorStartTime);
                    r.currentSector = 2;
                    r.sectorStartTime = raceTime;
                }
            }
            bool crossedLine = Mathf.FloorToInt(r.progress / track.length) > Mathf.FloorToInt(r.prevProgress / track.length) && r.progress > 0f;
            if (crossedLine)
            {
                if (r.currentSector == 2) RecordAiSector(r, 2, raceTime - r.sectorStartTime);
                r.currentSector = 0;
                r.sectorStartTime = raceTime;
            }
            if (r.progress >= laps * track.length || (winner != null && crossedLine)) Finish(r);
        }
    }

    // ---------- Player pit stop ----------
    // From the pit entry line the stop is automated, as in F1 games: limiter on, the car drives to the
    // team's box, the crew changes the tyres, the release light goes green and the car rejoins at the exit.

    public bool DrsZoneNow { get; private set; }

    float GapAheadSeconds()
    {
        float best = float.MaxValue;
        foreach (var r in racers)
        {
            if (r.isPlayer || r.finished || r.ai == null || !r.ai.gameObject.activeSelf) continue;
            float gap = r.progress - playerRacer.progress;
            if (gap > 0f && gap < best) best = gap;
        }
        return best == float.MaxValue ? float.MaxValue : best / Mathf.Max(player.Speed, 10f);
    }

    float body0Y = -1e6f;

    // One pit box and stop crew per team, positioned directly in front of each team's garage bay
    public void BuildPitCrews()
    {
        var parent = new GameObject("PitCrews").transform;
        pitBoxes = new PitBox[boxDistances.Length];
        for (int t = 0; t < boxDistances.Length && t < GameSession.Teams.Length; t++)
        {
            float d = boxDistances[t];
            float lat = BoxLat(t);
            Vector3 pos = track.PointAt(d) + track.RightAt(d) * lat;
            if (GroundAt(pos, out float gy)) pos.y = gy;
            Vector3 tan = track.TangentAt(d); tan.y = 0f;
            var team = GameSession.Teams[t];
            pitBoxes[t] = PitBox.Create(parent, t, pos, Quaternion.LookRotation(tan.normalized), garageSide, team.primary, team.secondary, team.name, team.livery);
            pitBoxes[t].trackDistance = d;
        }
    }
    static readonly RaycastHit[] groundHits = new RaycastHit[8];

    // Height of the drivable surface under a point (ignores cars, walls and anything above head height).
    public static bool GroundAt(Vector3 p, out float y)
    {
        y = p.y;
        int n = Physics.RaycastNonAlloc(p + Vector3.up * 2.5f, Vector3.down, groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = groundHits[i];
            if (h.collider.attachedRigidbody != null || h.normal.y < 0.7f) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; found = true; }
        }
        return found;
    }

    public void StartTestPitStop(int teamIdx, float startS, float startLat, bool breakWingFirst)
    {
        if (pitBoxes == null || pitBoxes.Length == 0) BuildPitCrews();
        if (playerRacer == null) playerRacer = new Racer { name = "TEST", code = "TST", color = GameSession.Teams[teamIdx].primary, team = teamIdx, isPlayer = true, tr = player.transform };
        else playerRacer.team = teamIdx;
        if (breakWingFirst && player is F1Car f1b)
        {
            f1b.wingBreakDeltaV = 1f;
            // Toggle via public repair inverse or physics impulse; invoke BreakFrontWing via high impulse simulation in test
        }
        prevD = startS;
        body0Y = -1e6f;
        testAutoPit = true;
        StartPlayerPit(startLat);
        pitV = pitSpeed;
    }

    public void StepTestPitFixed(float dt) => UpdatePlayerPit(dt);

    void OnDisable() { PitServiceActive = false; }

    void StartPlayerPit(float lateral)
    {
        if (pitBoxes != null && playerRacer.team < pitBoxes.Length && pitBoxes[playerRacer.team] != null)
        {
            pitBoxes[playerRacer.team].SetCompounds(playerCompound, playerNext);
            pitBoxes[playerRacer.team].Prepare();
        }
        var rb = player.GetComponent<Rigidbody>();
        if (testAutoPit)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            player.inputEnabled = false;
            pitV = Mathf.Clamp(player.Speed, 6f, pitSpeed * 1.15f);
        }
        else
        {
            rb.isKinematic = false;
            player.inputEnabled = !playerRacer.finished;
            pitV = player.Speed;
            if (player is F1Car f1m)
            {
                f1m.pitLimiterSpeed = pitSpeed;
                f1m.pitLimiterActive = InWindow(prevD, pitEntryEnd, pitExitStart, track.length);
            }
        }
        pitPhase = PitPhase.Approach;
        pitS = prevD;
        pitLat = lateral;
        pitLatVel = 0f;
        pitJackBlend = 0f;
        Vector3 tan = track.TangentAt(pitS); tan.y = 0f;
        Quaternion trackRot = tan.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(tan.normalized, Vector3.up) : player.transform.rotation;
        pitHeading = Quaternion.Slerp(player.transform.rotation, trackRot, 0.35f);
        PitCameraSide = -garageSide;
        PitServiceActive = false;
        pitRequested = false;
        tyresSwapped = false;
        pitNeedWingRepair = (player is F1Car f1c) && f1c.FrontWingLost;
        pitWingRepaired = false;
    }

    static readonly Collider[] pitWallHits = new Collider[8];
    static readonly float[] pitNudgeSteps = { 0.25f, -0.25f, 0.50f, -0.50f, 0.85f, -0.85f, 1.25f, -1.25f, 1.75f, -1.75f };

    bool PitPosHitsBarrier(float sDist, float lat, float radius)
    {
        Vector3 probe = track.PointAt(sDist) + track.RightAt(sDist) * lat;
        float gyProbe = snapToGround && GroundAt(probe, out float yp) ? yp : probe.y;
        probe.y = gyProbe + 0.65f;
        int wc = Physics.OverlapSphereNonAlloc(probe, radius, pitWallHits, ~0, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < wc; k++)
            if (pitWallHits[k].name.StartsWith("Barrier")) return true;
        return false;
    }

    void UpdatePlayerPit(float dt)
    {
        if (pitPhase == PitPhase.None || dt <= 1e-5f)
        {
            if (player is F1Car f1z) f1z.pitLimiterActive = false;
            PitServiceActive = false;
            return;
        }
        float L = track.length;
        int pTeam = playerRacer.team;
        var box = pitBoxes != null && pTeam < pitBoxes.Length ? pitBoxes[pTeam] : null;
        float boxD = BoxDistance(pTeam);
        float boxLat = BoxLat(pTeam);
        float fastLat = PitLat(pitS);
        float toBox = Mathf.Repeat(boxD - pitS, L);
        if (toBox > L * 0.5f) toBox = 0f;

        if (!testAutoPit)
        {
            var pf1 = player as F1Car;
            float liveLat = playerRacer != null ? playerRacer.lateral : PitLat(prevD);
            bool inCorridor = InWindow(prevD, pitEntryStart, pitExitEnd, L)
                           && (Mathf.Abs(liveLat - PitLat(prevD)) < 7.8f || liveLat * PitSide > track.HalfWidthAt(prevD) - 1.2f);
            bool inLimiterZone = InWindow(prevD, pitEntryEnd, pitExitStart, L);

            if (pitPhase == PitPhase.Approach || pitPhase == PitPhase.BoxEntry)
            {
                pitS = prevD;
                pitLat = liveLat;
                pitV = player.Speed;
                if (pf1 != null) { pf1.pitLimiterActive = inLimiterZone; pf1.pitLimiterSpeed = pitSpeed; }
                PitCameraSide = -garageSide;
                PitServiceActive = false;

                if (!inCorridor)
                {
                    if (pf1 != null) pf1.pitLimiterActive = false;
                    pitPhase = PitPhase.None;
                    return;
                }

                float signedBoxDelta = Mathf.Repeat(boxD - prevD + L * 0.5f, L) - L * 0.5f;
                float latToBox = Mathf.Abs(liveLat - boxLat);
                if (Mathf.Abs(signedBoxDelta) <= 28f)
                {
                    if (box != null) box.Prepare();
                    pitPhase = PitPhase.BoxEntry;
                }
                bool inBoxZone = (Mathf.Abs(signedBoxDelta) < 4.5f && latToBox < 3.6f && Mathf.Abs(player.Speed) < 11f)
                              || (Mathf.Abs(signedBoxDelta) < 7.0f && latToBox < 4.8f && Mathf.Abs(player.Speed) < 2.2f);
                if (inBoxZone)
                {
                    var rbDock = player.GetComponent<Rigidbody>();
                    rbDock.linearVelocity = Vector3.zero;
                    rbDock.angularVelocity = Vector3.zero;
                    rbDock.isKinematic = true;
                    player.inputEnabled = false;
                    if (pf1 != null) pf1.pitLimiterActive = true;
                    pitHeading = player.transform.rotation;
                    pitV = Mathf.Clamp(Mathf.Abs(player.Speed), 0f, 6f);
                    pitPhase = PitPhase.Stopping;
                }
                else
                {
                    return;
                }
            }
            else if (pitPhase == PitPhase.Release || pitPhase == PitPhase.Exit)
            {
                pitS = prevD;
                pitLat = liveLat;
                pitV = player.Speed;
                PitCameraSide = -garageSide;
                PitServiceActive = false;
                if (pitPhase == PitPhase.Release && (!InWindow(prevD, pitEntryStart, pitExitStart, L) || liveLat * PitSide < track.HalfWidthAt(prevD) - 0.5f))
                    pitPhase = PitPhase.Exit;
                if (pf1 != null) { pf1.pitLimiterActive = pitPhase == PitPhase.Release && inLimiterZone; pf1.pitLimiterSpeed = pitSpeed; }
                if (pitPhase == PitPhase.Exit && (!InWindow(prevD, pitEntryStart, pitExitEnd, L) || Mathf.Abs(liveLat) < track.HalfWidthAt(prevD) - 0.5f))
                {
                    if (pf1 != null) pf1.pitLimiterActive = false;
                    pitPhase = PitPhase.None;
                }
                return;
            }
        }

        float targetLat = fastLat;
        float maxLatSpeed = pitPhase == PitPhase.Approach ? 5.2f : (pitProfile != null && pitProfile.Length > 1 ? 6.5f : 4.5f);

        switch (pitPhase)
        {
            case PitPhase.Approach:
                pitV = Mathf.MoveTowards(pitV, pitSpeed, (pitV > pitSpeed ? 22f : 7f) * dt);
                targetLat = fastLat;
                if (toBox <= 24f)
                {
                    if (box != null) box.Prepare();
                    pitPhase = PitPhase.BoxEntry;
                }
                break;

            case PitPhase.BoxEntry:
            {
                float entryT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 4f, toBox));
                targetLat = Mathf.Lerp(fastLat, boxLat, entryT);
                pitV = Mathf.MoveTowards(pitV, pitSpeed, (pitV > pitSpeed ? 22f : 6f) * dt);
                if (pitV * pitV / 16f >= toBox - 0.15f) pitPhase = PitPhase.Stopping;
                break;
            }

            case PitPhase.Stopping:
                targetLat = boxLat;
                if (!testAutoPit)
                {
                    float signedStopDelta = Mathf.Repeat(boxD - pitS + L * 0.5f, L) - L * 0.5f;
                    float stepS = Mathf.Clamp(signedStopDelta * 6f, -8f, 8f);
                    pitS = Mathf.Repeat(pitS + Mathf.MoveTowards(0f, signedStopDelta, Mathf.Max(3f, Mathf.Abs(stepS)) * dt), L);
                    pitLat = Mathf.MoveTowards(pitLat, boxLat, 5.5f * dt);
                    pitV = 0f;
                    if (Mathf.Abs(Mathf.Repeat(boxD - pitS + L * 0.5f, L) - L * 0.5f) < 0.08f && Mathf.Abs(pitLat - boxLat) < 0.08f)
                    {
                        pitS = boxD;
                        pitLat = boxLat;
                        pitPhase = PitPhase.Service;
                        pitTimer = 0f;
                        pitNeedWingRepair = (player is F1Car f1w) && f1w.FrontWingLost;
                        pitWingRepaired = false;
                        serviceTime = 2.3f + Random.value * 0.5f + (pitNeedWingRepair ? 2.4f : 0f);
                        if (box != null) { box.SetCompounds(playerCompound, playerNext); box.Begin(serviceTime, pitNeedWingRepair); }
                        if (playerAudio != null) playerAudio.PlayPitGun();
                    }
                    break;
                }
                pitV = Mathf.Min(pitV, Mathf.Sqrt(16f * Mathf.Max(0f, toBox)));
                if (toBox < 0.12f || pitV < 0.18f)
                {
                    pitV = 0f;
                    pitS = boxD;
                    pitLat = boxLat;
                    pitPhase = PitPhase.Service;
                    pitTimer = 0f;
                    pitNeedWingRepair = (player is F1Car f1w) && f1w.FrontWingLost;
                    pitWingRepaired = false;
                    serviceTime = 2.3f + Random.value * 0.5f + (pitNeedWingRepair ? 2.4f : 0f);
                    if (box != null) { box.SetCompounds(playerCompound, playerNext); box.Begin(serviceTime, pitNeedWingRepair); }
                    if (playerAudio != null) playerAudio.PlayPitGun();
                }
                break;

            case PitPhase.Service:
            {
                pitTimer += dt;
                pitV = 0f;
                targetLat = boxLat;
                bool off = pitTimer > 0.35f && pitTimer < serviceTime - 0.40f;
                if (off != playerRacer.tyresHidden) { playerRacer.tyresHidden = off; Tyres.SetTyresVisible(player.gameObject, !off); }
                if (pitTimer > 0.85f && !tyresSwapped)
                {
                    tyresSwapped = true;
                    playerCompound = playerNext;
                    playerWear = 0f;
                    compoundsUsed[(int)playerCompound] = true;
                    Tyres.Paint(player.gameObject, playerCompound);
                }
                if (pitNeedWingRepair && !pitWingRepaired && pitTimer >= serviceTime - 0.65f)
                {
                    pitWingRepaired = true;
                    if (player is F1Car cf1) cf1.RepairFrontWing();
                }
                if (pitTimer >= serviceTime)
                {
                    if (box != null) { box.SetWorking(false); box.ShowGreen(); }
                    playerPitStops++;
                    playerNext = DefaultNextCompound();
                    if (!testAutoPit)
                    {
                        pitJackBlend = 0f;
                        Vector3 boxPos = track.PointAt(boxD) + track.RightAt(boxD) * boxLat;
                        if (snapToGround && GroundAt(boxPos, out float bgy)) boxPos.y = bgy + 0.04f;
                        Vector3 boxTan = track.TangentAt(boxD); boxTan.y = 0f; boxTan.Normalize();
                        Quaternion boxRot = Quaternion.LookRotation(boxTan, Vector3.up);
                        var rbRel = player.GetComponent<Rigidbody>();
                        rbRel.isKinematic = false;
                        rbRel.position = boxPos;
                        rbRel.rotation = boxRot;
                        player.transform.SetPositionAndRotation(boxPos, boxRot);
                        rbRel.linearVelocity = Vector3.zero;
                        rbRel.angularVelocity = Vector3.zero;
                        player.inputEnabled = !playerRacer.finished;
                        if (player is F1Car rf1)
                        {
                            rf1.pitLimiterSpeed = pitSpeed;
                            rf1.pitLimiterActive = true;
                        }
                        pitPhase = PitPhase.Release;
                        PitServiceActive = false;
                        Radio("Green, green, green! " + Tyres.Names[(int)playerCompound] + " tyres on - drive out carefully.", true);
                        return;
                    }
                    pitPhase = PitPhase.BoxExit;
                }
                break;
            }

            case PitPhase.BoxExit:
            {
                float fromBox = Mathf.Repeat(pitS - boxD, L);
                float exitT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 20f, fromBox));
                targetLat = Mathf.Lerp(boxLat, fastLat, exitT);
                pitV = Mathf.MoveTowards(pitV, pitSpeed, 10f * dt);
                if (fromBox >= 20f) pitPhase = PitPhase.Release;
                break;
            }

            case PitPhase.Release:
            {
                targetLat = fastLat;
                float exitLineD = Mathf.Max(pitExitEnd - 12f, pitExitStart);
                bool pastLine = !InWindow(pitS, pitEntryStart, exitLineD, L) && InWindow(pitS, exitLineD, pitExitEnd + 80f, L);
                pitV = Mathf.MoveTowards(pitV, pastLine ? 31f : pitSpeed, (pastLine ? 12f : 10f) * dt);
                // Strictly remain on the pit fast lane until AFTER pitExitEnd before merging out
                if (!InWindow(pitS, pitEntryStart, pitExitEnd, L) && InWindow(pitS, pitExitEnd, pitExitEnd + 100f, L))
                {
                    pitExitStartLat = pitLat;
                    pitPhase = PitPhase.Exit;
                }
                break;
            }

            case PitPhase.Exit:
            {
                float afterExit = Mathf.Repeat(pitS - pitExitEnd, L);
                float mergeT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(afterExit / 26f));
                float hw = track.HalfWidthAt(pitS);
                float trackBlendLat = Mathf.Clamp(track.RacingLineOffsetAt(pitS), -(hw - 2.1f), hw - 2.1f);
                targetLat = Mathf.Lerp(pitExitStartLat, trackBlendLat, mergeT);
                pitV = Mathf.MoveTowards(pitV, 36f, 13f * dt);
                float handoffDist = Application.isPlaying ? 18f : 26f;
                if (afterExit >= 38f || (afterExit >= handoffDist && Mathf.Abs(pitLat - trackBlendLat) < 0.35f))
                {
                    Vector3 tanDir = track.TangentAt(pitS); tanDir.y = 0f; tanDir.Normalize();
                    Vector3 rawFwd = (tanDir * Mathf.Max(pitV, 1f) + track.RightAt(pitS) * pitLatVel); rawFwd.y = 0f; rawFwd.Normalize();
                    float dev = Vector3.Angle(tanDir, rawFwd);
                    Vector3 handoffFwd = dev > 6f ? Vector3.Slerp(tanDir, rawFwd, 6f / dev).normalized : rawFwd;
                    Quaternion handoffRot = Quaternion.LookRotation(handoffFwd, Vector3.up);
                    if (player is F1Car pf1) pf1.OnPitExitHandoff(handoffFwd * pitV, handoffRot);
                    else
                    {
                        var rb = player.GetComponent<Rigidbody>();
                        rb.isKinematic = false;
                        rb.linearVelocity = handoffFwd * pitV;
                        rb.angularVelocity = Vector3.zero;
                    }
                    player.inputEnabled = !playerRacer.finished;
                    testAutoPit = false;
                    pitPhase = PitPhase.None;
                    PitServiceActive = false;
                    Radio("Good stop! " + Tyres.Names[(int)playerCompound] + (pitNeedWingRepair ? " tyres + fresh wing on." : " tyres on.") + " Push now.", true);
                    return;
                }
                break;
            }
        }

        PitCameraSide = -garageSide;
        PitServiceActive = pitPhase == PitPhase.Stopping || pitPhase == PitPhase.Service || (pitPhase == PitPhase.BoxExit && Mathf.Repeat(pitS - boxD, L) < 6f);

        if (pitPhase != PitPhase.Service)
        {
            float desiredLatVel = Mathf.Clamp((targetLat - pitLat) / Mathf.Max(dt, 1e-4f), -maxLatSpeed, maxLatSpeed);
            pitLatVel = Mathf.Lerp(pitLatVel, desiredLatVel, 1f - Mathf.Exp(-14f * dt));
            float nextS = pitS + pitV * dt;
            float nextLat = pitLat + pitLatVel * dt;

            // Real-time non-convex-safe barrier shield in pit corridor: probe offsets without Collider.ClosestPoint
            if (PitPosHitsBarrier(nextS, nextLat, 1.0f))
            {
                float centerDir = -Mathf.Sign(Mathf.Abs(nextLat) > 0.1f ? nextLat : 1f);
                bool resolved = false;
                foreach (float rad in new[] { 1.0f, 0.90f })
                {
                    foreach (float dAbs in pitNudgeSteps)
                    {
                        float cand = nextLat + (dAbs > 0f ? Mathf.Abs(dAbs) * centerDir : -Mathf.Abs(dAbs) * centerDir);
                        if (!PitPosHitsBarrier(nextS, cand, rad))
                        {
                            nextLat = cand;
                            pitLatVel = 0f;
                            resolved = true;
                            break;
                        }
                    }
                    if (resolved) break;
                }
            }
            pitLat = nextLat;
        }
        else
        {
            pitLatVel = 0f;
            pitLat = boxLat;
        }
        pitS += pitV * dt;

        bool jackTarget = pitPhase == PitPhase.Service && pitTimer > 0.20f && pitTimer < serviceTime - 0.28f;
        pitJackBlend = Mathf.MoveTowards(pitJackBlend, jackTarget ? 1f : 0f, dt / 0.22f);
        Vector3 pos = track.PointAt(pitS) + track.RightAt(pitS) * pitLat;
        if (snapToGround && GroundAt(pos, out float gy))
            pos.y = Mathf.Lerp(body0Y < -1e5f ? gy : body0Y, gy, 0.45f);
        body0Y = pos.y;
        pos += Vector3.up * (0.04f + 0.16f * Mathf.SmoothStep(0f, 1f, pitJackBlend));

        Vector3 headingDir = track.TangentAt(pitS) * Mathf.Max(pitV, 3f) + track.RightAt(pitS) * pitLatVel;
        headingDir.y = 0f;
        if (headingDir.sqrMagnitude > 1e-4f)
        {
            Quaternion targetHeading = Quaternion.LookRotation(headingDir.normalized, Vector3.up);
            pitHeading = Quaternion.Slerp(pitHeading, targetHeading, 1f - Mathf.Exp(-14f * dt));
        }

        var body = player.GetComponent<Rigidbody>();
        if (Application.isPlaying) { body.MovePosition(pos); body.MoveRotation(pitHeading); }
        else { body.position = pos; body.rotation = pitHeading; player.transform.SetPositionAndRotation(pos, pitHeading); }

        if (player is F1Car f1)
        {
            float visualSteer = Mathf.Clamp(Mathf.Atan2(pitLatVel, Mathf.Max(pitV, 4f)) * Mathf.Rad2Deg * 1.6f, -18f, 18f);
            f1.SetPitDriving(pitV, visualSteer);
        }
    }

    void RecordPlayerSector(int idx, float dur)
    {
        if (dur <= 0.1f) return;
        lastSectors[idx] = dur;
        bool overall = overallBestSectors[idx] < 0f || dur < overallBestSectors[idx];
        bool pb = bestSectors[idx] < 0f || dur < bestSectors[idx];
        if (overall) overallBestSectors[idx] = dur;
        if (pb) bestSectors[idx] = dur;
        sectorStatus[idx] = overall ? 3 : pb ? 2 : 1;
    }

    void RecordAiSector(Racer r, int idx, float dur)
    {
        if (dur <= 0.1f) return;
        if (overallBestSectors[idx] < 0f || dur < overallBestSectors[idx]) overallBestSectors[idx] = dur;
    }

    void OnPlayerCrossedLine()
    {
        if (crossings < 2 || crossings - 1 <= lapsTimed || playerRacer.finished) return;
        if (currentSector == 2) RecordPlayerSector(2, raceTime - sectorStartTime);
        currentSector = 0;
        sectorStartTime = raceTime;
        lapsTimed = crossings - 1;
        lastLap = raceTime - lapStartTime;
        bool best = bestLap < 0 || lastLap < bestLap;
        if (best) bestLap = lastLap;
        if (useSession && GameSession.SubmitLap(GameSession.CurrentTrack.id, lastLap))
            Radio("New lap record at " + GameSession.CurrentTrack.name + ": " + FormatTime(lastLap) + "!", true);
        lapStartTime = raceTime;
        if (lapsTimed >= laps || winner != null) Finish(playerRacer);
        else
        {
            var st = Standings();
            Radio("Lap " + lapsTimed + ": " + FormatTime(lastLap) + (best ? ", our best so far. " : ". ") + "P" + (st.IndexOf(playerRacer) + 1) + ", " + GapAheadText(st) + ".");
        }
    }

    void Finish(Racer r)
    {
        r.finished = true;
        r.finishTime = raceTime;
        finishOrder.Add(r);
        if (winner == null) { winner = r; winnerBannerTimer = 5f; if (ambient != null) ambient.Cheer(); }
        if (r.isPlayer)
        {
            resultsTimer = 12f;
            if (pitPhase == PitPhase.None) player.inputEnabled = false;
            // Dry-race rule: two different compounds must be used
            if (MandatoryStop && CompoundsUsedCount() < 2)
            {
                r.penalty += 30f;
                penaltyNote = (penaltyNote.Length > 0 ? penaltyNote + " · " : "") + "+30 s penalty: no second tyre compound";
            }
            int pos = Standings().IndexOf(r) + 1;
            Radio("P" + pos + "! " + (pos == 1 ? "We won it! What a drive!" : pos <= 3 ? "That's a podium, great job." : pos <= 10 ? "Points finish, well done." : "Tough one. We'll come back stronger."), true);
        }
        else
        {
            r.ai.parkProgress = pitEntryDistance > 0f
                ? Mathf.Floor(r.progress / track.length) * track.length + pitEntryDistance
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
        list.Sort((a, b) => (a.finishTime + a.penalty).CompareTo(b.finishTime + b.penalty));
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
        if (r == leader) return r.finished ? FormatTime(r.finishTime + r.penalty) : "LEADER";
        if (r.finished && leader.finished)
        {
            int lapsDown = Mathf.RoundToInt((leader.progress - r.progress) / track.length);
            return lapsDown >= 1 ? "+" + lapsDown + (lapsDown == 1 ? " LAP" : " LAPS")
                                 : "+" + (r.finishTime + r.penalty - leader.finishTime - leader.penalty).ToString("0.000");
        }
        float diff = leader.progress - r.progress;
        if (diff >= track.length) { int n = Mathf.FloorToInt(diff / track.length); return "+" + n + (n == 1 ? " LAP" : " LAPS"); }
        float refSpeed = Mathf.Clamp(track.length / 76f, 44f, 68f);
        return "+" + (diff / refSpeed).ToString("0.000");
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

    void BuildGuiStyles(float s)
    {
        styleScale = s;
        big = Style(Mathf.RoundToInt(34 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
        mid = Style(Mathf.RoundToInt(18 * s), FontStyle.Bold, TextAnchor.MiddleLeft);
        small = Style(Mathf.RoundToInt(15 * s), FontStyle.Normal, TextAnchor.MiddleCenter);
        small.normal.textColor = new Color(1, 1, 1, 0.7f);
        center = Style(Mathf.RoundToInt(64 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
        button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold };

        midCenter = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter };
        bannerStyle = new GUIStyle(center);
        towerHead = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        towerCode = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(14 * s) };
        towerGap = new GUIStyle(towerCode) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        towerPos = new GUIStyle(towerCode) { alignment = TextAnchor.MiddleCenter };
        miniBadge = new GUIStyle(towerPos) { fontSize = Mathf.RoundToInt(9 * s) };
        miniBadge.normal.textColor = Color.black;
        miniPit = new GUIStyle(towerPos) { fontSize = Mathf.RoundToInt(10 * s) };
        miniPit.normal.textColor = Color.black;
        mapHeader = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        mapNumBlack = new GUIStyle(small) { fontSize = Mathf.RoundToInt(9 * s), fontStyle = FontStyle.Bold };
        mapNumBlack.normal.textColor = Color.black;
        mapNumWhite = new GUIStyle(mapNumBlack);
        mapNumWhite.normal.textColor = Color.white;
        drsStyle = new GUIStyle(big) { fontSize = Mathf.RoundToInt(24 * s), alignment = TextAnchor.MiddleCenter };
        tyreLetter = new GUIStyle(big) { fontSize = Mathf.RoundToInt(24 * s) };
        smallLeft = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft };
        statusBold = new GUIStyle(smallLeft) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(13 * s) };
        pitChoiceStyle = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15 * s) };
        resultsName = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(15 * s) };
        resultsRight = new GUIStyle(resultsName) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        radioHead = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        radioBody = new GUIStyle(mid) { fontStyle = FontStyle.Normal, fontSize = Mathf.RoundToInt(17 * s) };
        tagPos = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.MiddleCenter };
        tagPos.normal.textColor = Color.black;
        tagCode = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.MiddleLeft };
        sectorStyle = new GUIStyle(small) { fontSize = Mathf.RoundToInt(12 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    void OnGUI()
    {
        if (playerRacer == null) return;
        float s = Mathf.Min(Screen.height / 1080f, Screen.width / 1920f);
        if (!Mathf.Approximately(s, styleScale)) BuildGuiStyles(s);
        var standings = Standings();

        DrawTopPanel(s, standings);
        if (!useSession || GameSession.Settings.showMinimap) DrawMinimap(s, standings);
        DrawTower(s, standings);
        DrawDrs(s);
        if (useSession && GameSession.Settings.showFps)
        {
            fpsSmooth = Mathf.Lerp(fpsSmooth, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f), 0.05f);
            GUI.Label(new Rect(Screen.width - 120 * s, Screen.height - 52 * s, 110 * s, 26 * s), Mathf.RoundToInt(fpsSmooth) + " FPS", small);
        }

        if (state == State.Countdown) DrawStartLights(s);
        if (goBannerTimer > 0) Banner(s, "GO!", new Color(0.3f, 1f, 0.4f));
        if (trackLimitBannerTimer > 0f && trackLimitMsg.Length > 0)
        {
            Rect tr = new Rect(Screen.width / 2f - 210 * s, 148 * s, 420 * s, 30 * s);
            Fill(tr, trackLimitWarnings % 3 == 0 ? new Color(0.85f, 0.15f, 0.12f, 0.92f) : new Color(0.95f, 0.65f, 0.1f, 0.92f), 8 * s);
            midCenter.normal.textColor = trackLimitWarnings % 3 == 0 ? Color.white : Color.black;
            GUI.Label(tr, trackLimitMsg, midCenter);
        }

        Vector3 tangent = track.TangentAt(prevD);
        bool onTrack = Mathf.Abs(playerRacer.lateral) < track.HalfWidthAt(prevD) + 2f;
        if (state == State.Racing && !playerRacer.finished && onTrack && player.Speed > 3f && Vector3.Dot(player.transform.forward, tangent) < -0.3f)
            Banner(s, "WRONG WAY", new Color(1f, 0.3f, 0.25f));
        if (winner != null && !playerRacer.finished && winnerBannerTimer <= 0 && state == State.Racing)
        {
            midCenter.normal.textColor = Color.white;
            Fill(new Rect(Screen.width / 2f - 200 * s, 148 * s, 400 * s, 34 * s), new Color(0, 0, 0, 0.7f), 8 * s);
            GUI.Label(new Rect(Screen.width / 2f - 200 * s, 148 * s, 400 * s, 34 * s), "CHEQUERED FLAG - finish this lap", midCenter);
        }

        DrawNameTags(s, standings);
        DrawRadio(s);
        if (HasPitLane && player is F1Car) DrawTyresAndPit(s);

        if (playerRacer.finished && resultsTimer > 0) DrawResults(s, standings);
        else if (playerRacer.finished) DrawFinishedStrip(s, standings);
        else if (winner != null && winnerBannerTimer > 0) Banner(s, winner.name.ToUpperInvariant() + " WINS!", new Color(1f, 0.85f, 0.2f));

        if (paused) DrawPause(s);
    }

    void Banner(float s, string text, Color c)
    {
        bannerStyle.normal.textColor = c;
        Rect r = new Rect(0, Screen.height * 0.3f, Screen.width, 90 * s);
        Fill(new Rect(Screen.width / 2f - 380 * s, r.y, 760 * s, r.height), new Color(0, 0, 0, 0.55f), 16 * s);
        GUI.Label(r, text, bannerStyle);
    }

    void DrawTopPanel(float s, List<Racer> standings)
    {
        Rect p = new Rect(Screen.width / 2f - 280 * s, 20 * s, 560 * s, 124 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.78f), 14 * s);
        int lap = Mathf.Clamp(crossings, 1, laps);
        int pos = standings.IndexOf(playerRacer) + 1;
        PlayerLap = lap;
        PlayerPosition = Mathf.Max(1, pos);
        float lapTime = state == State.Racing && !playerRacer.finished ? raceTime - lapStartTime : (playerRacer.finished ? lastLap : 0f);

        float col = p.width / 3f;
        GUI.Label(new Rect(p.x, p.y + 8 * s, col, 20 * s), "LAP", small);
        GUI.Label(new Rect(p.x, p.y + 28 * s, col, 44 * s), lap + "/" + laps, big);
        GUI.Label(new Rect(p.x + col, p.y + 8 * s, col, 20 * s), "POSITION", small);
        GUI.Label(new Rect(p.x + col, p.y + 28 * s, col, 44 * s), "P" + pos + "/" + racers.Count, big);
        GUI.Label(new Rect(p.x + 2 * col, p.y + 8 * s, col, 20 * s), "RACE TIME", small);
        GUI.Label(new Rect(p.x + 2 * col, p.y + 28 * s, col, 44 * s), FormatTime(playerRacer.finished ? playerRacer.finishTime : raceTime), big);
        GUI.Label(new Rect(p.x, p.y + 74 * s, p.width, 20 * s),
            "LAP " + FormatTime(lapTime) + "     LAST " + FormatTime(lastLap) + "     BEST " + FormatTime(bestLap), small);

        float sw = 166 * s, sh = 20 * s, sx0 = p.x + 18 * s, sy = p.y + 98 * s;
        for (int k = 0; k < 3; k++)
        {
            Rect sr = new Rect(sx0 + k * (sw + 13 * s), sy, sw, sh);
            int stCode = sectorStatus[k];
            Color bg = stCode == 3 ? new Color(0.66f, 0.22f, 0.94f, 0.92f)
                     : stCode == 2 ? new Color(0.16f, 0.78f, 0.34f, 0.92f)
                     : stCode == 1 ? new Color(0.92f, 0.72f, 0.15f, 0.9f)
                     : new Color(1f, 1f, 1f, 0.08f);
            Fill(sr, bg, 6 * s);
            sectorStyle.normal.textColor = (stCode == 1 || stCode == 2) ? Color.black : Color.white;
            string txt = "S" + (k + 1) + "  " + (lastSectors[k] > 0f ? lastSectors[k].ToString("0.000") : "--.---");
            GUI.Label(sr, txt, sectorStyle);
        }
    }

    void DrawStartLights(float s)
    {
        int lit = Mathf.Min(5, Mathf.FloorToInt(countdown));
        float size = 46 * s, gap = 14 * s;
        float total = 5 * size + 4 * gap;
        Rect panel = new Rect(Screen.width / 2f - total / 2f - 16 * s, 154 * s, total + 32 * s, size + 32 * s);
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
        Rect p = new Rect(24 * s, 232 * s, w, 34 * s + rowH * standings.Count);
        Fill(p, new Color(0.04f, 0.05f, 0.08f, 0.8f), 10 * s);
        GUI.Label(new Rect(p.x + 12 * s, p.y + 4 * s, w, 24 * s), "LAP " + Mathf.Clamp(winner != null ? laps : LapOf(standings[0]), 1, laps) + " / " + laps, towerHead);
        Racer leader = standings[0];
        for (int i = 0; i < standings.Count; i++)
        {
            var r = standings[i];
            float y = p.y + 30 * s + i * rowH;
            if (r.isPlayer) Fill(new Rect(p.x + 4 * s, y, w - 8 * s, rowH - 1 * s), new Color(0.15f, 0.4f, 1f, 0.5f), 4 * s);
            GUI.Label(new Rect(p.x + 4 * s, y, 26 * s, rowH), (i + 1).ToString(), towerPos);
            Fill(new Rect(p.x + 32 * s, y + 4 * s, 4 * s, rowH - 8 * s), r.color);
            GUI.Label(new Rect(p.x + 42 * s, y, 60 * s, rowH), r.code, towerCode);
            if (HasPitLane)
            {
                Compound c = r.isPlayer ? playerCompound : r.ai.compound;
                bool inPit = r.isPlayer ? pitPhase != PitPhase.None : r.ai.Pit != RaceAI.PitState.None;
                Fill(new Rect(p.x + 82 * s, y + 4 * s, rowH - 8 * s, rowH - 8 * s), Tyres.Colors[(int)c], rowH);
                GUI.Label(new Rect(p.x + 82 * s, y + 4 * s, rowH - 8 * s, rowH - 8 * s), Tyres.Letters[(int)c], miniBadge);
                if (inPit)
                {
                    Fill(new Rect(p.x + 102 * s, y + 3 * s, 30 * s, rowH - 6 * s), new Color(0.9f, 0.9f, 0.9f), 3 * s);
                    GUI.Label(new Rect(p.x + 102 * s, y + 3 * s, 30 * s, rowH - 6 * s), "PIT", miniPit);
                }
            }
            GUI.Label(new Rect(p.x + 90 * s, y, w - 100 * s, rowH), r.finished && r != leader && !leader.finished ? "FIN" : GapText(r, leader), towerGap);
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
        Rect panel = new Rect(Screen.width - w - 24 * s, 20 * s, w, h);
        Fill(panel, new Color(0.04f, 0.05f, 0.08f, 0.82f), 16 * s);
        Fill(panel, new Color(1f, 1f, 1f, 0.12f), 16 * s, 1.5f * s);

        GUI.Label(new Rect(panel.x + 16 * s, panel.y + 8 * s, w - 110 * s, 20 * s), track.trackName.ToUpperInvariant(), mapHeader);
        for (int k = 0; k < 3; k++)
        {
            Rect chip = new Rect(panel.xMax - (3 - k) * 30 * s - 10 * s, panel.y + 12 * s, 24 * s, 12 * s);
            Fill(chip, SectorColors[k], 4 * s);
            GUI.Label(chip, "S" + (k + 1), mapNumBlack);
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

        foreach (var r in racers)
        {
            if (r.isPlayer || !r.ai.gameObject.activeSelf) continue;
            Vector2 m = MapPoint(r.tr.position, area);
            float d = 14 * s;
            Fill(new Rect(m.x - d / 2 - 1.5f * s, m.y - d / 2 - 1.5f * s, d + 3 * s, d + 3 * s), new Color(0, 0, 0, 0.8f), d);
            Fill(new Rect(m.x - d / 2, m.y - d / 2, d, d), r.color, d / 2);
            GUI.Label(new Rect(m.x - d / 2, m.y - d / 2, d, d), (standings.IndexOf(r) + 1).ToString(), r.color.grayscale > 0.5f ? mapNumBlack : mapNumWhite);
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
        GUI.Label(new Rect(pm.x - pd / 2, pm.y - pd / 2, pd, pd), (standings.IndexOf(playerRacer) + 1).ToString(), mapNumWhite);

        float lapFrac = crossings == 0 ? 0f : Mathf.Clamp01(prevD / track.length);
        Rect bar = new Rect(panel.x + 16 * s, panel.yMax - 22 * s, w - 32 * s, 8 * s);
        Fill(bar, new Color(1f, 1f, 1f, 0.12f), 4 * s);
        Fill(new Rect(bar.x, bar.y, bar.width * lapFrac, bar.height), SectorColors[Mathf.Min(2, (int)(lapFrac * 3f))], 4 * s);
        for (int k = 1; k < 3; k++)
            Fill(new Rect(bar.x + bar.width * k / 3f - 1 * s, bar.y - 2 * s, 2 * s, bar.height + 4 * s), new Color(1f, 1f, 1f, 0.5f));
    }

    float fpsSmooth = 60f;

    // DRS light: grey = closed / not allowed, green outline = zone (press F), solid green = open
    void DrawDrs(float s)
    {
        if (drsZones == null || drsZones.Length == 0 || !(player is F1Car c)) return;
        Rect r = new Rect(590 * s, Screen.height - 76 * s, 92 * s, 52 * s);
        Color green = new Color(0.15f, 0.85f, 0.35f);
        Fill(r, c.DrsOpen ? green : new Color(0.05f, 0.06f, 0.09f, 0.78f), 10 * s);
        if (!c.DrsOpen && c.drsAvailable) { Fill(new Rect(r.x, r.yMax - 6 * s, r.width, 6 * s), green, 3 * s); }
        drsStyle.normal.textColor = c.DrsOpen ? Color.black : c.drsAvailable ? green : new Color(1f, 1f, 1f, 0.35f);
        GUI.Label(r, "DRS", drsStyle);
    }

    // Tyre panel (compound, wear, stop requirement) and pit lane / flag messages
    void DrawTyresAndPit(float s)
    {
        Rect p = new Rect(330 * s, Screen.height - 134 * s, 250 * s, 110 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.78f), 12 * s);
        float d = 52 * s;
        Rect circle = new Rect(p.x + 14 * s, p.y + 14 * s, d, d);
        Fill(circle, Tyres.Colors[(int)playerCompound], d);
        Fill(new Rect(circle.x + 6 * s, circle.y + 6 * s, d - 12 * s, d - 12 * s), new Color(0.08f, 0.08f, 0.09f), d);
        tyreLetter.normal.textColor = Tyres.Colors[(int)playerCompound];
        GUI.Label(circle, Tyres.Letters[(int)playerCompound], tyreLetter);

        GUI.Label(new Rect(p.x + 80 * s, p.y + 10 * s, 160 * s, 20 * s), Tyres.Names[(int)playerCompound].ToUpperInvariant() + " TYRES", smallLeft);
        float life = Mathf.Clamp01(1f - playerWear);
        Rect bar = new Rect(p.x + 80 * s, p.y + 36 * s, 150 * s, 12 * s);
        Fill(bar, new Color(1, 1, 1, 0.12f), 6 * s);
        Fill(new Rect(bar.x, bar.y, bar.width * life, bar.height), Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.9f, 0.4f), life), 6 * s);
        GUI.Label(new Rect(p.x + 80 * s, p.y + 50 * s, 160 * s, 20 * s), Mathf.RoundToInt(life * 100f) + "% life  ·  stops " + playerPitStops, smallLeft);

        string status = "";
        Color sc = Color.white;
        if (pitPhase == PitPhase.None)
        {
            if (pitRequested) { status = "PIT REQUESTED - box this lap (B cancels)"; sc = new Color(0.4f, 0.8f, 1f); }
            else if (MandatoryStop && CompoundsUsedCount() < 2 && !playerRacer.finished) { status = "STOP REQUIRED · B = box this lap"; sc = new Color(1f, 0.8f, 0.2f); }
            else if (playerWear > 0.7f) { status = "TYRES GONE - BOX, BOX (B)"; sc = new Color(1f, 0.35f, 0.25f); }
        }
        if (status.Length > 0)
        {
            statusBold.normal.textColor = sc;
            GUI.Label(new Rect(p.x + 14 * s, p.y + 76 * s, p.width - 20 * s, 24 * s), status, statusBold);
        }

        if (pitPhase != PitPhase.None)
        {
            bool showSelector = pitPhase == PitPhase.Approach || pitPhase == PitPhase.BoxEntry || pitPhase == PitPhase.Stopping || (pitPhase == PitPhase.Service && !tyresSwapped);
            if (showSelector)
            {
                Rect b = new Rect(Screen.width / 2f - 300 * s, Screen.height - 190 * s, 600 * s, 110 * s);
                Fill(b, new Color(0.03f, 0.04f, 0.07f, 0.88f), 14 * s);
                float boxD = BoxDistance(playerRacer.team);
                float toBoxDist = Mathf.Repeat(boxD - prevD + track.length * 0.5f, track.length) - track.length * 0.5f;
                string head = pitPhase == PitPhase.Service
                    ? "PIT STOP  " + pitTimer.ToString("0.0") + " s"
                    : "PIT LANE  ·  DRIVE TO PIT BOX (" + Mathf.RoundToInt(Mathf.Max(0f, toBoxDist)) + " m)  ·  LIMITER 80 km/h";
                midCenter.normal.textColor = pitPhase == PitPhase.Service ? new Color(1f, 0.35f, 0.3f) : new Color(0.4f, 1f, 0.5f);
                GUI.Label(new Rect(b.x, b.y + 8 * s, b.width, 30 * s), head, midCenter);
                GUI.Label(new Rect(b.x, b.y + 38 * s, b.width, 22 * s), "Choose new tyres:", small);
                for (int k = 0; k < 3; k++)
                {
                    Compound c = (Compound)k;
                    bool sel = c == playerNext;
                    Rect r = new Rect(b.x + 60 * s + k * 165 * s, b.y + 62 * s, 150 * s, 36 * s);
                    Fill(r, sel ? Tyres.Colors[k] : new Color(1, 1, 1, 0.08f), 8 * s);
                    pitChoiceStyle.normal.textColor = sel ? Color.black : Color.white;
                    GUI.Label(r, (k + 1) + "  " + Tyres.Names[k].ToUpperInvariant(), pitChoiceStyle);
                }
            }
            else
            {
                Rect b = new Rect(Screen.width / 2f - 240 * s, Screen.height - 120 * s, 480 * s, 44 * s);
                Fill(b, new Color(0.03f, 0.04f, 0.07f, 0.88f), 12 * s);
                Fill(new Rect(b.x + 12 * s, b.y + 10 * s, 10 * s, 24 * s), Tyres.Colors[(int)playerCompound], 4 * s);
                string msg = pitPhase == PitPhase.Service
                    ? "PIT STOP " + pitTimer.ToString("0.0") + " s  ·  FITTED: " + Tyres.Names[(int)playerCompound].ToUpperInvariant()
                    : "RELEASED - PIT EXIT  ·  " + Tyres.Names[(int)playerCompound].ToUpperInvariant();
                midCenter.normal.textColor = pitPhase == PitPhase.Service ? new Color(1f, 0.45f, 0.3f) : new Color(0.4f, 1f, 0.5f);
                GUI.Label(new Rect(b.x + 18 * s, b.y, b.width - 18 * s, b.height), msg, midCenter);
            }
        }

        // Yellow flag in the section genuinely ahead of the player
        if (Time.time - incidentTime < 5.5f && InWindow(prevD, incidentD - 260f, incidentD - 15f, track.length))
        {
            Rect y = new Rect(Screen.width / 2f - 160 * s, 184 * s, 320 * s, 34 * s);
            Fill(y, new Color(1f, 0.85f, 0.1f, 0.95f), 6 * s);
            midCenter.normal.textColor = Color.black;
            GUI.Label(y, "YELLOW FLAG - incident ahead", midCenter);
        }
    }

    void DrawFinishedStrip(float s, List<Racer> standings)
    {
        Rect p = new Rect(Screen.width / 2f - 330 * s, 148 * s, 660 * s, 40 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.8f), 10 * s);
        midCenter.normal.textColor = new Color(1f, 0.85f, 0.2f);
        GUI.Label(p, "WINNER: " + winner.name + "   ·   You finished P" + (finishOrder.IndexOf(playerRacer) + 1) + "   ·   TAB: results", midCenter);
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

        var first = standings[0];   // after penalties
        bool youWon = first == playerRacer;
        bannerStyle.normal.textColor = youWon ? new Color(1f, 0.85f, 0.2f) : Color.white;
        int savedFs = bannerStyle.fontSize;
        bannerStyle.fontSize = big.fontSize;
        GUI.Label(new Rect(p.x, p.y + 10 * s, p.width, 44 * s), youWon ? "YOU WIN!" : "WINNER: " + first.name.ToUpperInvariant(), bannerStyle);
        bannerStyle.fontSize = savedFs;
        string sub = track.trackName.ToUpperInvariant() + "  ·  " + laps + " LAPS  ·  BEST LAP " + FormatTime(bestLap);
        if (penaltyNote.Length > 0) sub += "  ·  " + penaltyNote;
        GUI.Label(new Rect(p.x, p.y + 52 * s, p.width, 22 * s), sub, small);

        Racer leader = standings[0];
        for (int i = 0; i < rows; i++)
        {
            var r = standings[i];
            float y = p.y + 80 * s + i * rowH;
            if (r.isPlayer) Fill(new Rect(p.x + 16 * s, y, p.width - 32 * s, rowH - 2 * s), new Color(0.15f, 0.4f, 1f, 0.45f), 6 * s);
            Fill(new Rect(p.x + 64 * s, y + 5 * s, 4 * s, rowH - 10 * s), r.color);
            GUI.Label(new Rect(p.x + 24 * s, y, 40 * s, rowH), (i + 1).ToString(), resultsName);
            string team = GameSession.Teams.Length > r.team && useSession ? GameSession.Teams[r.team].name : "";
            GUI.Label(new Rect(p.x + 76 * s, y, 330 * s, rowH), r.name + (team.Length > 0 ? "  —  " + team : ""), resultsName);
            int pts = useSession && i < GameSession.Points.Length ? GameSession.Points[i] : 0;
            GUI.Label(new Rect(p.x + 24 * s, y, p.width - 120 * s, rowH), r.finished ? GapText(r, leader) : "running", resultsRight);
            GUI.Label(new Rect(p.x + 24 * s, y, p.width - 48 * s, rowH), pts > 0 ? "+" + pts : "", resultsRight);
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
