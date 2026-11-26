using UnityEngine;

// Procedural F1 engine audio synthesizer with optional Resources/F1Audio/*.wav stem support.
// Models 4-stroke multi-cylinder combustion pulses + phase-locked crank/firing harmonic orders +
// resonant exhaust/intake biquad formants + turbo/MGU-K whine + overrun crackle + kerb/tyre/wind FX.
[RequireComponent(typeof(AudioSource))]
public class EngineAudio : MonoBehaviour
{
    public static readonly string[] EngineNames = { "V10", "V8", "V12", "V6 Turbo" };

    struct Profile
    {
        public int cylinders;
        public float rpmMin, rpmMax, f2, f3, drive, bodyQ;
        public bool turbo;
    }

    static readonly Profile[] Profiles =
    {
        new Profile { cylinders = 10, rpmMin = 3800f, rpmMax = 11200f, f2 = 340f, f3 = 680f, drive = 1.35f, bodyQ = 0.58f },
        new Profile { cylinders = 8,  rpmMin = 3600f, rpmMax = 10800f, f2 = 300f, f3 = 620f, drive = 1.45f, bodyQ = 0.56f },
        new Profile { cylinders = 12, rpmMin = 3600f, rpmMax = 11400f, f2 = 360f, f3 = 720f, drive = 1.30f, bodyQ = 0.60f },
        new Profile { cylinders = 6,  rpmMin = 3400f, rpmMax = 10400f, f2 = 250f, f3 = 520f, drive = 1.40f, bodyQ = 0.52f, turbo = true },
    };

    public VehicleBase car;
    public float volume = 0.75f;
    public bool rival;
    [System.NonSerialized] public float externalRpm = 6000f, externalSpeed;

    // Shared with audio thread (all safe defaults before Awake)
    volatile bool initialized;
    volatile int activeProfileIdx;
    volatile float firingHz = 220f, crankHz = 73.3f, load = 0.25f, engineGain = 0.45f;
    volatile float squeal, wind, limiterCut, boost, wastegate, mguk, kerbLevel, kerbHz = 68f, shiftCut;
    volatile bool muteSynthEngine;
    Profile prof;
    int sampleRate = 48000;

    // Audio-thread state
    double phase, crankPhase, sqPh, sqLfo, kerbPh;
    int cyl;
    float pulseA, pulseB, crackle, hpA, hpB, agc = 1f, rms = 0.06f, windLp, combLp, sqBp1, sqBp2, turboPh, mgukPh, wgPh;
    uint noise = 22222;
    readonly float[] cylAmp = new float[12];
    Biquad bodyA = new Biquad(), f2A = new Biquad(), f3A = new Biquad(), exhaustLpA = new Biquad();
    Biquad bodyB = new Biquad(), f2B = new Biquad(), f3B = new Biquad(), exhaustLpB = new Biquad();
    Biquad crackBp = new Biquad();
    readonly float[] delay = new float[64];
    int delayPos;

    AudioSource mainSrc, oneShots;
    AudioSource wavIdle, wavLow, wavMid, wavHigh, wavOffload, wavTurbo;
    Rigidbody carRb;
    bool hasCustomEngineLoops;
    static AudioClip clunk, thud, gun, radio, customKerbClip;
    // Recorded crash set (Resources/F1Audio/Crash): light bump, heavy hits (random), wall-scrape and gravel loops
    static AudioClip crashLight, scrapeClip, gravelClip;
    static AudioClip[] crashHeavy;
    AudioSource scrapeSrc, gravelSrc;
    static bool optionalClipsChecked;
    int lastGear;
    float lastImpact = -1f, smoothThrottle = 0.2f, smoothRpm = 4800f, prevSmoothThrottle = 0.2f;
    float limiterTimer, pitLimiterTimer, shiftCutTimer, blipTimer, wastegateTimer, lastKerbAudioTime = -1f;

    class Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public void BandPass(float f, float q, float sr)
        {
            if (sr <= 100f) sr = 48000f;
            f = Mathf.Clamp(f, 20f, sr * 0.43f);
            q = Mathf.Clamp(q, 0.35f, 8f);
            float w0 = 2f * Mathf.PI * f / sr;
            float alpha = Mathf.Sin(w0) / (2f * q);
            float a0 = 1f + alpha;
            if (a0 < 1e-5f) a0 = 1f;
            b0 = alpha / a0;
            b1 = 0f;
            b2 = -alpha / a0;
            a1 = -2f * Mathf.Cos(w0) / a0;
            a2 = (1f - alpha) / a0;
        }

        public void LowPass(float f, float q, float sr)
        {
            if (sr <= 100f) sr = 48000f;
            f = Mathf.Clamp(f, 80f, sr * 0.42f);
            q = Mathf.Clamp(q, 0.5f, 2.5f);
            float w0 = 2f * Mathf.PI * f / sr;
            float cosw = Mathf.Cos(w0), alpha = Mathf.Sin(w0) / (2f * q);
            float a0 = 1f + alpha;
            if (a0 < 1e-5f) a0 = 1f;
            b0 = (1f - cosw) * 0.5f / a0;
            b1 = (1f - cosw) / a0;
            b2 = b0;
            a1 = -2f * cosw / a0;
            a2 = (1f - alpha) / a0;
        }

        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            if (!float.IsFinite(y))
            {
                Reset();
                return 0f;
            }
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }

        public void Reset()
        {
            x1 = x2 = y1 = y2 = 0f;
        }
    }

    void Awake()
    {
        EnsureInitialized();
    }

    void Start()
    {
        EnsureInitialized();
        if (Application.isPlaying && mainSrc != null && !mainSrc.isPlaying)
            mainSrc.Play();
    }

    void EnsureInitialized()
    {
        if (initialized) return;
        if (car == null && !rival) car = GetComponentInParent<VehicleBase>();
        carRb = car != null ? car.GetComponent<Rigidbody>() : null;

        int sr = AudioSettings.outputSampleRate;
        sampleRate = sr > 1000 ? sr : 48000;

        int pIdx = Mathf.Clamp(GameSession.Settings.engineSound, 0, Profiles.Length - 1);
        activeProfileIdx = pIdx;
        prof = Profiles[pIdx];

        var r = new System.Random(rival ? 11 : 3);
        for (int i = 0; i < cylAmp.Length; i++) cylAmp[i] = 0.88f + (float)r.NextDouble() * 0.24f;

        smoothRpm = prof.rpmMin * 0.72f;
        crankHz = smoothRpm / 60f;
        firingHz = crankHz * prof.cylinders * 0.5f;
        volume = Mathf.Clamp(GameSession.Settings.engineVolume, 0.05f, 1.5f) * (rival ? 0.70f : 0.95f);
        engineGain = volume * 0.5f;

        mainSrc = GetComponent<AudioSource>();
        if (mainSrc != null)
        {
            // Non-zero micro-dither carrier ensures FMOD never virtualizes OnAudioFilterRead as silent
            var carrier = AudioClip.Create("EngineCarrier", sampleRate, 1, sampleRate, false);
            var dither = new float[sampleRate];
            for (int i = 0; i < dither.Length; i++) dither[i] = (i & 1) == 0 ? 1e-4f : -1e-4f;
            carrier.SetData(dither, 0);

            mainSrc.clip = carrier;
            mainSrc.loop = true;
            mainSrc.playOnAwake = false;
            mainSrc.volume = 1f;
            mainSrc.pitch = 1f;
            mainSrc.bypassEffects = false;
            mainSrc.bypassListenerEffects = false;
            mainSrc.bypassReverbZones = false;
            mainSrc.priority = rival ? 64 : 0;

            if (rival)
            {
                mainSrc.spatialBlend = 1f;
                mainSrc.minDistance = 10f;
                mainSrc.maxDistance = 260f;
                mainSrc.rolloffMode = AudioRolloffMode.Logarithmic;
                mainSrc.dopplerLevel = 1.4f;
            }
            else
            {
                mainSrc.spatialBlend = 0f;
            }

            if (Application.isPlaying && !mainSrc.isPlaying) mainSrc.Play();
        }

        if (!rival && Application.isPlaying)
        {
            var sfx = new GameObject("SFX");
            sfx.transform.SetParent(transform, false);
            oneShots = sfx.AddComponent<AudioSource>();
            oneShots.spatialBlend = 0f;
            oneShots.playOnAwake = false;
            oneShots.priority = 16;
            BuildClips();
            SetupOptionalEngineLoops();
        }

        initialized = true;
    }

    void SetupOptionalEngineLoops()
    {
        if (!optionalClipsChecked)
        {
            optionalClipsChecked = true;
            var shift = Resources.Load<AudioClip>("F1Audio/shift");
            var crash = Resources.Load<AudioClip>("F1Audio/crash");
            customKerbClip = Resources.Load<AudioClip>("F1Audio/kerb");
            if (shift != null) clunk = shift;
            if (crash != null) thud = crash;
            var radioClip = Resources.Load<AudioClip>("F1Audio/radio");
            if (radioClip != null) radio = radioClip;
            crashLight = Resources.Load<AudioClip>("F1Audio/Crash/crash_light");
            scrapeClip = Resources.Load<AudioClip>("F1Audio/Crash/scrape_loop");
            gravelClip = Resources.Load<AudioClip>("F1Audio/Crash/gravel_loop");
            var heavy = new System.Collections.Generic.List<AudioClip>();
            for (int i = 1; i <= 6; i++) { var c = Resources.Load<AudioClip>("F1Audio/Crash/crash_heavy_" + i); if (c != null) heavy.Add(c); }
            crashHeavy = heavy.ToArray();
        }
        if (scrapeClip != null && scrapeSrc == null) scrapeSrc = MakeLoop("SFX_Scrape", scrapeClip);
        if (gravelClip != null && gravelSrc == null) gravelSrc = MakeLoop("SFX_Gravel", gravelClip);
        // Recorded loops for the engine chosen in Settings (F1Audio/V10, V8, V12, V6Turbo); else the shared set
        string engineDir = "F1Audio/" + EngineNames[Mathf.Clamp(GameSession.Settings.engineSound, 0, EngineNames.Length - 1)].Replace(" ", "") + "/";
        AudioClip Load(string clipName) => Resources.Load<AudioClip>(engineDir + clipName) ?? Resources.Load<AudioClip>("F1Audio/" + clipName);
        var singleClip = Load("f1_engine") ?? Load("engine");
        var cIdle = Load("engine_idle");
        var cLow = Load("engine_low");
        var cMid = Load("engine_mid");
        var cHigh = Load("engine_high");
        if (cIdle != null && cLow != null && cMid != null && cHigh != null)
        {
            hasCustomEngineLoops = true;
            muteSynthEngine = true;
            wavIdle = MakeLoop("WAV_Idle", cIdle);
            wavLow = MakeLoop("WAV_Low", cLow);
            wavMid = MakeLoop("WAV_Mid", cMid);
            wavHigh = MakeLoop("WAV_High", cHigh);
            var cOff = Load("engine_offload");
            if (cOff != null) wavOffload = MakeLoop("WAV_Offload", cOff);
            var cTur = Load("turbo");
            if (cTur != null) wavTurbo = MakeLoop("WAV_Turbo", cTur);
        }
        else if (singleClip != null)
        {
            hasCustomEngineLoops = true;
            muteSynthEngine = true;
            wavIdle = MakeLoop("WAV_Idle", singleClip);
            wavLow = MakeLoop("WAV_Low", singleClip);
            wavMid = MakeLoop("WAV_Mid", singleClip);
            wavHigh = MakeLoop("WAV_High", singleClip);
        }
    }

    AudioSource MakeLoop(string n, AudioClip clip)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var a = go.AddComponent<AudioSource>();
        a.clip = clip;
        a.loop = true;
        a.spatialBlend = 0f;
        a.volume = 0f;
        a.Play();
        return a;
    }

    void Update()
    {
        if (!initialized) EnsureInitialized();

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float rpm01, speed, throttle;
        float onKerbNow = 0f;
        float deploySignal = 0f;

        if (rival || car == null)
        {
            rpm01 = Mathf.InverseLerp(4000f, 12500f, externalRpm);
            speed = externalSpeed;
            throttle = 0.85f;
        }
        else
        {
            float maxRpm = car.MaxRpm > 0f ? car.MaxRpm : 12500f;
            float rpm = car.Rpm > 0f ? car.Rpm : 3500f + car.SpeedKmh * 38f;
            rpm01 = Mathf.InverseLerp(3600f, maxRpm, rpm);
            speed = Mathf.Abs(car.Speed);
            var f1 = car as F1Car;
            throttle = f1 != null ? f1.ThrottleInput : 0.6f;

            float slip = f1 != null ? Mathf.Clamp01((f1.TyreSlip - 0.35f) / 0.65f) : 0f;
            squeal = Mathf.MoveTowards(squeal, slip * Mathf.Clamp01(speed / 14f), dt * 5f);

            // Kerb tactile thump when wheels ride sawtooth kerbs
            if (f1 != null && f1.OnKerb && speed > 8f)
            {
                onKerbNow = Mathf.Clamp01(speed / 60f);
                kerbHz = Mathf.Lerp(45f, 88f, Mathf.Clamp01(speed / 75f));
                if (customKerbClip != null && oneShots != null && Time.time - lastKerbAudioTime > 0.08f)
                {
                    lastKerbAudioTime = Time.time;
                    oneShots.PlayOneShot(customKerbClip, onKerbNow * 0.45f * volume);
                }
            }

            // MGU-K / ERS deployment or regen pitch modulation
            if (f1 != null)
                deploySignal = (throttle > 0.7f && f1.ErsCharge > 0.05f) ? 1f : (f1.LongitudinalG < -1.2f ? 0.8f : 0.25f);

            // Pit-limiter rhythmic exhaust burble vs rev limiter ignition cut
            bool inPitLimiter = ((carRb != null && carRb.isKinematic) || (car is F1Car f1lim && f1lim.pitLimiterActive)) && speed > 2f;
            if (inPitLimiter)
            {
                pitLimiterTimer += dt;
                limiterTimer = 0f;
                limiterCut = Mathf.Repeat(pitLimiterTimer * 15f, 1f) < 0.42f ? 0.45f : 0f;
                rpm = Mathf.Min(rpm, maxRpm * 0.42f);
                rpm01 = Mathf.InverseLerp(3600f, maxRpm, rpm);
                throttle = Mathf.Min(throttle, 0.28f);
            }
            else if (rpm >= maxRpm - 70f && throttle > 0.45f)
            {
                pitLimiterTimer = 0f;
                limiterTimer += dt;
                limiterCut = Mathf.Repeat(limiterTimer * 24f, 1f) < 0.48f ? 1f : 0f;
            }
            else
            {
                pitLimiterTimer = 0f;
                limiterTimer = 0f;
                limiterCut = 0f;
            }

            // Upshift ignition-cut bark vs Downshift throttle blip
            if (lastGear > 0 && car.Gear > lastGear)
            {
                shiftCutTimer = 0.048f;
                if (clunk != null && oneShots != null) oneShots.PlayOneShot(clunk, 0.36f * volume);
            }
            else if (lastGear > 1 && car.Gear > 0 && car.Gear < lastGear)
            {
                blipTimer = 0.095f;
                if (clunk != null && oneShots != null) oneShots.PlayOneShot(clunk, 0.28f * volume);
            }
            lastGear = car.Gear;

            if (car.LastImpactTime > lastImpact + 0.2f && car.LastImpactStrength > 4f && oneShots != null)
            {
                // Real recordings: a light bump for small knocks, a random heavy crash for big hits; synth thud as fallback
                float s = car.LastImpactStrength;
                AudioClip hit = s < 14f ? (crashLight != null ? crashLight : thud)
                              : (crashHeavy != null && crashHeavy.Length > 0 ? crashHeavy[Random.Range(0, crashHeavy.Length)] : thud);
                if (hit != null) oneShots.PlayOneShot(hit, Mathf.Clamp01(0.25f + s / 30f) * 1.2f * volume + 0.15f);
            }
            lastImpact = Mathf.Max(lastImpact, car.LastImpactTime);

            // Loops: metal scrape while sliding along a barrier, stones while in a gravel trap
            if (car is F1Car fc)
            {
                float spd = Mathf.Abs(car.Speed);
                if (scrapeSrc != null)
                {
                    float target = fc.WallContact && spd > 2f ? Mathf.Clamp01(spd / 35f) * 0.9f * volume : 0f;
                    scrapeSrc.volume = Mathf.MoveTowards(scrapeSrc.volume, target, dt * (target > scrapeSrc.volume ? 12f : 4f));
                    scrapeSrc.pitch = 0.8f + Mathf.Clamp01(spd / 60f) * 0.5f;
                }
                if (gravelSrc != null)
                {
                    float target = fc.OnGravel && spd > 1.5f ? Mathf.Clamp01(spd / 30f) * 0.75f * volume : 0f;
                    gravelSrc.volume = Mathf.MoveTowards(gravelSrc.volume, target, dt * 6f);
                    gravelSrc.pitch = 0.85f + Mathf.Clamp01(spd / 50f) * 0.4f;
                }
            }
        }

        if (shiftCutTimer > 0f) shiftCutTimer = Mathf.Max(0f, shiftCutTimer - dt);
        if (blipTimer > 0f) blipTimer = Mathf.Max(0f, blipTimer - dt);

        // Downshift blip transiently flares engine speed & throttle load
        float blipAmt = blipTimer > 0f ? Mathf.Sin((blipTimer / 0.095f) * Mathf.PI) * 0.24f : 0f;
        float targetRpm = Mathf.Lerp(prof.rpmMin * 0.65f, prof.rpmMax, Mathf.Clamp01(rpm01 + blipAmt));
        smoothRpm = Mathf.Lerp(smoothRpm, targetRpm, 1f - Mathf.Exp(-24f * dt));

        float effThrottle = Mathf.Max(throttle, blipAmt * 3.5f);
        if (prevSmoothThrottle > 0.65f && effThrottle < 0.25f && rpm01 > 0.45f)
            wastegateTimer = 0.26f;
        if (wastegateTimer > 0f) wastegateTimer = Mathf.Max(0f, wastegateTimer - dt);
        prevSmoothThrottle = smoothThrottle;
        smoothThrottle = Mathf.Lerp(smoothThrottle, effThrottle, 1f - Mathf.Exp(-14f * dt));

        volume = Mathf.Clamp(GameSession.Settings.engineVolume, 0.05f, 1.5f) * (rival ? 0.70f : 0.95f);
        bool running = Time.timeScale > 0f && (!rival || mainSrc == null || mainSrc.isPlaying);
        crankHz = smoothRpm / 60f;
        firingHz = crankHz * prof.cylinders * 0.5f;
        load = smoothThrottle;
        shiftCut = shiftCutTimer > 0f ? 0.78f : 0f;
        boost = prof.turbo ? Mathf.Lerp(boost, smoothThrottle * Mathf.Clamp01(rpm01 * 1.35f), 1f - Mathf.Exp(-4f * dt)) : 0f;
        wastegate = (prof.turbo && wastegateTimer > 0f) ? Mathf.Clamp01(wastegateTimer / 0.26f) : 0f;
        mguk = prof.turbo ? deploySignal * Mathf.Clamp01(rpm01 * 0.9f + 0.2f) : 0f;
        engineGain = running ? volume * Mathf.Lerp(0.58f, 1.0f, smoothThrottle) : 0f;
        wind = running ? volume * Mathf.Pow(Mathf.Clamp01(speed / 92f), 1.85f) * 0.36f : 0f;
        kerbLevel = running ? onKerbNow * volume : 0f;
        if (!running) squeal = 0f;

        if (hasCustomEngineLoops)
        {
            float baseGain = running ? volume * Mathf.Lerp(0.55f, 1.0f, smoothThrottle) : 0f;
            float wIdle = Mathf.Clamp01(1f - rpm01 * 3.2f);
            float wLow = Mathf.Clamp01(1f - Mathf.Abs(rpm01 - 0.30f) * 3.2f);
            float wMid = Mathf.Clamp01(1f - Mathf.Abs(rpm01 - 0.62f) * 3.0f);
            float wHigh = Mathf.Clamp01((rpm01 - 0.58f) * 2.6f);
            float wSum = Mathf.Max(0.001f, wIdle + wLow + wMid + wHigh);
            float pScale = Mathf.Lerp(0.82f, 1.32f, rpm01);
            wavIdle.volume = baseGain * (wIdle / wSum); wavIdle.pitch = Mathf.Lerp(0.85f, 1.25f, rpm01 * 2f);
            wavLow.volume = baseGain * (wLow / wSum);   wavLow.pitch = pScale * 0.95f;
            wavMid.volume = baseGain * (wMid / wSum);   wavMid.pitch = pScale;
            wavHigh.volume = baseGain * (wHigh / wSum); wavHigh.pitch = Mathf.Lerp(0.88f, 1.28f, rpm01);
            if (wavOffload != null)
            {
                wavOffload.volume = running ? volume * Mathf.Clamp01((0.35f - smoothThrottle) * 2f) * rpm01 * 0.7f : 0f;
                wavOffload.pitch = pScale;
            }
            if (wavTurbo != null)
            {
                wavTurbo.volume = running ? volume * boost * 0.45f : 0f;
                wavTurbo.pitch = 0.85f + boost * 0.65f;
            }
        }
    }

    public void PlayPitGun()
    {
        if (oneShots != null && gun != null) oneShots.PlayOneShot(gun, 0.65f * volume);
    }

    public void PlayRadio()
    {
        if (oneShots != null && radio != null) oneShots.PlayOneShot(radio, 0.55f);
    }

    float Noise()
    {
        noise ^= noise << 13;
        noise ^= noise >> 17;
        noise ^= noise << 5;
        return noise / (float)uint.MaxValue * 2f - 1f;
    }

    static float BandGain(float freqHz, float nyqLimit)
    {
        if (freqHz >= nyqLimit) return 0f;
        float t = freqHz / nyqLimit;
        return t > 0.75f ? Mathf.Clamp01(1f - (t - 0.75f) / 0.25f) : 1f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        Synthesize(data, channels);
    }

    internal void Synthesize(float[] data, int channels)
    {
        if (!initialized || prof.cylinders <= 0 || sampleRate <= 0 || data == null || data.Length == 0)
            return;

        float sr = sampleRate;
        float nyq = sr * 0.42f;
        float chz = Mathf.Max(crankHz, 20f);
        float fh = Mathf.Max(firingHz, 60f);
        float ld = Mathf.Clamp01(load);
        float g = engineGain;
        float sq = squeal;
        float wd = wind;
        float cut = Mathf.Max(limiterCut, shiftCut);
        float bst = boost;
        float wg = wastegate;
        float mk = mguk;
        float kLvl = kerbLevel;
        float kHz = kerbHz;
        bool synthEng = !muteSynthEngine && g > 0.0005f;

        float revRise = Mathf.Clamp01(fh / 900f);
        bodyA.BandPass(Mathf.Min(fh * 0.5f, 450f), prof.bodyQ, sr);
        bodyB.BandPass(Mathf.Min(fh * 0.506f, 455f), prof.bodyQ, sr);
        f2A.BandPass(prof.f2 * (0.85f + 0.20f * revRise), 1.15f, sr);
        f2B.BandPass(prof.f2 * (0.88f + 0.20f * revRise), 1.15f, sr);
        f3A.BandPass(prof.f3 * (0.82f + 0.22f * revRise), 1.25f, sr);
        f3B.BandPass(prof.f3 * (0.85f + 0.22f * revRise), 1.25f, sr);
        float lpCut = Mathf.Min(1060f, 480f + 420f * ld + 160f * revRise);
        exhaustLpA.LowPass(lpCut, 0.65f, sr);
        exhaustLpB.LowPass(lpCut, 0.65f, sr);
        crackBp.BandPass(380f, 1.05f, sr);

        double inc = fh / sr;
        double crankInc = chz / sr;
        float halfCyl = prof.cylinders * 0.5f;
        float decay = Mathf.Exp(-fh * 2.5f / sr);
        float hpK = 1f - Mathf.Exp(-2f * Mathf.PI * 34f / sr);
        float aw = 1f - Mathf.Exp(-2f * Mathf.PI * 420f / sr);
        float crackDecay = Mathf.Exp(-1f / (0.018f * sr));
        float drive = prof.drive * Mathf.Lerp(0.70f, 1.12f, ld);
        int delaySamples = Mathf.Clamp(Mathf.RoundToInt(sr * 0.00055f), 1, delay.Length - 1);
        float twoPi = 2f * Mathf.PI;

        // Warm deep chest harmonics only (0.25x, 0.5x, 1.0x fundamental + restrained 1.5x/2.0x, zero 3x/4x/6x screech)
        float scream = prof.cylinders >= 10 ? 0.85f : (prof.turbo ? 0.55f : 0.72f);
        float g025 = BandGain(fh * 0.25f, nyq) * 0.38f;
        float g05 = BandGain(fh * 0.5f, nyq) * 0.56f;
        float g10 = BandGain(fh * 1.0f, nyq) * 0.72f;
        float g15 = BandGain(fh * 1.5f, nyq) * 0.14f * scream;
        float g20 = BandGain(fh * 2.0f, nyq) * (0.06f + 0.04f * ld) * scream;

        for (int i = 0; i < data.Length; i += channels)
        {
            combLp += 0.03f * (Noise() - combLp);
            phase += inc;
            if (phase >= 1.0)
            {
                phase -= 1.0;
                cyl = (cyl + 1) % prof.cylinders;
                float a = cylAmp[cyl] * (0.35f + 0.65f * ld) * (1f - cut) * (1f + 0.14f * combLp);
                if ((cyl & 1) == 0) pulseA += a;
                else pulseB += a;

                if (ld < 0.16f && fh > 360f && Noise() > 0.90f)
                    crackle = 0.85f + 0.45f * Noise();
            }

            crankPhase += crankInc;
            if (crankPhase >= 1.0) crankPhase -= 1.0;

            pulseA *= decay;
            pulseB *= decay;

            float eL = 0f, eR = 0f;
            if (synthEng)
            {
                // Phase-locked multi-order F1 harmonic wave (warm chest spine without high-pitch whistle)
                float th = (float)(crankPhase * halfCyl * twoPi);
                float harm =
                    Mathf.Sin(th * 0.25f) * g025 +
                    Mathf.Sin(th * 0.5f) * g05 +
                    Mathf.Sin(th) * g10 +
                    Mathf.Sin(th * 1.5f + 0.4f) * g15 +
                    Mathf.Sin(th * 2.0f + 0.8f) * g20;
                harm *= (1f - cut * 0.85f);

                // Uneven cylinder combustion pulses + resonant formants + warm cabinet low-pass
                float nA = Noise(), nB = Noise();
                float xA = pulseA * (1f + 0.24f * nA);
                float xB = pulseB * (1f + 0.24f * nB);
                hpA += hpK * (xA - hpA); xA -= hpA;
                hpB += hpK * (xB - hpB); xB -= hpB;

                float bright = 0.34f + 0.45f * ld;
                float pulseResA = xA * 0.36f + bodyA.Process(xA) * 1.15f + f2A.Process(xA) * 0.72f * bright + f3A.Process(xA) * 0.28f * ld;
                float pulseResB = xB * 0.36f + bodyB.Process(xB) * 1.15f + f2B.Process(xB) * 0.72f * bright + f3B.Process(xB) * 0.28f * ld;

                float sA = exhaustLpA.Process((float)System.Math.Tanh((harm * 0.54f + pulseResA * 0.58f) * drive));
                float sB = exhaustLpB.Process((float)System.Math.Tanh((harm * 0.54f + pulseResB * 0.58f + 0.02f) * drive));

                crackle *= crackDecay;
                float pop = crackle > 0.01f ? crackBp.Process(Noise() * crackle) * 1.9f : 0f;

                // Subtle band-limited turbo air spool & lift-off flutter (zero pure-sine whistle)
                float hybridFx = 0f;
                if (prof.turbo)
                {
                    if (bst > 0.05f)
                    {
                        turboPh = turboPh + 0.14f * (Noise() - turboPh);
                        hybridFx += turboPh * 0.04f * bst;
                    }
                    if (wg > 0.01f)
                    {
                        wgPh += 24f / sr;
                        if (wgPh > 1f) wgPh -= 1f;
                        float flutter = 0.5f + 0.5f * Mathf.Sin(wgPh * twoPi);
                        hybridFx += Noise() * flutter * wg * 0.08f;
                    }
                }

                // Automatic level control for punchy consistency across RPM range
                float mono = sA + sB;
                rms += 0.0006f * (mono * mono - rms);
                float target = 0.42f / Mathf.Sqrt(Mathf.Max(rms, 0.0005f));
                agc += 0.001f * (Mathf.Clamp(target, 0.35f, 2.6f) - agc);

                eL = ((sA * 0.60f + sB * 0.40f) * agc + pop + hybridFx) * g;
                delay[delayPos] = ((sB * 0.60f + sA * 0.40f) * agc + pop + hybridFx) * g;
                eR = delay[(delayPos - delaySamples + delay.Length) % delay.Length];
                delayPos = (delayPos + 1) % delay.Length;
            }

            // Kerb rumble
            float kerbS = 0f;
            if (kLvl > 0.01f)
            {
                kerbPh += kHz / sr;
                if (kerbPh >= 1.0) kerbPh -= 1.0;
                kerbS = (Mathf.Sin((float)kerbPh * twoPi) * 0.7f + Mathf.Sin((float)kerbPh * twoPi * 2f) * 0.3f) * kLvl * 0.24f;
            }

            // Wind
            windLp += aw * (Noise() - windLp);
            float windS = windLp * wd * 1.4f;

            // Soft low-passed rubber scrub noise (no pure 1050 Hz sine screech)
            float squealS = 0f;
            if (sq > 0.01f)
            {
                sqBp1 += 0.18f * (Noise() - sqBp1);
                sqBp2 += 0.18f * (sqBp1 - sqBp2);
                squealS = (sqBp1 - sqBp2) * 0.55f * sq * volume * 0.14f;
            }

            float common = windS + squealS + kerbS;
            if (channels >= 2)
            {
                float lOut = eL * 0.72f + common;
                float rOut = eR * 0.72f + common;
                data[i] = float.IsFinite(lOut) ? Mathf.Clamp(lOut, -0.95f, 0.95f) : 0f;
                data[i + 1] = float.IsFinite(rOut) ? Mathf.Clamp(rOut, -0.95f, 0.95f) : 0f;
                for (int c = 2; c < channels; c++)
                {
                    float cOut = (eL + eR) * 0.36f + common;
                    data[i + c] = float.IsFinite(cOut) ? Mathf.Clamp(cOut, -0.95f, 0.95f) : 0f;
                }
            }
            else
            {
                float mOut = (eL + eR) * 0.38f + common;
                data[i] = float.IsFinite(mOut) ? Mathf.Clamp(mOut, -0.95f, 0.95f) : 0f;
            }
        }
    }

    // ---------- Self-test for CI / batchmode build verification ----------

    public static bool RunAudioSelfTest(out string report)
    {
        var sb = new System.Text.StringBuilder();
        bool allPass = true;
        int prevSetting = GameSession.Settings.engineSound;

        var temp = new GameObject("AudioSelfTestTemp") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var src = temp.AddComponent<AudioSource>();
            src.playOnAwake = false;
            var ea = temp.AddComponent<EngineAudio>();

            for (int p = 0; p < Profiles.Length; p++)
            {
                ea.initialized = false;
                GameSession.Settings.engineSound = p;
                ea.EnsureInitialized();
                ea.sampleRate = 48000;
                ea.volume = 0.75f;
                ea.muteSynthEngine = false;
                ea.rms = 0.06f;
                ea.agc = 1f;
                ea.pulseA = ea.pulseB = 0f;
                ea.exhaustLpA.Reset();
                ea.exhaustLpB.Reset();

                (float idleRms, float idlePeak, bool idleFinite) = MeasureBuffer(ea, 4500f, 0.08f, 0.42f);
                (float loadRms, float loadPeak, bool loadFinite) = MeasureBuffer(ea, 12000f, 1.0f, 0.72f);

                bool ok = idleFinite && loadFinite
                       && idleRms >= 0.04f && idleRms <= 0.55f
                       && loadRms >= 0.12f && loadRms <= 0.75f
                       && loadRms > idleRms
                       && idlePeak <= 0.96f && loadPeak <= 0.96f;
                if (!ok) allPass = false;
                sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {EngineNames[p]}: idleRMS={idleRms:0.000} (peak={idlePeak:0.000}), fullLoadRMS={loadRms:0.000} (peak={loadPeak:0.000}), finite={idleFinite && loadFinite}");
            }
        }
        finally
        {
            GameSession.Settings.engineSound = prevSetting;
            DestroyImmediate(temp);
        }

        report = sb.ToString();
        return allPass;
    }

    static (float rms, float peak, bool allFinite) MeasureBuffer(EngineAudio ea, float rpm, float ld, float gain)
    {
        float chz = rpm / 60f;
        ea.crankHz = chz;
        ea.firingHz = chz * ea.prof.cylinders * 0.5f;
        ea.load = ld;
        ea.engineGain = gain;
        ea.limiterCut = 0f;
        ea.shiftCut = 0f;
        ea.wind = 0f;
        ea.squeal = 0f;
        ea.kerbLevel = 0f;
        ea.boost = ea.prof.turbo ? ld * 0.9f : 0f;
        ea.mguk = ea.prof.turbo ? ld * 0.8f : 0f;
        ea.wastegate = 0f;

        // Warm-up 1024 stereo frames then measure 2048 stereo frames
        var warm = new float[1024 * 2];
        ea.Synthesize(warm, 2);

        var buf = new float[2048 * 2];
        ea.Synthesize(buf, 2);
        double sumSq = 0.0;
        float peak = 0f;
        bool finite = true;
        for (int i = 0; i < buf.Length; i++)
        {
            float v = buf[i];
            if (!float.IsFinite(v)) { finite = false; continue; }
            float av = Mathf.Abs(v);
            if (av > peak) peak = av;
            sumSq += v * v;
        }
        float rms = (float)System.Math.Sqrt(sumSq / buf.Length);
        return (rms, peak, finite);
    }

    // ---------- One-shot clips, synthesised once ----------

    static void BuildClips()
    {
        if (clunk != null) return;
        int sr = AudioSettings.outputSampleRate > 1000 ? AudioSettings.outputSampleRate : 48000;
        var rnd = new System.Random(5);
        float N() => (float)rnd.NextDouble() * 2f - 1f;

        clunk = Make("ShiftClunk", sr, 0.065f, (t, n) =>
            (Mathf.Sin(t * 2f * Mathf.PI * (140f - 55f * (t / 0.065f))) * 0.72f + N() * 0.30f * Mathf.Exp(-t * 95f)) * Mathf.Exp(-t * 48f));
        float thudLp = 0f;
        thud = Make("CrashThud", sr, 0.72f, (t, n) =>
        {
            thudLp += 0.10f * (N() - thudLp);
            return Mathf.Sin(t * 2f * Mathf.PI * (62f - 36f * t)) * Mathf.Exp(-t * 6.5f) * 0.95f
                 + thudLp * Mathf.Exp(-t * 14f) * 0.75f
                 + N() * Mathf.Exp(-t * 42f) * 0.25f;
        });
        radio = Make("RadioBeep", sr, 0.55f, (t, n) =>
        {
            float squelch = t < 0.06f ? N() * 0.6f * (1f - t / 0.06f) : 0f;
            float beep = (t > 0.08f && t < 0.16f) || (t > 0.2f && t < 0.28f) ? Mathf.Sin(t * 2f * Mathf.PI * 1250f) * 0.45f : 0f;
            float hiss = t > 0.3f ? N() * 0.08f * (1f - (t - 0.3f) / 0.25f) : 0f;
            return squelch + beep + hiss;
        });
        float gunLp = 0f;
        gun = Make("WheelGun", sr, 2.2f, (t, n) =>
        {
            float Burst(float t0, float dur)
            {
                float u = t - t0;
                if (u < 0f || u > dur) return 0f;
                float env = Mathf.Min(1f, u / 0.025f) * Mathf.Min(1f, (dur - u) / 0.035f);
                float hammer = 0.5f + 0.5f * Mathf.Sin(u * 2f * Mathf.PI * 36f);
                float spinHz = 1220f + 260f * Mathf.Sin(u * 2f * Mathf.PI * 7f) + 180f * (u / dur);
                gunLp += 0.28f * (N() - gunLp);
                return (Mathf.Sin(u * 2f * Mathf.PI * spinHz) * (0.40f + 0.45f * hammer) + (N() - gunLp) * 0.42f * hammer) * env;
            }
            return (Burst(0.10f, 0.36f) + Burst(1.50f, 0.36f)) * 0.95f;
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
