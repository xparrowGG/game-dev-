using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Arcade driving: W/S or arrows to drive, A/D to steer, Space handbrake,
// Left Shift boost, R respawn, T reset trip.
// Crashes: impacts bounce the car off (momentum is shared with other cars), off-centre hits spin it,
// the car slides until the tyres regain grip, sparks fly, and a hard frontal hit breaks off the front wing.
[RequireComponent(typeof(Rigidbody))]
public class CarController : VehicleBase
{
    public float maxSpeed = 50f;        // m/s (~180 km/h)
    public float boostSpeed = 66f;      // m/s (~240 km/h)
    public float maxReverse = 10f;
    public float acceleration = 11f;
    public float brakeForce = 28f;
    public float coastDrag = 2.5f;
    public float wheelRadius = 0.34f;

    [Header("Handling")]
    public float wheelbase = 3.2f;           // m, front to rear axle
    public float maxSteerAngle = 22f;        // degrees at the front wheels
    public float mechanicalGrip = 18f;       // sideways grip at low speed (m/s²)
    public float aeroGrip = 0.012f;          // extra grip per (m/s)² from downforce
    public float aeroBrake = 0.008f;         // extra braking per (m/s)² from downforce and drag

    [Header("Gravity")]
    public float gravityScale = 1.6f;        // heavier, planted feel
    public float downforce = 0.02f;          // extra downward pull per (m/s)²

    [Header("Crashes")]
    public float restitution = 0.3f;         // bounciness of impacts
    public float tyreGrip = 12f;             // how fast a sideways slide is caught (m/s²)
    public float wingBreakSpeed = 13f;       // frontal impact speed (m/s) that breaks off the front wing

    public bool FrontWingLost { get; private set; }

    Rigidbody rb;
    Collider bodyCollider;
    ParticleSystem sparks;
    Vector3 lastPos, spawnPos;
    Quaternion spawnRot;
    Transform[] wheels = new Transform[4];   // FL, FR, RL, RR
    float vertical, horizontal, steer, wheelSpin;
    Vector3 slide;       // sideways / knock-back velocity, bled off by tyre grip
    float spinRate;      // yaw rate from an impact, deg/s
    bool handbrake, grounded;
    Transform body;
    float bodyPitch, bodyRoll, prevSpeed, yawRateRad;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, 0.4f, 0);
        rb.maxDepenetrationVelocity = 3f;
        rb.useGravity = true;
        bodyCollider = GetComponent<Collider>();
        body = transform.Find("Body");
        var sparksTf = transform.Find("Sparks");
        if (sparksTf != null) sparks = sparksTf.GetComponent<ParticleSystem>();
        spawnPos = transform.position;
        spawnRot = transform.rotation;
        lastPos = rb.position;
        string[] names = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        for (int i = 0; i < 4; i++) wheels[i] = transform.Find("Wheels/" + names[i]);
    }

    void Update()
    {
        bool reset = false, resetTrip = false, boost = false;
        float v = 0, h = 0;
        handbrake = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.wKey.isPressed || k.upArrowKey.isPressed) v += 1;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) v -= 1;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) h += 1;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) h -= 1;
            handbrake = k.spaceKey.isPressed;
            boost = k.leftShiftKey.isPressed;
            reset = k.rKey.wasPressedThisFrame;
            resetTrip = k.tKey.wasPressedThisFrame;
        }
        var g = Gamepad.current;
        if (g != null)
        {
            v += g.rightTrigger.ReadValue() - g.leftTrigger.ReadValue();
            h += g.leftStick.x.ReadValue();
            handbrake |= g.buttonSouth.isPressed;
            boost |= g.buttonWest.isPressed;
        }
#else
        v = Input.GetAxisRaw("Vertical");
        h = Input.GetAxisRaw("Horizontal");
        handbrake = Input.GetKey(KeyCode.Space);
        boost = Input.GetKey(KeyCode.LeftShift);
        reset = Input.GetKeyDown(KeyCode.R);
        resetTrip = Input.GetKeyDown(KeyCode.T);
#endif
        if (!inputEnabled) { v = 0; h = 0; boost = false; handbrake = true; reset = false; }
        vertical = Mathf.Clamp(v, -1f, 1f);
        horizontal = Mathf.Clamp(h, -1f, 1f);
        Boosting = boost && vertical > 0;

        if (reset)
        {
            if (onRespawnRequested != null) onRespawnRequested();
            else Respawn();
        }
        if (resetTrip) { DistanceMeters = 0; TripTime = 0; TopSpeedKmh = 0; }
        AnimateWheels();
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 fwd = Flat(rb.rotation * Vector3.forward);

        // If something (a wall, a tree) is holding us back, ease the speed toward what we actually have
        float actual = Vector3.Dot(rb.linearVelocity - slide, fwd);
        if (Mathf.Abs(Speed) - Mathf.Abs(actual) > 3f) Speed = Mathf.Lerp(Speed, actual, 0.3f);

        // Impact spin: the car rotates but its momentum keeps its world direction (it slides)
        if (Mathf.Abs(spinRate) > 0.5f)
        {
            Vector3 vel = fwd * Speed + slide;
            fwd = Flat(Quaternion.Euler(0, spinRate * dt, 0) * fwd);
            Speed = Vector3.Dot(vel, fwd);
            slide = vel - fwd * Speed;
            spinRate = Mathf.MoveTowards(spinRate, 0, 200f * dt);
        }

        // Gravity and downforce keep the car planted; drive and steering need the wheels on the ground
        float v2 = Speed * Speed;
        rb.AddForce(Physics.gravity * (gravityScale - 1f) + Vector3.down * downforce * v2, ForceMode.Acceleration);
        grounded = Physics.Raycast(rb.position + Vector3.up * 0.5f, Vector3.down, 0.9f, ~0, QueryTriggerInteraction.Ignore);

        float top = (Boosting ? boostSpeed : maxSpeed) * speedMultiplier;
        float brake = brakeForce + aeroBrake * v2;
        if (grounded)
        {
            // Acceleration fades toward top speed (air drag)
            float pull = acceleration * (1f - 0.75f * Mathf.Pow(Mathf.Clamp01(Mathf.Abs(Speed) / top), 2f));
            if (vertical > 0)
                Speed = Speed < -0.1f ? Mathf.MoveTowards(Speed, 0, brake * dt)
                                      : Mathf.MoveTowards(Speed, top, pull * vertical * (Boosting ? 1.5f : 1f) * dt);
            else if (vertical < 0)
                Speed = Speed > 0.1f ? Mathf.MoveTowards(Speed, 0, brake * -vertical * dt)
                                     : Mathf.MoveTowards(Speed, -maxReverse, acceleration * 0.6f * -vertical * dt);
            else
                Speed = Mathf.MoveTowards(Speed, 0, (coastDrag + aeroBrake * 0.3f * v2) * dt);
            if (handbrake) Speed = Mathf.MoveTowards(Speed, 0, brakeForce * 0.6f * dt);
        }

        // Steering: wheel geometry asks for a yaw rate, tyre grip (more with downforce) caps it.
        // Losing the front wing costs front grip, so the car understeers.
        steer = Mathf.MoveTowards(steer, horizontal, 5f * dt);
        float absV = Mathf.Abs(Speed);
        float grip = (mechanicalGrip + aeroGrip * v2) * (FrontWingLost ? 0.72f : 1f);
        float geomYaw = Speed * Mathf.Tan(steer * maxSteerAngle * Mathf.Deg2Rad) / wheelbase;   // rad/s
        float maxYaw = grip / Mathf.Max(absV, 1f) * (handbrake ? 1.6f : 1f);
        yawRateRad = grounded ? Mathf.Clamp(geomYaw, -maxYaw, maxYaw) : 0f;
        float yaw = yawRateRad * Mathf.Rad2Deg * dt;

        Quaternion rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0, yaw, 0);
        rb.MoveRotation(rot);
        Vector3 horizontalVel = Flat(rot * Vector3.forward) * Speed + slide;
        rb.linearVelocity = new Vector3(horizontalVel.x, rb.linearVelocity.y, horizontalVel.z);
        rb.angularVelocity = Vector3.zero;
        if (grounded) slide = Vector3.MoveTowards(slide, Vector3.zero, tyreGrip * dt);

        // Body dives under braking, squats under power and leans out of corners
        float longAccel = (Speed - prevSpeed) / dt;
        prevSpeed = Speed;
        bodyPitch = Mathf.Lerp(bodyPitch, Mathf.Clamp(-longAccel * 0.12f, -2.5f, 2.5f), 8f * dt);
        bodyRoll = Mathf.Lerp(bodyRoll, Mathf.Clamp(yawRateRad * Speed * 0.08f, -2.5f, 2.5f), 8f * dt);
        if (body != null) body.localRotation = Quaternion.Euler(bodyPitch, 0, bodyRoll);

        Vector3 delta = rb.position - lastPos;
        delta.y = 0;
        DistanceMeters += delta.magnitude;
        lastPos = rb.position;
        TripTime += dt;
        TopSpeedKmh = Mathf.Max(TopSpeedKmh, SpeedKmh);
    }

    void OnCollisionEnter(Collision c)
    {
        // Use the first contact that isn't the ground
        ContactPoint cp = default;
        bool found = false;
        for (int i = 0; i < c.contactCount; i++)
        {
            var p = c.GetContact(i);
            if (Mathf.Abs(p.normal.y) < 0.5f) { cp = p; found = true; break; }
        }
        if (!found) return;

        Vector3 n = Flat(cp.normal);              // points from the obstacle toward us
        Vector3 fwd = Flat(transform.forward);
        Vector3 v = fwd * Speed + slide;
        var ai = c.collider.GetComponentInParent<RaceAI>();
        Vector3 vOther = ai != null ? ai.Velocity : Vector3.zero;
        Vector3 vRel = v - vOther;
        float vn = Vector3.Dot(vRel, n);
        if (vn > -0.5f) return;                   // already separating

        // Impulse along the contact normal; with another car (similar mass) the change is shared
        float share = ai != null ? 0.5f : 1f;
        Vector3 dv = -(1f + restitution) * vn * share * n;
        Vector3 vt = vRel - vn * n;
        dv -= vt * 0.15f * share;                 // scraping friction
        Vector3 vNew = v + dv;
        Speed = Vector3.Dot(vNew, fwd);
        slide = vNew - fwd * Speed;

        // Off-centre hits twist the car
        Vector3 lever = cp.point - transform.position; lever.y = 0;
        spinRate = Mathf.Clamp(spinRate + Vector3.Cross(lever, dv).y * 9f, -240f, 240f);

        if (ai != null) ai.ApplyImpact(-dv, cp.point);

        float strength = -vn;
        LastImpactStrength = strength;
        LastImpactTime = Time.time;
        if (sparks != null)
        {
            sparks.transform.position = cp.point;
            sparks.Emit(Mathf.RoundToInt(Mathf.Clamp(strength * 5f, 6f, 120f)));
        }
        if (strength > wingBreakSpeed && transform.InverseTransformPoint(cp.point).z > 1.2f) BreakFrontWing(v);
    }

    void BreakFrontWing(Vector3 carVelocity)
    {
        if (FrontWingLost) return;
        var body = transform.Find("Body");
        if (body == null) return;
        var parts = new List<Transform>();
        foreach (Transform t in body)
            if (t.name.StartsWith("FrontWing") || t.name.StartsWith("FrontEndplate")) parts.Add(t);
        if (parts.Count == 0) return;
        FrontWingLost = true;

        var debris = new GameObject("FrontWingDebris");
        debris.transform.SetPositionAndRotation(transform.TransformPoint(new Vector3(0, 0.15f, 2.7f)), transform.rotation);
        foreach (var p in parts)
        {
            p.SetParent(debris.transform, true);
            var col = p.gameObject.AddComponent<BoxCollider>();
            if (bodyCollider != null) Physics.IgnoreCollision(col, bodyCollider);
        }
        var drb = debris.AddComponent<Rigidbody>();
        drb.mass = 20f;
        drb.linearVelocity = carVelocity * 0.6f + Vector3.up * 3f + Random.insideUnitSphere * 2f;
        drb.angularVelocity = Random.insideUnitSphere * 8f;
    }

    void Respawn() => Teleport(spawnPos, spawnRot);

    public override void Teleport(Vector3 pos, Quaternion rot)
    {
        Speed = 0;
        slide = Vector3.zero;
        spinRate = 0;
        rb.linearVelocity = Vector3.zero;
        rb.position = pos;
        rb.rotation = rot;
        transform.SetPositionAndRotation(pos, rot);
        lastPos = pos;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    void AnimateWheels()
    {
        wheelSpin += Speed * Time.deltaTime / wheelRadius * Mathf.Rad2Deg;
        wheelSpin %= 360f;
        for (int i = 0; i < 4; i++)
        {
            if (wheels[i] == null) continue;
            float steerAngle = i < 2 ? steer * 30f : 0f;
            wheels[i].localRotation = Quaternion.Euler(wheelSpin, steerAngle, 0);
        }
    }
}
