using UnityEngine;

// A team's pit box: the crew moves in around the car during a stop and steps back on release;
// the release light shows red while the car is being serviced and green to go.
public class PitBox : MonoBehaviour
{
    public int team;
    public float trackDistance;          // where the car stops, along the track
    public Transform[] crew;
    public Vector3[] workOffsets;        // local offsets each crew member moves by when working
    public Renderer releaseLight;
    public Material lightRed, lightGreen, lightOff;

    Vector3[] home;
    bool working;
    float blend;

    void Awake()
    {
        home = new Vector3[crew.Length];
        for (int i = 0; i < crew.Length; i++) home[i] = crew[i].localPosition;
    }

    public void SetWorking(bool on)
    {
        working = on;
        if (releaseLight != null) releaseLight.sharedMaterial = on ? lightRed : lightOff;
    }

    public void ShowGreen()
    {
        if (releaseLight != null) releaseLight.sharedMaterial = lightGreen;
    }

    void Update()
    {
        blend = Mathf.MoveTowards(blend, working ? 1f : 0f, Time.deltaTime * 3f);
        float bob = working ? Mathf.Sin(Time.time * 18f) * 0.03f : 0f;
        for (int i = 0; i < crew.Length; i++)
            crew[i].localPosition = home[i] + workOffsets[i] * blend + Vector3.up * bob * blend;
    }
}
