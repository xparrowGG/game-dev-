using UnityEngine;

// AI racer: follows the track centreline (including its height) on a chosen lane, brakes for the
// corners ahead using a grip model (optionally with aerodynamic downforce), overtakes slower cars by
// changing lane, reacts to being hit, wears its tyres, makes a pit stop for fresh tyres on its planned
// lap (pit lane speed limit, stop at the team's box, release, rejoin), and after the flag leaves the track.
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

    [Header("Tyres & strategy")]
    public Compound compound = Compound.Medium;
    public Compound nextCompound = Compound.Hard;
    public float tyreWear;
    public int pitLap = -1;            // pit at the end of this lap (-1 = no stop planned)

    public enum PitState { None, Entering, InLane, Stopped, Leaving }
    public PitState Pit { get; private set; }
    public int PitStops { get; private set; }
    public bool TyresOff { get; private set; }
    public bool TyresChanged { get; set; }   // set when new tyres go on; the race manager repaints them

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
    float stopTime, stopElapsed;

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
        Pit = PitState.None;
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

    float GripFactor => manager != null && manager.HasPitLane ? Tyres.GripFactor(compound, tyreWear) : 1f;

    float CornerSpeed(float radius, float skill)
    {
        float g = skill * GripFactor;
        float a0 = cornerGrip * g;
        if (aeroGrip <= 0f) return Mathf.Sqrt(a0 * radius);
        // lateral grip a(v) = a0 + k·v²  →  v² = a0·R / (1 − k·R)
        float k = aeroGrip * g;
        float denom = 1f - k * radius;
        return denom <= 0.05f ? 999f : Mathf.Sqrt(a0 * radius / denom);
    }

    float RacingSpeed(float skill, RaceTrack track)
    {
        float desired = maxSpeed * skill;
        float horizon = aeroGrip > 0f ? 220f : 80f;
        for (float ahead = 0f; ahead <= horizon; ahead += 8f)
        {
            float vCorner = CornerSpeed(track.RadiusAt(progress + ahead), skill);
            float decel = braking + aeroBraking * vCorner * vCorner * 0.5f;
            desired = Mathf.Min(desired, Mathf.Sqrt(vCorner * vCorner + 2f * decel * ahead));
        }
        return desired;
    }

    public void Step(float dt, bool racing, bool finished, float skill)
    {
        var track = manager.track;
        bool narrow = track.halfWidth <= 6f;
        float desired = 0f;
        float laneSpeed = 2.5f;
        float laneLimit = track.halfWidth - 1.4f;
        float d = Mathf.Repeat(progress, track.length);
        TyresOff = false;

        if (finished && Pit == PitState.None)
        {
            float remaining = float.IsNaN(parkProgress) ? 0f : parkProgress - progress;
            laneLimit = Mathf.Abs(parkLane) + 0.5f;
            if (leaveAfterFinish)
            {
                // Cool-down, then turn into the pit lane and leave the track
                desired = remaining > 45f ? 22f : 12f;
                if (remaining < 45f) { laneTarget = parkLane; laneSpeed = 6f; }
                if (!Parked && Mathf.Abs(lane - parkLane) < 0.5f) { Parked = true; gameObject.SetActive(false); }
            }
            else
            {
                desired = Mathf.Min(22f, Mathf.Sqrt(2f * 7f * Mathf.Max(0f, remaining)));
                if (remaining < 0.3f) desired = 0f;
                laneTarget = parkLane;
                laneSpeed = 3.5f;
                if (!Parked && remaining < 0.3f && Speed < 0.1f) Parked = true;
            }
        }
        else if (racing || Pit != PitState.None)
        {
            var m = manager;
            int lap = Mathf.FloorToInt(progress / track.length) + 1;
            switch (Pit)
            {
                case PitState.None:
                    desired = RacingSpeed(skill, track);
                    desired = Mathf.Min(desired, AvoidTraffic(narrow ? NarrowLanes : WideLanes));
                    if (m.HasPitLane && pitLap > 0 && lap == pitLap && !finished
                        && RaceManager.InWindow(d, m.pitEntryStart - 260f, m.pitEntryStart - 20f, track.length))
                        Pit = PitState.Entering;
                    break;

                case PitState.Entering:
                {
                    // Brake to the pit speed limit at the entry line, then move across into the pit lane
                    float toEntry = Mathf.Repeat(m.pitEntryStart - d, track.length);
                    if (toEntry > track.length / 2f) toEntry = 0f;
                    desired = Mathf.Min(RacingSpeed(skill, track), Mathf.Sqrt(m.pitSpeed * m.pitSpeed + 2f * 14f * toEntry));
                    desired = Mathf.Min(desired, FollowLimit(false));
                    if (RaceManager.InWindow(d, m.pitEntryStart, m.pitEntryEnd + 20f, track.length))
                    {
                        laneTarget = m.pitLaneOffset;
                        laneSpeed = 6f;
                        desired = Mathf.Min(desired, m.pitSpeed);
                    }
                    laneLimit = m.pitLaneOffset + 0.5f;
                    if (lane > m.pitLaneOffset - 2f) Pit = PitState.InLane;
                    break;
                }

                case PitState.InLane:
                {
                    laneTarget = m.pitLaneOffset;
                    laneSpeed = 6f;
                    laneLimit = m.pitLaneOffset + 0.5f;
                    float toBox = Mathf.Repeat(m.BoxDistance(team) - d, track.length);
                    if (toBox > track.length / 2f) toBox = 0f;
                    desired = Mathf.Min(m.pitSpeed, Mathf.Sqrt(2f * 10f * Mathf.Max(0f, toBox - 0.2f)));
                    desired = Mathf.Min(desired, FollowLimit(true));
                    if (toBox < 0.6f && Speed < 1.5f)
                    {
                        Pit = PitState.Stopped;
                        Speed = 0f;
                        stopElapsed = 0f;
                        stopTime = 2.2f + Random.value * 0.7f + (Random.value < 0.08f ? 2f : 0f);
                        m.OnAiPitStop(this, true);
                    }
                    break;
                }

                case PitState.Stopped:
                    desired = 0f;
                    laneLimit = m.pitLaneOffset + 0.5f;
                    stopElapsed += dt;
                    TyresOff = stopElapsed > 0.35f && stopElapsed < stopTime - 0.5f;
                    if (stopElapsed > 0.8f && compound != nextCompound) { compound = nextCompound; tyreWear = 0f; TyresChanged = true; }
                    if (stopElapsed >= stopTime)
                    {
                        Pit = PitState.Leaving;
                        PitStops++;
                        m.OnAiPitStop(this, false);
                    }
                    break;

                case PitState.Leaving:
                {
                    laneLimit = m.pitLaneOffset + 0.5f;
                    desired = Mathf.Min(m.pitSpeed, FollowLimit(true));
                    if (RaceManager.InWindow(d, m.pitExitStart, m.pitExitEnd + 40f, track.length))
                    {
                        laneTarget = narrow ? 2.6f : 4.5f;
                        laneSpeed = 5f;
                        if (!RaceManager.InWindow(d, m.pitExitStart, m.pitExitEnd, track.length)) desired = RacingSpeed(skill, track);
                    }
                    if (lane < (narrow ? 3.2f : 5f) && !RaceManager.InWindow(d, m.pitEntryStart, m.pitExitStart, track.length)) Pit = PitState.None;
                    break;
                }
            }
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
        if (manager.HasPitLane && Pit == PitState.None && !finished) tyreWear += Speed * dt / Tyres.LifeMeters[(int)compound];
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

    // Keep about 7 m behind the car ahead in the same lane (or, in the pit lane, any car in the pit lane)
    float FollowLimit(bool pitLane)
    {
        RaceManager.Racer ahead = null;
        float gapBest = float.MaxValue;
        foreach (var r in manager.racers)
        {
            if (r.ai == this || r.retired) continue;
            if (r.ai != null && !r.ai.gameObject.activeSelf) continue;
            float dp = r.progress - progress;
            bool sameLane = pitLane ? r.lateral > manager.pitLaneOffset - 2.5f : Mathf.Abs(r.lateral - lane) < 2.3f;
            if (dp > 0f && dp < 18f && sameLane && dp < gapBest) { gapBest = dp; ahead = r; }
        }
        if (ahead == null) return float.MaxValue;
        return Mathf.Max(0f, ahead.speed + (gapBest - 7f) * 1.2f);
    }

    float AvoidTraffic(float[] lanes)
    {
        float limit = float.MaxValue;
        RaceManager.Racer blocker = null;
        foreach (var r in manager.racers)
        {
            if (r.ai == this || r.retired) continue;
            if (r.ai != null && (!r.ai.gameObject.activeSelf || r.ai.Pit != PitState.None)) continue;
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

        // Until we are clear sideways, keep a following gap of about 7 m to the car ahead
        float gap = blocker.progress - progress;
        limit = Mathf.Max(0f, blocker.speed + (gap - 7f) * 1.2f);
        return limit;
    }

    void Update()
    {
        wheelSpin = (wheelSpin + Speed * Time.deltaTime / 0.36f * Mathf.Rad2Deg) % 360f;
        for (int i = 0; i < 4; i++)
            if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(wheelSpin, 0, 0);
    }
}
