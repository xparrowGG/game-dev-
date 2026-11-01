using UnityEngine;

// Procedural turbo-hybrid V6 sound: firing frequency follows engine rpm (3 pulses per revolution),
// mixed harmonics plus a little intake noise, louder on throttle. No audio assets needed.
[RequireComponent(typeof(AudioSource))]
public class EngineAudio : MonoBehaviour
{
    public VehicleBase car;
    public float volume = 0.6f;

    volatile float freq = 200f, gain;
    double phase, phase2;
    int sampleRate = 48000;
    uint noiseState = 22222;
    float lastSpeed;

    void Start()
    {
        if (car == null) car = GetComponentInParent<VehicleBase>();
        sampleRate = AudioSettings.outputSampleRate;
        var src = GetComponent<AudioSource>();
        src.clip = AudioClip.Create("EngineCarrier", sampleRate, 1, sampleRate, false);   // silent carrier, filled below
        src.loop = true;
        src.spatialBlend = 0f;
        src.playOnAwake = true;
        src.Play();
        var s = GameSession.Settings;
        volume = s.engineVolume;
    }

    void Update()
    {
        if (car == null) return;
        float rpm = car.Rpm > 0f ? car.Rpm : 3000f + car.SpeedKmh * 40f;
        freq = rpm / 60f * 3f;
        float accel = (car.Speed - lastSpeed) / Mathf.Max(Time.deltaTime, 0.001f);
        lastSpeed = car.Speed;
        float load = Mathf.Clamp01(0.35f + accel * 0.05f);
        gain = volume * Mathf.Lerp(0.25f, 0.6f, load) * (Time.timeScale > 0f ? 1f : 0f);
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        double inc = freq / sampleRate;
        float g = gain;
        for (int i = 0; i < data.Length; i += channels)
        {
            phase += inc; if (phase > 1.0) phase -= 1.0;
            phase2 += inc * 0.5; if (phase2 > 1.0) phase2 -= 1.0;
            float saw = (float)(phase * 2.0 - 1.0);
            float sub = Mathf.Sin((float)(phase2 * 2.0 * Mathf.PI));
            float harm = Mathf.Sin((float)(phase * 4.0 * Mathf.PI)) * 0.35f;
            noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
            float noise = (noiseState / (float)uint.MaxValue * 2f - 1f) * 0.08f;
            float sample = (saw * 0.45f + sub * 0.3f + harm + noise) * g * 0.5f;
            for (int c = 0; c < channels; c++) data[i + c] = sample;
        }
    }
}
