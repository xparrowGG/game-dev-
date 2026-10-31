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
    public float parkLane = -10f;      // after finishing, pull onto the run-off inside the barrier

    [System.NonSerialized] public float parkProgress = float.NaN;   // where to stop after the race

    [System.NonSerialized] public RaceManager manager;
    public float Speed { get; private set; }
    public Vector3 Velocity => transform.forward * Speed + transform.right * lateralVel;

    static readonly float[] Lanes = { -4.5f, -1.5f, 1.5f, 4.5f };
    Rigidbody rb;
    Transform[] wheels = new Transform[4];
    float laneTarget, wheelSpin;
    float lateralVel, wobble, wobbleVel, recoverTimer;   // crash reaction

    // Called by the player's car on contact: dv is this car's change in velocity.
    public void ApplyImpact(Vector3 dv, Vector3 point)
    {
        Speed = Mathf.Max(0f, Speed + Vector3.Dot(dv, transform.forward));
        lateralVel += Vector3.Dot(dv, transform.right);
        Vector3 lever = point - transform.position; lever.y = 0;
        wobbleVel += Mathf.Clamp(Vector3.Cross(lever, dv).y * 9f, -200f, 200f);
        recoverTimer = Mathf.Clamp(dv.magnitude * 0.25f, 0.5f, 2.5f);
    }

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
        float laneSpeed = 2.5f;
        if (finished)
        {
            // Cool down, pull off to the side and stop at the parking spot
            float remaining = float.IsNaN(parkProgress) ? 0f : parkProgress - progress;
            desired = Mathf.Min(22f, Mathf.Sqrt(2f * 7f * Mathf.Max(0f, remaining)));
            if (remaining < 0.3f) desired = 0f;
            laneTarget = parkLane;
            laneSpeed = 3.5f;
        }
        else if (racing)
        {
            desired = maxSpeed * skill;
            // Brake early enough for the tightest corner in the next 80 m
            for (float ahead = 0f; ahead <= 80f; ahead += 8f)
            {
                float vCorner = Mathf.Sqrt(cornerGrip * skill * track.RadiusAt(progress + ahead));
                desired = Mathf.Min(desired, Mathf.Sqrt(vCorner * vCorner + 2f * braking * ahead));
            }
            desired = Mathf.Min(desired, AvoidTraffic());
        }

        // After being hit: knocked sideways and twisted, then the driver gathers it up and rejoins
        if (recoverTimer > 0f)
        {
            recoverTimer -= dt;
            desired = Mathf.Min(desired, 14f);
        }
        lane = Mathf.Clamp(lane + lateralVel * dt, -10.8f, 10.8f);   // stay inside the barriers
        lateralVel = Mathf.MoveTowards(lateralVel, 0f, 10f * dt);
        wobbleVel += (-wobble * 30f - wobbleVel * 6f) * dt;
        wobble += wobbleVel * dt;

        Speed = Mathf.MoveTowards(Speed, desired, (desired < Speed ? braking : acceleration) * dt);
        if (Speed > 0.5f && Mathf.Abs(lateralVel) < 0.5f) lane = Mathf.MoveTowards(lane, laneTarget, laneSpeed * dt);
        progress += Speed * dt;

        Vector3 pos = track.PointAt(progress) + track.RightAt(progress) * lane;
        pos.y = 0.02f;
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.MovePosition(pos);
        rb.MoveRotation(Quaternion.LookRotation(track.TangentAt(progress), Vector3.up) * Quaternion.Euler(0, wobble, 0));
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
