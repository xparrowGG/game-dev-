using System;
using System.Collections.Generic;
using UnityEngine;

// Settings, team/driver roster, quick race setup and career save data shared by the menu and race scenes.
// Everything persists in PlayerPrefs as JSON.

[Serializable]
public class GameSettings
{
    public int quality = -1;              // -1 = leave the project default
    public bool fullscreen = true;
    public bool vsync = true;
    public float masterVolume = 0.8f;
    public float engineVolume = 0.7f;
    public bool useMph;
    public int defaultCamera;             // 0 TV pod, 1 chase, 2 cockpit
    public bool abs = true, tractionControl = true, steeringAssist = true, autoGearbox = true;
    public bool brakingLine = true;       // green / yellow / red braking markers on the track
    public bool teamRadio = true;         // race engineer messages
    public bool nameTags = true;          // driver names above the cars
    public int difficulty = 1;            // index into GameSession.DifficultyNames
    public int careerLaps = 10;
}

[Serializable]
public class RaceSetup
{
    public int laps = 10;
    public int opponents = 19;
    public int difficulty = 1;
    public int grid;                      // 0 pole, 1 random, 2 back of the grid, 3 qualifying (by car pace)
    public int startCompound = 1;         // 0 soft, 1 medium, 2 hard
    public int team;
    public string driverName = "Player";
    public string driverCode = "YOU";
    public int number = 7;
}

[Serializable]
public class DriverStanding
{
    public string name, code;
    public int team, points, wins, podiums;
    public bool isPlayer;
}

[Serializable]
public class CareerData
{
    public string driverName = "Player";
    public string code = "PLY";
    public int number = 7;
    public int team = 8;
    public int season = 1, round = 1, roundsPerSeason = 10;
    public int races, wins, podiums, poles, careerPoints, bestFinish = 99, championships;
    public int rdPoints;                  // research & development points to spend on upgrades
    public int engine, aero, chassis;     // upgrade levels 0..5
    public List<DriverStanding> standings = new List<DriverStanding>();
    public List<string> lastResult = new List<string>();
    public bool seasonOver;
    public int lastSeasonPosition;
    public List<int> offers = new List<int>();
}

public class TeamInfo
{
    public string name;
    public Color primary, secondary;
    public float performance;             // car pace multiplier, ~0.95 .. 1.04
    public string[] drivers, codes;
}

public static class GameSession
{
    public const string GameTitle = "XPARROW F1 GP";
    public const string MenuScene = "Main Menu";
    public const string RaceScene = "Monaco GP";
    public const string TrackName = "Monte Carlo Street Circuit";
    public static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
    public static readonly string[] DifficultyNames = { "Easy", "Medium", "Hard" };
    public static readonly float[] DifficultySkill = { 0.88f, 0.96f, 1.03f };
    public static readonly string[] GridNames = { "Pole position", "Random", "Back of the grid", "Qualifying (car pace)" };
    public const int MaxUpgrade = 5;
    public static bool StartedFromMenu;   // the race scene sends you to the menu if opened directly

    // The 2026 grid (ten teams, two race drivers each). Colours are team-colour schemes only:
    // no sponsor logos or official livery artwork. Edit here when line-ups change.
    public static readonly TeamInfo[] Teams =
    {
        new TeamInfo { name = "McLaren",         primary = Hex("FF8000"), secondary = Hex("1B1B1F"), performance = 1.040f, drivers = new[] { "Lando Norris", "Oscar Piastri" },        codes = new[] { "NOR", "PIA" } },
        new TeamInfo { name = "Red Bull Racing", primary = Hex("1E2A4A"), secondary = Hex("DC1E28"), performance = 1.034f, drivers = new[] { "Max Verstappen", "Isack Hadjar" },      codes = new[] { "VER", "HAD" } },
        new TeamInfo { name = "Mercedes",        primary = Hex("A9B1B8"), secondary = Hex("00A19C"), performance = 1.030f, drivers = new[] { "George Russell", "Kimi Antonelli" },    codes = new[] { "RUS", "ANT" } },
        new TeamInfo { name = "Ferrari",         primary = Hex("DC0000"), secondary = Hex("FFF200"), performance = 1.028f, drivers = new[] { "Charles Leclerc", "Lewis Hamilton" },    codes = new[] { "LEC", "HAM" } },
        new TeamInfo { name = "Williams",        primary = Hex("0B3C8C"), secondary = Hex("00A0E9"), performance = 0.995f, drivers = new[] { "Alexander Albon", "Carlos Sainz" },     codes = new[] { "ALB", "SAI" } },
        new TeamInfo { name = "Aston Martin",    primary = Hex("00594F"), secondary = Hex("CEDC00"), performance = 0.990f, drivers = new[] { "Fernando Alonso", "Lance Stroll" },     codes = new[] { "ALO", "STR" } },
        new TeamInfo { name = "Racing Bulls",    primary = Hex("F2F4F7"), secondary = Hex("1634CB"), performance = 0.985f, drivers = new[] { "Liam Lawson", "Arvid Lindblad" },      codes = new[] { "LAW", "LIN" } },
        new TeamInfo { name = "Haas",            primary = Hex("EDEDED"), secondary = Hex("DA291C"), performance = 0.978f, drivers = new[] { "Esteban Ocon", "Oliver Bearman" },      codes = new[] { "OCO", "BEA" } },
        new TeamInfo { name = "Alpine",          primary = Hex("0078C1"), secondary = Hex("FF87BC"), performance = 0.972f, drivers = new[] { "Pierre Gasly", "Franco Colapinto" },   codes = new[] { "GAS", "COL" } },
        new TeamInfo { name = "Audi",            primary = Hex("3A3D40"), secondary = Hex("E1251B"), performance = 0.968f, drivers = new[] { "Nico Hulkenberg", "Gabriel Bortoleto" }, codes = new[] { "HUL", "BOR" } },
    };

    public static bool CareerMode;
    public static bool OpenCareerHub;

    static GameSettings settings;
    static RaceSetup race;
    static CareerData career;
    static bool careerLoaded;

    public static GameSettings Settings
    {
        get
        {
            if (settings == null)
            {
                string json = PlayerPrefs.GetString("gp_settings", "");
                settings = string.IsNullOrEmpty(json) ? new GameSettings() : JsonUtility.FromJson<GameSettings>(json);
                settings.difficulty = Mathf.Clamp(settings.difficulty, 0, DifficultyNames.Length - 1);
            }
            return settings;
        }
    }

    public static RaceSetup Race
    {
        get { return race ?? (race = new RaceSetup { laps = Settings.careerLaps, difficulty = Settings.difficulty }); }
        set { race = value; }
    }

    public static CareerData Career
    {
        get
        {
            if (!careerLoaded)
            {
                careerLoaded = true;
                string json = PlayerPrefs.GetString("gp_career", "");
                career = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<CareerData>(json);
            }
            return career;
        }
    }

    public static bool HasCareer => Career != null;

    public static void SaveSettings()
    {
        PlayerPrefs.SetString("gp_settings", JsonUtility.ToJson(Settings));
        PlayerPrefs.Save();
    }

    public static void ApplySettings()
    {
        var s = Settings;
        if (s.quality >= 0 && s.quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(s.quality, true);
        QualitySettings.vSyncCount = s.vsync ? 1 : 0;
        if (Screen.fullScreen != s.fullscreen) Screen.fullScreen = s.fullscreen;
        AudioListener.volume = s.masterVolume;
    }

    public static void SaveCareer()
    {
        if (career == null) return;
        PlayerPrefs.SetString("gp_career", JsonUtility.ToJson(career));
        PlayerPrefs.Save();
    }

    public static void DeleteCareer()
    {
        career = null;
        careerLoaded = true;
        PlayerPrefs.DeleteKey("gp_career");
        PlayerPrefs.Save();
    }

    public static CareerData NewCareer(string name, string code, int number, int team)
    {
        career = new CareerData { driverName = name, code = code, number = number, team = team };
        careerLoaded = true;
        ResetStandings(career);
        SaveCareer();
        return career;
    }

    static void ResetStandings(CareerData c)
    {
        c.standings.Clear();
        foreach (var d in Grid(c.team, c.driverName, c.code))
            c.standings.Add(new DriverStanding { name = d.name, code = d.code, team = d.team, isPlayer = d.isPlayer });
    }

    public struct GridDriver { public string name, code; public int team; public bool isPlayer; public float skill; }

    // The 20-car field: the player takes the second seat of their team.
    public static List<GridDriver> Grid(int playerTeam, string playerName, string playerCode)
    {
        var list = new List<GridDriver>();
        for (int t = 0; t < Teams.Length; t++)
            for (int k = 0; k < 2; k++)
            {
                bool isPlayer = t == playerTeam && k == 1;
                list.Add(new GridDriver
                {
                    name = isPlayer ? playerName : Teams[t].drivers[k],
                    code = isPlayer ? playerCode : Teams[t].codes[k],
                    team = t, isPlayer = isPlayer,
                    skill = isPlayer ? 1f : DriverSkill(Teams[t].drivers[k])
                });
            }
        return list;
    }

    // Stable per-driver talent between 0.975 and 1.025
    public static float DriverSkill(string name)
    {
        int h = 17;
        foreach (char ch in name) h = h * 31 + ch;
        return 0.975f + (Mathf.Abs(h) % 1000) / 1000f * 0.05f;
    }

    public static float UpgradeFactor(int level, float perLevel) => 1f + level * perLevel;

    // Record a finished race (finishing order by driver name). Returns points the player scored.
    public static int RecordCareerRace(List<string> order, bool playerDnf)
    {
        var c = Career;
        if (c == null) return 0;
        int playerPoints = 0, playerPos = 99;
        for (int i = 0; i < order.Count; i++)
        {
            var st = c.standings.Find(s => s.name == order[i]);
            if (st == null) continue;
            bool dnf = st.isPlayer && playerDnf;
            int pts = !dnf && i < Points.Length ? Points[i] : 0;
            st.points += pts;
            if (!dnf && i == 0) st.wins++;
            if (!dnf && i < 3) st.podiums++;
            if (st.isPlayer) { playerPoints = pts; playerPos = dnf ? 99 : i + 1; }
        }
        c.races++;
        c.careerPoints += playerPoints;
        if (playerPos == 1) c.wins++;
        if (playerPos <= 3) c.podiums++;
        c.bestFinish = Mathf.Min(c.bestFinish, playerPos);
        c.rdPoints += 1 + playerPoints / 6 + (playerPos <= 10 ? 1 : 0);
        c.lastResult = new List<string>(order);
        c.round++;
        if (c.round > c.roundsPerSeason)
        {
            c.seasonOver = true;
            var table = SortedStandings();
            c.lastSeasonPosition = table.FindIndex(s => s.isPlayer) + 1;
            if (c.lastSeasonPosition == 1) c.championships++;
            // Contract offers: teams faster than the current one, more of them for a strong season
            c.offers.Clear();
            int reach = c.lastSeasonPosition <= 3 ? 4 : c.lastSeasonPosition <= 8 ? 2 : c.lastSeasonPosition <= 12 ? 1 : 0;
            for (int t = 0; t < Teams.Length && c.offers.Count < reach; t++)
                if (Teams[t].performance > Teams[c.team].performance) c.offers.Add(t);
        }
        SaveCareer();
        return playerPoints;
    }

    public static void StartNextSeason(int newTeam)
    {
        var c = Career;
        if (c == null) return;
        if (newTeam != c.team) { c.team = newTeam; c.engine = c.aero = c.chassis = 0; }
        c.season++;
        c.round = 1;
        c.seasonOver = false;
        c.offers.Clear();
        ResetStandings(c);
        SaveCareer();
    }

    public static List<DriverStanding> SortedStandings()
    {
        var list = new List<DriverStanding>(Career.standings);
        list.Sort((a, b) => b.points != a.points ? b.points.CompareTo(a.points) : b.wins.CompareTo(a.wins));
        return list;
    }

    // Race setup used when a career round starts
    public static RaceSetup CareerRaceSetup()
    {
        var c = Career;
        return new RaceSetup
        {
            laps = Settings.careerLaps, opponents = 19, difficulty = Settings.difficulty, grid = 3,
            team = c.team, driverName = c.driverName, driverCode = c.code, number = c.number
        };
    }

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }
}
