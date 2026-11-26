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
    public float StopTime => stopTime;
    public int PitStops { get; private set; }
    public bool TyresOff { get; private set; }
    public bool TyresChanged { get; set; }   // set when new tyres go on; the race manager repaints them

    [System.NonSerialized] public float parkProgress = float.NaN;   // where to stop after the race
    [System.NonSerialized] public RaceManager manager;
    public float Speed { get; private set; }
    public bool Parked { get; private set; }
    public Vector3 Velocity => transform.forward * Speed + transform.right * lateralVel;

    static readonly float[] NarrowLanes = { -2.6f, 0f, 2.6f };
    readonly float[] dynamicLanes = new float[3];
    Rigidbody rb;
    readonly Transform[] steerWheels = new Transform[2];
    readonly Transform[] spinWheels = new Transform[4];
    Transform drsPivot;
    Quaternion drsBaseRot;
    Vector3 drsAxisLocal = Vector3.right;
    float laneTarget, wheelSpin, visualSteer, drsAngle;
    bool drsOpen, inSlipstream;
    float lateralVel, wobble, wobbleVel, recoverTimer;   // crash reaction
    float stopTime, stopElapsed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        Transform wRoot = transform.Find("Wheels");
        string[] legacyNames = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        string[] steerNames = { "SteerFL", "SteerFR" };
        string[] spinNames = { "SpinFL", "SpinFR", "SpinRL", "SpinRR" };
        for (int i = 0; i < 4; i++)
        {
            if (i < 2 && wRoot != null)
            {
                steerWheels[i] = wRoot.Find(steerNames[i]);
                spinWheels[i] = steerWheels[i] != null ? steerWheels[i].Find(spinNames[i]) : wRoot.Find(legacyNames[i]);
            }
            else if (wRoot != null)
            {
                spinWheels[i] = wRoot.Find(spinNames[i]) ?? wRoot.Find(legacyNames[i]);
            }
        }
        var body = transform.Find("Body");
        if (body != null)
        {
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
                if (t.name == "DRS_Flap_Pivot") { drsPivot = t; break; }
            if (drsPivot != null)
            {
                drsBaseRot = drsPivot.localRotation;
                drsAxisLocal = drsPivot.InverseTransformDirection(transform.TransformDirection(Vector3.right)).normalized;
            }
        }
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
        lateralVel = Mathf.Clamp(lateralVel + Vector3.Dot(dv, transform.right), -6f, 6f);
        Vector3 lever = point - transform.position; lever.y = 0;
        wobbleVel = Mathf.Clamp(wobbleVel + Vector3.Cross(lever, dv).y * 2.2f, -35f, 35f);
        wobble = Mathf.Clamp(wobble, -10f, 10f);
        recoverTimer = Mathf.Clamp(dv.magnitude * 0.10f, 0.35f, 0.85f);
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
        float hw = track.HalfWidthAt(progress);
        bool narrow = hw <= 6f;
        float desired = 0f;
        float laneSpeed = 2.5f;
        float laneLimit = Mathf.Max(2.2f, hw - 1.4f);
        float d = Mathf.Repeat(progress, track.length);
        TyresOff = false;
        drsOpen = false;
        inSlipstream = false;

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
                {
                    desired = RacingSpeed(skill, track);
                    if (!narrow)
                    {
                        float usable = Mathf.Max(2.2f, hw - 2.2f);
                        dynamicLanes[0] = -usable; dynamicLanes[1] = 0f; dynamicLanes[2] = usable;
                    }
                    float trafficLimit = AvoidTraffic(narrow ? NarrowLanes : dynamicLanes, out bool blocked);
                    if (!blocked && racing && !finished)
                    {
                        laneTarget = track.RacingLineOffsetAt(progress);
                        laneSpeed = progress < 380f ? 2.0f : 3.2f;
                    }
                    bool hasAheadDrs = false;
                    foreach (var r in manager.racers)
                    {
                        if (r.ai == this || r.retired) continue;
                        if (r.ai != null && (!r.ai.gameObject.activeSelf || r.ai.Pit != PitState.None)) continue;
                        float dp = r.progress - progress;
                        if (dp > 0f && dp < Speed * 1.0f && lap >= 2) hasAheadDrs = true;
                        if (dp > 4f && dp < 28f && Mathf.Abs(r.lateral - lane) < 2.6f && Speed > 25f) inSlipstream = true;
                    }
                    drsOpen = racing && !finished && manager.InDrsZone(d) && Speed > 35f && track.RadiusAt(progress) > 250f && hasAheadDrs;
                    desired *= (drsOpen ? 1.06f : 1f) * (inSlipstream ? 1.035f : 1f);
                    desired = Mathf.Min(desired, trafficLimit);
                    if (m.HasPitLane && pitLap > 0 && lap == pitLap && !finished
                        && RaceManager.InWindow(d, m.pitEntryStart - 260f, m.pitEntryStart - 20f, track.length))
                        Pit = PitState.Entering;
                    break;
                }

                case PitState.Entering:
                {
                    // Brake to the pit speed limit at the entry line, then move across into the pit lane
                    float toEntry = Mathf.Repeat(m.pitEntryStart - d, track.length);
                    if (toEntry > track.length / 2f) toEntry = 0f;
                    desired = Mathf.Min(RacingSpeed(skill, track), Mathf.Sqrt(m.pitSpeed * m.pitSpeed + 2f * 14f * toEntry));
                    desired = Mathf.Min(desired, FollowLimit(false));
                    if (RaceManager.InWindow(d, m.pitEntryStart, m.pitEntryEnd + 20f, track.length))
                    {
                        laneTarget = m.PitLat(d);
                        laneSpeed = 6f;
                        desired = Mathf.Min(desired, m.pitSpeed);
                    }
                    laneLimit = m.PitLimit;
                    if (lane * m.PitSide > Mathf.Abs(m.PitLat(d)) - 2f) Pit = PitState.InLane;
                    break;
                }

                case PitState.InLane:
                {
                    float toBox = Mathf.Repeat(m.BoxDistance(team) - d, track.length);
                    if (toBox > track.length / 2f) toBox = 0f;
                    float boxBlend = toBox < 18f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 3f, toBox)) : 0f;
                    laneTarget = Mathf.Lerp(m.PitLat(d), m.BoxLat(team), boxBlend);
                    laneSpeed = 8f;
                    laneLimit = m.PitLimit;
                    desired = Mathf.Min(m.pitSpeed, Mathf.Sqrt(2f * 10f * Mathf.Max(0f, toBox - 0.15f)));
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
                    laneLimit = m.PitLimit;
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
                    laneLimit = m.PitLimit;
                    float fromBox = Mathf.Repeat(d - m.BoxDistance(team), track.length);
                    float outBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 16f, fromBox));
                    laneTarget = Mathf.Lerp(m.BoxLat(team), m.PitLat(d), outBlend);
                    laneSpeed = 8f;
                    desired = Mathf.Min(m.pitSpeed, FollowLimit(true));
                    // Strictly stay in pit fast lane until AFTER pitExitEnd before merging onto circuit!
                    bool inPitChannel = RaceManager.InWindow(d, m.pitEntryStart, m.pitExitEnd, track.length);
                    if (!inPitChannel && RaceManager.InWindow(d, m.pitExitEnd, m.pitExitEnd + 80f, track.length))
                    {
                        laneTarget = (narrow ? 2.6f : 4.5f) * m.PitSide;
                        laneSpeed = 4.5f;
                        desired = RacingSpeed(skill, track);
                    }
                    if (!inPitChannel && lane * m.PitSide < (narrow ? 3.2f : 5f) && !RaceManager.InWindow(d, m.pitEntryStart, m.pitExitEnd, track.length))
                        Pit = PitState.None;
                    break;
                }
            }
        }

        // After being hit: critically damped wobble decay without elastic bounce
        if (recoverTimer > 0f)
        {
            recoverTimer -= dt;
            desired = Mathf.Min(desired, 14f);
        }
        lane = Mathf.Clamp(lane + lateralVel * dt, -laneLimit, laneLimit);
        lateralVel = Mathf.MoveTowards(lateralVel, 0f, 12f * dt);
        wobbleVel += (-wobble * 24f - wobbleVel * 11f) * dt;
        wobble = Mathf.Clamp(wobble + wobbleVel * dt, -18f, 18f);

        float accel = (acceleration * (1f - 0.7f * Mathf.Pow(Mathf.Clamp01(Speed / Mathf.Max(maxSpeed, 1f)), 2f)) + 0.5f) * (inSlipstream || drsOpen ? 1.08f : 1f);
        float brake = braking + aeroBraking * Speed * Speed;
        Speed = Mathf.MoveTowards(Speed, desired, (desired < Speed ? brake : accel) * dt);
        if (Speed > 0.5f && Mathf.Abs(lateralVel) < 0.5f) lane = Mathf.MoveTowards(lane, laneTarget, laneSpeed * dt);
        float signedCurv = Vector3.SignedAngle(track.TangentAt(progress - 4f), track.TangentAt(progress + 6f), Vector3.up);
        float laneSteer = Mathf.Clamp((laneTarget - lane) * 4f, -12f, 12f);
        float targetSteer = Mathf.Clamp(signedCurv * 1.4f + laneSteer, -20f, 20f);
        visualSteer = Mathf.MoveTowards(visualSteer, Speed > 0.5f ? targetSteer : 0f, 120f * dt);
        progress += Speed * dt;
        if (manager.HasPitLane && Pit == PitState.None && !finished) tyreWear += Speed * dt / Tyres.LifeMeters[(int)compound];
        Place();
    }

    float groundY; Vector3 groundN = Vector3.up; bool groundInit;
    static readonly RaycastHit[] groundHits = new RaycastHit[8];

    bool SampleGround(Vector3 p, out float y, out Vector3 normal)
    {
        y = p.y; normal = Vector3.up;
        if (manager == null || !manager.snapToGround) return false;
        int n = Physics.RaycastNonAlloc(p + Vector3.up * 2.5f, Vector3.down, groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = groundHits[i];
            if (h.collider.attachedRigidbody != null || h.normal.y < 0.7f) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; normal = h.normal; found = true; }
        }
        return found;
    }

    void Place()
    {
        var track = manager.track;
        Vector3 pos = track.PointAt(progress) + track.RightAt(progress) * lane;
        Vector3 up = Vector3.up;
        // Real circuits are cambered and banked: sit the car on the actual surface under it
        if (SampleGround(pos, out float gy, out Vector3 n))
        {
            groundY = groundInit ? Mathf.Lerp(groundY, gy, 0.5f) : gy;
            groundN = groundInit ? Vector3.Slerp(groundN, n, 0.3f) : n;
            groundInit = true;
            pos.y = groundY;
            up = groundN;
        }
        pos.y += 0.03f;
        if (rb == null) rb = GetComponent<Rigidbody>();
        Vector3 fwd = Vector3.ProjectOnPlane(track.TangentAt(progress), up).normalized;
        Quaternion rot = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0, wobble, 0);
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
            bool sameLane = pitLane ? r.lateral * manager.PitSide > Mathf.Abs(manager.PitLat(Mathf.Repeat(progress, manager.track.length))) - 2.5f : Mathf.Abs(r.lateral - lane) < 2.3f;
            if (dp > 0f && dp < 18f && sameLane && dp < gapBest) { gapBest = dp; ahead = r; }
        }
        if (ahead == null) return float.MaxValue;
        return Mathf.Max(0f, ahead.speed + (gapBest - 7f) * 1.2f);
    }

    float AvoidTraffic(float[] lanes, out bool blocked)
    {
        blocked = false;
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
        blocked = true;

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

        // Until we are clear sideways, keep a generous opening-lap gap (~9.5m) then ~7m
        float minGap = progress < 380f ? 9.5f : 7f;
        float gap = blocker.progress - progress;
        limit = Mathf.Max(0f, blocker.speed + (gap - minGap) * 1.2f);
        return limit;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        wheelSpin = (wheelSpin + Speed * dt / 0.36f * Mathf.Rad2Deg) % 360f;
        for (int i = 0; i < 4; i++)
        {
            if (i < 2 && steerWheels[i] != null)
            {
                steerWheels[i].localRotation = Quaternion.Euler(0f, visualSteer, 0f);
                if (spinWheels[i] != null) spinWheels[i].localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);
            }
            else if (spinWheels[i] != null)
            {
                spinWheels[i].localRotation = i < 2
                    ? Quaternion.Euler(0f, visualSteer, 0f) * Quaternion.Euler(wheelSpin, 0f, 0f)
                    : Quaternion.Euler(wheelSpin, 0f, 0f);
            }
        }
        if (drsPivot != null)
        {
            drsAngle = Mathf.MoveTowards(drsAngle, drsOpen ? -28f : 0f, 220f * dt);
            drsPivot.localRotation = drsBaseRot * Quaternion.AngleAxis(drsAngle, drsAxisLocal);
        }
    }
}
