using UnityEngine;

// AI traffic: drives a loop of waypoints on the right-hand lane,
// slows for corners and stops for cars in front.
[RequireComponent(typeof(Rigidbody))]
public class NpcCar : MonoBehaviour
{
    public Vector3[] waypoints;
    public int targetIndex;
    public float cruiseSpeed = 11f;
    public float halfLength = 2.1f;
    public float turnRate = 110f;

    Rigidbody rb;
    Transform[] wheels;
    float speed, wheelSpin, blockedTimer, ghostTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in transform)
            if (t.name.StartsWith("Wheel")) list.Add(t);
        wheels = list.ToArray();
    }

    void FixedUpdate()
    {
        if (waypoints == null || waypoints.Length < 2) return;
        float dt = Time.fixedDeltaTime;
        Vector3 pos = rb.position;

        Vector3 wp = waypoints[targetIndex];
        Vector3 to = wp - pos; to.y = 0;
        if (to.magnitude < 3f)
        {
            targetIndex = (targetIndex + 1) % waypoints.Length;
            wp = waypoints[targetIndex];
            to = wp - pos; to.y = 0;
        }

        // Slow down approaching a corner
        Vector3 next = waypoints[(targetIndex + 1) % waypoints.Length] - wp; next.y = 0;
        float desired = cruiseSpeed;
        float dist = to.magnitude;
        if (Vector3.Angle(to, next) > 30f && dist < 16f)
            desired = Mathf.Lerp(5.5f, cruiseSpeed, (dist - 3f) / 13f);
        if (Vector3.Angle(transform.forward, to) > 45f) desired = Mathf.Min(desired, 5.5f);

        // Keep distance to whatever car is ahead
        if (ghostTimer > 0) ghostTimer -= dt;
        else
        {
            Vector3 fwd = rb.rotation * Vector3.forward;
            Vector3 origin = pos + Vector3.up * 0.8f + fwd * (halfLength + 0.3f);
            bool blockedByNpc = false;
            if (Physics.BoxCast(origin, new Vector3(0.85f, 0.4f, 0.1f), fwd, out RaycastHit hit, rb.rotation, 12f)
                && hit.rigidbody != null && hit.rigidbody != rb)
            {
                desired = Mathf.Min(desired, Mathf.Max(0f, (hit.distance - 2.5f) * 1.2f));
                blockedByNpc = hit.rigidbody.GetComponent<NpcCar>() != null;
            }
            // Two NPCs stuck waiting on each other at a junction: let this one through
            if (desired < 0.5f && blockedByNpc)
            {
                blockedTimer += dt;
                if (blockedTimer > 4f) { ghostTimer = 2.5f; blockedTimer = 0; }
            }
            else blockedTimer = 0;
        }

        speed = Mathf.MoveTowards(speed, desired, (desired < speed ? 14f : 6f) * dt);

        Quaternion targetRot = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to.normalized, Vector3.up) : rb.rotation;
        Quaternion rot = Quaternion.RotateTowards(rb.rotation, targetRot, turnRate * dt * Mathf.Clamp01(speed / 2f + 0.2f));
        rb.MoveRotation(rot);
        rb.MovePosition(pos + rot * Vector3.forward * speed * dt);
    }

    void Update()
    {
        wheelSpin = (wheelSpin + speed * Time.deltaTime / 0.33f * Mathf.Rad2Deg) % 360f;
        foreach (var w in wheels) w.localRotation = Quaternion.Euler(wheelSpin, 0, 0);
    }
}
