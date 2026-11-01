using UnityEngine;

// Distant crowd and city ambience: soft filtered noise with slow swells, plus occasional cheers.
[RequireComponent(typeof(AudioSource))]
public class AmbientAudio : MonoBehaviour
{
    public float volume = 0.18f;
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
        src.spatialBlend = 0f;
        src.Play();
        volume *= GameSession.Settings.masterVolume + 0.2f;
    }

    void Update()
    {
        gain = Time.timeScale > 0f ? volume : 0f;
        cheer = Mathf.MoveTowards(cheer, 0f, Time.deltaTime * 0.3f);
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
