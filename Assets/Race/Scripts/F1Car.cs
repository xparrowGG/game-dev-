using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Simulation-style single-seater physics on a free Rigidbody:
//  - four raycast wheels with stiff spring/damper suspension and anti-roll bars
//  - tyre forces from slip angle (Pacejka-style curve), load sensitive grip, friction circle
//  - aerodynamic downforce (front/rear balance) and drag growing with speed²
//  - engine torque curve, 8-speed gearbox (auto or manual), rev limiter
//  - brakes with front/rear bias, optional ABS and traction control (without them wheels lock / spin)
//  - ERS battery boost that recharges under braking
// Controls: W/S throttle/brake, A/D steer, E/Q gear up/down (manual), Shift ERS, Space handbrake, R reset.
[RequireComponent(typeof(Rigidbody))]
public class F1Car : VehicleBase
{
    [Header("Mass")]
    public float mass = 798f;
    public Vector3 centerOfMass = new Vector3(0, 0.28f, -0.15f);

    [Header("Engine & gearbox")]
    public float peakTorque = 620f;             // Nm at the crank
    public float idleRpm = 4000f;
    public float redline = 12500f;
    public float[] gearRatios = { 19.2f, 14.0f, 11.0f, 9.0f, 7.6f, 6.5f, 5.6f, 4.94f };   // overall, incl. final drive
    public float reverseRatio = 16f;
    public float drivetrainEfficiency = 0.92f;
    public float shiftTime = 0.05f;
    public float upshiftRpm = 11900f;
    public float downshiftRpm = 7200f;

    [Header("ERS")]
    public float ersPower = 120000f;            // W of extra deployment
    public float ersCapacity = 4.0e6f;          // J
    public float ersRegenPower = 120000f;       // W recovered under braking

    [Header("Aerodynamics")]
    public float downforceArea = 4.0f;          // Cl·A
    public float dragArea = 0.95f;              // Cd·A
    [Range(0, 1)] public float aeroBalanceFront = 0.44f;
    public float airDensity = 1.225f;

    [Header("Tyres")]
    public float tyreRadius = 0.36f;
    public float frictionCoef = 1.7f;           // peak μ of a slick at nominal load
    public float nominalLoad = 3000f;           // N
    public float loadSensitivity = 0.08f;       // μ drop per extra nominal load
    public float corneringStiffness = 11f;      // Pacejka B
    public float shapeFactor = 1.35f;           // Pacejka C

    [Header("Brakes")]
    public float maxBrakeForce = 42000f;        // N total at the tyres
    [Range(0, 1)] public float brakeBias = 0.57f;

    [Header("Steering")]
    public float maxSteerAngle = 20f;
    public float steerSpeed = 110f;             // deg/s at the wheels

    [Header("Suspension")]
    public float frontAxleZ = 1.8f, rearAxleZ = -1.8f;
    public float frontTrack = 0.8f, rearTrack = 0.78f;      // half track
    public float restLength = 0.10f;
    public float springRate = 120000f;
    public float damperRate = 7000f;
    public float antiRoll = 40000f;

    [Header("Assists")]
    public bool abs = true;
    public bool tractionControl = true;
    public bool steeringAssist = true;
    public bool autoGearbox = true;

    [Header("Crashes")]
    public float wingBreakDeltaV = 9f;          // m/s velocity change that breaks the front wing

    // External control (tests, autopilot)
    [System.NonSerialized] public bool externalInput;
    [System.NonSerialized] public float extThrottle, extBrake, extSteer;

    public override int Gear => gear;
    public override bool HasGearbox => true;
    public override float Rpm => rpm;
    public override float MaxRpm => redline;
    public override float ErsCharge => ersEnergy / ersCapacity;
    [System.NonSerialized] public float tyreGripScale = 1f;   // compound and wear, set by the race manager
    public bool OnGravel { get; private set; }
    public float ThrottleInput { get; private set; }
    public float TyreSlip { get; private set; }      // 0 = gripping, 1+ = sliding hard (for the tyre squeal)
    public float LateralG { get; private set; }
    public float LongitudinalG { get; private set; }
    public bool FrontWingLost { get; private set; }
    public bool WheelLocking { get; private set; }
    public bool WheelSpinning { get; private set; }

    class Wheel
    {
        public string name;
        public Transform visual;
        public Vector3 attach;          // local suspension top
        public bool front, left;
        public float compression, prevCompression, load, spin;
        public bool grounded;
        public RaycastHit hit;
    }

    readonly Wheel[] wheels = new Wheel[4];
    Rigidbody rb;
    Collider bodyCollider;
    ParticleSystem sparks;
    Vector3 spawnPos, lastPos;
    Quaternion spawnRot;
    bool initialized;

    int gear = 1;
    float rpm, shiftTimer, steerAngle, ersEnergy, upsideDownTimer, reverseHoldTimer;
    float inThrottle, inBrake, inSteer;
    bool inHandbrake, inErs, shiftUpPressed, shiftDownPressed, resetPressed;
    Vector3 prevVelocity;

    void Awake() => Init();

    public void Init()
    {
        if (initialized) return;
        initialized = true;
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.centerOfMass = centerOfMass;
        rb.inertiaTensor = new Vector3(1200f, 1400f, 300f);
        rb.inertiaTensorRotation = Quaternion.identity;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxDepenetrationVelocity = 3f;
        bodyCollider = GetComponent<Collider>();
        var sparksTf = transform.Find("Sparks");
        if (sparksTf != null) sparks = sparksTf.GetComponent<ParticleSystem>();

        string[] names = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2, left = i % 2 == 0;
            float x = (front ? frontTrack : rearTrack) * (left ? -1 : 1);
            float z = front ? frontAxleZ : rearAxleZ;
            wheels[i] = new Wheel
            {
                name = names[i], front = front, left = left,
                visual = transform.Find("Wheels/" + names[i]),
                attach = new Vector3(x, tyreRadius + restLength, z)
            };
        }
        ersEnergy = ersCapacity;
        spawnPos = transform.position;
        spawnRot = transform.rotation;
        lastPos = transform.position;
        rpm = idleRpm;
    }

    // ---------- Input ----------

    void Update()
    {
        if (externalInput) { UpdateWheelVisuals(Time.deltaTime); return; }
        float throttleKey = 0, brakeKey = 0, steerKey = 0;
        bool analogPedals = false, analogSteer = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.wKey.isPressed || k.upArrowKey.isPressed) throttleKey = 1;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) brakeKey = 1;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) steerKey += 1;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) steerKey -= 1;
            inHandbrake = k.spaceKey.isPressed;
            inErs = k.leftShiftKey.isPressed;
            shiftUpPressed |= k.eKey.wasPressedThisFrame;
            shiftDownPressed |= k.qKey.wasPressedThisFrame;
            resetPressed |= k.rKey.wasPressedThisFrame;
        }
        var g = Gamepad.current;
        if (g != null)
        {
            float rt = g.rightTrigger.ReadValue(), lt = g.leftTrigger.ReadValue(), sx = g.leftStick.x.ReadValue();
            if (rt > 0.02f || lt > 0.02f) { analogPedals = true; throttleKey = Mathf.Max(throttleKey, rt); brakeKey = Mathf.Max(brakeKey, lt); }
            if (Mathf.Abs(sx) > 0.05f) { analogSteer = true; steerKey = sx; }
            inErs |= g.buttonWest.isPressed;
            inHandbrake |= g.buttonSouth.isPressed;
            shiftUpPressed |= g.rightShoulder.wasPressedThisFrame;
            shiftDownPressed |= g.leftShoulder.wasPressedThisFrame;
        }
#else
        throttleKey = Mathf.Max(0, Input.GetAxisRaw("Vertical"));
        brakeKey = Mathf.Max(0, -Input.GetAxisRaw("Vertical"));
        steerKey = Input.GetAxisRaw("Horizontal");
        inHandbrake = Input.GetKey(KeyCode.Space);
        inErs = Input.GetKey(KeyCode.LeftShift);
        shiftUpPressed |= Input.GetKeyDown(KeyCode.E);
        shiftDownPressed |= Input.GetKeyDown(KeyCode.Q);
        resetPressed |= Input.GetKeyDown(KeyCode.R);
#endif
        float dt = Time.deltaTime;
        // Digital keys are eased in like a driver squeezing the pedal; analog input is used directly
        inThrottle = analogPedals ? throttleKey : Mathf.MoveTowards(inThrottle, throttleKey, (throttleKey > inThrottle ? 3.5f : 9f) * dt);
        inBrake = analogPedals ? brakeKey : Mathf.MoveTowards(inBrake, brakeKey, (brakeKey > inBrake ? 8f : 12f) * dt);
        inSteer = analogSteer ? steerKey : steerKey;

        if (resetPressed)
        {
            resetPressed = false;
            if (inputEnabled)
            {
                if (onRespawnRequested != null) onRespawnRequested();
                else Teleport(spawnPos, spawnRot);
            }
        }
        UpdateWheelVisuals(dt);
    }

    void FixedUpdate()
    {
        SimStep(Time.fixedDeltaTime);
        if (rb.isKinematic)
        {
            // Pit lane: still count distance and time for the dashboard
            Vector3 delta = transform.position - lastPos; delta.y = 0;
            DistanceMeters += delta.magnitude;
            lastPos = transform.position;
            TripTime += Time.fixedDeltaTime;
        }
    }

    // ---------- Simulation ----------

    // While the race manager drives the car through the pit lane (kinematic), it reports the speed here.
    public void SetPitSpeed(float v)
    {
        Speed = v;
        gear = v < 0.5f ? 0 : 2;
        rpm = Mathf.Max(idleRpm + 1500f, v / tyreRadius * gearRatios[1] * 60f / (2f * Mathf.PI));
    }

    public void SimStep(float dt)
    {
        if (!initialized) Init();
        if (rb.isKinematic) { prevVelocity = Vector3.zero; return; }

        float throttle = externalInput ? extThrottle : inThrottle;
        float brake = externalInput ? extBrake : inBrake;
        float steerIn = externalInput ? extSteer : inSteer;
        bool handbrake = !externalInput && inHandbrake;
        bool ers = !externalInput && inErs;
        if (!inputEnabled) { throttle = 0; brake = 1; steerIn = 0; handbrake = false; ers = false; }

        Vector3 vel = rb.linearVelocity;
        Vector3 fwd = transform.forward;
        float vLong = Vector3.Dot(vel, fwd);
        float absV = Mathf.Abs(vLong);
        Speed = vLong;

        // Steering: the assist narrows the lock at speed so keyboard input stays controllable
        // Lateral grip available right now (tyres + downforce), used by the steering and stability assists
        float wheelbase = frontAxleZ - rearAxleZ;
        float latGrip = frictionCoef * tyreGripScale * (9.81f + 0.5f * airDensity * vLong * vLong * downforceArea / mass) * 0.9f;
        // Assist: never ask for more steering angle than the tyres can use at this speed (no scrubbing, no snap)
        float usable = Mathf.Atan(wheelbase * latGrip / Mathf.Max(vLong * vLong, 1f)) * Mathf.Rad2Deg + 1.5f;
        float lockAngle = steeringAssist ? Mathf.Min(maxSteerAngle, usable) : maxSteerAngle;
        float targetSteer = Mathf.Clamp(steerIn, -1f, 1f) * lockAngle;
        // Quick at low speed, calm at high speed; returns to centre faster than it turns in
        float rate = Mathf.Lerp(150f, 55f, Mathf.Clamp01(absV / 80f)) * (steeringAssist ? 1f : 1.4f);
        if (Mathf.Abs(targetSteer) < Mathf.Abs(steerAngle)) rate *= 1.8f;
        steerAngle = Mathf.MoveTowards(steerAngle, targetSteer, rate * dt);

        // Aerodynamics
        float q = 0.5f * airDensity * vLong * vLong;
        float downforce = q * downforceArea * (FrontWingLost ? 0.8f : 1f);
        float frontShare = FrontWingLost ? aeroBalanceFront * 0.55f : aeroBalanceFront;
        rb.AddForceAtPosition(-transform.up * downforce * frontShare, transform.TransformPoint(0, 0.3f, frontAxleZ));
        rb.AddForceAtPosition(-transform.up * downforce * (1f - frontShare), transform.TransformPoint(0, 0.3f, rearAxleZ));
        if (vel.sqrMagnitude > 0.01f) rb.AddForce(-vel.normalized * 0.5f * airDensity * vel.sqrMagnitude * dragArea);

        // Gearbox
        shiftTimer -= dt;
        HandleGears(throttle, brake, vLong);
        bool reverse = gear == -1;
        float driveInput = reverse ? brake : throttle;
        float brakeInput = reverse ? throttle : brake;

        float ratio = gear > 0 ? gearRatios[gear - 1] : (reverse ? reverseRatio : 0f);
        float wheelOmega = absV / tyreRadius;
        float engineRpm = ratio > 0 ? wheelOmega * ratio * 60f / (2f * Mathf.PI) : idleRpm + driveInput * (redline - idleRpm);
        rpm = Mathf.Lerp(rpm, Mathf.Clamp(Mathf.Max(idleRpm, engineRpm), idleRpm, redline + 100f), 1f - Mathf.Exp(-25f * dt));
        bool limiter = engineRpm >= redline;
        float torque = TorqueCurve(Mathf.Max(idleRpm, engineRpm)) * peakTorque;
        float driveForce = (shiftTimer > 0f || limiter || ratio <= 0f) ? 0f : driveInput * torque * ratio * drivetrainEfficiency / tyreRadius;

        Boosting = false;
        if (ers && driveInput > 0.5f && gear > 0 && ersEnergy > 0f && absV > 3f && !limiter)
        {
            driveForce += ersPower / Mathf.Max(absV, 8f);
            ersEnergy = Mathf.Max(0f, ersEnergy - ersPower * dt);
            Boosting = true;
        }
        if (brakeInput > 0.2f && absV > 5f) ersEnergy = Mathf.Min(ersCapacity, ersEnergy + ersRegenPower * brakeInput * dt);
        if (reverse) driveForce = -driveForce;

        float brakeTotal = brakeInput * maxBrakeForce;

        // Suspension
        bool anyGrounded = false;
        foreach (var w in wheels)
        {
            Vector3 top = transform.TransformPoint(w.attach);
            float rest = restLength + tyreRadius;
            w.prevCompression = w.compression;
            w.grounded = Physics.Raycast(top, -transform.up, out w.hit, rest + 0.15f, ~0, QueryTriggerInteraction.Ignore)
                         && w.hit.distance <= rest + 0.01f && w.hit.collider != bodyCollider;
            w.compression = w.grounded ? rest - w.hit.distance : 0f;
            if (w.grounded) anyGrounded = true;
        }
        for (int axle = 0; axle < 2; axle++)
        {
            Wheel l = wheels[axle * 2], r = wheels[axle * 2 + 1];
            float roll = (l.compression - r.compression) * antiRoll;
            foreach (var w in new[] { l, r })
            {
                if (!w.grounded) { w.load = 0f; continue; }
                float damper = damperRate * (w.compression - w.prevCompression) / dt;
                float f = springRate * w.compression + damper + (w == l ? roll : -roll);
                w.load = Mathf.Max(0f, f);
                rb.AddForceAtPosition(transform.up * w.load, w.hit.point);
            }
        }

        // Tyres
        WheelLocking = false;
        WheelSpinning = false;
        OnGravel = false;
        ThrottleInput = driveInput;
        float slipMax = 0f;
        float cornerMass = mass * 0.25f;
        foreach (var w in wheels)
        {
            if (!w.grounded || w.load <= 0f) continue;
            Vector3 n = w.hit.normal;
            Vector3 wFwd = Quaternion.AngleAxis(w.front ? steerAngle : 0f, transform.up) * fwd;
            wFwd = Vector3.ProjectOnPlane(wFwd, n).normalized;
            Vector3 wRight = Vector3.Cross(n, wFwd);
            Vector3 pv = rb.GetPointVelocity(w.hit.point);
            float vx = Vector3.Dot(pv, wFwd), vy = Vector3.Dot(pv, wRight);

            float mu = frictionCoef * tyreGripScale * Mathf.Clamp(1f - loadSensitivity * (w.load / nominalLoad - 1f), 0.75f, 1.25f);
            if (w.front && FrontWingLost) mu *= 0.9f;
            // Gravel trap: little grip and a lot of drag
            var surface = w.hit.collider.sharedMaterial;
            bool gravel = surface != null && surface.name.StartsWith("Gravel");
            if (gravel) { mu *= 0.45f; OnGravel = true; }
            float maxF = mu * w.load;

            // Lateral: slip angle through a Pacejka-style curve (speed floor keeps it stable at a crawl)
            float slip = Mathf.Atan2(vy, Mathf.Max(Mathf.Abs(vx), 4f));
            if (Mathf.Abs(vx) > 5f) slipMax = Mathf.Max(slipMax, Mathf.Abs(slip) / 0.12f);
            float fy = -maxF * Mathf.Sin(shapeFactor * Mathf.Atan(corneringStiffness * slip));

            // Longitudinal request: drive (rear, split equally) and brakes (with bias)
            float fx = w.front ? 0f : driveForce * 0.5f;
            float wheelBrake = brakeTotal * (w.front ? brakeBias : 1f - brakeBias) * 0.5f;
            if (handbrake && !w.front) wheelBrake = Mathf.Max(wheelBrake, maxF * 1.2f);
            if (wheelBrake > 0f)
            {
                float hold = -vx * cornerMass / dt;          // force that would stop the wheel this step
                fx += Mathf.Clamp(hold, -wheelBrake, wheelBrake);
            }
            fx -= vx * (gravel ? 380f : 12f);                 // rolling resistance (gravel digs in)

            // Friction circle: assists keep the tyre at the limit, without them it locks or spins and loses side grip
            float latUse = Mathf.Abs(fy) / Mathf.Max(maxF, 1f);
            float longAvail = maxF * Mathf.Sqrt(Mathf.Max(0f, 1f - latUse * latUse));
            if (Mathf.Abs(fx) > longAvail)
            {
                bool braking = fx * vx < 0f;
                bool assisted = braking ? abs : tractionControl;
                if (handbrake && !w.front) assisted = false;
                if (assisted || longAvail > maxF * 0.98f)
                {
                    fx = Mathf.Sign(fx) * longAvail;
                }
                else
                {
                    float over = Mathf.Abs(fx) / Mathf.Max(maxF, 1f);
                    fx = Mathf.Sign(fx) * maxF * 0.85f;
                    fy *= Mathf.Lerp(0.6f, 0.3f, Mathf.Clamp01(over - 1f));
                    if (braking) WheelLocking = true; else WheelSpinning = true;
                }
            }
            rb.AddForceAtPosition(wFwd * fx + wRight * fy, w.hit.point);
        }

        TyreSlip = OnGravel ? 0f : Mathf.Max(slipMax - 0.6f, (WheelLocking || WheelSpinning) && absV > 5f ? 1f : 0f);

        // Stability assist: if the car rotates faster than the steering asks for (oversteer), gently stop the slide
        if (steeringAssist && anyGrounded && absV > 8f)
        {
            float desiredYaw = Mathf.Clamp(vLong * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / wheelbase, -latGrip / absV, latGrip / absV);
            float actualYaw = Vector3.Dot(rb.angularVelocity, transform.up);
            float err = actualYaw - desiredYaw;
            if (Mathf.Abs(actualYaw) > Mathf.Abs(desiredYaw) + 0.03f && Mathf.Sign(err) == Mathf.Sign(actualYaw))
                rb.AddTorque(-transform.up * err * rb.inertiaTensor.y * 6f);
        }

        // Body wants to stay upright when nearly rolled over: help it back (driver can't, the marshals would)
        if (transform.up.y < 0.3f) upsideDownTimer += dt; else upsideDownTimer = 0f;
        if (upsideDownTimer > 2.5f && !externalInput)
        {
            upsideDownTimer = 0f;
            if (onRespawnRequested != null) onRespawnRequested(); else Teleport(spawnPos, spawnRot);
        }

        // Telemetry
        Vector3 accel = (rb.linearVelocity - prevVelocity) / dt;
        prevVelocity = rb.linearVelocity;
        LateralG = Vector3.Dot(accel, transform.right) / 9.81f;
        LongitudinalG = Vector3.Dot(accel, fwd) / 9.81f;

        Vector3 delta = rb.position - lastPos; delta.y = 0;
        DistanceMeters += delta.magnitude;
        lastPos = rb.position;
        TripTime += dt;
        TopSpeedKmh = Mathf.Max(TopSpeedKmh, SpeedKmh);
        if (!anyGrounded) WheelSpinning = false;
    }

    void HandleGears(float throttle, float brake, float vLong)
    {
        if (autoGearbox)
        {
            shiftUpPressed = shiftDownPressed = false;
            if (gear >= 1)
            {
                float r = vLong / tyreRadius * gearRatios[gear - 1] * 60f / (2f * Mathf.PI);
                if (r > upshiftRpm && gear < gearRatios.Length && shiftTimer <= 0f) { gear++; shiftTimer = shiftTime; }
                else if (r < downshiftRpm && gear > 1 && shiftTimer <= 0f) { gear--; shiftTimer = shiftTime * 0.5f; }
                // Hold the brake at a standstill to engage reverse
                if (inputEnabled && vLong < 0.5f && brake > 0.6f && throttle < 0.1f) reverseHoldTimer += Time.fixedDeltaTime; else reverseHoldTimer = 0f;
                if (reverseHoldTimer > 0.6f) { gear = -1; reverseHoldTimer = 0f; }
            }
            else if (gear == -1 && vLong > -0.5f && throttle > 0.1f && brake < 0.1f) gear = 1;
            else if (gear == 0) gear = 1;
        }
        else
        {
            if (shiftUpPressed && shiftTimer <= 0f)
            {
                if (gear == -1) gear = 0;
                else if (gear < gearRatios.Length) gear++;
                shiftTimer = shiftTime;
            }
            if (shiftDownPressed && shiftTimer <= 0f)
            {
                if (gear > 1)
                {
                    // Refuse a downshift that would over-rev the engine
                    float r = vLong / tyreRadius * gearRatios[gear - 2] * 60f / (2f * Mathf.PI);
                    if (r < redline) gear--;
                }
                else if (gear == 1) gear = 0;
                else if (gear == 0 && vLong < 1f) gear = -1;
                shiftTimer = shiftTime * 0.5f;
            }
            shiftUpPressed = shiftDownPressed = false;
        }
    }

    static float TorqueCurve(float r)
    {
        if (r < 7000f) return Mathf.Lerp(0.62f, 0.82f, (r - 4000f) / 3000f);
        if (r < 10500f) return Mathf.Lerp(0.82f, 1f, (r - 7000f) / 3500f);
        if (r < 12000f) return Mathf.Lerp(1f, 0.95f, (r - 10500f) / 1500f);
        return Mathf.Lerp(0.95f, 0.9f, (r - 12000f) / 500f);
    }

    // ---------- Visuals ----------

    void UpdateWheelVisuals(float dt)
    {
        if (!initialized) return;
        foreach (var w in wheels)
        {
            if (w.visual == null) continue;
            float drop = w.grounded ? (w.hit.distance - tyreRadius) : restLength + 0.03f;
            Vector3 p = w.attach - new Vector3(0, Mathf.Clamp(drop, 0f, restLength + 0.03f), 0);
            w.visual.localPosition = p;
            float surface = Speed / tyreRadius;
            if (!w.front && WheelSpinning) surface *= 1.6f;
            if (WheelLocking && w.front) surface = 0f;
            w.spin = (w.spin + surface * Mathf.Rad2Deg * dt) % 360f;
            w.visual.localRotation = Quaternion.Euler(0, w.front ? steerAngle : 0f, 0) * Quaternion.Euler(w.spin, 0, 0);
        }
    }

    // ---------- Crashes ----------

    void OnCollisionEnter(Collision c)
    {
        float dv = c.impulse.magnitude / mass;
        if (dv < 1.5f) return;
        ContactPoint cp = c.GetContact(0);
        LastImpactStrength = dv * 2f;
        LastImpactTime = Time.time;
        if (sparks != null)
        {
            sparks.transform.position = cp.point;
            sparks.Emit(Mathf.RoundToInt(Mathf.Clamp(dv * 10f, 8f, 150f)));
        }
        var ai = c.collider.GetComponentInParent<RaceAI>();
        if (ai != null) ai.ApplyImpact(-c.impulse / mass, cp.point);
        if (dv > wingBreakDeltaV && transform.InverseTransformPoint(cp.point).z > 2.0f) BreakFrontWing();
    }

    void BreakFrontWing()
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
        debris.transform.SetPositionAndRotation(transform.TransformPoint(new Vector3(0, 0.15f, 2.8f)), transform.rotation);
        foreach (var p in parts)
        {
            p.SetParent(debris.transform, true);
            var col = p.gameObject.AddComponent<BoxCollider>();
            if (bodyCollider != null) Physics.IgnoreCollision(col, bodyCollider);
        }
        var drb = debris.AddComponent<Rigidbody>();
        drb.mass = 15f;
        drb.linearVelocity = rb.linearVelocity * 0.6f + Vector3.up * 3f + Random.insideUnitSphere * 2f;
        drb.angularVelocity = Random.insideUnitSphere * 8f;
    }

    public override void Teleport(Vector3 pos, Quaternion rot)
    {
        if (!initialized) Init();
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = pos;
        rb.rotation = rot;
        transform.SetPositionAndRotation(pos, rot);
        prevVelocity = Vector3.zero;
        lastPos = pos;
        Speed = 0f;
        steerAngle = 0f;
        gear = 1;
        foreach (var w in wheels) { w.compression = w.prevCompression = 0f; }
    }

    public void SetPerformance(float powerScale, float aeroScale, float gripScale)
    {
        peakTorque *= powerScale;
        ersPower *= powerScale;
        downforceArea *= aeroScale;
        frictionCoef *= gripScale;
    }
}
