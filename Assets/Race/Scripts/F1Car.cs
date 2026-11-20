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
    public float downforceArea = 5.0f;          // Cl·A
    public float dragArea = 0.95f;              // Cd·A
    [Range(0, 1)] public float aeroBalanceFront = 0.44f;
    public float airDensity = 1.225f;

    [Header("Tyres")]
    public float tyreRadius = 0.36f;
    public float frictionCoef = 2.0f;           // peak μ of a F1 slick at nominal load
    public float nominalLoad = 3000f;           // N
    public float loadSensitivity = 0.04f;       // μ drop per extra nominal load
    public float corneringStiffness = 11f;      // Pacejka B
    public float shapeFactor = 1.35f;           // Pacejka C

    [Header("Brakes")]
    public float maxBrakeForce = 78000f;        // N total carbon-carbon braking capacity
    [Range(0, 1)] public float brakeBias = 0.64f;

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
    [System.NonSerialized] public float slipstreamFactor;      // 0..1 aero tow behind another car
    public bool OnGravel { get; private set; }
    public bool OnGrass { get; private set; }
    public bool OnKerb { get; private set; }
    public float TyreTemp { get; private set; } = 90f;
    // DRS: the race manager says when the car is in an activation zone; the driver opens the flap (F / Y)
    [System.NonSerialized] public bool drsAvailable, drsAuto;
    public bool DrsOpen { get; private set; }
    bool drsPressed;
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
        public Transform steer;         // front upright / brake hub pivot (null on rear)
        public Transform visual;        // spinning tyre mesh
        public Vector3 attach;          // local suspension top
        public bool front, left;
        public float compression, prevCompression, load, spin;
        public bool grounded;
        public RaycastHit hit;
    }

    readonly Wheel[] wheels = new Wheel[4];
    Transform steeringWheelPivot, drsFlapPivot;
    Quaternion steeringWheelBaseRot, drsFlapBaseRot;
    Vector3 steeringAxisLocal = Vector3.forward, drsAxisLocal = Vector3.right;
    float drsVisualAngle;
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
    Vector3 prevVelocity, lastWallNormal;
    float wallContactTimer, lastImpactLogTime = -10f;
    readonly List<GameObject> frontWingParts = new List<GameObject>();
    static readonly RaycastHit[] noseHits = new RaycastHit[4];

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
        rb.angularDamping = 0.35f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxDepenetrationVelocity = 0.65f;   // barriers resolve smoothly without vibration or flinging
        bodyCollider = GetComponent<Collider>();
        // Carbon on steel: zero bounce, low friction so wall hits slide smoothly without shuddering
        if (bodyCollider != null)
            bodyCollider.sharedMaterial = new PhysicsMaterial("F1Body")
            {
                bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum,
                dynamicFriction = 0.12f, staticFriction = 0.12f, frictionCombine = PhysicsMaterialCombine.Minimum
            };
        var sparksTf = transform.Find("Sparks");
        if (sparksTf != null) sparks = sparksTf.GetComponent<ParticleSystem>();

        string[] legacyNames = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
        string[] steerNames = { "SteerFL", "SteerFR", null, null };
        string[] spinNames = { "SpinFL", "SpinFR", "SpinRL", "SpinRR" };
        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2, left = i % 2 == 0;
            float x = (front ? frontTrack : rearTrack) * (left ? -1 : 1);
            float z = front ? frontAxleZ : rearAxleZ;
            Transform wheelsRoot = transform.Find("Wheels");
            Transform steerTf = (wheelsRoot != null && steerNames[i] != null) ? wheelsRoot.Find(steerNames[i]) : null;
            Transform spinTf = steerTf != null ? steerTf.Find(spinNames[i]) : (wheelsRoot != null ? wheelsRoot.Find(spinNames[i]) : null);
            if (spinTf == null && wheelsRoot != null) spinTf = wheelsRoot.Find(legacyNames[i]);
            wheels[i] = new Wheel
            {
                name = legacyNames[i], front = front, left = left,
                steer = steerTf,
                visual = spinTf,
                attach = new Vector3(x, tyreRadius + restLength, z)
            };
        }
        var body = transform.Find("Body");
        if (body != null)
        {
            steeringWheelPivot = FindDeepChild(body, "SteeringWheel_Pivot");
            if (steeringWheelPivot != null)
            {
                steeringWheelBaseRot = steeringWheelPivot.localRotation;
                steeringAxisLocal = steeringWheelPivot.InverseTransformDirection(transform.TransformDirection(new Vector3(0f, -0.25f, 0.968f))).normalized;
            }
            drsFlapPivot = FindDeepChild(body, "DRS_Flap_Pivot");
            if (drsFlapPivot != null)
            {
                drsFlapBaseRot = drsFlapPivot.localRotation;
                drsAxisLocal = drsFlapPivot.InverseTransformDirection(transform.TransformDirection(Vector3.right)).normalized;
            }
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("FrontWing") || t.name.StartsWith("FrontEndplate")) frontWingParts.Add(t.gameObject);
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
            drsPressed |= k.fKey.wasPressedThisFrame;
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
            drsPressed |= g.buttonNorth.wasPressedThisFrame;
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
        drsPressed |= Input.GetKeyDown(KeyCode.F);
#endif
        float dt = Time.deltaTime;
        // Digital keys are eased in like a driver squeezing the pedal; analog input is used directly
        inThrottle = analogPedals ? throttleKey : Mathf.MoveTowards(inThrottle, throttleKey, (throttleKey > inThrottle ? 3.5f : 9f) * dt);
        inBrake = analogPedals ? brakeKey : Mathf.MoveTowards(inBrake, brakeKey, (brakeKey > inBrake ? 14f : 12f) * dt);
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

    // While the race manager drives the car through the pit lane (kinematic), it reports speed & steer here.
    public void SetPitSpeed(float v) => SetPitDriving(v, 0f);

    public void SetPitDriving(float v, float visualSteerDeg)
    {
        Speed = v;
        steerAngle = Mathf.MoveTowards(steerAngle, Mathf.Clamp(visualSteerDeg, -maxSteerAngle, maxSteerAngle), 140f * Time.fixedDeltaTime);
        gear = v < 0.4f ? 0 : (v > 24f ? 3 : 2);
        rpm = v < 0.4f ? idleRpm : Mathf.Clamp(idleRpm + v / 34f * 4500f, idleRpm, redline);
        ThrottleInput = v > 1f ? 0.35f : 0.05f;
    }

    public void OnPitExitHandoff(Vector3 exitVelocity, Quaternion exitRotation)
    {
        if (!initialized) Init();
        rb.isKinematic = false;
        rb.position = transform.position;
        rb.rotation = exitRotation;
        transform.rotation = exitRotation;
        rb.linearVelocity = exitVelocity;
        rb.angularVelocity = Vector3.zero;
        prevVelocity = exitVelocity;
        steerAngle = 0f;
        inBrake = 0f;
        float speed = exitVelocity.magnitude;
        Speed = speed;
        // Match gearbox cleanly to exit speed so no downshift/rev spike occurs on release
        int bestGear = 1;
        for (int g = 1; g <= gearRatios.Length; g++)
        {
            float r = speed / tyreRadius * gearRatios[g - 1] * 60f / (2f * Mathf.PI);
            if (r <= upshiftRpm) { bestGear = g; break; }
            bestGear = g;
        }
        gear = bestGear;
        rpm = Mathf.Clamp(speed / tyreRadius * gearRatios[gear - 1] * 60f / (2f * Mathf.PI), idleRpm, redline);
        foreach (var w in wheels) { w.compression = 0.055f; w.prevCompression = 0.055f; w.grounded = true; }
        lastPos = rb.position;
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

        float dvDrop = Vector3.Dot(prevVelocity - vel, fwd);
        if (dvDrop > wingBreakDeltaV && TripTime > 0.25f)
        {
            int nh = Physics.RaycastNonAlloc(transform.TransformPoint(0f, 0.42f, 1.0f), fwd, noseHits, 2.8f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < nh; i++)
            {
                var h = noseHits[i];
                if (h.collider.transform.IsChildOf(transform) || Mathf.Abs(h.normal.y) >= 0.55f) continue;
                if (h.distance < bestDist) { bestDist = h.distance; bestIdx = i; }
            }
            if (bestIdx >= 0)
            {
                var hit = noseHits[bestIdx];
                Vector3 wn = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (wn.sqrMagnitude > 1e-4f)
                {
                    lastWallNormal = wn.normalized;
                    wallContactTimer = 0.14f;
                }
                if (TripTime - lastImpactLogTime > 0.22f)
                {
                    lastImpactLogTime = TripTime;
                    RegisterImpact(dvDrop, hit.point);
                }
                else if (!FrontWingLost)
                {
                    BreakFrontWing();
                }
            }
        }

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

        // DRS: opens only in a zone, shuts the moment the driver touches the brakes
        if (!drsAvailable || brake > 0.1f || !inputEnabled || externalInput) DrsOpen = false;
        else if (drsPressed || drsAuto) DrsOpen = true;
        drsPressed = false;

        // Aerodynamics (open DRS flap: less drag, less rear downforce)
        float q = 0.5f * airDensity * vLong * vLong;
        float downforce = q * downforceArea * (FrontWingLost ? 0.8f : 1f) * (DrsOpen ? 0.86f : 1f);
        float frontShare = FrontWingLost ? aeroBalanceFront * 0.55f : aeroBalanceFront;
        rb.AddForceAtPosition(-transform.up * downforce * frontShare, transform.TransformPoint(0, 0.3f, frontAxleZ));
        rb.AddForceAtPosition(-transform.up * downforce * (1f - frontShare), transform.TransformPoint(0, 0.3f, rearAxleZ));
        float dragScale = (DrsOpen ? 0.8f : 1f) * (1f - 0.14f * Mathf.Clamp01(slipstreamFactor));
        if (vel.sqrMagnitude > 0.01f) rb.AddForce(-vel.normalized * 0.5f * airDensity * vel.sqrMagnitude * dragArea * dragScale);

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
            w.grounded = GroundRay(top, -transform.up, rest + 0.15f, out w.hit) && w.hit.distance <= rest + 0.01f;
            w.compression = w.grounded ? Mathf.Clamp(rest - w.hit.distance, 0f, restLength) : 0f;
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
                w.load = Mathf.Clamp(f, 0f, mass * 9.81f * 4.5f);
                rb.AddForceAtPosition(transform.up * w.load, w.hit.point);
            }
        }

        // Tyres
        WheelLocking = false;
        WheelSpinning = false;
        OnGravel = false;
        OnGrass = false;
        OnKerb = false;
        ThrottleInput = driveInput;
        float tempGrip = 1f;
        if (TyreTemp < 70f) tempGrip = Mathf.Lerp(0.92f, 1f, Mathf.InverseLerp(30f, 70f, TyreTemp));
        else if (TyreTemp > 112f) tempGrip = Mathf.Lerp(1f, 0.93f, Mathf.InverseLerp(112f, 135f, TyreTemp));
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

            float mu = frictionCoef * tyreGripScale * tempGrip * Mathf.Clamp(1f - loadSensitivity * (w.load / nominalLoad - 1f), 0.75f, 1.25f);
            if (w.front && FrontWingLost) mu *= 0.9f;
            // Gravel trap: little grip and a lot of drag
            var surface = w.hit.collider.sharedMaterial;
            bool gravel = surface != null && surface.name.StartsWith("Gravel");
            if (gravel) { mu *= 0.45f; OnGravel = true; }
            else if (surface != null && surface.name.StartsWith("Grass")) { mu *= 0.55f; OnGrass = true; }
            else if (surface != null && surface.name.StartsWith("Kerb")) { mu *= 0.93f; OnKerb = true; }
            float maxF = mu * w.load;

            // Lateral: slip angle through a Pacejka-style curve (speed floor keeps it stable at a crawl)
            float slip = Mathf.Atan2(vy, Mathf.Max(Mathf.Abs(vx), 4f));
            if (Mathf.Abs(vx) > 5f) slipMax = Mathf.Max(slipMax, Mathf.Abs(slip) / 0.12f);
            float fy = -maxF * Mathf.Sin(shapeFactor * Mathf.Atan(corneringStiffness * slip));

        // Longitudinal request: drive (zero out head-on wall push when pinned), engine/MGU-K braking, carbon brakes
            bool pinnedHeadOnWall = wallContactTimer > 0f && Vector3.Dot(fwd, lastWallNormal) < -0.55f && !reverse;
            float fx = w.front ? 0f : (pinnedHeadOnWall ? 0f : driveForce * 0.5f);
            if (!w.front && !reverse && (brakeInput > 0.05f || (throttle < 0.05f && absV > 8f)))
            {
                float eb = (brakeInput > 0.05f ? 3200f * brakeInput : 1350f) * Mathf.Clamp01(absV / 40f);
                fx += -Mathf.Sign(vx) * eb * 0.5f;
            }
            float wheelBrake = brakeTotal * (w.front ? brakeBias : 1f - brakeBias) * 0.5f;
            if (handbrake && !w.front) wheelBrake = Mathf.Max(wheelBrake, maxF * 1.2f);
            if (wheelBrake > 0f)
            {
                float hold = -vx * cornerMass / dt;          // force that would stop the wheel this step
                fx += Mathf.Clamp(hold, -wheelBrake, wheelBrake);
            }
            fx -= vx * (gravel ? 380f : OnGrass ? 70f : 12f);                 // rolling resistance (gravel digs in)

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
            Vector3 wForce = wFwd * fx + wRight * fy;
            if (wallContactTimer > 0f)
            {
                float into = Vector3.Dot(wForce, lastWallNormal);
                if (into < 0f) wForce -= lastWallNormal * into;
            }
            rb.AddForceAtPosition(wForce, w.hit.point);
        }
        wallContactTimer = Mathf.Max(0f, wallContactTimer - dt);

        if (wallContactTimer > 0f)
        {
            Vector3 vWall = rb.linearVelocity;
            float vInto = Vector3.Dot(vWall, lastWallNormal);
            if (vInto < 0f) vWall -= lastWallNormal * vInto;
            vWall.y = Mathf.Min(vWall.y, 0.01f);
            rb.linearVelocity = vWall;
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

        // After a hit: keep F1 car planted with zero upward launch or elastic bounce (uses TripTime so works in EditMode too)
        if (TripTime - lastImpactLogTime < 0.8f && anyGrounded)
        {
            Vector3 v = rb.linearVelocity;
            if (v.y > 0.02f) { v.y = 0.02f; rb.linearVelocity = v; }
            Vector3 local = transform.InverseTransformDirection(rb.angularVelocity);
            local.x *= 0.35f;
            local.z *= 0.35f;
            rb.angularVelocity = transform.TransformDirection(local);
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
        float heat = (0.18f * (Mathf.Abs(LateralG) + Mathf.Abs(LongitudinalG)) + 0.5f * TyreSlip) * Mathf.Min(absV, 80f) * 0.02f;
        float cool = (TyreTemp - 35f) * (0.006f + 0.00004f * absV) + (OnGrass ? 1.5f : 0f);
        TyreTemp = Mathf.Clamp(TyreTemp + (heat - cool) * dt, 35f, 130f);

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
            float surface = Speed / tyreRadius;
            if (!w.front && WheelSpinning) surface *= 1.6f;
            if (WheelLocking && w.front) surface = 0f;
            w.spin = (w.spin + surface * Mathf.Rad2Deg * dt) % 360f;
            if (w.steer != null)
            {
                w.steer.localPosition = p;
                w.steer.localRotation = Quaternion.Euler(0, w.front ? steerAngle : 0f, 0);
                w.visual.localRotation = Quaternion.Euler(w.spin, 0, 0);
            }
            else
            {
                w.visual.localPosition = p;
                w.visual.localRotation = Quaternion.Euler(0, w.front ? steerAngle : 0f, 0) * Quaternion.Euler(w.spin, 0, 0);
            }
        }
        if (steeringWheelPivot != null)
            steeringWheelPivot.localRotation = steeringWheelBaseRot * Quaternion.AngleAxis(-steerAngle * 5f, steeringAxisLocal);
        if (drsFlapPivot != null)
        {
            drsVisualAngle = Mathf.MoveTowards(drsVisualAngle, DrsOpen ? -28f : 0f, 220f * dt);
            drsFlapPivot.localRotation = drsFlapBaseRot * Quaternion.AngleAxis(drsVisualAngle, drsAxisLocal);
        }
    }

    // ---------- Crashes ----------

    // Wheel sensors only count the racing surface: never another car, debris, or the side/top of a barrier
    static readonly RaycastHit[] rayHits = new RaycastHit[8];
    bool GroundRay(Vector3 origin, Vector3 dir, float length, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, rayHits, length, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = rayHits[i];
            if (h.collider == bodyCollider || h.collider.attachedRigidbody != null) continue;
            if (h.normal.y < 0.6f || h.collider.name.StartsWith("Barrier")) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; }
        }
        return bestDist < float.MaxValue;
    }

    // Walls and barriers: smooth inelastic absorption without elastic bounce or PhysX depenetration buzz.
    void OnCollisionEnter(Collision c)
    {
        if (!HandleBarrierCollision(c, out float dv, out Vector3 hitPoint)) return;
        if (dv >= 1.5f && TripTime - lastImpactLogTime > 0.22f)
        {
            lastImpactLogTime = TripTime;
            RegisterImpact(dv, hitPoint);
        }
    }

    void OnCollisionStay(Collision c)
    {
        if (HandleBarrierCollision(c, out float dv, out Vector3 hitPoint) && !FrontWingLost && dv > wingBreakDeltaV && transform.InverseTransformPoint(hitPoint).z > 1.25f)
            BreakFrontWing();
    }

    bool HandleBarrierCollision(Collision c, out float dvOut, out Vector3 hitPoint)
    {
        dvOut = 0f;
        hitPoint = transform.position;
        if (c.collider.attachedRigidbody != null && c.collider.attachedRigidbody.isKinematic && c.collider.GetComponentInParent<RaceAI>() != null)
            return false;

        Vector3 n = Vector3.zero;
        int nCount = c.contactCount;
        float bestLocalZ = float.MinValue;
        for (int i = 0; i < nCount; i++)
        {
            var cp = c.GetContact(i);
            if (Mathf.Abs(cp.normal.y) < 0.55f)
            {
                n += cp.normal;
                float lz = transform.InverseTransformPoint(cp.point).z;
                if (lz > bestLocalZ)
                {
                    bestLocalZ = lz;
                    hitPoint = cp.point;
                }
            }
        }
        n.y = 0f;
        if (n.sqrMagnitude < 1e-4f) return false;
        n.Normalize();

        lastWallNormal = n;
        wallContactTimer = 0.14f;

        Vector3 imp = c.impulse; imp.y = 0f;
        float dvImp = imp.magnitude / mass;

        Vector3 v = rb.linearVelocity;
        float vIn = Vector3.Dot(v, n);
        float closing = vIn < 0f ? -vIn : 0f;
        float relNormal = Mathf.Abs(Vector3.Dot(c.relativeVelocity, n));
        if (vIn < 0f)
        {
            Vector3 vHoriz = new Vector3(v.x, 0f, v.z);
            Vector3 vTan = vHoriz - n * vIn;
            vTan *= Mathf.Lerp(0.96f, 0.82f, Mathf.Clamp01(closing / 25f));
            Vector3 newV = vTan + n * (closing * 0.01f);
            newV.y = Mathf.Min(v.y, 0.01f);
            rb.linearVelocity = newV;
        }
        else if (v.y > 0.01f)
        {
            v.y = 0.01f;
            rb.linearVelocity = v;
        }

        Vector3 localAng = transform.InverseTransformDirection(rb.angularVelocity);
        localAng.x *= 0.15f;
        localAng.z *= 0.15f;
        localAng.y = Mathf.Clamp(localAng.y, -2.0f, 2.0f);
        rb.angularVelocity = transform.TransformDirection(localAng);

        dvOut = Mathf.Max(dvImp, closing, relNormal);
        return true;
    }

    void RegisterImpact(float dv, Vector3 point)
    {
        LastImpactStrength = dv * 2f;
        LastImpactTime = Time.time;
        if (sparks != null)
        {
            sparks.transform.position = point;
            sparks.Emit(Mathf.RoundToInt(Mathf.Clamp(dv * 10f, 8f, 150f)));
        }
        if (dv > wingBreakDeltaV && transform.InverseTransformPoint(point).z > 1.25f) BreakFrontWing();
    }

    // Car-to-car contact (rival cars are triggers): velocity-based separation + inelastic momentum sharing (no jitter/teleport)
    void OnTriggerStay(Collider other)
    {
        var ai = other.GetComponentInParent<RaceAI>();
        if (ai == null || rb.isKinematic || bodyCollider == null) return;
        if (!Physics.ComputePenetration(bodyCollider, rb.position, rb.rotation, other, other.transform.position, other.transform.rotation,
                                        out Vector3 dir, out float depth)) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();

        Vector3 v = rb.linearVelocity;
        float sepV = Mathf.Clamp(depth * 14f, 0f, 3.2f);
        Vector3 vSep = v + dir * sepV;
        Vector3 vRel = vSep - ai.Velocity;
        float closing = Vector3.Dot(vRel, dir);
        if (closing < 0f)
        {
            Vector3 change = -dir * closing * 0.42f;
            vSep += change;
            Vector3 cp = other.ClosestPoint(rb.position);
            ai.ApplyImpact(-change * 0.85f, cp);
            float dv = change.magnitude;
            if (dv > 1.8f && Time.time - LastImpactTime > 0.35f) RegisterImpact(dv, cp);
        }
        vSep.y = Mathf.Min(vSep.y, 0.1f);
        rb.linearVelocity = vSep;
        Vector3 la = transform.InverseTransformDirection(rb.angularVelocity);
        la.x *= 0.4f; la.z *= 0.4f;
        rb.angularVelocity = transform.TransformDirection(la);
    }

    void BreakFrontWing()
    {
        if (FrontWingLost || frontWingParts.Count == 0) return;
        FrontWingLost = true;
        var debris = new GameObject("FrontWingDebris");
        debris.transform.SetPositionAndRotation(transform.TransformPoint(new Vector3(0, 0.15f, 2.55f)), transform.rotation);
        foreach (var go in frontWingParts)
        {
            if (go == null) continue;
            var clone = Instantiate(go, debris.transform);
            clone.transform.position = go.transform.position;
            clone.transform.rotation = go.transform.rotation;
            clone.transform.localScale = go.transform.lossyScale;
            clone.SetActive(true);
            var col = clone.AddComponent<BoxCollider>();
            if (bodyCollider != null) Physics.IgnoreCollision(col, bodyCollider);
            go.SetActive(false);
        }
        var drb = debris.AddComponent<Rigidbody>();
        drb.mass = 15f;
        drb.linearVelocity = rb.linearVelocity * 0.55f + Vector3.up * 2.5f + Random.insideUnitSphere * 1.6f;
        drb.angularVelocity = Random.insideUnitSphere * 7f;
        if (Application.isPlaying) Destroy(debris, 8f); else DestroyImmediate(debris);
    }

    public void RepairFrontWing()
    {
        if (!FrontWingLost) return;
        FrontWingLost = false;
        foreach (var go in frontWingParts)
            if (go != null) go.SetActive(true);
    }

    public void ForceBreakFrontWingForTest() => BreakFrontWing();

    static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
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
        wallContactTimer = 0f;
        lastWallNormal = Vector3.zero;
        lastImpactLogTime = -10f;
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
