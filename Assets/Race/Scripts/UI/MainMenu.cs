using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// XPARROW F1 GP main menu (IMGUI): big, simple cards — Career, Quick Race, Settings, Controls —
// with a team picker that shows each team's colours and drivers, and a rotating showroom car
// that wears the selected team's colours.
public class MainMenu : MonoBehaviour
{
    public Transform showroomCar;
    public float spinSpeed = 14f;

    enum Page { Main, QuickRace, TimeTrial, CareerNew, CareerHub, Garage, Records, Settings, Controls, Credits }
    Page page = Page.Main;
    int hubTab;
    bool confirmDelete;
    Vector2 scroll, listScroll;

    RaceSetup quick;
    string newName = "Your Name", newNumber = "7";
    int newTeam;
    int shownTeam = -1, shownCompound = -1;

    Texture2D tex;
    GUIStyle title, subtitle, header, label, value, small, field, cardTitle, cardText, chip, chipOn, bigButton;
    float styleScale = -1f;
    static readonly Color Accent = new Color(0.92f, 0.1f, 0.14f);
    static readonly Color Panel = new Color(0.035f, 0.04f, 0.06f, 0.9f);

    void Awake()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        GameSession.ApplySettings();
        quick = GameSession.Race;
        quick.team = Mathf.Clamp(quick.team, 0, GameSession.Teams.Length - 1);
        quick.track = Mathf.Clamp(quick.track, 0, GameSession.Tracks.Length - 1);
        quick.opponents = Mathf.Clamp(quick.opponents, 1, GameSession.Teams.Length * 2 - 1);
        if (GameSession.OpenCareerHub && GameSession.HasCareer) page = Page.CareerHub;
        GameSession.OpenCareerHub = false;
        tex = Texture2D.whiteTexture;
    }

    void Update()
    {
        if (showroomCar != null) showroomCar.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        int team = page == Page.CareerHub && GameSession.HasCareer ? GameSession.Career.team : page == Page.CareerNew ? newTeam : quick.team;
        team = Mathf.Clamp(team, 0, GameSession.Teams.Length - 1);
        if (team != shownTeam && showroomCar != null)
        {
            shownTeam = team;
            var t = GameSession.Teams[team];
            RaceManager.ApplyLivery(showroomCar.gameObject, t.primary, t.secondary, new Color(1f, 0.85f, 0.1f), t.livery);
        }
        int comp = Mathf.Clamp(quick.startCompound, 0, 2);
        if (comp != shownCompound && showroomCar != null)
        {
            shownCompound = comp;
            Tyres.Paint(showroomCar.gameObject, (Compound)comp);
        }
    }

    // ---------- Styles & widgets ----------

    void BuildStyles(float s)
    {
        styleScale = s;
        title = Make(GUI.skin.label, 66, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        subtitle = Make(GUI.skin.label, 17, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.55f));
        header = Make(GUI.skin.label, 32, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        label = Make(GUI.skin.label, 17, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.75f));
        value = Make(GUI.skin.label, 17, FontStyle.Bold, TextAnchor.MiddleRight, Color.white);
        small = Make(GUI.skin.label, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f));
        cardTitle = Make(GUI.skin.label, 26, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
        cardText = Make(GUI.skin.label, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.65f));
        cardText.wordWrap = true;
        field = Make(GUI.skin.textField, 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        field.normal.background = Solid(new Color(1, 1, 1, 0.1f));
        field.focused.background = Solid(new Color(1, 1, 1, 0.18f));
        field.padding = new RectOffset(10, 10, 4, 4);
        chip = Make(GUI.skin.button, 15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        chip.normal.background = Solid(new Color(1, 1, 1, 0.07f));
        chip.hover.background = Solid(new Color(1, 1, 1, 0.16f));
        chip.active.background = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.8f));
        chip.hover.textColor = chip.active.textColor = Color.white;
        chip.border = new RectOffset(0, 0, 0, 0);
        chipOn = new GUIStyle(chip);
        chipOn.normal.background = Solid(Accent);
        chipOn.hover.background = Solid(new Color(1f, 0.25f, 0.28f));
        bigButton = new GUIStyle(chip) { fontSize = Mathf.RoundToInt(24 * s) };
        bigButton.normal.background = Solid(Accent);
        bigButton.hover.background = Solid(new Color(1f, 0.25f, 0.28f));
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

    bool Chip(Rect r, string text, bool on) => GUI.Button(r, text, on ? chipOn : chip);

    // A big clickable card with a coloured edge, title and one line of explanation
    bool Card(Rect r, string titleText, string text, Color edge)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Fill(r, hover ? new Color(1, 1, 1, 0.12f) : new Color(1, 1, 1, 0.05f), 10 * styleScale);
        Fill(new Rect(r.x, r.y, 6 * styleScale, r.height), edge, 3 * styleScale);
        GUI.Label(new Rect(r.x + 24 * styleScale, r.y + 12 * styleScale, r.width - 40 * styleScale, 34 * styleScale), titleText, cardTitle);
        GUI.Label(new Rect(r.x + 24 * styleScale, r.y + 48 * styleScale, r.width - 60 * styleScale, r.height - 50 * styleScale), text, cardText);
        GUI.Label(new Rect(r.xMax - 40 * styleScale, r.y, 30 * styleScale, r.height), ">", new GUIStyle(cardTitle) { alignment = TextAnchor.MiddleCenter });
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    void Section(ref float y, float x, float w, float s, string text)
    {
        GUI.Label(new Rect(x, y, w, 26 * s), text.ToUpperInvariant(), subtitle);
        y += 30 * s;
    }

    void Toggle(ref float y, float x, float w, float s, string name, ref bool v)
    {
        GUI.Label(new Rect(x, y, w * 0.6f, 34 * s), name, label);
        if (Chip(new Rect(x + w - 120 * s, y, 120 * s, 34 * s), v ? "ON" : "OFF", v)) v = !v;
        y += 40 * s;
    }

    void FloatSlider(ref float y, float x, float w, float s, string name, ref float v)
    {
        GUI.Label(new Rect(x, y, w * 0.45f, 34 * s), name, label);
        v = GUI.HorizontalSlider(new Rect(x + w * 0.45f, y + 12 * s, w * 0.42f, 20 * s), v, 0f, 1f);
        GUI.Label(new Rect(x, y, w, 34 * s), Mathf.RoundToInt(v * 100) + "%", value);
        y += 40 * s;
    }

    void Choice(ref float y, float x, float w, float s, string name, ref int v, string[] options)
    {
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), name, label);
        float cw = (w * 0.65f - (options.Length - 1) * 6 * s) / options.Length;
        for (int i = 0; i < options.Length; i++)
            if (Chip(new Rect(x + w * 0.35f + i * (cw + 6 * s), y, cw, 34 * s), options[i], v == i)) v = i;
        y += 40 * s;
    }

    // ---------- Layout ----------

    // Menu groups (top navigation): PLAY / GARAGE / OPTIONS
    static readonly string[] GroupNames = { "PLAY", "GARAGE", "OPTIONS" };
    static readonly Color[] GroupColors = { new Color(0.92f, 0.1f, 0.14f), new Color(0.1f, 0.78f, 0.82f), new Color(0.3f, 0.85f, 0.45f) };

    int GroupOf(Page p)
    {
        switch (p)
        {
            case Page.Garage: case Page.Records: return 1;
            case Page.Settings: case Page.Controls: case Page.Credits: return 2;
            default: return 0;
        }
    }

    void Go(Page p) { if (page == Page.Settings) GameSession.SaveSettings(); page = p; scroll = Vector2.zero; confirmDelete = false; }

    void OnGUI()
    {
        float s = Screen.height / 1080f;
        if (!Mathf.Approximately(s, styleScale)) BuildStyles(s);
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && page != Page.Main) { Go(Page.Main); Event.current.Use(); }

        // Side panel with a soft fade into the showroom, and a slow animated light sweep
        float pw = 780 * s;
        Fill(new Rect(0, 0, pw, Screen.height), Panel);
        // Smooth fade into the showroom (one gradient texture, no banding)
        if (fadeTex == null)
        {
            fadeTex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < 64; i++) { float k = 1f - i / 63f; fadeTex.SetPixel(i, 0, new Color(Panel.r, Panel.g, Panel.b, Panel.a * k * k)); }
            fadeTex.Apply();
        }
        GUI.DrawTexture(new Rect(pw, 0, 220 * s, Screen.height), fadeTex, ScaleMode.StretchToFill, true);
        float sweep = Mathf.Repeat(Time.time * 0.12f, 1.4f) - 0.2f;
        Fill(new Rect(0, Screen.height * sweep, pw, 2 * s), new Color(1, 1, 1, 0.035f));
        Color gc = GroupColors[GroupOf(page)];
        Fill(new Rect(0, 0, 6 * s, Screen.height), new Color(gc.r, gc.g, gc.b, 0.85f));

        // Title
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 2f);
        Fill(new Rect(56 * s, 44 * s, 10 * s, 78 * s), new Color(Accent.r, Accent.g, Accent.b, pulse));
        Fill(new Rect(70 * s, 44 * s, 4 * s, 78 * s), new Color(1, 1, 1, 0.8f));
        GUI.Label(new Rect(90 * s, 34 * s, 900 * s, 70 * s), GameSession.GameTitle, new GUIStyle(title) { fontSize = Mathf.RoundToInt(54 * s) });
        GUI.Label(new Rect(94 * s, 96 * s, 900 * s, 28 * s), GameSession.Tracks.Length + " CIRCUITS  ·  " + GameSession.Teams.Length + " TEAMS  ·  " + GameSession.Teams.Length * 2 + " DRIVERS  ·  2026 CARS", subtitle);

        // Top navigation: group tabs + the pages of the active group
        DrawNav(84 * s, 140 * s, 620 * s, s);

        float x = 84 * s, w = 600 * s, top = 250 * s;
        Rect area = new Rect(x, top, w + 24 * s, Screen.height - top - 70 * s);
        switch (page)
        {
            case Page.Main: DrawMain(x, top, w, s); break;
            case Page.QuickRace: Scrolled(area, s, 2000f, (yy) => DrawQuickRace(0, yy, w, s)); break;
            case Page.TimeTrial: Scrolled(area, s, 1500f, (yy) => DrawTimeTrial(0, yy, w, s)); break;
            case Page.Garage: Scrolled(area, s, 1100f, (yy) => DrawGarage(0, yy, w, s)); break;
            case Page.Records: DrawRecords(x, top, w, s); break;
            case Page.Credits: Scrolled(area, s, 1100f, (yy) => DrawCredits(0, yy, w, s)); break;
            case Page.CareerNew: Scrolled(area, s, 1250f, (yy) => DrawCareerNew(0, yy, w, s)); break;
            case Page.CareerHub: DrawCareerHub(x, top, w, s); break;
            case Page.Settings: Scrolled(area, s, 1500f, (yy) => DrawSettings(0, yy, w, s)); break;
            case Page.Controls: DrawControls(x, top, w, s); break;
        }

        // Footer: key hints + legal line
        Fill(new Rect(0, Screen.height - 58 * s, pw, 1 * s), new Color(1, 1, 1, 0.08f));
        GUI.Label(new Rect(84 * s, Screen.height - 54 * s, 600 * s, 24 * s), page == Page.Main ? "Mouse to choose  ·  pick a mode to start" : "ESC  back to home  ·  mouse to choose", small);
        GUI.Label(new Rect(84 * s, Screen.height - 32 * s, 900 * s, 24 * s), "Team names for identification only, not affiliated with Formula 1 or any team.  " + MapCredit.Text, new GUIStyle(small) { fontSize = Mathf.RoundToInt(12 * s) });

        // Right side: what's selected right now (team on the turntable, next event)
        DrawInfoCard(s);
    }

    void DrawNav(float x, float y, float w, float s)
    {
        int g = GroupOf(page);
        float gw = (w - 2 * 8 * s) / 3f;
        for (int i = 0; i < 3; i++)
        {
            Rect r = new Rect(x + i * (gw + 8 * s), y, gw, 40 * s);
            bool on = g == i;
            bool hover = r.Contains(Event.current.mousePosition);
            Fill(r, on ? new Color(GroupColors[i].r, GroupColors[i].g, GroupColors[i].b, 0.9f) : new Color(1, 1, 1, hover ? 0.12f : 0.05f), 6 * s);
            GUI.Label(r, GroupNames[i], new GUIStyle(value) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(17 * s) });
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                Go(i == 0 ? Page.Main : i == 1 ? Page.Garage : Page.Settings);
        }
        // Sub-tabs of the active group
        string[] names; Page[] pages;
        if (g == 0)
        {
            names = new[] { "HOME", "CAREER", "QUICK RACE", "TIME TRIAL" };
            pages = new[] { Page.Main, GameSession.HasCareer ? Page.CareerHub : Page.CareerNew, Page.QuickRace, Page.TimeTrial };
        }
        else if (g == 1) { names = new[] { "CARS & TEAMS", "RECORDS" }; pages = new[] { Page.Garage, Page.Records }; }
        else { names = new[] { "SETTINGS", "CONTROLS", "CREDITS" }; pages = new[] { Page.Settings, Page.Controls, Page.Credits }; }
        float tw = (w - (names.Length - 1) * 6 * s) / names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            Rect r = new Rect(x + i * (tw + 6 * s), y + 50 * s, tw, 32 * s);
            bool on = page == pages[i] || (pages[i] == Page.CareerHub && page == Page.CareerNew) || (pages[i] == Page.CareerNew && page == Page.CareerHub);
            if (Chip(r, names[i], on)) Go(pages[i]);
        }
    }

    // Small live summary on the right: selected team and the next event / record
    void DrawInfoCard(float s)
    {
        if (page == Page.CareerHub || page == Page.Records || page == Page.Credits) return;
        int teamIdx = Mathf.Clamp(page == Page.CareerNew ? newTeam : quick.team, 0, GameSession.Teams.Length - 1);
        var team = GameSession.Teams[teamIdx];
        var trk = GameSession.Tracks[Mathf.Clamp(quick.track, 0, GameSession.Tracks.Length - 1)];
        Rect r = new Rect(Screen.width - 420 * s, Screen.height - 200 * s, 380 * s, 150 * s);
        Fill(r, new Color(0.03f, 0.035f, 0.05f, 0.78f), 10 * s);
        Fill(new Rect(r.x, r.y, 6 * s, r.height), team.primary, 3 * s);
        Fill(new Rect(r.x + 6 * s, r.y, 3 * s, r.height), team.secondary);
        GUI.Label(new Rect(r.x + 24 * s, r.y + 12 * s, r.width - 30 * s, 26 * s), "ON THE TURNTABLE", subtitle);
        GUI.Label(new Rect(r.x + 24 * s, r.y + 38 * s, r.width - 30 * s, 32 * s), team.name.ToUpperInvariant(), new GUIStyle(value) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(22 * s) });
        GUI.Label(new Rect(r.x + 24 * s, r.y + 70 * s, r.width - 30 * s, 22 * s), team.drivers[0] + "  ·  " + team.drivers[1], small);
        string next = GameSession.HasCareer ? "Career next: " + GameSession.Tracks[GameSession.CareerTrackIndex].grandPrix
                                            : "Selected circuit: " + trk.name;
        GUI.Label(new Rect(r.x + 24 * s, r.y + 100 * s, r.width - 30 * s, 22 * s), next, small);
        float best = GameSession.BestLap(trk.id);
        GUI.Label(new Rect(r.x + 24 * s, r.y + 122 * s, r.width - 30 * s, 22 * s), "Your record at " + trk.id + ": " + FormatLap(best), small);
    }

    void Scrolled(Rect area, float s, float contentHeight, System.Action<float> draw)
    {
        scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, area.width - 24 * s, contentHeight * s));
        draw(0f);
        GUI.EndScrollView();
    }

    Texture2D fadeTex;

    // A tall "hero" card for the home page
    bool Hero(Rect r, string titleText, string text, string tag, Color edge)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        float lift = hover ? 4f * styleScale : 0f;
        Rect rr = new Rect(r.x, r.y - lift, r.width, r.height);
        Fill(rr, hover ? new Color(1, 1, 1, 0.13f) : new Color(1, 1, 1, 0.055f), 12 * styleScale);
        Fill(new Rect(rr.x, rr.yMax - 5 * styleScale, rr.width, 5 * styleScale), new Color(edge.r, edge.g, edge.b, hover ? 1f : 0.75f), 3 * styleScale);
        GUI.Label(new Rect(rr.x + 18 * styleScale, rr.y + 14 * styleScale, rr.width - 30 * styleScale, 20 * styleScale), tag, new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = edge } });
        var heroTitle = new GUIStyle(cardTitle) { fontSize = Mathf.RoundToInt(21 * styleScale), wordWrap = true };
        GUI.Label(new Rect(rr.x + 16 * styleScale, rr.y + 36 * styleScale, rr.width - 24 * styleScale, 56 * styleScale), titleText, heroTitle);
        GUI.Label(new Rect(rr.x + 16 * styleScale, rr.y + 96 * styleScale, rr.width - 24 * styleScale, rr.height - 104 * styleScale), text, cardText);
        return GUI.Button(rr, GUIContent.none, GUIStyle.none);
    }

    void DrawMain(float x, float y, float w, float s)
    {
        bool has = GameSession.HasCareer;
        float gap = 12 * s, cw = (w - 2 * gap) / 3f, ch = 210 * s;
        if (Hero(new Rect(x, y, cw, ch), "CAREER",
                 has ? GameSession.Teams[GameSession.Career.team].name + ", round " + GameSession.Career.round + ": " + GameSession.Tracks[GameSession.CareerTrackIndex].grandPrix
                     : "Pick your team, race the world tour, earn upgrades and win the title.",
                 has ? "CONTINUE" : "NEW SEASON", GroupColors[0]))
            Go(has ? Page.CareerHub : Page.CareerNew);
        if (Hero(new Rect(x + cw + gap, y, cw, ch), "QUICK RACE", "Any circuit, any team, 22-car grid. Laps, AI level and tyres your way.", "RACE NOW", new Color(0.2f, 0.6f, 1f)))
            Go(Page.QuickRace);
        if (Hero(new Rect(x + 2 * (cw + gap), y, cw, ch), "TIME TRIAL", "Empty track, just you and the clock. Chase your lap records.", "HOT LAP", new Color(0.7f, 0.35f, 1f)))
            Go(Page.TimeTrial);
        y += ch + 26 * s;

        // Circuit strip: quick look at the five tracks with your records
        Section(ref y, x, w, s, "Circuits");
        float tw = (w - 4 * 8 * s) / 5f;
        for (int i = 0; i < GameSession.Tracks.Length && i < 5; i++)
        {
            var info = GameSession.Tracks[i];
            Rect r = new Rect(x + i * (tw + 8 * s), y, tw, 118 * s);
            bool sel = quick.track == i;
            Fill(r, sel ? new Color(1, 1, 1, 0.14f) : new Color(1, 1, 1, 0.05f), 8 * s);
            var img = Image(info.preview);
            if (img != null) GUI.DrawTexture(new Rect(r.x + 4 * s, r.y + 4 * s, r.width - 8 * s, 66 * s), img, ScaleMode.ScaleAndCrop, false, 0, Color.white, 0, 6 * s);
            GUI.Label(new Rect(r.x + 6 * s, r.y + 72 * s, r.width - 8 * s, 22 * s), info.id.ToUpperInvariant(), new GUIStyle(value) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(14 * s) });
            GUI.Label(new Rect(r.x + 6 * s, r.y + 94 * s, r.width - 8 * s, 20 * s), FormatLap(GameSession.BestLap(info.id)), new GUIStyle(small) { fontSize = Mathf.RoundToInt(12 * s) });
            if (sel) Fill(new Rect(r.x, r.yMax - 3 * s, r.width, 3 * s), Accent);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) quick.track = i;
        }
        y += 136 * s;

        if (Chip(new Rect(x, y, 180 * s, 44 * s), "QUIT GAME", false))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
    // ---------- Circuits ----------

    readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
    Texture2D Image(string path)
    {
        if (!images.TryGetValue(path, out var t)) { t = Resources.Load<Texture2D>(path); images[path] = t; }
        return t;
    }

    static string FormatLap(float t) => t <= 0f ? "--:--.---" : Mathf.FloorToInt(t / 60f) + ":" + (t % 60f).ToString("00.000");

    // Circuit chips + a preview card (render, length, corners, your record)
    void TrackPicker(ref float y, float x, float w, float s, ref int track)
    {
        int n = GameSession.Tracks.Length;
        float cw = (w - (n - 1) * 6 * s) / n;
        for (int i = 0; i < n; i++)
            if (Chip(new Rect(x + i * (cw + 6 * s), y, cw, 36 * s), GameSession.Tracks[i].id.ToUpperInvariant(), track == i)) track = i;
        y += 44 * s;
        var info = GameSession.Tracks[track];
        Rect card = new Rect(x, y, w, 230 * s);
        Fill(card, new Color(1, 1, 1, 0.05f), 10 * s);
        var img = Image(info.preview);
        if (img != null) GUI.DrawTexture(new Rect(card.x + 8 * s, card.y + 8 * s, 330 * s, 214 * s), img, ScaleMode.ScaleAndCrop, false, 0, Color.white, 0, 8 * s);
        float tx = card.x + 352 * s, tw = card.width - 360 * s;
        GUI.Label(new Rect(tx, card.y + 12 * s, tw, 30 * s), info.grandPrix.ToUpperInvariant(), new GUIStyle(value) { alignment = TextAnchor.MiddleLeft, wordWrap = true });
        GUI.Label(new Rect(tx, card.y + 46 * s, tw, 44 * s), info.name, new GUIStyle(small) { wordWrap = true });
        GUI.Label(new Rect(tx, card.y + 96 * s, tw, 24 * s), info.country, label);
        GUI.Label(new Rect(tx, card.y + 124 * s, tw, 24 * s), info.lengthKm.ToString("0.000") + " km  ·  " + info.corners + " turns", label);
        GUI.Label(new Rect(tx, card.y + 160 * s, tw, 24 * s), "YOUR RECORD", subtitle);
        GUI.Label(new Rect(tx, card.y + 186 * s, tw, 30 * s), FormatLap(GameSession.BestLap(info.id)), new GUIStyle(value) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(22 * s) });
        y += 244 * s;
    }

    void Launch(RaceSetup setup, bool career)
    {
        GameSession.Race = setup;
        GameSession.CareerMode = career;
        GameSession.StartedFromMenu = true;
        GameSession.SaveSettings();
        string scene = GameSession.RaceScene;
        if (!Application.CanStreamedLevelBeLoaded(scene)) { missingScene = scene; return; }
        SceneManager.LoadScene(scene);
    }
    string missingScene;
    // 2-column grid of team cards: colours, name, drivers, pace bar
    void TeamGrid(ref float y, float x, float w, float s, ref int team)
    {
        float cw = (w - 12 * s) / 2f, ch = 70 * s;
        for (int i = 0; i < GameSession.Teams.Length; i++)
        {
            var t = GameSession.Teams[i];
            Rect r = new Rect(x + (i % 2) * (cw + 12 * s), y + (i / 2) * (ch + 10 * s), cw, ch);
            bool sel = team == i;
            Fill(r, sel ? new Color(1, 1, 1, 0.16f) : new Color(1, 1, 1, 0.05f), 8 * s);
            if (sel) Fill(new Rect(r.x, r.yMax - 4 * s, r.width, 4 * s), Accent);
            Fill(new Rect(r.x + 10 * s, r.y + 12 * s, 14 * s, ch - 24 * s), t.primary, 3 * s);
            Fill(new Rect(r.x + 26 * s, r.y + 12 * s, 6 * s, ch - 24 * s), t.secondary, 2 * s);
            GUI.Label(new Rect(r.x + 42 * s, r.y + 6 * s, cw - 50 * s, 28 * s), t.name.ToUpperInvariant(), new GUIStyle(value) { alignment = TextAnchor.MiddleLeft });
            GUI.Label(new Rect(r.x + 42 * s, r.y + 32 * s, cw - 50 * s, 20 * s), t.drivers[0] + "  ·  " + t.drivers[1], small);
            float pace = Mathf.InverseLerp(0.95f, 1.045f, t.performance);
            Fill(new Rect(r.x + 42 * s, r.y + 55 * s, (cw - 60 * s), 5 * s), new Color(1, 1, 1, 0.1f), 2 * s);
            Fill(new Rect(r.x + 42 * s, r.y + 55 * s, (cw - 60 * s) * pace, 5 * s), Color.Lerp(new Color(1f, 0.6f, 0.2f), new Color(0.3f, 1f, 0.5f), pace), 2 * s);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) team = i;
        }
        y += Mathf.CeilToInt(GameSession.Teams.Length / 2f) * (ch + 10 * s) + 6 * s;
        GUI.Label(new Rect(x, y, w, 22 * s), "You take the second seat: you'll race alongside " + GameSession.Teams[team].drivers[0] + ".  Bar = car pace.", small);
        y += 32 * s;
    }

    void DifficultyPicker(ref float y, float x, float w, float s, ref int difficulty)
    {
        string[] desc = { "Relaxed rivals, learn the track", "A fair fight", "Fast, aggressive rivals" };
        float cw = (w - 16 * s) / 3f;
        for (int i = 0; i < 3; i++)
        {
            Rect r = new Rect(x + i * (cw + 8 * s), y, cw, 74 * s);
            bool sel = difficulty == i;
            Fill(r, sel ? Accent : new Color(1, 1, 1, 0.06f), 8 * s);
            GUI.Label(new Rect(r.x, r.y + 8 * s, r.width, 30 * s), GameSession.DifficultyNames[i].ToUpperInvariant(), new GUIStyle(value) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(20 * s) });
            GUI.Label(new Rect(r.x + 8 * s, r.y + 38 * s, r.width - 16 * s, 30 * s), desc[i], new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = true });
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) difficulty = i;
        }
        y += 84 * s;
    }

    void TyreChips(ref float y, float x, float w, float s, ref int compound)
    {
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Starting tyres", label);
        float cw = (w * 0.65f - 12 * s) / 3f;
        for (int i = 0; i < 3; i++)
        {
            Rect r = new Rect(x + w * 0.35f + i * (cw + 6 * s), y, cw, 34 * s);
            bool sel = compound == i;
            Fill(r, sel ? Tyres.Colors[i] : new Color(1, 1, 1, 0.06f), 17 * s);
            var st = new GUIStyle(value) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15 * s) };
            st.normal.textColor = sel ? Color.black : Tyres.Colors[i];
            GUI.Label(r, Tyres.Names[i].ToUpperInvariant(), st);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) compound = i;
        }
        y += 40 * s;
    }

    void Back(float y, float x, float s)
    {
        if (Chip(new Rect(x, y, 140 * s, 44 * s), "< BACK", false)) { GameSession.SaveSettings(); page = Page.Main; confirmDelete = false; }
    }

    void DrawQuickRace(float x, float y, float w, float s)
    {
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "QUICK RACE", header);
        y += 64 * s;
        Section(ref y, x, w, s, "1. Circuit");
        TrackPicker(ref y, x, w, s, ref quick.track);
        Section(ref y, x, w, s, "2. Your team");
        TeamGrid(ref y, x, w, s, ref quick.team);
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Driver name", label);
        quick.driverName = GUI.TextField(new Rect(x + w * 0.35f, y, w * 0.65f, 34 * s), quick.driverName, 20, field);
        y += 50 * s;

        Section(ref y, x, w, s, "3. AI level");
        DifficultyPicker(ref y, x, w, s, ref quick.difficulty);

        Section(ref y, x, w, s, "4. Race");
        int[] lapOptions = { 3, 5, 10, 15, 20 };
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Laps", label);
        float lw = (w * 0.65f - 24 * s) / lapOptions.Length;
        for (int i = 0; i < lapOptions.Length; i++)
            if (Chip(new Rect(x + w * 0.35f + i * (lw + 6 * s), y, lw, 34 * s), lapOptions[i].ToString(), quick.laps == lapOptions[i])) quick.laps = lapOptions[i];
        y += 40 * s;
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Opponents", label);
        quick.opponents = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + w * 0.35f, y + 12 * s, w * 0.52f, 20 * s), quick.opponents, 1, GameSession.Teams.Length * 2 - 1));
        GUI.Label(new Rect(x, y, w, 34 * s), quick.opponents.ToString(), value);
        y += 40 * s;
        Choice(ref y, x, w, s, "Start from", ref quick.grid, new[] { "Pole", "Random", "Back", "Pace" });
        TyreChips(ref y, x, w, s, ref quick.startCompound);

        Section(ref y, x, w, s, "5. Assists");
        var st = GameSession.Settings;
        Toggle(ref y, x, w, s, "Braking markers (green / yellow / red)", ref st.brakingLine);
        Toggle(ref y, x, w, s, "Automatic DRS", ref st.drsAssist);
        Toggle(ref y, x, w, s, "Steering & stability assist", ref st.steeringAssist);
        Toggle(ref y, x, w, s, "ABS", ref st.abs);
        Toggle(ref y, x, w, s, "Traction control", ref st.tractionControl);
        Toggle(ref y, x, w, s, "Automatic gears", ref st.autoGearbox);
        Toggle(ref y, x, w, s, "Team radio", ref st.teamRadio);
        y += 16 * s;
        if (GUI.Button(new Rect(x, y, w, 70 * s), "START RACE  >", bigButton))
        {
            quick.driverCode = MakeCode(quick.driverName);
            quick.timeTrial = false;
            Launch(quick, false);
        }
        if (missingScene != null) GUI.Label(new Rect(x, y + 76 * s, w, 26 * s), "Scene \"" + missingScene + "\" is not built yet (Tools > XPARROW).", small);
    }

    void DrawTimeTrial(float x, float y, float w, float s)
    {
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "TIME TRIAL", header);
        y += 64 * s;
        GUI.Label(new Rect(x, y, w, 44 * s), "No rivals, no tyre rule. Every lap is timed; your best lap per circuit is saved.\nPit lane, DRS and all assists work as in a race.", small);
        y += 56 * s;
        Section(ref y, x, w, s, "1. Circuit");
        TrackPicker(ref y, x, w, s, ref quick.track);
        Section(ref y, x, w, s, "2. Car");
        TeamGrid(ref y, x, w, s, ref quick.team);
        Section(ref y, x, w, s, "3. Session");
        int[] lapOptions = { 3, 5, 10, 20 };
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Laps", label);
        float lw = (w * 0.65f - 18 * s) / lapOptions.Length;
        for (int i = 0; i < lapOptions.Length; i++)
            if (Chip(new Rect(x + w * 0.35f + i * (lw + 6 * s), y, lw, 34 * s), lapOptions[i].ToString(), quick.laps == lapOptions[i])) quick.laps = lapOptions[i];
        y += 40 * s;
        TyreChips(ref y, x, w, s, ref quick.startCompound);
        var st = GameSession.Settings;
        Toggle(ref y, x, w, s, "Braking markers", ref st.brakingLine);
        Toggle(ref y, x, w, s, "Steering & stability assist", ref st.steeringAssist);
        Toggle(ref y, x, w, s, "ABS", ref st.abs);
        Toggle(ref y, x, w, s, "Traction control", ref st.tractionControl);
        y += 16 * s;
        if (GUI.Button(new Rect(x, y, w, 70 * s), "START TIME TRIAL  >", bigButton))
        {
            quick.driverCode = MakeCode(quick.driverName);
            quick.timeTrial = true;
            quick.grid = 0;
            Launch(quick, false);
        }
        if (missingScene != null) GUI.Label(new Rect(x, y + 76 * s, w, 26 * s), "Scene \"" + missingScene + "\" is not built yet (Tools > XPARROW).", small);
    }

    void DrawGarage(float x, float y, float w, float s)
    {
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "GARAGE", header);
        y += 64 * s;
        GUI.Label(new Rect(x, y, w, 24 * s), "Pick a team to put its 2026 car on the turntable.", small);
        y += 34 * s;
        TeamGrid(ref y, x, w, s, ref quick.team);
        var t = GameSession.Teams[quick.team];
        Section(ref y, x, w, s, t.name + " 2026");
        Stat(ref y, x, w, s, "Drivers", t.drivers[0] + "  ·  " + t.drivers[1]);
        Stat(ref y, x, w, s, "Car pace", Mathf.RoundToInt(Mathf.InverseLerp(0.95f, 1.045f, t.performance) * 100f) + " / 100");
        Stat(ref y, x, w, s, "Power unit", "1.6 V6 turbo hybrid, ~50% electric");
        Stat(ref y, x, w, s, "Minimum mass", "768 kg + driver");
        Stat(ref y, x, w, s, "Aero", "Active aero: DRS flap in the zones (F / Y)");
        Stat(ref y, x, w, s, "Gearbox", "8-speed seamless shift");
        var img = Image("F1UI/Team_" + t.livery.ToString("00") + "_" + LiveryFile(t.livery));
        if (img != null) { y += 8 * s; GUI.DrawTexture(new Rect(x, y, w, w * 9f / 16f), img, ScaleMode.ScaleAndCrop, false, 0, Color.white, 0, 10 * s); }
    }

    static string LiveryFile(int i)
    {
        string[] n = { "Concept2026", "Ferrari", "RedBull", "McLaren", "Mercedes", "AstonMartin", "Alpine", "Williams", "RacingBulls", "Haas", "Audi", "Cadillac" };
        return n[Mathf.Clamp(i, 0, n.Length - 1)];
    }

    void DrawRecords(float x, float y, float w, float s)
    {
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "RECORDS", header);
        y += 70 * s;
        foreach (var info in GameSession.Tracks)
        {
            Rect r = new Rect(x, y, w, 96 * s);
            Fill(r, new Color(1, 1, 1, 0.05f), 8 * s);
            var img = Image(info.preview);
            if (img != null) GUI.DrawTexture(new Rect(r.x + 6 * s, r.y + 6 * s, 140 * s, 84 * s), img, ScaleMode.ScaleAndCrop, false, 0, Color.white, 0, 6 * s);
            GUI.Label(new Rect(r.x + 160 * s, r.y + 10 * s, w - 170 * s, 30 * s), info.grandPrix.ToUpperInvariant(), new GUIStyle(value) { alignment = TextAnchor.MiddleLeft });
            GUI.Label(new Rect(r.x + 160 * s, r.y + 42 * s, w - 170 * s, 24 * s), info.name + "  ·  " + info.lengthKm.ToString("0.000") + " km", small);
            GUI.Label(new Rect(r.x, r.y + 30 * s, w - 20 * s, 40 * s), FormatLap(GameSession.BestLap(info.id)), new GUIStyle(value) { fontSize = Mathf.RoundToInt(26 * s) });
            y += 106 * s;
        }
    }

    void DrawCredits(float x, float y, float w, float s)
    {
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "CREDITS", header);
        y += 70 * s;
        void Line(string head, string text)
        {
            GUI.Label(new Rect(x, y, w, 26 * s), head.ToUpperInvariant(), subtitle);
            var st = new GUIStyle(label) { wordWrap = true };
            float hgt = st.CalcHeight(new GUIContent(text), w);
            GUI.Label(new Rect(x, y + 28 * s, w, hgt), text, st);
            y += 40 * s + hgt;
        }
        Line(GameSession.GameTitle, "A Formula 1 style racing game made in Unity (URP).");
        Line("Circuits", "Monaco, Baku, Monza, Spa-Francorchamps and Suzuka from the XPARROW circuit asset pack, built in Blender.");
        Line("Map data", "\u00a9 OpenStreetMap contributors. Available under the Open Database Licence (ODbL), openstreetmap.org/copyright.");
        Line("Elevation", "Copernicus GLO-90 Digital Elevation Model (European Union / ESA Copernicus programme), via Open-Meteo.");
        Line("Textures", "Track surface and terrain materials from Poly Haven (CC0), plus generated trackside textures.");
        Line("Cars", "2026-regulation F1 car model with eleven team liveries, tyre and pit-prop pack (XPARROW asset pack).");
        Line("Physics & AI", "Raycast suspension, slip-angle tyre model, aero downforce and drag, 8-speed gearbox, ERS, DRS, pit strategy AI.");
        Line("Notice", "Team and driver names are used for identification only. This game is not affiliated with Formula 1, the FIA or any team.");
    }

    void DrawCareerNew(float x, float y, float w, float s)
    {
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "NEW CAREER", header);
        y += 64 * s;
        GUI.Label(new Rect(x, y, w, 44 * s), "Join any team for a 5-round world tour: Monaco, Baku, Monza, Spa, Suzuka.\nScore points, spend R&D on upgrades and earn offers from the top teams.", small);
        y += 56 * s;
        Section(ref y, x, w, s, "1. Driver");
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Name", label);
        newName = GUI.TextField(new Rect(x + w * 0.35f, y, w * 0.65f, 34 * s), newName, 20, field);
        y += 42 * s;
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Race number", label);
        newNumber = GUI.TextField(new Rect(x + w * 0.35f, y, 100 * s, 34 * s), newNumber, 2, field);
        y += 50 * s;
        Section(ref y, x, w, s, "2. Team");
        TeamGrid(ref y, x, w, s, ref newTeam);
        Section(ref y, x, w, s, "3. AI level");
        var st = GameSession.Settings;
        DifficultyPicker(ref y, x, w, s, ref st.difficulty);
        y += 10 * s;
        if (GUI.Button(new Rect(x, y, w, 70 * s), "START CAREER  >", bigButton))
        {
            int.TryParse(newNumber, out int num);
            GameSession.SaveSettings();
            GameSession.NewCareer(newName.Trim().Length > 0 ? newName.Trim() : "Rookie", MakeCode(newName), Mathf.Clamp(num, 1, 99), newTeam);
            page = Page.CareerHub;
        }
    }

    void DrawCareerHub(float x, float y, float w, float s)
    {
        var c = GameSession.Career;
        if (c == null) { page = Page.CareerNew; return; }
        var team = GameSession.Teams[c.team];
        var table = GameSession.SortedStandings();
        int champPos = table.FindIndex(d => d.isPlayer) + 1;
        var me = table[champPos - 1];

        Back(y, x, s);
        y += 56 * s;
        Fill(new Rect(x, y, w, 110 * s), new Color(1, 1, 1, 0.05f), 10 * s);
        Fill(new Rect(x, y, 10 * s, 110 * s), team.primary, 4 * s);
        Fill(new Rect(x + 10 * s, y, 5 * s, 110 * s), team.secondary);
        GUI.Label(new Rect(x + 30 * s, y + 8 * s, w, 44 * s), c.driverName.ToUpperInvariant() + "  #" + c.number, header);
        GUI.Label(new Rect(x + 32 * s, y + 52 * s, w, 24 * s), team.name + "  ·  teammate " + team.drivers[0], label);
        GUI.Label(new Rect(x + 32 * s, y + 78 * s, w - 50 * s, 24 * s), "Season " + c.season + "  ·  " + (c.seasonOver ? "season complete" : "round " + c.round + " / " + c.roundsPerSeason)
                  + "  ·  P" + champPos + " (" + me.points + " pts)  ·  R&D " + c.rdPoints, new GUIStyle(value) { alignment = TextAnchor.MiddleLeft });
        y += 126 * s;

        string[] tabs = { "OVERVIEW", "STANDINGS", "UPGRADES" };
        float tw = (w - 12 * s) / 3f;
        for (int i = 0; i < 3; i++)
            if (Chip(new Rect(x + i * (tw + 6 * s), y, tw, 40 * s), tabs[i], hubTab == i)) hubTab = i;
        y += 54 * s;

        float listH = Screen.height - y - 210 * s;
        if (c.seasonOver) DrawSeasonEnd(x, y, w, s, c);
        else if (hubTab == 0) DrawOverview(x, y, w, s, c);
        else if (hubTab == 1) DrawStandings(x, y, w, s, table, listH);
        else DrawDevelopment(x, y, w, s, c);

        float by = Screen.height - 150 * s;
        if (!c.seasonOver && GUI.Button(new Rect(x, by, w, 66 * s), "RACE  ·  ROUND " + c.round + "  ·  " + GameSession.Tracks[GameSession.CareerTrackIndex].id.ToUpperInvariant() + "  >", bigButton))
            Launch(GameSession.CareerRaceSetup(), true);
        if (missingScene != null) GUI.Label(new Rect(x + 220 * s, by + 74 * s, w, 26 * s), "Scene not built: " + missingScene, small);
        if (!confirmDelete)
        {
            if (GUI.Button(new Rect(x, by + 74 * s, 200 * s, 26 * s), "Delete career", small)) confirmDelete = true;
        }
        else
        {
            GUI.Label(new Rect(x, by + 74 * s, 200 * s, 26 * s), "Delete this career?", small);
            if (GUI.Button(new Rect(x + 200 * s, by + 74 * s, 120 * s, 26 * s), "Yes, delete", small)) { GameSession.DeleteCareer(); confirmDelete = false; page = Page.Main; }
            if (GUI.Button(new Rect(x + 330 * s, by + 74 * s, 80 * s, 26 * s), "Cancel", small)) confirmDelete = false;
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
        Stat(ref y, x, w, s, "Next race", GameSession.Tracks[GameSession.CareerTrackIndex].grandPrix);
        Stat(ref y, x, w, s, "Race distance", GameSession.Settings.careerLaps + " laps");
        Stat(ref y, x, w, s, "AI level", GameSession.DifficultyNames[GameSession.Settings.difficulty]);
        y += 10 * s;
        Stat(ref y, x, w, s, "Starts", c.races.ToString());
        Stat(ref y, x, w, s, "Wins  ·  podiums  ·  poles", c.wins + "  ·  " + c.podiums + "  ·  " + c.poles);
        Stat(ref y, x, w, s, "Career points", c.careerPoints.ToString());
        Stat(ref y, x, w, s, "Best finish", c.bestFinish < 99 ? "P" + c.bestFinish : "—");
        Stat(ref y, x, w, s, "Championships", c.championships.ToString());
        // Calendar strip
        y += 6 * s;
        float cw = (w - 4 * 6 * s) / 5f;
        for (int i = 0; i < GameSession.Calendar.Length; i++)
        {
            var tr = GameSession.Tracks[GameSession.Calendar[i]];
            Rect r = new Rect(x + i * (cw + 6 * s), y, cw, 40 * s);
            bool done = i + 1 < c.round, next = i + 1 == c.round;
            Fill(r, next ? Accent : done ? new Color(1, 1, 1, 0.16f) : new Color(1, 1, 1, 0.05f), 6 * s);
            GUI.Label(r, "R" + (i + 1) + " " + tr.id.ToUpperInvariant(), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
        }
        y += 48 * s;
        if (c.lastResult.Count > 0)
        {
            int myPos = c.lastResult.IndexOf(c.driverName) + 1;
            GUI.Label(new Rect(x, y + 10 * s, w, 30 * s), "Last race: won by " + c.lastResult[0] + (myPos > 0 ? "   ·   you were P" + myPos : ""), small);
        }
    }

    void DrawStandings(float x, float y, float w, float s, List<DriverStanding> table, float height)
    {
        float rowH = 26 * s;
        var nameSt = new GUIStyle(label) { fontSize = Mathf.RoundToInt(16 * s) };
        var valSt = new GUIStyle(value) { fontSize = Mathf.RoundToInt(16 * s) };
        listScroll = GUI.BeginScrollView(new Rect(x, y, w, height), listScroll, new Rect(0, 0, w - 20 * s, rowH * table.Count));
        for (int i = 0; i < table.Count; i++)
        {
            var d = table[i];
            float ry = i * rowH;
            if (d.isPlayer) Fill(new Rect(0, ry, w - 20 * s, rowH - 2 * s), new Color(Accent.r, Accent.g, Accent.b, 0.35f));
            Fill(new Rect(38 * s, ry + 5 * s, 4 * s, rowH - 10 * s), GameSession.Teams[d.team].primary);
            GUI.Label(new Rect(6 * s, ry, 30 * s, rowH), (i + 1).ToString(), nameSt);
            GUI.Label(new Rect(50 * s, ry, 400 * s, rowH), d.name + "   " + GameSession.Teams[d.team].name, nameSt);
            GUI.Label(new Rect(0, ry, w - 30 * s, rowH), d.points + " pts", valSt);
        }
        GUI.EndScrollView();
    }

    void DrawDevelopment(float x, float y, float w, float s, CareerData c)
    {
        GUI.Label(new Rect(x, y, w, 44 * s), "Spend R&D points earned in races. Each level costs more.\nUpgrades reset if you change team.", small);
        y += 56 * s;
        Upgrade(ref y, x, w, s, "Power unit  (+2% power)", ref c.engine, c);
        Upgrade(ref y, x, w, s, "Aerodynamics  (+2.5% downforce)", ref c.aero, c);
        Upgrade(ref y, x, w, s, "Chassis  (+1.2% grip)", ref c.chassis, c);
    }

    void Upgrade(ref float y, float x, float w, float s, string name, ref int level, CareerData c)
    {
        int cost = 2 + level;
        GUI.Label(new Rect(x, y, w * 0.6f, 32 * s), name, label);
        for (int i = 0; i < GameSession.MaxUpgrade; i++)
            Fill(new Rect(x + i * 26 * s, y + 36 * s, 20 * s, 10 * s), i < level ? Accent : new Color(1, 1, 1, 0.15f), 3 * s);
        GUI.enabled = level < GameSession.MaxUpgrade && c.rdPoints >= cost;
        if (Chip(new Rect(x + w - 190 * s, y, 190 * s, 44 * s), level >= GameSession.MaxUpgrade ? "MAXED" : "UPGRADE (" + cost + ")", false))
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
        GUI.Label(new Rect(x, y, w, 30 * s), c.offers.Count == 0 ? "No offers this year. Finish higher to attract faster teams." : "Contract offers for next season:", label);
        y += 40 * s;
        foreach (int t in c.offers)
        {
            if (Chip(new Rect(x, y, w, 48 * s), "SIGN FOR  " + GameSession.Teams[t].name.ToUpperInvariant(), false)) GameSession.StartNextSeason(t);
            y += 56 * s;
        }
        if (Chip(new Rect(x, y, w, 48 * s), "STAY WITH  " + GameSession.Teams[c.team].name.ToUpperInvariant(), true)) GameSession.StartNextSeason(c.team);
    }

    void DrawSettings(float x, float y, float w, float s)
    {
        var st = GameSession.Settings;
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "SETTINGS", header);
        y += 64 * s;
        Section(ref y, x, w, s, "Graphics");
        string[] q = QualitySettings.names;
        int qi = Mathf.Clamp(st.quality < 0 ? QualitySettings.GetQualityLevel() : st.quality, 0, q.Length - 1);
        GUI.Label(new Rect(x, y, w * 0.35f, 34 * s), "Quality", label);
        float qw = (w * 0.65f - (q.Length - 1) * 6 * s) / q.Length;
        for (int i = 0; i < q.Length; i++)
            if (Chip(new Rect(x + w * 0.35f + i * (qw + 6 * s), y, qw, 34 * s), q[i], qi == i)) qi = i;
        st.quality = qi;
        y += 40 * s;
        Toggle(ref y, x, w, s, "Fullscreen", ref st.fullscreen);
        Toggle(ref y, x, w, s, "V-Sync", ref st.vsync);
        GUI.Label(new Rect(x, y, w * 0.45f, 34 * s), "Render scale (lower = faster)", label);
        st.renderScale = Mathf.Round(GUI.HorizontalSlider(new Rect(x + w * 0.45f, y + 12 * s, w * 0.42f, 20 * s), st.renderScale, 0.5f, 1f) * 20f) / 20f;
        GUI.Label(new Rect(x, y, w, 34 * s), Mathf.RoundToInt(st.renderScale * 100) + "%", value);
        y += 40 * s;
        Toggle(ref y, x, w, s, "Show FPS", ref st.showFps);
        Section(ref y, x, w, s, "Sound");
        FloatSlider(ref y, x, w, s, "Master volume", ref st.masterVolume);
        FloatSlider(ref y, x, w, s, "Engine volume", ref st.engineVolume);
        Choice(ref y, x, w, s, "Engine sound", ref st.engineSound, EngineAudio.EngineNames);
        Toggle(ref y, x, w, s, "Team radio", ref st.teamRadio);
        Section(ref y, x, w, s, "Display");
        Choice(ref y, x, w, s, "Speed", ref UnitIndex(st), new[] { "km/h", "mph" });
        Choice(ref y, x, w, s, "Camera", ref st.defaultCamera, new[] { "Chase", "T-cam", "Cockpit", "Far" });
        Toggle(ref y, x, w, s, "Driver name tags", ref st.nameTags);
        Toggle(ref y, x, w, s, "Minimap", ref st.showMinimap);
        Toggle(ref y, x, w, s, "Camera shake on impacts", ref st.cameraShake);
        GUI.Label(new Rect(x, y, w * 0.45f, 34 * s), "Field of view", label);
        st.fieldOfView = Mathf.Round(GUI.HorizontalSlider(new Rect(x + w * 0.45f, y + 12 * s, w * 0.42f, 20 * s), st.fieldOfView, 45f, 85f));
        GUI.Label(new Rect(x, y, w, 34 * s), Mathf.RoundToInt(st.fieldOfView) + "°", value);
        y += 40 * s;
        Section(ref y, x, w, s, "Career");
        Choice(ref y, x, w, s, "AI level", ref st.difficulty, GameSession.DifficultyNames);
        Choice(ref y, x, w, s, "Race laps", ref LapsIndex(st), new[] { "3", "5", "10", "15", "20" });
        Section(ref y, x, w, s, "Assists");
        Toggle(ref y, x, w, s, "Braking markers", ref st.brakingLine);
        Toggle(ref y, x, w, s, "Steering & stability assist", ref st.steeringAssist);
        Toggle(ref y, x, w, s, "ABS", ref st.abs);
        Toggle(ref y, x, w, s, "Traction control", ref st.tractionControl);
        Toggle(ref y, x, w, s, "Automatic gears", ref st.autoGearbox);
        Toggle(ref y, x, w, s, "Automatic DRS", ref st.drsAssist);
        st.useMph = unitIndex == 1;
        st.careerLaps = new[] { 3, 5, 10, 15, 20 }[lapsIndex];
        y += 10 * s;
        if (GUI.Button(new Rect(x, y, w, 60 * s), "SAVE SETTINGS", bigButton))
        {
            GameSession.SaveSettings();
            GameSession.ApplySettings();
            page = Page.Main;
        }
    }

    int unitIndex = -1, lapsIndex = -1;
    ref int UnitIndex(GameSettings st) { if (unitIndex < 0) unitIndex = st.useMph ? 1 : 0; return ref unitIndex; }
    ref int LapsIndex(GameSettings st)
    {
        if (lapsIndex < 0) { int[] opts = { 3, 5, 10, 15, 20 }; lapsIndex = System.Array.IndexOf(opts, st.careerLaps); if (lapsIndex < 0) lapsIndex = 2; }
        return ref lapsIndex;
    }

    void DrawControls(float x, float y, float w, float s)
    {
        Back(y, x, s);
        GUI.Label(new Rect(x + 160 * s, y, w, 44 * s), "CONTROLS", header);
        y += 70 * s;
        string[,] rows =
        {
            { "Throttle", "W / Up", "Right trigger" }, { "Brake (hold when stopped: reverse)", "S / Down", "Left trigger" },
            { "Steer", "A D / Left Right", "Left stick" }, { "Gear up / down (manual)", "E / Q", "RB / LB" },
            { "ERS boost", "Left Shift", "X" }, { "DRS (in a DRS zone)", "F", "Y" }, { "Camera: chase / T-cam / cockpit / far", "C", "-" },
            { "Request pit stop", "B", "-" }, { "Pit tyres: Soft / Medium / Hard", "1 / 2 / 3", "-" },
            { "Back on track", "R", "-" }, { "Pause", "Esc / P", "Start" }, { "Show help", "H", "-" }, { "Results after the flag", "Tab", "-" }
        };
        var head = new GUIStyle(value) { alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(x + w * 0.52f, y, 150 * s, 30 * s), "Keyboard", head);
        GUI.Label(new Rect(x + w * 0.78f, y, 150 * s, 30 * s), "Gamepad", head);
        y += 36 * s;
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            if (i % 2 == 0) Fill(new Rect(x - 8 * s, y, w + 16 * s, 32 * s), new Color(1, 1, 1, 0.04f), 4 * s);
            GUI.Label(new Rect(x, y, w * 0.52f, 32 * s), rows[i, 0], label);
            GUI.Label(new Rect(x + w * 0.52f, y, w * 0.26f, 32 * s), rows[i, 1], new GUIStyle(label) { fontStyle = FontStyle.Bold });
            GUI.Label(new Rect(x + w * 0.78f, y, w * 0.22f, 32 * s), rows[i, 2], label);
            y += 34 * s;
        }
    }

    static string MakeCode(string name)
    {
        string[] parts = name.Trim().Split(' ');
        string s = parts[parts.Length - 1].ToUpperInvariant();
        if (s.Length < 3) s = (s + "XXX").Substring(0, 3);
        return s.Substring(0, 3);
    }
}
