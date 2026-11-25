using System.Collections.Generic;
using UnityEngine;

// Renders a crisp F1 22-grade 512x320 OLED telemetry display onto SteeringWheel_Screen at 20 Hz with zero GC allocations.
public class SteeringWheelDisplay : MonoBehaviour
{
    public F1Car car;
    public RaceManager manager;

    const int W = 512;
    const int H = 320;

    Texture2D tex;
    Color32[] pixels;
    Material runtimeMat;
    Renderer rend;
    float accum;

    static readonly Dictionary<char, byte[]> Font5x7 = BuildFont();

    void Awake()
    {
        EnsureSurface();
    }

    void Start()
    {
        if (car == null) car = GetComponentInParent<F1Car>();
        if (manager == null) manager = FindFirstObjectByType<RaceManager>();
        EnsureSurface();
        RenderTelemetry(0, 0, 0.35f, 1f, false, false, 1, 1, 88f, GameSession.Settings.useMph);
    }

    void Update()
    {
        if (car == null) return;
        accum += Time.unscaledDeltaTime;
        if (accum < 0.05f) return;
        accum = 0f;

        float maxRpm = car.MaxRpm > 0f ? car.MaxRpm : 12500f;
        float rpm01 = Mathf.InverseLerp(3200f, maxRpm, car.Rpm > 0f ? car.Rpm : car.idleRpm);
        int lap = manager != null ? manager.PlayerLap : 1;
        int pos = manager != null ? manager.PlayerPosition : 1;
        RenderTelemetry(
            car.Gear,
            Mathf.RoundToInt(car.SpeedKmh),
            rpm01,
            car.ErsCharge,
            car.DrsOpen,
            car.drsAvailable,
            lap,
            pos,
            car.TyreTemp,
            GameSession.Settings.useMph
        );
    }

    void OnDestroy()
    {
        if (tex != null) Destroy(tex);
        if (runtimeMat != null) Destroy(runtimeMat);
    }

    void EnsureSurface()
    {
        if (rend == null) rend = GetComponent<Renderer>();
        if (pixels == null || pixels.Length != W * H) pixels = new Color32[W * H];
        if (tex == null)
        {
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                name = "SteeringWheel_OLED_HD",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }
        if (rend != null)
        {
            if (Application.isPlaying)
            {
                if (runtimeMat == null)
                {
                    var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                    runtimeMat = rend.sharedMaterial != null ? new Material(rend.sharedMaterial) : new Material(unlit);
                    runtimeMat.name = "SteeringWheel_Screen_Runtime";
                }
                ApplyTex(runtimeMat);
                rend.sharedMaterial = runtimeMat;
            }
            else if (rend.sharedMaterial != null)
            {
                ApplyTex(rend.sharedMaterial);
            }
        }
    }

    void ApplyTex(Material m)
    {
        if (m == null || tex == null) return;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
    }

    public void RenderTelemetry(int gear, int speedKmh, float rpm01, float ers01, bool drsOpen, bool drsAvail, int lap, int pos, float tyreTempC, bool useMph = false)
    {
        EnsureSurface();
        rpm01 = Mathf.Clamp01(rpm01);
        ers01 = Mathf.Clamp01(ers01 < 0f ? 1f : ers01);

        // Deep pitch-black OLED canvas with hairline dividers matching F1 22 MFD cluster
        var bg = new Color32(4, 5, 8, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        var bezel = new Color32(34, 40, 52, 255);
        var cyan = new Color32(45, 200, 240, 255);
        var dimText = new Color32(160, 172, 190, 255);
        var white = new Color32(248, 250, 255, 255);
        var gold = new Color32(250, 205, 38, 255);

        FillRect(0, 0, W, 2, bezel);
        FillRect(0, H - 2, W, 2, bezel);
        FillRect(0, 0, 2, H, bezel);
        FillRect(W - 2, 0, 2, H, bezel);

        // Razor-crisp hairline rules without clunky side boxes
        FillRect(12, 275, W - 24, 1, bezel);
        FillRect(12, 66, W - 24, 1, bezel);
        FillRect(156, 76, 1, 190, bezel);
        FillRect(354, 76, 1, 190, bezel);

        // 1. Top 15 Circular Shift LEDs (y=295): 5 Green, 5 Red, 5 Magenta/Blue with active glow
        bool limiterFlash = rpm01 >= 0.965f && Mathf.Repeat(Time.realtimeSinceStartup * 14f, 1f) < 0.5f;
        int litCount = rpm01 >= 0.70f ? Mathf.Clamp(Mathf.CeilToInt((rpm01 - 0.70f) / 0.28f * 15f), 0, 15) : 0;
        for (int k = 0; k < 15; k++)
        {
            float lx = 38f + k * 31.1f;
            Color32 offCol = k < 5 ? new Color32(14, 34, 18, 255) : (k < 10 ? new Color32(42, 14, 14, 255) : new Color32(20, 24, 52, 255));
            Color32 onCol = k < 5 ? new Color32(36, 242, 72, 255) : (k < 10 ? new Color32(252, 42, 34, 255) : new Color32(82, 148, 255, 255));
            bool isOn = !limiterFlash && k < litCount;
            FillLedDisc(lx, 296f, 10.5f, isOn ? onCol : offCol, isOn);
        }

        // 2. Hero Center Gear Indicator ("R", "N", "1".."8")
        string gStr = gear < 0 ? "R" : (gear == 0 ? "N" : gear.ToString());
        Color32 gCol = gear < 0 ? new Color32(252, 84, 68, 255) : white;
        DrawStringSmooth(gStr, 212, 106, 17, gCol);

        // 3. Left Column: L<lap>, P<pos>, Speed
        DrawString("L" + Mathf.Clamp(lap, 1, 99), 22, 232, 4, dimText);
        DrawString("P" + Mathf.Clamp(pos, 1, 22), 22, 188, 4, gold);
        int shownSpeed = useMph ? Mathf.RoundToInt(speedKmh * 0.621371f) : speedKmh;
        DrawString(shownSpeed.ToString(), 22, 122, 5, white);
        DrawString(useMph ? "MPH" : "KPH", 24, 92, 2, dimText);

        // 4. Right Column: RPM + Compact DRS Pill
        float kRpm = Mathf.Lerp(4.2f, 12.5f, rpm01);
        DrawString(kRpm.ToString("0.0") + "K", 368, 222, 4, white);
        DrawString("RPM", 370, 194, 2, dimText);

        Color32 drsBg = drsOpen ? new Color32(24, 218, 76, 255)
                       : (drsAvail ? new Color32(238, 186, 24, 255) : new Color32(34, 40, 50, 255));
        Color32 drsFg = (drsOpen || drsAvail) ? new Color32(8, 10, 12, 255) : dimText;
        FillRoundedRect(368, 96, 118, 42, 6, drsBg);
        DrawString("DRS", 394, 104, 4, drsFg);

        // 5. Bottom Row: Clean F1 22 Coloured Tyre Temps (FL/RL Left, FR/RR Right) + Slim Golden ERS Bar
        Color32 tCol = tyreTempC < 65f ? new Color32(64, 168, 255, 255)
                     : (tyreTempC <= 104f ? new Color32(72, 228, 108, 255)
                     : (tyreTempC <= 118f ? new Color32(246, 182, 28, 255) : new Color32(246, 62, 48, 255)));
        string tTxt = Mathf.RoundToInt(tyreTempC) + "C";

        DrawString(tTxt, 26, 40, 2, tCol);
        DrawString(tTxt, 26, 14, 2, tCol);
        DrawString(tTxt, 434, 40, 2, tCol);
        DrawString(tTxt, 434, 14, 2, tCol);

        // Center Golden F1 22 ERS Battery Bar + Cyan Accent
        int ersPct = Mathf.RoundToInt(ers01 * 100f);
        DrawString(ersPct + "%", 232, 40, 2, gold);
        FillRoundedRect(150, 18, 212, 12, 3, new Color32(26, 30, 38, 255));
        int fillW = Mathf.RoundToInt(206f * ers01);
        if (fillW > 2) FillRoundedRect(153, 21, fillW, 6, 2, gold);
        FillRect(153, 13, 206, 2, cyan);

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
    }

    void FillRect(int x0, int y0, int rw, int rh, Color32 c)
    {
        int x1 = Mathf.Min(W, x0 + rw);
        int y1 = Mathf.Min(H, y0 + rh);
        x0 = Mathf.Max(0, x0);
        y0 = Mathf.Max(0, y0);
        for (int y = y0; y < y1; y++)
        {
            int row = y * W;
            for (int x = x0; x < x1; x++) pixels[row + x] = c;
        }
    }

    void FillRoundedRect(int x0, int y0, int rw, int rh, int r, Color32 c)
    {
        int x1 = Mathf.Min(W, x0 + rw);
        int y1 = Mathf.Min(H, y0 + rh);
        int r2 = r * r;
        for (int y = Mathf.Max(0, y0); y < y1; y++)
        {
            int dy = y < y0 + r ? (y0 + r - y) : (y >= y1 - r ? y - (y1 - 1 - r) : 0);
            int row = y * W;
            for (int x = Mathf.Max(0, x0); x < x1; x++)
            {
                int dx = x < x0 + r ? (x0 + r - x) : (x >= x1 - r ? x - (x1 - 1 - r) : 0);
                if (dx == 0 || dy == 0 || dx * dx + dy * dy <= r2) pixels[row + x] = c;
            }
        }
    }

    void FillLedDisc(float cx, float cy, float radius, Color32 c, bool lit)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 2f));
        int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius + 2f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 2f));
        int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius + 2f));
        for (int y = y0; y <= y1; y++)
        {
            int row = y * W;
            float dy = y - cy;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= radius)
                {
                    float core = lit ? Mathf.Clamp01(1f - d / (radius * 1.15f)) * 0.35f : 0f;
                    pixels[row + x] = new Color32(
                        (byte)Mathf.Min(255, c.r + (int)((255 - c.r) * core)),
                        (byte)Mathf.Min(255, c.g + (int)((255 - c.g) * core)),
                        (byte)Mathf.Min(255, c.b + (int)((255 - c.b) * core)),
                        255
                    );
                }
            }
        }
    }

    void DrawStringSmooth(string s, int x, int y, int scale, Color32 c)
    {
        if (string.IsNullOrEmpty(s)) return;
        int cursor = x;
        int bevel = Mathf.Max(1, scale / 5);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = char.ToUpperInvariant(s[i]);
            if (Font5x7.TryGetValue(ch, out byte[] rows))
            {
                for (int r = 0; r < 7; r++)
                {
                    byte bits = rows[r];
                    int py = y + (6 - r) * scale;
                    for (int col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) != 0)
                            FillRoundedRect(cursor + col * scale, py, scale, scale, bevel, c);
                    }
                }
            }
            cursor += 6 * scale;
        }
    }

    void DrawString(string s, int x, int y, int scale, Color32 c)
    {
        if (string.IsNullOrEmpty(s)) return;
        int cursor = x;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = char.ToUpperInvariant(s[i]);
            if (Font5x7.TryGetValue(ch, out byte[] rows))
            {
                // rows[0] is TOP row -> Unity y is bottom-up, so draw at y + (6 - r) * scale
                for (int r = 0; r < 7; r++)
                {
                    byte bits = rows[r];
                    int py = y + (6 - r) * scale;
                    for (int col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) != 0)
                            FillRect(cursor + col * scale, py, scale, scale, c);
                    }
                }
            }
            cursor += 6 * scale;
        }
    }

    static Dictionary<char, byte[]> BuildFont()
    {
        return new Dictionary<char, byte[]>
        {
            ['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
            ['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
            ['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F },
            ['3'] = new byte[] { 0x1F, 0x02, 0x04, 0x06, 0x01, 0x11, 0x0E },
            ['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
            ['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
            ['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
            ['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
            ['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
            ['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
            ['C'] = new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
            ['D'] = new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C },
            ['E'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
            ['H'] = new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
            ['K'] = new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
            ['L'] = new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
            ['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
            ['N'] = new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 },
            ['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
            ['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
            ['S'] = new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
            ['.'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C },
            [':'] = new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 },
            ['%'] = new byte[] { 0x19, 0x19, 0x02, 0x04, 0x08, 0x13, 0x13 },
            ['-'] = new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 },
            [' '] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
        };
    }
}
