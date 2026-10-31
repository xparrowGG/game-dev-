using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Runs the race: start-light countdown, lap counting with checkpoints, timing, positions,
// winner banner, results, race HUD and minimap. The player may drive freely off the circuit.
public class RaceManager : MonoBehaviour
{
    public RaceTrack track;
    public CarController player;
    public RaceAI[] ais;
    public int laps = 3;
    [Range(0.7f, 1.3f)] public float aiSkill = 1f;
    public Renderer[] startLights;
    public Material lightOn, lightOff;

    public class Racer
    {
        public string name;
        public Color color;
        public bool isPlayer;
        public RaceAI ai;
        public Transform tr;
        public float progress, lateral, speed, finishTime;
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
    readonly List<Racer> finishOrder = new List<Racer>();

    Texture2D tex;
    float mapScale, mapMinX, mapMaxZ, styleScale = -1f;
    GUIStyle big, mid, small, center;

    void Start()
    {
        tex = Texture2D.whiteTexture;
        goDelay = 5f + Random.Range(0.4f, 1.4f);
        playerRacer = new Racer { name = "YOU", color = Color.white, isPlayer = true, tr = player.transform };
        racers.Add(playerRacer);
        foreach (var ai in ais)
        {
            ai.manager = this;
            racers.Add(new Racer { name = ai.driverName, color = ai.color, ai = ai, tr = ai.transform, progress = ai.progress, lateral = ai.lane });
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

    void Update()
    {
        bool restart = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        restart = k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
#else
        restart = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        if (restart) { SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); return; }

        goBannerTimer -= Time.deltaTime;
        winnerBannerTimer -= Time.deltaTime;
        resultsTimer -= Time.deltaTime;
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

        // Player progress. The car can roam freely, so only track it while near the circuit,
        // and a lap only counts after passing the three checkpoints (no cutting across the infield).
        float d = track.ClosestDistance(player.transform.position);
        float lateral = track.LateralOffset(player.transform.position, d);
        if (Mathf.Abs(lateral) < track.halfWidth + 8f)
        {
            for (int k = 0; k < checkpoints.Length; k++)
                if (Mathf.Abs(d - (k + 1) * track.length / 4f) < 12f) checkpoints[k] = true;

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
            if (r.isPlayer) continue;
            r.ai.Step(dt, racing, r.finished, aiSkill);
            r.progress = r.ai.progress;
            r.lateral = r.ai.lane;
            r.speed = r.ai.Speed;
            if (!r.finished && r.progress >= laps * track.length) Finish(r);
        }
    }

    void OnPlayerCrossedLine()
    {
        if (crossings < 2 || crossings - 1 <= lapsTimed || playerRacer.finished) return;
        lapsTimed = crossings - 1;
        lastLap = raceTime - lapStartTime;
        if (bestLap < 0 || lastLap < bestLap) bestLap = lastLap;
        lapStartTime = raceTime;
        if (lapsTimed >= laps) Finish(playerRacer);
    }

    void Finish(Racer r)
    {
        r.finished = true;
        r.finishTime = raceTime;
        finishOrder.Add(r);
        if (r.isPlayer) resultsTimer = 10f;
        if (winner == null) { winner = r; winnerBannerTimer = 5f; }
    }

    void RespawnPlayer()
    {
        Vector3 pos = track.PointAt(prevD) + Vector3.up * 0.05f;
        player.Teleport(pos, Quaternion.LookRotation(track.TangentAt(prevD)));
    }

    void SetLights(int count)
    {
        for (int i = 0; i < startLights.Length; i++)
            if (startLights[i] != null) startLights[i].sharedMaterial = i < count ? lightOn : lightOff;
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

    static string FormatTime(float t)
    {
        if (t < 0) return "--:--.--";
        int m = Mathf.FloorToInt(t / 60f);
        float sec = t - m * 60f;
        return string.Format("{0:00}:{1:00.00}", m, sec);
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
        float s = Screen.height / 1080f;
        if (!Mathf.Approximately(s, styleScale))
        {
            styleScale = s;
            big = Style(Mathf.RoundToInt(34 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
            mid = Style(Mathf.RoundToInt(20 * s), FontStyle.Bold, TextAnchor.MiddleLeft);
            small = Style(Mathf.RoundToInt(15 * s), FontStyle.Normal, TextAnchor.MiddleCenter);
            small.normal.textColor = new Color(1, 1, 1, 0.7f);
            center = Style(Mathf.RoundToInt(64 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
        }
        var standings = Standings();

        DrawTopPanel(s, standings);
        DrawMinimap(s);
        DrawStandings(s, standings);

        if (state == State.Countdown) DrawStartLights(s);
        if (goBannerTimer > 0) Banner(s, "GO!", new Color(0.3f, 1f, 0.4f));

        Vector3 tangent = track.TangentAt(prevD);
        bool onTrack = Mathf.Abs(playerRacer.lateral) < track.halfWidth + 2f;
        if (state == State.Racing && !playerRacer.finished && onTrack && player.Speed > 3f && Vector3.Dot(player.transform.forward, tangent) < -0.3f)
            Banner(s, "WRONG WAY", new Color(1f, 0.3f, 0.25f));

        // Full results for 10 s after you finish, then a small strip so you can keep driving freely
        if (playerRacer.finished && resultsTimer > 0) DrawResults(s, standings);
        else if (playerRacer.finished) DrawWinnerStrip(s);
        else if (winner != null && winnerBannerTimer > 0)
            Banner(s, winner.name + " WINS!", new Color(1f, 0.85f, 0.2f));
    }

    void Banner(float s, string text, Color c)
    {
        var st = new GUIStyle(center);
        st.normal.textColor = c;
        Rect r = new Rect(0, Screen.height * 0.3f, Screen.width, 90 * s);
        Fill(new Rect(Screen.width / 2f - 330 * s, r.y, 660 * s, r.height), new Color(0, 0, 0, 0.55f), 16 * s);
        GUI.Label(r, text, st);
    }

    void DrawTopPanel(float s, List<Racer> standings)
    {
        Rect p = new Rect(Screen.width / 2f - 260 * s, 16 * s, 520 * s, 104 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.75f), 14 * s);
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

    Vector2 MapPoint(Vector3 p, Rect area)
    {
        return new Vector2(area.x + (p.x - mapMinX) * mapScale * area.width, area.y + (mapMaxZ - p.z) * mapScale * area.height);
    }

    void DrawMinimap(float s)
    {
        float size = 250 * s;
        Rect panel = new Rect(Screen.width - size - 16 * s, 16 * s, size, size);
        Fill(panel, new Color(0.05f, 0.06f, 0.09f, 0.72f), 14 * s);
        Rect area = new Rect(panel.x + 18 * s, panel.y + 18 * s, size - 36 * s, size - 36 * s);

        float dot = 5 * s;
        for (int i = 0; i < track.Count; i += 2)
        {
            Vector2 m = MapPoint(track.points[i], area);
            Fill(new Rect(m.x - dot / 2, m.y - dot / 2, dot, dot), new Color(0.85f, 0.85f, 0.9f, 0.85f), dot / 2);
        }
        Vector2 start = MapPoint(track.points[0], area);
        Fill(new Rect(start.x - 2 * s, start.y - 7 * s, 4 * s, 14 * s), new Color(1f, 0.85f, 0.2f));

        foreach (var r in racers)
        {
            if (r.isPlayer) continue;
            Vector2 m = MapPoint(r.tr.position, area);
            Fill(new Rect(m.x - 5 * s, m.y - 5 * s, 10 * s, 10 * s), r.color, 5 * s);
        }
        Vector2 pm = MapPoint(player.transform.position, area);
        Fill(new Rect(pm.x - 8 * s, pm.y - 8 * s, 16 * s, 16 * s), Color.white, 8 * s);
        Fill(new Rect(pm.x - 5 * s, pm.y - 5 * s, 10 * s, 10 * s), new Color(0.15f, 0.4f, 1f), 5 * s);
    }

    void DrawStandings(float s, List<Racer> standings)
    {
        float rowH = 28 * s;
        Rect p = new Rect(24 * s, 240 * s, 250 * s, 20 * s + rowH * standings.Count);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.72f), 12 * s);
        var right = new GUIStyle(mid) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        for (int i = 0; i < standings.Count; i++)
        {
            var r = standings[i];
            float y = p.y + 10 * s + i * rowH;
            if (r.isPlayer) Fill(new Rect(p.x + 6 * s, y, p.width - 12 * s, rowH), new Color(0.15f, 0.4f, 1f, 0.45f), 6 * s);
            Fill(new Rect(p.x + 14 * s, y + 9 * s, 10 * s, 10 * s), r.isPlayer ? new Color(0.15f, 0.4f, 1f) : r.color, 5 * s);
            GUI.Label(new Rect(p.x + 32 * s, y, 170 * s, rowH), (i + 1) + ". " + r.name, mid);
            GUI.Label(new Rect(p.x + 14 * s, y, p.width - 28 * s, rowH), r.finished ? "FIN" : "L" + LapOf(r), right);
        }
    }

    void DrawWinnerStrip(float s)
    {
        Rect p = new Rect(Screen.width / 2f - 260 * s, 128 * s, 520 * s, 40 * s);
        Fill(p, new Color(0.05f, 0.06f, 0.09f, 0.75f), 10 * s);
        var st = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(1f, 0.85f, 0.2f);
        int pos = finishOrder.IndexOf(playerRacer) + 1;
        GUI.Label(p, "WINNER: " + winner.name + "   ·   You finished P" + pos + "   ·   ENTER to race again", st);
    }

    void DrawResults(float s, List<Racer> standings)
    {
        float rowH = 40 * s;
        Rect p = new Rect(Screen.width / 2f - 300 * s, Screen.height / 2f - 200 * s, 600 * s, 170 * s + rowH * standings.Count);
        Fill(p, new Color(0.03f, 0.04f, 0.07f, 0.9f), 18 * s);

        bool youWon = winner == playerRacer;
        var title = new GUIStyle(big);
        title.normal.textColor = youWon ? new Color(1f, 0.85f, 0.2f) : Color.white;
        GUI.Label(new Rect(p.x, p.y + 14 * s, p.width, 50 * s), youWon ? "YOU WIN!" : "WINNER: " + winner.name, title);
        GUI.Label(new Rect(p.x, p.y + 62 * s, p.width, 24 * s), "RACE RESULTS", small);

        var right = new GUIStyle(mid) { alignment = TextAnchor.MiddleRight };
        for (int i = 0; i < standings.Count; i++)
        {
            var r = standings[i];
            float y = p.y + 96 * s + i * rowH;
            if (r.isPlayer) Fill(new Rect(p.x + 20 * s, y, p.width - 40 * s, rowH - 4 * s), new Color(0.15f, 0.4f, 1f, 0.45f), 8 * s);
            GUI.Label(new Rect(p.x + 40 * s, y, 300 * s, rowH - 4 * s), (i + 1) + ".  " + r.name, mid);
            string time = r.finished ? (r == winner ? FormatTime(r.finishTime) : "+" + (r.finishTime - winner.finishTime).ToString("0.00") + "s") : "racing...";
            GUI.Label(new Rect(p.x + 40 * s, y, p.width - 80 * s, rowH - 4 * s), time, right);
        }
        GUI.Label(new Rect(p.x, p.yMax - 46 * s, p.width, 30 * s), "Best lap " + FormatTime(bestLap) + "   ·   Press ENTER to race again", small);
    }
}
