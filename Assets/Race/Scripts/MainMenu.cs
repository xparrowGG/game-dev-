using UnityEngine;
using UnityEngine.SceneManagement;

// Main menu (IMGUI): Career, Quick Race, Settings, Controls, Quit, with a rotating showroom car
// that wears the selected team's livery.
public class MainMenu : MonoBehaviour
{
    public Transform showroomCar;
    public float spinSpeed = 14f;

    enum Screen_ { Main, QuickRace, CareerNew, CareerHub, Settings, Controls }
    Screen_ screen = Screen_.Main;
    int hubTab;                 // 0 overview, 1 standings, 2 development
    bool confirmDelete;
    Vector2 scroll;

    RaceSetup quick;
    string newName = "Your Name", newCode = "YOU", newNumber = "7";
    int newTeam = 9;
    int shownTeam = -1;

    Texture2D tex;
    GUIStyle title, subtitle, header, label, value, btn, btnSelected, small, field;
    float styleScale = -1f;
    static readonly Color Accent = new Color(0.9f, 0.12f, 0.15f);

    void Awake()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        GameSession.ApplySettings();
        quick = GameSession.Race;
        if (GameSession.OpenCareerHub && GameSession.HasCareer) screen = Screen_.CareerHub;
        GameSession.OpenCareerHub = false;
        tex = Texture2D.whiteTexture;
    }

    void Update()
    {
        if (showroomCar != null) showroomCar.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        int team = screen == Screen_.CareerHub && GameSession.HasCareer ? GameSession.Career.team
                 : screen == Screen_.CareerNew ? newTeam : quick.team;
        if (team != shownTeam && showroomCar != null)
        {
            shownTeam = team;
            var t = GameSession.Teams[team];
            RaceManager.ApplyLivery(showroomCar.gameObject, t.primary, t.secondary, new Color(1f, 0.85f, 0.1f));
        }
    }

    // ---------- Styles & widgets ----------

    void BuildStyles(float s)
    {
        styleScale = s;
        title = Make(GUI.skin.label, 64, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        subtitle = Make(GUI.skin.label, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f));
        header = Make(GUI.skin.label, 30, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        label = Make(GUI.skin.label, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.75f));
        value = Make(GUI.skin.label, 18, FontStyle.Bold, TextAnchor.MiddleRight, Color.white);
        small = Make(GUI.skin.label, 15, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f));
        btn = Make(GUI.skin.button, 20, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        btn.normal.background = Solid(new Color(1, 1, 1, 0.06f));
        btn.hover.background = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.85f));
        btn.active.background = Solid(Accent);
        btn.hover.textColor = btn.active.textColor = Color.white;
        btn.padding = new RectOffset(Mathf.RoundToInt(18 * s), 8, 4, 4);
        btn.border = new RectOffset(0, 0, 0, 0);
        btnSelected = new GUIStyle(btn);
        btnSelected.normal.background = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.6f));
        field = Make(GUI.skin.textField, 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        field.normal.background = Solid(new Color(1, 1, 1, 0.1f));
        field.focused.background = Solid(new Color(1, 1, 1, 0.18f));
        field.padding = new RectOffset(10, 10, 4, 4);
    }

    GUIStyle Make(GUIStyle src, int size, FontStyle fs, TextAnchor a, Color c)
    {
        var st = new GUIStyle(src) { fontSize = Mathf.RoundToInt(size * styleScale), fontStyle = fs, alignment = a };
        st.normal.textColor = st.hover.textColor = st.focused.textColor = c;
        return st;
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void Fill(Rect r, Color c, float radius = 0) => GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true, 0, c, 0, radius);

    bool Button(Rect r, string text, bool selected = false) => GUI.Button(r, text, selected ? btnSelected : btn);

    void Selector(ref float y, float x, float w, float s, string name, ref int index, string[] options)
    {
        GUI.Label(new Rect(x, y, w * 0.45f, 36 * s), name, label);
        float bx = x + w * 0.45f;
        if (Button(new Rect(bx, y, 40 * s, 36 * s), "<")) index = (index - 1 + options.Length) % options.Length;
        var c = new GUIStyle(value) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(bx + 44 * s, y, w * 0.55f - 92 * s, 36 * s), options[index], c);
        if (Button(new Rect(x + w - 40 * s, y, 40 * s, 36 * s), ">")) index = (index + 1) % options.Length;
        y += 44 * s;
    }

    void IntSlider(ref float y, float x, float w, float s, string name, ref int v, int min, int max)
    {
        GUI.Label(new Rect(x, y, w * 0.45f, 36 * s), name, label);
        v = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + w * 0.45f, y + 12 * s, w * 0.42f, 20 * s), v, min, max));
        GUI.Label(new Rect(x, y, w, 36 * s), v.ToString(), value);
        y += 44 * s;
    }

    void FloatSlider(ref float y, float x, float w, float s, string name, ref float v)
    {
        GUI.Label(new Rect(x, y, w * 0.45f, 36 * s), name, label);
        v = GUI.HorizontalSlider(new Rect(x + w * 0.45f, y + 12 * s, w * 0.42f, 20 * s), v, 0f, 1f);
        GUI.Label(new Rect(x, y, w, 36 * s), Mathf.RoundToInt(v * 100) + "%", value);
        y += 44 * s;
    }

    void Toggle(ref float y, float x, float w, float s, string name, ref bool v)
    {
        GUI.Label(new Rect(x, y, w * 0.6f, 36 * s), name, label);
        if (Button(new Rect(x + w - 110 * s, y, 110 * s, 36 * s), v ? "  ON" : "  OFF", v)) v = !v;
        y += 44 * s;
    }

    // ---------- Screens ----------

    void OnGUI()
    {
        float s = Screen.height / 1080f;
        if (!Mathf.Approximately(s, styleScale)) BuildStyles(s);

        // Left panel with a soft fade toward the showroom
        float pw = 720 * s;
        Fill(new Rect(0, 0, pw, Screen.height), new Color(0.03f, 0.035f, 0.05f, 0.88f));
        for (int i = 0; i < 40; i++)
            Fill(new Rect(pw + i * 4 * s, 0, 4 * s, Screen.height), new Color(0.03f, 0.035f, 0.05f, 0.88f * (1f - i / 40f)));
        Fill(new Rect(60 * s, 70 * s, 8 * s, 92 * s), Accent);
        GUI.Label(new Rect(84 * s, 60 * s, 800 * s, 80 * s), "JOY GP RACING", title);
        GUI.Label(new Rect(88 * s, 132 * s, 800 * s, 30 * s), GameSession.TrackName.ToUpperInvariant() + "  ·  " + GameSession.Teams.Length * 2 + " DRIVERS  ·  10 TEAMS", subtitle);

        float x = 84 * s, y = 210 * s, w = 560 * s;
        switch (screen)
        {
            case Screen_.Main: DrawMain(x, y, w, s); break;
            case Screen_.QuickRace: DrawQuickRace(x, y, w, s); break;
            case Screen_.CareerNew: DrawCareerNew(x, y, w, s); break;
            case Screen_.CareerHub: DrawCareerHub(x, y, w, s); break;
            case Screen_.Settings: DrawSettings(x, y, w, s); break;
            case Screen_.Controls: DrawControls(x, y, w, s); break;
        }
        GUI.Label(new Rect(84 * s, Screen.height - 50 * s, 800 * s, 30 * s), "Fictional teams and drivers. Use the mouse to navigate.", small);
    }

    void DrawMain(float x, float y, float w, float s)
    {
        float h = 62 * s, gap = 12 * s;
        if (Button(new Rect(x, y, w, h), GameSession.HasCareer ? "CAREER  ·  continue" : "CAREER  ·  new driver"))
            screen = GameSession.HasCareer ? Screen_.CareerHub : Screen_.CareerNew;
        y += h + gap;
        if (Button(new Rect(x, y, w, h), "QUICK RACE")) screen = Screen_.QuickRace;
        y += h + gap;
        if (Button(new Rect(x, y, w, h), "SETTINGS")) screen = Screen_.Settings;
        y += h + gap;
        if (Button(new Rect(x, y, w, h), "CONTROLS")) screen = Screen_.Controls;
        y += h + gap;
        if (Button(new Rect(x, y, w, h), "QUIT"))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    void TeamPicker(ref float y, float x, float w, float s, ref int team, int minTeam = 0)
    {
        var t = GameSession.Teams[team];
        GUI.Label(new Rect(x, y, w * 0.45f, 36 * s), "Team", label);
        float bx = x + w * 0.45f;
        if (Button(new Rect(bx, y, 40 * s, 36 * s), "<")) team = team - 1 < minTeam ? GameSession.Teams.Length - 1 : team - 1;
        Fill(new Rect(bx + 50 * s, y + 8 * s, 10 * s, 20 * s), t.primary);
        Fill(new Rect(bx + 62 * s, y + 8 * s, 10 * s, 20 * s), t.secondary);
        GUI.Label(new Rect(bx + 80 * s, y, w * 0.55f - 128 * s, 36 * s), t.name, new GUIStyle(value) { alignment = TextAnchor.MiddleLeft });
        if (Button(new Rect(x + w - 40 * s, y, 40 * s, 36 * s), ">")) team = team + 1 >= GameSession.Teams.Length ? minTeam : team + 1;
        y += 40 * s;
        int stars = Mathf.Clamp(Mathf.RoundToInt((t.performance - 0.95f) / 0.09f * 4f) + 1, 1, 5);
        GUI.Label(new Rect(x + w * 0.45f, y, w * 0.55f, 26 * s), "Car pace  " + new string('★', stars) + new string('☆', 5 - stars), small);
        y += 34 * s;
    }

    void DrawQuickRace(float x, float y, float w, float s)
    {
        GUI.Label(new Rect(x, y, w, 44 * s), "QUICK RACE", header);
        y += 56 * s;
        TeamPicker(ref y, x, w, s, ref quick.team);
        GUI.Label(new Rect(x, y, w * 0.45f, 36 * s), "Driver name", label);
        quick.driverName = GUI.TextField(new Rect(x + w * 0.45f, y, w * 0.55f, 36 * s), quick.driverName, 20, field);
        y += 44 * s;
        IntSlider(ref y, x, w, s, "Laps", ref quick.laps, 1, 78);
        IntSlider(ref y, x, w, s, "Opponents", ref quick.opponents, 1, 19);
        Selector(ref y, x, w, s, "AI difficulty", ref quick.difficulty, GameSession.DifficultyNames);
        Selector(ref y, x, w, s, "Starting grid", ref quick.grid, GameSession.GridNames);
        var st = GameSession.Settings;
        Toggle(ref y, x, w, s, "ABS", ref st.abs);
        Toggle(ref y, x, w, s, "Traction control", ref st.tractionControl);
        Toggle(ref y, x, w, s, "Steering assist", ref st.steeringAssist);
        Toggle(ref y, x, w, s, "Automatic gearbox", ref st.autoGearbox);
        y += 10 * s;
        if (Button(new Rect(x, y, w * 0.6f, 60 * s), "START RACE  ▶"))
        {
            quick.driverCode = MakeCode(quick.driverName);
            GameSession.Race = quick;
            GameSession.CareerMode = false;
            GameSession.SaveSettings();
            SceneManager.LoadScene(GameSession.RaceScene);
        }
        if (Button(new Rect(x + w * 0.62f, y, w * 0.38f, 60 * s), "BACK")) { GameSession.SaveSettings(); screen = Screen_.Main; }
    }

    void DrawCareerNew(float x, float y, float w, float s)
    {
        GUI.Label(new Rect(x, y, w, 44 * s), "NEW CAREER", header);
        y += 56 * s;
        GUI.Label(new Rect(x, y, w, 50 * s), "Every rookie starts at the back of the field. Score points,\nearn development upgrades and win offers from faster teams.", small);
        y += 66 * s;
        GUI.Label(new Rect(x, y, w * 0.45f, 36 * s), "Driver name", label);
        newName = GUI.TextField(new Rect(x + w * 0.45f, y, w * 0.55f, 36 * s), newName, 20, field);
        y += 44 * s;
        GUI.Label(new Rect(x, y, w * 0.45f, 36 * s), "Race number", label);
        newNumber = GUI.TextField(new Rect(x + w * 0.45f, y, 100 * s, 36 * s), newNumber, 2, field);
        y += 44 * s;
        if (newTeam < 6) newTeam = 9;
        TeamPicker(ref y, x, w, s, ref newTeam, 6);
        GUI.Label(new Rect(x, y, w, 26 * s), "Rookie seats available: the four slowest teams.", small);
        y += 40 * s;
        if (Button(new Rect(x, y, w * 0.6f, 60 * s), "START CAREER  ▶"))
        {
            int.TryParse(newNumber, out int num);
            GameSession.NewCareer(newName.Trim().Length > 0 ? newName.Trim() : "Rookie", MakeCode(newName), Mathf.Clamp(num, 1, 99), newTeam);
            screen = Screen_.CareerHub;
        }
        if (Button(new Rect(x + w * 0.62f, y, w * 0.38f, 60 * s), "BACK")) screen = Screen_.Main;
    }

    void DrawCareerHub(float x, float y, float w, float s)
    {
        var c = GameSession.Career;
        if (c == null) { screen = Screen_.CareerNew; return; }
        var team = GameSession.Teams[c.team];
        var table = GameSession.SortedStandings();
        int champPos = table.FindIndex(d => d.isPlayer) + 1;
        var me = table[champPos - 1];

        GUI.Label(new Rect(x, y, w, 44 * s), c.driverName.ToUpperInvariant() + "  #" + c.number, header);
        y += 44 * s;
        Fill(new Rect(x, y + 6 * s, 10 * s, 20 * s), team.primary);
        Fill(new Rect(x + 12 * s, y + 6 * s, 10 * s, 20 * s), team.secondary);
        GUI.Label(new Rect(x + 30 * s, y, w, 32 * s), team.name + "   ·   Season " + c.season + "   ·   " +
            (c.seasonOver ? "season complete" : "Round " + c.round + " of " + c.roundsPerSeason), label);
        y += 40 * s;
        GUI.Label(new Rect(x, y, w, 30 * s), "Championship: P" + champPos + "  ·  " + me.points + " pts  ·  R&D points: " + c.rdPoints, value);
        y += 44 * s;

        string[] tabs = { "OVERVIEW", "STANDINGS", "CAR DEVELOPMENT" };
        float tw = w / 3f;
        for (int i = 0; i < 3; i++)
            if (Button(new Rect(x + i * tw, y, tw - 6 * s, 40 * s), tabs[i], hubTab == i)) hubTab = i;
        y += 54 * s;

        float listTop = y, listH = Screen.height - y - 190 * s;
        if (c.seasonOver) DrawSeasonEnd(x, y, w, s, c);
        else if (hubTab == 0) DrawOverview(x, y, w, s, c);
        else if (hubTab == 1) DrawStandings(x, y, w, s, table, listH);
        else DrawDevelopment(x, y, w, s, c);

        float by = Screen.height - 130 * s;
        if (!c.seasonOver && Button(new Rect(x, by, w * 0.6f, 60 * s), "RACE  ·  ROUND " + c.round + "  ▶"))
        {
            GameSession.CareerMode = true;
            GameSession.Race = GameSession.CareerRaceSetup();
            SceneManager.LoadScene(GameSession.RaceScene);
        }
        if (Button(new Rect(x + w * 0.62f, by, w * 0.38f, 60 * s), "MAIN MENU")) { screen = Screen_.Main; confirmDelete = false; }
        if (!confirmDelete)
        {
            if (GUI.Button(new Rect(x, by + 66 * s, 200 * s, 26 * s), "Delete career", small)) confirmDelete = true;
        }
        else
        {
            GUI.Label(new Rect(x, by + 66 * s, 200 * s, 26 * s), "Delete this career?", small);
            if (GUI.Button(new Rect(x + 200 * s, by + 66 * s, 120 * s, 26 * s), "Yes, delete", small)) { GameSession.DeleteCareer(); confirmDelete = false; screen = Screen_.Main; }
            if (GUI.Button(new Rect(x + 330 * s, by + 66 * s, 80 * s, 26 * s), "Cancel", small)) confirmDelete = false;
        }
    }

    void Stat(ref float y, float x, float w, float s, string name, string v)
    {
        GUI.Label(new Rect(x, y, w, 30 * s), name, label);
        GUI.Label(new Rect(x, y, w, 30 * s), v, value);
        y += 32 * s;
    }

    void DrawOverview(float x, float y, float w, float s, CareerData c)
    {
        Stat(ref y, x, w, s, "Next race", GameSession.TrackName);
        Stat(ref y, x, w, s, "Race distance", GameSession.Settings.careerLaps + " laps");
        Stat(ref y, x, w, s, "AI difficulty", GameSession.DifficultyNames[GameSession.Settings.difficulty]);
        y += 10 * s;
        Stat(ref y, x, w, s, "Career starts", c.races.ToString());
        Stat(ref y, x, w, s, "Wins  ·  podiums  ·  poles", c.wins + "  ·  " + c.podiums + "  ·  " + c.poles);
        Stat(ref y, x, w, s, "Career points", c.careerPoints.ToString());
        Stat(ref y, x, w, s, "Best finish", c.bestFinish < 99 ? "P" + c.bestFinish : "—");
        Stat(ref y, x, w, s, "Championships", c.championships.ToString());
        if (c.lastResult.Count > 0)
        {
            y += 10 * s;
            int myPos = c.lastResult.IndexOf(c.driverName) + 1;
            GUI.Label(new Rect(x, y, w, 30 * s), "Last race: winner " + c.lastResult[0] + (myPos > 0 ? "   ·   you finished P" + myPos : ""), small);
        }
    }

    void DrawStandings(float x, float y, float w, float s, System.Collections.Generic.List<DriverStanding> table, float height)
    {
        float rowH = 26 * s;
        var nameSt = new GUIStyle(label) { fontSize = Mathf.RoundToInt(16 * s) };
        var valSt = new GUIStyle(value) { fontSize = Mathf.RoundToInt(16 * s) };
        scroll = GUI.BeginScrollView(new Rect(x, y, w, height), scroll, new Rect(0, 0, w - 20 * s, rowH * table.Count));
        for (int i = 0; i < table.Count; i++)
        {
            var d = table[i];
            float ry = i * rowH;
            if (d.isPlayer) Fill(new Rect(0, ry, w - 20 * s, rowH - 2 * s), new Color(Accent.r, Accent.g, Accent.b, 0.35f));
            Fill(new Rect(38 * s, ry + 5 * s, 4 * s, rowH - 10 * s), GameSession.Teams[d.team].primary);
            GUI.Label(new Rect(6 * s, ry, 30 * s, rowH), (i + 1).ToString(), nameSt);
            GUI.Label(new Rect(50 * s, ry, 380 * s, rowH), d.name + "   " + GameSession.Teams[d.team].name, nameSt);
            GUI.Label(new Rect(0, ry, w - 30 * s, rowH), d.points + " pts", valSt);
        }
        GUI.EndScrollView();
    }

    void DrawDevelopment(float x, float y, float w, float s, CareerData c)
    {
        GUI.Label(new Rect(x, y, w, 50 * s), "Spend R&D points earned in races. Each level costs more.\nUpgrades reset if you change team.", small);
        y += 64 * s;
        Upgrade(ref y, x, w, s, "Power unit  (+2% power / level)", ref c.engine, c);
        Upgrade(ref y, x, w, s, "Aerodynamics  (+2.5% downforce)", ref c.aero, c);
        Upgrade(ref y, x, w, s, "Chassis  (+1.2% mechanical grip)", ref c.chassis, c);
    }

    void Upgrade(ref float y, float x, float w, float s, string name, ref int level, CareerData c)
    {
        int cost = 2 + level;
        GUI.Label(new Rect(x, y, w * 0.6f, 32 * s), name, label);
        for (int i = 0; i < GameSession.MaxUpgrade; i++)
            Fill(new Rect(x + i * 26 * s, y + 36 * s, 20 * s, 10 * s), i < level ? Accent : new Color(1, 1, 1, 0.15f), 3 * s);
        bool can = level < GameSession.MaxUpgrade && c.rdPoints >= cost;
        GUI.enabled = can;
        if (Button(new Rect(x + w - 190 * s, y, 190 * s, 44 * s), level >= GameSession.MaxUpgrade ? "  MAXED" : "  UPGRADE (" + cost + ")"))
        {
            c.rdPoints -= cost;
            level++;
            GameSession.SaveCareer();
        }
        GUI.enabled = true;
        y += 64 * s;
    }

    void DrawSeasonEnd(float x, float y, float w, float s, CareerData c)
    {
        GUI.Label(new Rect(x, y, w, 36 * s), c.lastSeasonPosition == 1 ? "WORLD CHAMPION!" : "Season " + c.season + " finished: P" + c.lastSeasonPosition, header);
        y += 54 * s;
        if (c.offers.Count == 0) GUI.Label(new Rect(x, y, w, 30 * s), "No other team made an offer. Finish higher to attract faster teams.", small);
        else GUI.Label(new Rect(x, y, w, 30 * s), "Contract offers for next season:", label);
        y += 40 * s;
        foreach (int t in c.offers)
        {
            if (Button(new Rect(x, y, w, 48 * s), "SIGN FOR  " + GameSession.Teams[t].name.ToUpperInvariant())) GameSession.StartNextSeason(t);
            y += 56 * s;
        }
        if (Button(new Rect(x, y, w, 48 * s), "STAY WITH  " + GameSession.Teams[c.team].name.ToUpperInvariant())) GameSession.StartNextSeason(c.team);
    }

    void DrawSettings(float x, float y, float w, float s)
    {
        var st = GameSession.Settings;
        GUI.Label(new Rect(x, y, w, 44 * s), "SETTINGS", header);
        y += 56 * s;
        string[] q = QualitySettings.names;
        int qi = st.quality < 0 ? QualitySettings.GetQualityLevel() : st.quality;
        Selector(ref y, x, w, s, "Graphics quality", ref qi, q);
        st.quality = qi;
        Toggle(ref y, x, w, s, "Fullscreen", ref st.fullscreen);
        Toggle(ref y, x, w, s, "V-Sync", ref st.vsync);
        FloatSlider(ref y, x, w, s, "Master volume", ref st.masterVolume);
        FloatSlider(ref y, x, w, s, "Engine volume", ref st.engineVolume);
        Toggle(ref y, x, w, s, "Speed in mph", ref st.useMph);
        Selector(ref y, x, w, s, "Default camera", ref st.defaultCamera, new[] { "TV pod", "Chase", "Cockpit" });
        Selector(ref y, x, w, s, "Career AI difficulty", ref st.difficulty, GameSession.DifficultyNames);
        IntSlider(ref y, x, w, s, "Career race laps", ref st.careerLaps, 1, 78);
        Toggle(ref y, x, w, s, "ABS", ref st.abs);
        Toggle(ref y, x, w, s, "Traction control", ref st.tractionControl);
        Toggle(ref y, x, w, s, "Steering assist", ref st.steeringAssist);
        Toggle(ref y, x, w, s, "Automatic gearbox", ref st.autoGearbox);
        if (Button(new Rect(x, y + 6 * s, w * 0.6f, 56 * s), "APPLY & BACK"))
        {
            GameSession.SaveSettings();
            GameSession.ApplySettings();
            screen = Screen_.Main;
        }
    }

    void DrawControls(float x, float y, float w, float s)
    {
        GUI.Label(new Rect(x, y, w, 44 * s), "CONTROLS", header);
        y += 56 * s;
        string[,] rows =
        {
            { "Throttle", "W / ↑", "Right trigger" }, { "Brake (hold at a stop: reverse)", "S / ↓", "Left trigger" },
            { "Steer", "A D / ← →", "Left stick" }, { "Gear up / down (manual)", "E / Q", "RB / LB" },
            { "ERS boost", "Left Shift", "X" }, { "Camera: TV pod / chase / cockpit", "C", "—" },
            { "Back on track", "R", "—" }, { "Pause", "Esc / P", "Start" }, { "Show help", "H", "—" }, { "Results after the flag", "Tab", "—" }
        };
        var head = new GUIStyle(value) { alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(x + w * 0.5f, y, 150 * s, 30 * s), "Keyboard", head);
        GUI.Label(new Rect(x + w * 0.76f, y, 150 * s, 30 * s), "Gamepad", head);
        y += 36 * s;
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            GUI.Label(new Rect(x, y, w * 0.5f, 30 * s), rows[i, 0], label);
            GUI.Label(new Rect(x + w * 0.5f, y, w * 0.26f, 30 * s), rows[i, 1], new GUIStyle(label) { fontStyle = FontStyle.Bold });
            GUI.Label(new Rect(x + w * 0.76f, y, w * 0.24f, 30 * s), rows[i, 2], label);
            y += 34 * s;
        }
        if (Button(new Rect(x, y + 20 * s, w * 0.6f, 56 * s), "BACK")) screen = Screen_.Main;
    }

    static string MakeCode(string name)
    {
        string[] parts = name.Trim().Split(' ');
        string s = parts[parts.Length - 1].ToUpperInvariant();
        if (s.Length < 3) s = (s + "XXX").Substring(0, 3);
        return s.Substring(0, 3);
    }
}
