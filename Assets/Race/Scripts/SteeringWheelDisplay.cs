using System.Collections.Generic;
using UnityEngine;

// Renders a crisp F1 22-style OLED telemetry screen onto SteeringWheel_Screen at 20 Hz with zero GC allocations.
public class SteeringWheelDisplay : MonoBehaviour
{
    public F1Car car;
    public RaceManager manager;

    const int W = 256;
    const int H = 160;

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
                name = "SteeringWheel_OLED_RT",
                filterMode = FilterMode.Point,
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

        // Background OLED pitch-black slate + crisp cyan bezel border
        var bg = new Color32(10, 12, 16, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        var cyan = new Color32(45, 200, 240, 255);
        var panel = new Color32(22, 26, 34, 255);
        var dimText = new Color32(165, 175, 190, 255);
        var white = new Color32(248, 250, 255, 255);

        FillRect(0, H - 3, W, 3, cyan);
        FillRect(0, 0, W, 3, cyan);
        FillRect(6, 40, 74, 88, panel);
        FillRect(176, 40, 74, 88, panel);

        // Top 15 Shift LEDs (y=137..151): 5 green, 5 red, 5 blue
        bool limiterFlash = rpm01 >= 0.965f && Mathf.Repeat(Time.realtimeSinceStartup * 14f, 1f) < 0.5f;
        int litCount = rpm01 >= 0.70f ? Mathf.Clamp(Mathf.CeilToInt((rpm01 - 0.70f) / 0.28f * 15f), 0, 15) : 0;
        for (int k = 0; k < 15; k++)
        {
            int lx = 13 + k * 15;
            Color32 offCol = k < 5 ? new Color32(16, 42, 20, 255) : (k < 10 ? new Color32(48, 16, 16, 255) : new Color32(18, 24, 52, 255));
            Color32 onCol = k < 5 ? new Color32(40, 240, 70, 255) : (k < 10 ? new Color32(250, 45, 35, 255) : new Color32(60, 145, 255, 255));
            bool isOn = limiterFlash ? false : (k < litCount);
            FillRect(lx, 137, 11, 14, isOn ? onCol : offCol);
        }

        // Giant Center Gear character ("R", "N", "1".."8")
        string gStr = gear < 0 ? "R" : (gear == 0 ? "N" : gear.ToString());
        DrawString(gStr, 108, 56, 8, gear < 0 ? new Color32(250, 90, 70, 255) : white);

        // Left Column: L<lap>, P<pos>, Speed
        DrawString("L" + Mathf.Clamp(lap, 1, 99), 12, 108, 2, dimText);
        DrawString("P" + Mathf.Clamp(pos, 1, 22), 12, 86, 2, new Color32(250, 210, 60, 255));
        int shownSpeed = useMph ? Mathf.RoundToInt(speedKmh * 0.621371f) : speedKmh;
        DrawString(shownSpeed.ToString(), 12, 60, 2, white);
        DrawString(useMph ? "MPH" : "KPH", 12, 45, 1, dimText);

        // Right Column: RPM K + DRS Pill
        float kRpm = Mathf.Lerp(4.2f, 12.5f, rpm01);
        DrawString(kRpm.ToString("0.0") + "K", 182, 104, 2, white);
        DrawString("RPM", 182, 90, 1, dimText);

        Color32 drsBg = drsOpen ? new Color32(25, 215, 75, 255)
                       : (drsAvail ? new Color32(235, 185, 25, 255) : new Color32(42, 46, 54, 255));
        Color32 drsFg = (drsOpen || drsAvail) ? new Color32(10, 12, 14, 255) : dimText;
        FillRect(182, 48, 62, 26, drsBg);
        DrawString("DRS", 195, 54, 2, drsFg);

        // Bottom Bar: 4 Tyre Temps (FL/FR left, RL/RR right) + Yellow ERS Bar center
        Color32 tCol = tyreTempC < 65f ? new Color32(65, 165, 255, 255)
                     : (tyreTempC <= 104f ? new Color32(55, 225, 95, 255)
                     : (tyreTempC <= 118f ? new Color32(245, 180, 30, 255) : new Color32(245, 55, 45, 255)));
        string tTxt = Mathf.RoundToInt(tyreTempC) + "C";
        FillRect(10, 21, 56, 12, tCol);
        FillRect(10, 7, 56, 12, tCol);
        DrawString(tTxt, 18, 24, 1, bg);
        DrawString(tTxt, 18, 10, 1, bg);

        FillRect(190, 21, 56, 12, tCol);
        FillRect(190, 7, 56, 12, tCol);
        DrawString(tTxt, 198, 24, 1, bg);
        DrawString(tTxt, 198, 10, 1, bg);

        // ERS Charge bar (center bottom)
        DrawString("ERS " + Mathf.RoundToInt(ers01 * 100f) + "%", 92, 25, 1, new Color32(248, 212, 48, 255));
        FillRect(74, 8, 108, 13, new Color32(36, 38, 44, 255));
        int fillW = Mathf.RoundToInt(104f * ers01);
        if (fillW > 0) FillRect(76, 10, fillW, 9, new Color32(250, 210, 35, 255));

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
