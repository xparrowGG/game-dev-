using UnityEngine;

// A trackside marshal post: shows a green flag, waves yellow when the race manager reports
// an incident in the section ahead of it.
public class MarshalPost : MonoBehaviour
{
    public float trackDistance;
    public Renderer flag;
    public Transform flagPivot;
    public Material green, yellow;

    public bool Yellow { get; private set; }
    float t;

    public void SetYellow(bool on)
    {
        if (on == Yellow) return;
        Yellow = on;
        if (flag != null) flag.sharedMaterial = on ? yellow : green;
    }

    void Update()
    {
        if (flagPivot == null) return;
        t += Time.deltaTime;
        float a = Yellow ? Mathf.Sin(t * 9f) * 40f : Mathf.Sin(t * 1.3f) * 5f;
        flagPivot.localRotation = Quaternion.Euler(0, 0, a);
    }
}
