using UnityEngine;

// Procedural car audio, no sound files needed:
//  - engine: smooth harmonic V6 tone (firing frequency = rpm/60 × 3), brighter and louder on throttle,
//    gently saturated and low-pass filtered so it growls instead of buzzing
//  - tyre squeal when the tyres slide, lock or spin
//  - wind rush rising with speed
//  - one-shots: gear-shift clunk, crash thud, pit wheel-gun bursts
// With no car assigned it can voice a rival (rpm and speed set from outside, 3D positioned).
[RequireComponent(typeof(AudioSource))]
public class EngineAudio : MonoBehaviour
{
    public VehicleBase car;
    public float volume = 0.6f;
    public bool rival;                    // driven by externalRpm / externalSpeed instead of a car

    [System.NonSerialized] public float externalRpm = 6000f, externalSpeed;

    // values shared with the audio thread
    volatile float freq = 200f, engineGain, bright, squeal, wind;
    double ph1, ph2, ph3, ph4, phSub, phSq;
    float lp1, lp2, windLp, sqBp1, sqBp2;
    uint noise = 22222;
    int sampleRate = 48000;

    AudioSource oneShots;
    static AudioClip clunk, thud, gun;
    int lastGear;
    float lastImpact = -1f, smoothThrottle, smoothRpm = 4000f;

    void Start()
    {
        if (car == null && !rival) car = GetComponentInParent<VehicleBase>();
        sampleRate = AudioSettings.outputSampleRate;
        var src = GetComponent<AudioSource>();
        src.clip = AudioClip.Create("EngineCarrier", sampleRate, 1, sampleRate, false);   // silent carrier, filled below
        src.loop = true;
        src.playOnAwake = true;
        if (rival) { src.spatialBlend = 1f; src.minDistance = 8f; src.maxDistance = 220f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 1.2f; }
        else src.spatialBlend = 0f;
        src.Play();
        volume = GameSession.Settings.engineVolume * (rival ? 0.8f : 0.55f);

        if (!rival)
        {
            // One-shots need their own object: the synthesiser's filter may only feed one AudioSource
            var sfx = new GameObject("SFX");
            sfx.transform.SetParent(transform, false);
            oneShots = sfx.AddComponent<AudioSource>();
            oneShots.spatialBlend = 0f;
            oneShots.playOnAwake = false;
            BuildClips();
        }
    }

    void Update()
    {
        float rpm, speed, throttle;
        if (rival || car == null) { rpm = externalRpm; speed = externalSpeed; throttle = 0.8f; }
        else
        {
            rpm = car.Rpm > 0f ? car.Rpm : 3000f + car.SpeedKmh * 40f;
            speed = Mathf.Abs(car.Speed);
            var f1 = car as F1Car;
            throttle = f1 != null ? f1.ThrottleInput : 0.6f;
            squeal = Mathf.MoveTowards(squeal, f1 != null ? Mathf.Clamp01(f1.TyreSlip) * Mathf.Clamp01(speed / 15f) : 0f, Time.deltaTime * 6f);

            if (car.Gear != lastGear && lastGear != 0 && car.Gear > 0 && clunk != null) oneShots.PlayOneShot(clunk, 0.35f * volume);
            lastGear = car.Gear;
            if (car.LastImpactTime > lastImpact + 0.2f && car.LastImpactStrength > 4f && thud != null)
                oneShots.PlayOneShot(thud, Mathf.Clamp01(car.LastImpactStrength / 25f) * 1.4f * volume + 0.2f);
            lastImpact = Mathf.Max(lastImpact, car.LastImpactTime);
        }
        smoothRpm = Mathf.Lerp(smoothRpm, rpm, 1f - Mathf.Exp(-18f * Time.deltaTime));
        smoothThrottle = Mathf.Lerp(smoothThrottle, throttle, 1f - Mathf.Exp(-10f * Time.deltaTime));
        bool running = Time.timeScale > 0f;
        freq = smoothRpm / 60f * 3f;
        bright = Mathf.Lerp(0.25f, 1f, smoothThrottle);
        engineGain = running ? volume * Mathf.Lerp(0.35f, 0.8f, smoothThrottle) * Mathf.Lerp(0.75f, 1f, smoothRpm / 12500f) : 0f;
        wind = running ? volume * Mathf.Clamp01(speed / 85f) * Mathf.Clamp01(speed / 85f) * 0.5f : 0f;
        if (!running) squeal = 0f;
    }

    public void PlayPitGun()
    {
        if (oneShots != null && gun != null) oneShots.PlayOneShot(gun, 0.7f * volume);
    }

    float Noise()
    {
        noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
        return noise / (float)uint.MaxValue * 2f - 1f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        float sr = sampleRate;
        double inc = freq / sr;
        float g = engineGain, b = bright, sq = squeal, wd = wind;
        // One-pole low-pass for the engine: cutoff rises with throttle
        float cutoff = Mathf.Lerp(900f, 3200f, b);
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sr);
        float aw = 1f - Mathf.Exp(-2f * Mathf.PI * 450f / sr);
        double sqInc = 1150.0 / sr;
        for (int i = 0; i < data.Length; i += channels)
        {
            ph1 += inc; if (ph1 > 1.0) ph1 -= 1.0;
            ph2 += inc * 2.0; if (ph2 > 1.0) ph2 -= 1.0;
            ph3 += inc * 3.0; if (ph3 > 1.0) ph3 -= 1.0;
            ph4 += inc * 4.0; if (ph4 > 1.0) ph4 -= 1.0;
            phSub += inc * 0.5; if (phSub > 1.0) phSub -= 1.0;
            float twoPi = 2f * Mathf.PI;
            float tone = Mathf.Sin((float)ph1 * twoPi)
                       + 0.55f * Mathf.Sin((float)ph2 * twoPi)
                       + 0.32f * b * Mathf.Sin((float)ph3 * twoPi)
                       + 0.18f * b * Mathf.Sin((float)ph4 * twoPi)
                       + 0.45f * Mathf.Sin((float)phSub * twoPi);
            tone += Noise() * 0.06f * b;                       // exhaust breath
            tone = (float)System.Math.Tanh(tone * 0.9f);       // soft saturation: growl, no harsh edges
            lp1 += a * (tone - lp1);
            lp2 += a * (lp1 - lp2);
            float engine = lp2 * g;

            // Wind: low-passed noise
            windLp += aw * (Noise() - windLp);
            float windS = windLp * wd * 1.5f;

            // Tyre squeal: a wavering tone mixed with band-limited noise
            float squealS = 0f;
            if (sq > 0.01f)
            {
                phSq += sqInc * (1.0 + 0.04 * Mathf.Sin((float)ph1 * 0.3f)); if (phSq > 1.0) phSq -= 1.0;
                sqBp1 += 0.25f * (Noise() - sqBp1);
                sqBp2 += 0.25f * (sqBp1 - sqBp2);
                squealS = (Mathf.Sin((float)phSq * twoPi) * 0.5f + (sqBp1 - sqBp2) * 1.5f) * sq * volume * 0.35f;
            }

            float s = engine * 0.6f + windS + squealS;
            for (int c = 0; c < channels; c++) data[i + c] = s;
        }
    }

    // ---------- One-shot clips, synthesised once ----------

    static void BuildClips()
    {
        if (clunk != null) return;
        int sr = AudioSettings.outputSampleRate;
        var rnd = new System.Random(5);
        float N() => (float)rnd.NextDouble() * 2f - 1f;

        // Gear shift: short mechanical knock
        clunk = Make("ShiftClunk", sr, 0.09f, (t, n) => (Mathf.Sin(t * 2f * Mathf.PI * 140f) * 0.7f + N() * 0.4f) * Mathf.Exp(-t * 60f));
        // Crash: low thump with a metallic ring and debris noise
        thud = Make("CrashThud", sr, 0.8f, (t, n) =>
            Mathf.Sin(t * 2f * Mathf.PI * (70f - 30f * t)) * Mathf.Exp(-t * 7f) * 0.9f
          + Mathf.Sin(t * 2f * Mathf.PI * 820f) * Mathf.Exp(-t * 9f) * 0.25f
          + N() * Mathf.Exp(-t * 12f) * 0.5f);
        // Pit wheel gun: four quick pneumatic "brrrt" bursts
        gun = Make("WheelGun", sr, 1.4f, (t, n) =>
        {
            float local = t % 0.35f;
            float burst = local < 0.16f ? 1f : 0f;
            return burst * (Mathf.Sin(t * 2f * Mathf.PI * 320f) * 0.4f + N() * 0.5f) * (0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * 45f));
        });
    }

    static AudioClip Make(string name, int sr, float seconds, System.Func<float, int, float> f)
    {
        int n = Mathf.RoundToInt(sr * seconds);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)sr, i), -1f, 1f) * 0.8f;
        var clip = AudioClip.Create(name, n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
}
