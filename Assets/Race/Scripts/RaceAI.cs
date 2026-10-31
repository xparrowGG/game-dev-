using UnityEngine;

// AI racer: follows the track centreline on a chosen lane, brakes for corners ahead,
// and changes lane to overtake slower cars.
[RequireComponent(typeof(Rigidbody))]
public class RaceAI : MonoBehaviour
{
    public string driverName = "AI";
    public Color color = Color.red;
    public float maxSpeed = 52f;       // m/s on straights
    public float cornerGrip = 30f;     // lateral grip, higher = faster corners
    public float acceleration = 9f;
    public float braking = 15f;
    public float lane;                 // sideways offset from centreline
    public float progress;             // total distance from the start line (negative on the grid)

    [System.NonSerialized] public RaceManager manager;
    public float Speed { get; private set; }

    static readonly float[] Lanes = { -4.5f, -1.5f, 1.5f, 4.5f };
    Rigidbody rb;
    Transform[] wheels = new Transform[4];
    float laneTarget, wheelSpin;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        string[] names = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        for (int i = 0; i < 4; i++) wheels[i] = transform.Find("Wheels/" + names[i]);
        laneTarget = lane;
    }

    public void Step(float dt, bool racing, bool finished, float skill)
    {
        var track = manager.track;
        float desired = 0f;
        if (racing)
        {
            desired = finished ? 18f : maxSpeed * skill;
            // Brake early enough for the tightest corner in the next 80 m
            for (float ahead = 0f; ahead <= 80f; ahead += 8f)
            {
                float vCorner = Mathf.Sqrt(cornerGrip * skill * track.RadiusAt(progress + ahead));
                desired = Mathf.Min(desired, Mathf.Sqrt(vCorner * vCorner + 2f * braking * ahead));
            }
            desired = Mathf.Min(desired, AvoidTraffic());
        }

        Speed = Mathf.MoveTowards(Speed, desired, (desired < Speed ? braking : acceleration) * dt);
        lane = Mathf.MoveTowards(lane, laneTarget, 2.5f * dt);
        progress += Speed * dt;

        Vector3 pos = track.PointAt(progress) + track.RightAt(progress) * lane;
        pos.y = 0.02f;
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.MovePosition(pos);
        rb.MoveRotation(Quaternion.LookRotation(track.TangentAt(progress), Vector3.up));
    }

    float AvoidTraffic()
    {
        float limit = float.MaxValue;
        RaceManager.Racer blocker = null;
        foreach (var r in manager.racers)
        {
            if (r.ai == this) continue;
            float dp = r.progress - progress;
            if (dp > 0f && dp < 14f && Mathf.Abs(r.lateral - lane) < 2.6f && (blocker == null || dp < blocker.progress - progress))
                blocker = r;
        }
        if (blocker == null) return limit;

        // Try the nearest free lane to overtake
        float bestLane = float.NaN;
        foreach (float c in Lanes)
        {
            if (Mathf.Abs(c - lane) < 0.1f) continue;
            bool free = true;
            foreach (var r in manager.racers)
            {
                if (r.ai == this) continue;
                float dp = r.progress - progress;
                if (dp > -7f && dp < 16f && Mathf.Abs(r.lateral - c) < 2.6f) { free = false; break; }
            }
            if (free && (float.IsNaN(bestLane) || Mathf.Abs(c - lane) < Mathf.Abs(bestLane - lane))) bestLane = c;
        }
        if (!float.IsNaN(bestLane)) laneTarget = bestLane;

        // Don't drive into the car ahead while waiting for a gap
        float gap = blocker.progress - progress;
        if (gap < 9f && float.IsNaN(bestLane)) limit = Mathf.Max(0f, blocker.speed - (9f - gap));
        return limit;
    }

    void Update()
    {
        wheelSpin = (wheelSpin + Speed * Time.deltaTime / 0.34f * Mathf.Rad2Deg) % 360f;
        for (int i = 0; i < 4; i++)
            if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(wheelSpin, 0, 0);
    }
}
