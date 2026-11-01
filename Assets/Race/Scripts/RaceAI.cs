using UnityEngine;

// AI racer: follows the track centreline (including its height) on a chosen lane, brakes for the
// corners ahead using a grip model (optionally with aerodynamic downforce), overtakes slower cars by
// changing lane, reacts to being hit, and after the flag cools down and leaves the track.
[RequireComponent(typeof(Rigidbody))]
public class RaceAI : MonoBehaviour
{
    public string driverName = "AI";
    public string driverCode = "AI";
    public int team;
    public Color color = Color.red;
    public float maxSpeed = 52f;       // m/s on straights
    public float cornerGrip = 30f;     // lateral grip at low speed (m/s²)
    public float aeroGrip;             // extra lateral grip per (m/s)²; 0 = no downforce model
    public float acceleration = 9f;
    public float braking = 15f;        // m/s² at low speed
    public float aeroBraking;          // extra braking per (m/s)²
    public float lane;                 // sideways offset from centreline
    public float progress;             // total distance from the start line (negative on the grid)
    public float parkLane = -10f;      // after finishing, pull off to this side offset
    public bool leaveAfterFinish;      // disappear (into the pit lane) once parked

    [System.NonSerialized] public float parkProgress = float.NaN;   // where to stop after the race
    [System.NonSerialized] public RaceManager manager;
    public float Speed { get; private set; }
    public bool Parked { get; private set; }
    public Vector3 Velocity => transform.forward * Speed + transform.right * lateralVel;

    static readonly float[] WideLanes = { -4.5f, -1.5f, 1.5f, 4.5f };
    static readonly float[] NarrowLanes = { -2.6f, 0f, 2.6f };
    Rigidbody rb;
    Transform[] wheels = new Transform[4];
    float laneTarget, wheelSpin;
    float lateralVel, wobble, wobbleVel, recoverTimer;   // crash reaction

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        string[] names = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        for (int i = 0; i < 4; i++) wheels[i] = transform.Find("Wheels/" + names[i]);
        laneTarget = lane;
    }

    public void SetGrid(float startProgress, float startLane)
    {
        progress = startProgress;
        lane = laneTarget = startLane;
        Speed = 0f;
        Parked = false;
        if (manager != null) Place();
    }

    // Called by the player's car on contact: dv is this car's change in velocity.
    public void ApplyImpact(Vector3 dv, Vector3 point)
    {
        Speed = Mathf.Max(0f, Speed + Vector3.Dot(dv, transform.forward));
        lateralVel += Vector3.Dot(dv, transform.right);
        Vector3 lever = point - transform.position; lever.y = 0;
        wobbleVel += Mathf.Clamp(Vector3.Cross(lever, dv).y * 9f, -200f, 200f);
        recoverTimer = Mathf.Clamp(dv.magnitude * 0.25f, 0.5f, 2.5f);
    }

    float CornerSpeed(float radius, float skill)
    {
        float a0 = cornerGrip * skill;
        if (aeroGrip <= 0f) return Mathf.Sqrt(a0 * radius);
        // lateral grip a(v) = a0 + k·v²  →  v² = a0·R / (1 − k·R)
        float k = aeroGrip * skill;
        float denom = 1f - k * radius;
        return denom <= 0.05f ? 999f : Mathf.Sqrt(a0 * radius / denom);
    }

    public void Step(float dt, bool racing, bool finished, float skill)
    {
        var track = manager.track;
        bool narrow = track.halfWidth <= 6f;
        float desired = 0f;
        float laneSpeed = 2.5f;
        float laneLimit = track.halfWidth - 1.4f;
        if (finished)
        {
            // Cool down, pull off to the side and stop (or head into the pit lane)
            float remaining = float.IsNaN(parkProgress) ? 0f : parkProgress - progress;
            desired = Mathf.Min(22f, Mathf.Sqrt(2f * 7f * Mathf.Max(0f, remaining)));
            if (remaining < 0.3f) desired = 0f;
            if (!leaveAfterFinish || remaining < 30f) laneTarget = parkLane;
            laneSpeed = leaveAfterFinish ? 5f : 3.5f;
            laneLimit = Mathf.Abs(parkLane) + 0.5f;
            if (leaveAfterFinish && Mathf.Abs(lane - parkLane) < 0.6f) desired = Mathf.Max(desired, 4f);
            if (!Parked && ((leaveAfterFinish && Mathf.Abs(lane - parkLane) < 0.5f && remaining < 1f) || (!leaveAfterFinish && remaining < 0.3f && Speed < 0.1f)))
            {
                Parked = true;
                if (leaveAfterFinish) gameObject.SetActive(false);
            }
        }
        else if (racing)
        {
            desired = maxSpeed * skill;
            // Brake early enough for every corner within braking distance
            float horizon = aeroGrip > 0f ? 220f : 80f;
            for (float ahead = 0f; ahead <= horizon; ahead += 8f)
            {
                float vCorner = CornerSpeed(track.RadiusAt(progress + ahead), skill);
                float decel = braking + aeroBraking * vCorner * vCorner * 0.5f;
                desired = Mathf.Min(desired, Mathf.Sqrt(vCorner * vCorner + 2f * decel * ahead));
            }
            desired = Mathf.Min(desired, AvoidTraffic(narrow ? NarrowLanes : WideLanes));
        }

        // After being hit: knocked sideways and twisted, then the driver gathers it up and rejoins
        if (recoverTimer > 0f)
        {
            recoverTimer -= dt;
            desired = Mathf.Min(desired, 14f);
        }
        lane = Mathf.Clamp(lane + lateralVel * dt, -laneLimit, laneLimit);
        lateralVel = Mathf.MoveTowards(lateralVel, 0f, 10f * dt);
        wobbleVel += (-wobble * 30f - wobbleVel * 6f) * dt;
        wobble += wobbleVel * dt;

        float accel = acceleration * (1f - 0.7f * Mathf.Pow(Mathf.Clamp01(Speed / Mathf.Max(maxSpeed, 1f)), 2f)) + 0.5f;
        float brake = braking + aeroBraking * Speed * Speed;
        Speed = Mathf.MoveTowards(Speed, desired, (desired < Speed ? brake : accel) * dt);
        if (Speed > 0.5f && Mathf.Abs(lateralVel) < 0.5f) lane = Mathf.MoveTowards(lane, laneTarget, laneSpeed * dt);
        progress += Speed * dt;
        Place();
    }

    void Place()
    {
        var track = manager.track;
        Vector3 pos = track.PointAt(progress) + track.RightAt(progress) * lane;
        pos.y += 0.03f;
        if (rb == null) rb = GetComponent<Rigidbody>();
        Quaternion rot = Quaternion.LookRotation(track.TangentAt(progress), Vector3.up) * Quaternion.Euler(0, wobble, 0);
        if (Application.isPlaying) { rb.MovePosition(pos); rb.MoveRotation(rot); }
        else transform.SetPositionAndRotation(pos, rot);
    }

    float AvoidTraffic(float[] lanes)
    {
        float limit = float.MaxValue;
        RaceManager.Racer blocker = null;
        foreach (var r in manager.racers)
        {
            if (r.ai == this || r.retired) continue;
            float dp = r.progress - progress;
            if (dp > 0f && dp < 16f && Mathf.Abs(r.lateral - lane) < 2.3f && (blocker == null || dp < blocker.progress - progress))
                blocker = r;
        }
        if (blocker == null) return limit;

        // Try the nearest free lane to overtake
        float bestLane = float.NaN;
        foreach (float c in lanes)
        {
            if (Mathf.Abs(c - lane) < 0.1f) continue;
            bool free = true;
            foreach (var r in manager.racers)
            {
                if (r.ai == this || r.retired) continue;
                float dp = r.progress - progress;
                if (dp > -7f && dp < 18f && Mathf.Abs(r.lateral - c) < 2.3f) { free = false; break; }
            }
            if (free && (float.IsNaN(bestLane) || Mathf.Abs(c - lane) < Mathf.Abs(bestLane - lane))) bestLane = c;
        }
        if (!float.IsNaN(bestLane)) laneTarget = bestLane;

        // Don't drive into the car ahead while waiting for a gap
        float gap = blocker.progress - progress;
        if (gap < 10f && float.IsNaN(bestLane)) limit = Mathf.Max(0f, blocker.speed - (10f - gap) * 0.8f);
        return limit;
    }

    void Update()
    {
        wheelSpin = (wheelSpin + Speed * Time.deltaTime / 0.36f * Mathf.Rad2Deg) % 360f;
        for (int i = 0; i < 4; i++)
            if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(wheelSpin, 0, 0);
    }
}
