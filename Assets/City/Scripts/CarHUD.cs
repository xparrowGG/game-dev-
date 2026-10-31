using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Heads-up display: analog speedometer, digital speed, gear, trip distance, time, top speed, heading.
public class CarHUD : MonoBehaviour
{
    public CarController car;
    public float dialMaxKmh = 240f;
    public float hintSeconds = 12f;

    static readonly string[] Headings = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    Texture2D tex;
    GUIStyle bigStyle, midStyle, smallStyle, tickStyle, hintStyle;
    float styleScale = -1f;
    float hintTimer;

    void Awake()
    {
        if (car == null) car = GetComponent<CarController>();
        tex = Texture2D.whiteTexture;
        hintTimer = hintSeconds;
    }

    void Update()
    {
        hintTimer -= Time.deltaTime;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.hKey.isPressed) hintTimer = 1f;
#else
        if (Input.GetKey(KeyCode.H)) hintTimer = 1f;
#endif
    }

    void BuildStyles(float s)
    {
        styleScale = s;
        bigStyle = Style(Mathf.RoundToInt(54 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
        midStyle = Style(Mathf.RoundToInt(26 * s), FontStyle.Bold, TextAnchor.MiddleLeft);
        smallStyle = Style(Mathf.RoundToInt(15 * s), FontStyle.Normal, TextAnchor.MiddleLeft);
        smallStyle.normal.textColor = new Color(1, 1, 1, 0.65f);
        tickStyle = Style(Mathf.RoundToInt(15 * s), FontStyle.Bold, TextAnchor.MiddleCenter);
        hintStyle = Style(Mathf.RoundToInt(16 * s), FontStyle.Normal, TextAnchor.UpperLeft);
    }

    GUIStyle Style(int size, FontStyle fs, TextAnchor anchor)
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = anchor };
        st.normal.textColor = Color.white;
        return st;
    }

    void Fill(Rect r, Color c, float radius = 0, float border = 0)
    {
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true, 0, c, border, radius);
    }

    void OnGUI()
    {
        if (car == null) return;
        float s = Screen.height / 1080f;
        if (!Mathf.Approximately(s, styleScale)) BuildStyles(s);

        DrawSpeedometer(s);
        DrawTripPanel(s);
        if (hintTimer > 0) DrawHints(s, Mathf.Clamp01(hintTimer));
    }

    void DrawSpeedometer(float s)
    {
        float r = 125 * s;
        Vector2 c = new Vector2(Screen.width - 175 * s, Screen.height - 165 * s);
        Fill(new Rect(c.x - r - 18 * s, c.y - r - 18 * s, 2 * (r + 18 * s), 2 * (r + 18 * s)), new Color(0.05f, 0.06f, 0.09f, 0.72f), r + 18 * s);
        Fill(new Rect(c.x - r - 18 * s, c.y - r - 18 * s, 2 * (r + 18 * s), 2 * (r + 18 * s)), new Color(1, 1, 1, 0.18f), r + 18 * s, 3 * s);

        Matrix4x4 saved = GUI.matrix;
        for (int kmh = 0; kmh <= dialMaxKmh; kmh += 10)
        {
            float a = Mathf.Lerp(-120f, 120f, kmh / dialMaxKmh);
            bool major = kmh % 20 == 0;
            Color col = kmh >= 180 ? new Color(1f, 0.3f, 0.25f) : Color.white;
            col.a = major ? 0.95f : 0.5f;
            float len = (major ? 16 : 9) * s, w = (major ? 3 : 2) * s;
            GUIUtility.RotateAroundPivot(a, c);
            Fill(new Rect(c.x - w / 2, c.y - r, w, len), col);
            GUI.matrix = saved;
            if (kmh % 40 == 0)
            {
                float rad = a * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * (r - 36 * s);
                GUI.Label(new Rect(p.x - 25 * s, p.y - 12 * s, 50 * s, 24 * s), kmh.ToString(), tickStyle);
            }
        }

        float speed = car.SpeedKmh;
        float needle = Mathf.Lerp(-120f, 120f, Mathf.Clamp01(speed / dialMaxKmh));
        GUIUtility.RotateAroundPivot(needle, c);
        Fill(new Rect(c.x - 2.5f * s, c.y - (r - 10 * s), 5 * s, r - 10 * s), new Color(1f, 0.35f, 0.1f), 2 * s);
        GUI.matrix = saved;
        Fill(new Rect(c.x - 11 * s, c.y - 11 * s, 22 * s, 22 * s), new Color(0.15f, 0.15f, 0.18f), 11 * s);

        GUI.Label(new Rect(c.x - 80 * s, c.y + 22 * s, 160 * s, 56 * s), Mathf.RoundToInt(speed).ToString(), bigStyle);
        var unit = new GUIStyle(tickStyle) { fontStyle = FontStyle.Normal };
        unit.normal.textColor = new Color(1, 1, 1, 0.6f);
        GUI.Label(new Rect(c.x - 40 * s, c.y + 74 * s, 80 * s, 20 * s), "km/h", unit);

        string gear = car.Speed < -0.3f ? "R" : (Mathf.Abs(car.Speed) < 0.3f ? "N" : "D");
        var gearStyle = new GUIStyle(tickStyle) { fontSize = Mathf.RoundToInt(22 * s) };
        gearStyle.normal.textColor = gear == "R" ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.85f, 1f);
        GUI.Label(new Rect(c.x - 20 * s, c.y - 62 * s, 40 * s, 30 * s), gear, gearStyle);
        if (car.Boosting)
        {
            var boostStyle = new GUIStyle(tickStyle);
            boostStyle.normal.textColor = new Color(1f, 0.75f, 0.2f);
            GUI.Label(new Rect(c.x - 50 * s, c.y - 36 * s, 100 * s, 20 * s), "BOOST", boostStyle);
        }
    }

    void DrawTripPanel(float s)
    {
        Rect panel = new Rect(24 * s, Screen.height - 214 * s, 290 * s, 190 * s);
        Fill(panel, new Color(0.05f, 0.06f, 0.09f, 0.72f), 14 * s);
        float x = panel.x + 20 * s, y = panel.y + 14 * s, w = panel.width - 40 * s;

        float d = car.DistanceMeters;
        string dist = d < 1000f ? Mathf.RoundToInt(d) + " m" : (d / 1000f).ToString("0.00") + " km";
        GUI.Label(new Rect(x, y, w, 20 * s), "DISTANCE TRAVELLED", smallStyle);
        GUI.Label(new Rect(x, y + 18 * s, w, 36 * s), dist, midStyle);

        int t = Mathf.FloorToInt(car.TripTime);
        Row(x, y + 64 * s, w, s, "TRIP TIME", string.Format("{0:00}:{1:00}", t / 60, t % 60));
        Row(x, y + 92 * s, w, s, "TOP SPEED", Mathf.RoundToInt(car.TopSpeedKmh) + " km/h");
        float avg = car.TripTime > 1f ? car.DistanceMeters / car.TripTime * 3.6f : 0f;
        Row(x, y + 120 * s, w, s, "AVG SPEED", Mathf.RoundToInt(avg) + " km/h");
        int idx = Mathf.RoundToInt(Mathf.Repeat(car.transform.eulerAngles.y, 360f) / 45f) % 8;
        Row(x, y + 148 * s, w, s, "HEADING", Headings[idx]);
    }

    void Row(float x, float y, float w, float s, string label, string value)
    {
        GUI.Label(new Rect(x, y, w, 24 * s), label, smallStyle);
        var right = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        right.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, w, 24 * s), value, right);
    }

    void DrawHints(float s, float alpha)
    {
        Rect panel = new Rect(24 * s, 24 * s, 300 * s, 196 * s);
        Fill(panel, new Color(0.05f, 0.06f, 0.09f, 0.65f * alpha), 12 * s);
        var st = new GUIStyle(hintStyle);
        st.normal.textColor = new Color(1, 1, 1, alpha);
        GUI.Label(new Rect(panel.x + 16 * s, panel.y + 12 * s, panel.width - 32 * s, panel.height - 24 * s),
            "W / ↑   accelerate\nS / ↓   brake / reverse\nA D / ← →   steer\nSpace   handbrake\nShift   boost\nC   camera: TV pod / chase / cockpit\nR   back on track   ·   Enter   restart\nH   show this help", st);
    }
}
