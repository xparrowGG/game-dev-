using UnityEngine;

// Procedural F1 engine audio, no sound files needed. Unlike a sum of sine tones, this models how a racing
// engine actually makes its sound:
//  - every cylinder firing sends a pressure pulse down the exhaust (pulse train at rpm/60 × cylinders/2),
//    each cylinder slightly different, harder pulses under load
//  - the pulses ring through exhaust and intake resonances (band-pass formants) and are driven into a soft
//    clipper: the bright "scream" of a V10 comes from the high firing rate plus the upper formants
//  - lifting off: quieter, darker, with overrun crackles; on the limiter the ignition cuts in and out
//  - two exhaust banks slightly offset left/right for width; automatic level control keeps it pleasant
// Engine choice (settings): V10 (2000s), V8 (2006-13), V12 (90s), V6 turbo hybrid (2014+, with turbo whistle).
// Also: tyre squeal, wind, and one-shots (gear shift, crash, wheel guns, team radio).
// With no car assigned it voices a rival (rpm and speed set from outside, 3D positioned).
[RequireComponent(typeof(AudioSource))]
public class EngineAudio : MonoBehaviour
{
    public static readonly string[] EngineNames = { "V10", "V8", "V12", "V6 Turbo" };

    struct Profile
    {
        public int cylinders; public float rpmMin, rpmMax, f2, f3, drive, bodyQ; public bool turbo;
    }
    static readonly Profile[] Profiles =
    {
        new Profile { cylinders = 10, rpmMin = 7000f, rpmMax = 19000f, f2 = 2200f, f3 = 4300f, drive = 2.2f, bodyQ = 0.8f },
        new Profile { cylinders = 8,  rpmMin = 6500f, rpmMax = 18000f, f2 = 1700f, f3 = 3600f, drive = 2.6f, bodyQ = 0.7f },
        new Profile { cylinders = 12, rpmMin = 6000f, rpmMax = 17500f, f2 = 2700f, f3 = 5200f, drive = 1.8f, bodyQ = 0.9f },
        new Profile { cylinders = 6,  rpmMin = 5000f, rpmMax = 12500f, f2 = 1150f, f3 = 2700f, drive = 2.0f, bodyQ = 0.6f, turbo = true },
    };

    public VehicleBase car;
    public float volume = 0.6f;
    public bool rival;                    // driven by externalRpm / externalSpeed instead of a car
    [System.NonSerialized] public float externalRpm = 6000f, externalSpeed;

    // values shared with the audio thread
    volatile float firingHz = 200f, load, engineGain, squeal, wind, limiterCut, boost;
    Profile prof;
    int sampleRate = 48000;

    // audio-thread state
    double phase;
    int cyl;
    float pulseA, pulseB, crackle, hpA, hpB, agc = 1f, rms = 0.05f, windLp, sqBp1, sqBp2, turboPh;
    double phSq, sqLfo;
    uint noise = 22222;
    readonly float[] cylAmp = new float[12];
    Biquad bodyA = new Biquad(), f2A = new Biquad(), f3A = new Biquad(), bodyB = new Biquad(), f2B = new Biquad(), f3B = new Biquad(), crackBp = new Biquad();
    readonly float[] delay = new float[64];
    int delayPos;

    AudioSource oneShots;
    static AudioClip clunk, thud, gun, radio;
    int lastGear;
    float lastImpact = -1f, smoothThrottle, smoothRpm = 4000f, limiterTimer;

    class Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;
        public void BandPass(float f, float q, float sr)
        {
            f = Mathf.Clamp(f, 20f, sr * 0.45f);
            float w0 = 2f * Mathf.PI * f / sr, alpha = Mathf.Sin(w0) / (2f * q), a0 = 1f + alpha;
            b0 = alpha / a0; b1 = 0f; b2 = -alpha / a0; a1 = -2f * Mathf.Cos(w0) / a0; a2 = (1f - alpha) / a0;
        }
        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    void Start()
    {
        if (car == null && !rival) car = GetComponentInParent<VehicleBase>();
        sampleRate = AudioSettings.outputSampleRate;
        prof = Profiles[Mathf.Clamp(GameSession.Settings.engineSound, 0, Profiles.Length - 1)];
        var r = new System.Random(rival ? 11 : 3);
        for (int i = 0; i < cylAmp.Length; i++) cylAmp[i] = 0.85f + (float)r.NextDouble() * 0.3f;
        var src = GetComponent<AudioSource>();
        src.clip = AudioClip.Create("EngineCarrier", sampleRate, 1, sampleRate, false);   // silent carrier, filled below
        src.loop = true;
        src.playOnAwake = true;
        if (rival) { src.spatialBlend = 1f; src.minDistance = 10f; src.maxDistance = 260f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 1.4f; }
        else src.spatialBlend = 0f;
        src.Play();
        volume = GameSession.Settings.engineVolume * (rival ? 0.75f : 0.5f);

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
        float rpm01, speed, throttle;
        if (rival || car == null)
        {
            rpm01 = Mathf.InverseLerp(4000f, 12500f, externalRpm);
            speed = externalSpeed;
            throttle = 0.85f;
        }
        else
        {
            float maxRpm = car.MaxRpm > 0f ? car.MaxRpm : 12500f;
            float rpm = car.Rpm > 0f ? car.Rpm : 3000f + car.SpeedKmh * 40f;
            rpm01 = Mathf.InverseLerp(4000f, maxRpm, rpm);
            speed = Mathf.Abs(car.Speed);
            var f1 = car as F1Car;
            throttle = f1 != null ? f1.ThrottleInput : 0.6f;
            float slip = f1 != null ? Mathf.Clamp01((f1.TyreSlip - 0.35f) / 0.65f) : 0f;
            squeal = Mathf.MoveTowards(squeal, slip * Mathf.Clamp01(speed / 15f), Time.deltaTime * 4f);

            // Rev limiter: ignition cuts in and out
            if (rpm >= maxRpm - 60f && throttle > 0.5f) { limiterTimer += Time.deltaTime; limiterCut = Mathf.Repeat(limiterTimer * 22f, 1f) < 0.45f ? 1f : 0f; }
            else { limiterTimer = 0f; limiterCut = 0f; }

            if (car.Gear != lastGear && lastGear != 0 && car.Gear > 0 && clunk != null) oneShots.PlayOneShot(clunk, 0.3f * volume);
            lastGear = car.Gear;
            if (car.LastImpactTime > lastImpact + 0.2f && car.LastImpactStrength > 4f && thud != null)
                oneShots.PlayOneShot(thud, Mathf.Clamp01(car.LastImpactStrength / 25f) * 1.4f * volume + 0.2f);
            lastImpact = Mathf.Max(lastImpact, car.LastImpactTime);
        }
        smoothRpm = Mathf.Lerp(smoothRpm, Mathf.Lerp(prof.rpmMin * 0.55f, prof.rpmMax, rpm01), 1f - Mathf.Exp(-20f * Time.deltaTime));
        smoothThrottle = Mathf.Lerp(smoothThrottle, throttle, 1f - Mathf.Exp(-12f * Time.deltaTime));
        bool running = Time.timeScale > 0f;
        firingHz = smoothRpm / 60f * prof.cylinders * 0.5f;
        load = smoothThrottle;
        boost = prof.turbo ? Mathf.Lerp(boost, smoothThrottle * Mathf.Clamp01(rpm01 * 1.3f), 1f - Mathf.Exp(-3f * Time.deltaTime)) : 0f;
        engineGain = running ? volume * Mathf.Lerp(0.45f, 1f, smoothThrottle) : 0f;
        wind = running ? volume * Mathf.Pow(Mathf.Clamp01(speed / 90f), 2f) * 0.35f : 0f;
        if (!running) squeal = 0f;
    }

    public void PlayPitGun()
    {
        if (oneShots != null && gun != null) oneShots.PlayOneShot(gun, 0.6f * volume);
    }

    public void PlayRadio()
    {
        if (oneShots != null && radio != null) oneShots.PlayOneShot(radio, 0.5f);
    }

    float Noise()
    {
        noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
        return noise / (float)uint.MaxValue * 2f - 1f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        float sr = sampleRate;
        float fh = firingHz, ld = load, g = engineGain, sq = squeal, wd = wind, cut = limiterCut, bst = boost;
        // Formants follow the engine: the body tracks the firing frequency, the upper bands rise a little with revs
        float revRise = Mathf.Clamp01(fh / 1600f);
        bodyA.BandPass(fh, prof.bodyQ, sr); bodyB.BandPass(fh * 1.01f, prof.bodyQ, sr);
        f2A.BandPass(prof.f2 * (0.85f + 0.3f * revRise), 1.6f, sr); f2B.BandPass(prof.f2 * (0.88f + 0.3f * revRise), 1.6f, sr);
        f3A.BandPass(prof.f3 * (0.8f + 0.4f * revRise), 2.2f, sr); f3B.BandPass(prof.f3 * (0.83f + 0.4f * revRise), 2.2f, sr);
        crackBp.BandPass(900f, 1.2f, sr);
        double inc = fh / sr;
        float decay = Mathf.Exp(-fh * 3.2f / sr);                       // each pulse dies away before the next one
        float hpK = 1f - Mathf.Exp(-2f * Mathf.PI * 40f / sr);
        float aw = 1f - Mathf.Exp(-2f * Mathf.PI * 450f / sr);
        float crackDecay = Mathf.Exp(-1f / (0.018f * sr));
        float drive = prof.drive * Mathf.Lerp(0.7f, 1.15f, ld);
        int delaySamples = Mathf.Clamp(Mathf.RoundToInt(sr * 0.0006f), 1, delay.Length - 1);   // second bank ~0.6 ms later
        double sqInc = 1050.0 / sr;
        float twoPi = 2f * Mathf.PI;

        for (int i = 0; i < data.Length; i += channels)
        {
            phase += inc;
            if (phase >= 1.0)
            {
                phase -= 1.0;
                cyl = (cyl + 1) % prof.cylinders;
                float a = cylAmp[cyl] * (0.3f + 0.7f * ld) * (1f - cut);
                if ((cyl & 1) == 0) pulseA += a; else pulseB += a;          // alternate banks
                // Overrun: unburnt fuel popping in the exhaust when lifting at high revs
                if (ld < 0.15f && fh > 500f && Noise() > 0.93f) crackle = 0.8f + 0.4f * Noise();
            }
            pulseA *= decay; pulseB *= decay;
            float nA = Noise(), nB = Noise();
            float xA = pulseA * (1f + 0.25f * nA), xB = pulseB * (1f + 0.25f * nB);
            // Remove DC from the pulse trains
            hpA += hpK * (xA - hpA); xA -= hpA;
            hpB += hpK * (xB - hpB); xB -= hpB;

            float bright = 0.35f + 0.65f * ld;
            float sA = xA * 0.3f + bodyA.Process(xA) * 1.1f + f2A.Process(xA) * 0.9f * bright + f3A.Process(xA) * 0.55f * ld;
            float sB = xB * 0.3f + bodyB.Process(xB) * 1.1f + f2B.Process(xB) * 0.9f * bright + f3B.Process(xB) * 0.55f * ld;
            sA = (float)System.Math.Tanh(sA * drive);
            sB = (float)System.Math.Tanh(sB * drive);

            crackle *= crackDecay;
            float pop = crackle > 0.01f ? crackBp.Process(Noise() * crackle) * 2.2f : 0f;

            // Turbo whistle (V6 hybrid)
            float turbo = 0f;
            if (prof.turbo && bst > 0.02f)
            {
                turboPh += (2600f + 5200f * bst) / sr; if (turboPh > 1f) turboPh -= 1f;
                turbo = Mathf.Sin(turboPh * twoPi) * 0.05f * bst;
            }

            // Automatic level control: keeps the engine at a steady, comfortable loudness across the rev range
            float mono = sA + sB;
            rms += 0.0005f * (mono * mono - rms);
            float target = 0.35f / Mathf.Sqrt(Mathf.Max(rms, 0.0004f));
            agc += 0.0008f * (Mathf.Clamp(target, 0.3f, 3f) - agc);
            float eL = (sA * 0.6f + sB * 0.4f) * agc * g + (pop + turbo) * g;
            delay[delayPos] = (sB * 0.6f + sA * 0.4f) * agc * g + (pop + turbo) * g;
            float eR = delay[(delayPos - delaySamples + delay.Length) % delay.Length];
            delayPos = (delayPos + 1) % delay.Length;

            // Wind: low-passed noise
            windLp += aw * (Noise() - windLp);
            float windS = windLp * wd * 1.5f;

            // Tyre squeal: only when the tyres really slide
            float squealS = 0f;
            if (sq > 0.01f)
            {
                sqLfo += 4.0 / sr; phSq += sqInc * (1.0 + 0.03 * System.Math.Sin(sqLfo * 6.2832)); if (phSq > 1.0) phSq -= 1.0;
                sqBp1 += 0.25f * (Noise() - sqBp1);
                sqBp2 += 0.25f * (sqBp1 - sqBp2);
                squealS = (Mathf.Sin((float)phSq * twoPi) * 0.35f + (sqBp1 - sqBp2) * 1.2f) * sq * volume * 0.25f;
            }

            float common = windS + squealS;
            if (channels >= 2)
            {
                data[i] = eL * 0.55f + common;
                data[i + 1] = eR * 0.55f + common;
                for (int c = 2; c < channels; c++) data[i + c] = (eL + eR) * 0.27f + common;
            }
            else data[i] = (eL + eR) * 0.3f + common;
        }
    }

    // ---------- One-shot clips, synthesised once ----------

    static void BuildClips()
    {
        if (clunk != null) return;
        int sr = AudioSettings.outputSampleRate;
        var rnd = new System.Random(5);
        float N() => (float)rnd.NextDouble() * 2f - 1f;

        // Seamless-shift gearbox: a short dry knock
        clunk = Make("ShiftClunk", sr, 0.07f, (t, n) => (Mathf.Sin(t * 2f * Mathf.PI * 160f) * 0.6f + N() * 0.3f) * Mathf.Exp(-t * 70f));
        // Crash: low thump with a carbon crack and debris noise
        thud = Make("CrashThud", sr, 0.8f, (t, n) =>
            Mathf.Sin(t * 2f * Mathf.PI * (70f - 30f * t)) * Mathf.Exp(-t * 7f) * 0.9f
          + N() * Mathf.Exp(-t * 30f) * 0.6f
          + N() * Mathf.Exp(-t * 6f) * 0.2f);
        // Team radio: squelch, two short beeps, then a little channel hiss
        radio = Make("RadioBeep", sr, 0.55f, (t, n) =>
        {
            float squelch = t < 0.06f ? N() * 0.6f * (1f - t / 0.06f) : 0f;
            float beep = (t > 0.08f && t < 0.16f) || (t > 0.2f && t < 0.28f) ? Mathf.Sin(t * 2f * Mathf.PI * 1250f) * 0.45f : 0f;
            float hiss = t > 0.3f ? N() * 0.08f * (1f - (t - 0.3f) / 0.25f) : 0f;
            return squelch + beep + hiss;
        });
        // Pit wheel guns: four rattling pneumatic bursts (off), a pause, four more (on)
        gun = Make("WheelGun", sr, 2.2f, (t, n) =>
        {
            bool on = (t < 0.3f) || (t > 1.6f && t < 1.9f);
            if (!on) return 0f;
            float rattle = Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * 95f));
            return (rattle * 0.25f + N() * 0.45f) * (0.7f + 0.3f * Mathf.Sin(t * 2f * Mathf.PI * 13f));
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
