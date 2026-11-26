using UnityEngine;

// Distant crowd and city ambience: soft filtered noise with slow swells, plus occasional cheers.
[RequireComponent(typeof(AudioSource))]
public class AmbientAudio : MonoBehaviour
{
    public float volume = 0.18f;
    public bool positional;               // a grandstand emitter: 3D, louder up close, cheers as cars pass
    public Transform watch;               // the car the crowd reacts to (player)
    volatile float gain, cheer;
    float lp1, lp2, swellPhase;
    uint noise = 987654;
    int sampleRate = 48000;

    void Start()
    {
        sampleRate = AudioSettings.outputSampleRate;
        var src = GetComponent<AudioSource>();
        src.clip = AudioClip.Create("AmbientCarrier", sampleRate, 1, sampleRate, false);
        src.loop = true;
        src.spatialBlend = positional ? 1f : 0f;
        if (positional)
        {
            src.minDistance = 18f; src.maxDistance = 260f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            src.spread = 120f;
            swellPhase = Random.value;
        }
        src.Play();
        volume *= GameSession.Settings.masterVolume + 0.2f;
    }

    void Update()
    {
        gain = Time.timeScale > 0f ? volume : 0f;
        cheer = Mathf.MoveTowards(cheer, 0f, Time.deltaTime * 0.3f);
        if (positional && watch != null)
        {
            // Crowd surges as the car goes past the stand
            float d = Vector3.Distance(watch.position, transform.position);
            cheer = Mathf.Max(cheer, Mathf.Clamp01(1f - d / 70f) * 0.9f);
        }
    }

    public void Cheer() => cheer = 1f;

    void OnAudioFilterRead(float[] data, int channels)
    {
        float a1 = 1f - Mathf.Exp(-2f * Mathf.PI * 700f / sampleRate);
        float a2 = 1f - Mathf.Exp(-2f * Mathf.PI * 2200f / sampleRate);
        float g = gain, ch = cheer;
        for (int i = 0; i < data.Length; i += channels)
        {
            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
            float n = noise / (float)uint.MaxValue * 2f - 1f;
            lp1 += a1 * (n - lp1);
            lp2 += a2 * (n - lp2);
            swellPhase += 0.25f / sampleRate;
            float swell = 0.75f + 0.25f * Mathf.Sin(swellPhase * 2f * Mathf.PI);
            float s = (lp1 * 0.9f * swell + (lp2 - lp1) * 0.6f * ch) * g;
            for (int c = 0; c < channels; c++) data[i + c] = s;
        }
    }
}
