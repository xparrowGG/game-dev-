using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Arcade driving: W/S or arrows to drive, A/D to steer, Space handbrake,
// Left Shift boost, R respawn, T reset trip.
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    public float maxSpeed = 50f;        // m/s (~180 km/h)
    public float boostSpeed = 66f;      // m/s (~240 km/h)
    public float maxReverse = 10f;
    public float acceleration = 11f;
    public float brakeForce = 28f;
    public float coastDrag = 2.5f;
    public float turnSpeedLow = 95f;    // deg/s at low speed
    public float turnSpeedHigh = 40f;   // deg/s at top speed
    public float wheelRadius = 0.34f;

    public bool inputEnabled = true;             // false holds the car (e.g. on the start grid)
    public float speedMultiplier = 1f;           // < 1 when off track
    public System.Action onRespawnRequested;     // overrides the R key respawn when set

    public float Speed { get; private set; }            // signed, m/s
    public float SpeedKmh => Mathf.Abs(Speed) * 3.6f;
    public float DistanceMeters { get; private set; }
    public float TopSpeedKmh { get; private set; }
    public float TripTime { get; private set; }
    public bool Boosting { get; private set; }

    Rigidbody rb;
    Vector3 lastPos, spawnPos;
    Quaternion spawnRot;
    Transform[] wheels = new Transform[4];   // FL, FR, RL, RR
    float vertical, horizontal, steer, wheelSpin;
    bool handbrake;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, 0.4f, 0);
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
        Vector3 fwd = rb.rotation * Vector3.forward;
        fwd.y = 0; fwd.Normalize();

        // If something (a wall, a car) stopped us, drop the speed to what we actually have
        float actual = Vector3.Dot(rb.linearVelocity, fwd);
        if (Mathf.Abs(Speed) - Mathf.Abs(actual) > 3f) Speed = actual;

        float top = (Boosting ? boostSpeed : maxSpeed) * speedMultiplier;
        if (vertical > 0)
            Speed = Speed < -0.1f ? Mathf.MoveTowards(Speed, 0, brakeForce * dt)
                                  : Mathf.MoveTowards(Speed, top, acceleration * vertical * (Boosting ? 1.5f : 1f) * dt);
        else if (vertical < 0)
            Speed = Speed > 0.1f ? Mathf.MoveTowards(Speed, 0, brakeForce * dt)
                                 : Mathf.MoveTowards(Speed, -maxReverse, acceleration * 0.6f * -vertical * dt);
        else
            Speed = Mathf.MoveTowards(Speed, 0, coastDrag * dt);
        if (handbrake) Speed = Mathf.MoveTowards(Speed, 0, brakeForce * 0.8f * dt);

        steer = Mathf.MoveTowards(steer, horizontal, 4f * dt);
        float speedFactor = Mathf.Clamp01(Mathf.Abs(Speed) / 6f);
        float turnRate = Mathf.Lerp(turnSpeedLow, turnSpeedHigh, Mathf.Abs(Speed) / maxSpeed) * (handbrake ? 1.5f : 1f);
        float yaw = steer * turnRate * speedFactor * Mathf.Sign(Speed) * dt;

        rb.MoveRotation(rb.rotation * Quaternion.Euler(0, yaw, 0));
        rb.linearVelocity = fwd * Speed + Vector3.up * rb.linearVelocity.y;
        rb.angularVelocity = Vector3.zero;

        Vector3 delta = rb.position - lastPos;
        delta.y = 0;
        DistanceMeters += delta.magnitude;
        lastPos = rb.position;
        TripTime += dt;
        TopSpeedKmh = Mathf.Max(TopSpeedKmh, SpeedKmh);
    }

    void OnCollisionEnter(Collision c)
    {
        foreach (var contact in c.contacts)
        {
            if (Mathf.Abs(contact.normal.y) < 0.5f) { Speed *= 0.35f; break; }
        }
    }

    void Respawn() => Teleport(spawnPos, spawnRot);

    public void Teleport(Vector3 pos, Quaternion rot)
    {
        Speed = 0;
        rb.linearVelocity = Vector3.zero;
        rb.position = pos;
        rb.rotation = rot;
        transform.SetPositionAndRotation(pos, rot);
        lastPos = pos;
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
