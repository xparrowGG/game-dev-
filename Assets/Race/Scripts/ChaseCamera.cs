using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Race cameras (C cycles):
//  Chase     - low cinematic chase: trails the car's heading so you see it turn in, rises a little with speed,
//              never clips into walls, speed-based field of view and a fine high-speed vibration
//  T-cam     - the F1 TV onboard above the airbox, looking over the halo and the nose
//  Cockpit   - driver's eye line
//  Far chase - higher, further back: good for racing in traffic
public class ChaseCamera : MonoBehaviour
{
    public enum View { Chase, TVPod, Cockpit, FarChase }

    public Transform target;
    public View view = View.Chase;
    public float distance = 5.6f;
    public float height = 1.45f;
    public float lookAhead = 3.5f;
    public float followSharpness = 7f;
    public Vector3 cockpitOffset = new Vector3(0, 0.95f, 0.1f);    // local to the car
    public Vector3 tvPodOffset = new Vector3(0, 1.32f, -0.35f);    // local to the car
    public float tvPodPitch = 8f;                                   // degrees looking down
    public float baseFov = 62f;                                     // settings: field of view at low speed
    public bool shake = true;

    Camera cam;
    VehicleBase car;
    Vector3 heading, smoothPos, vel;
    bool init;
    static readonly RaycastHit[] hits = new RaycastHit[8];

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target != null) car = target.GetComponent<VehicleBase>();
    }

    void LateUpdate()
    {
        if (target == null) return;
#if ENABLE_INPUT_SYSTEM
        bool cycle = Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
#else
        bool cycle = Input.GetKeyDown(KeyCode.C);
#endif
        if (cycle) { view = (View)(((int)view + 1) % 4); init = false; }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 fwd = target.forward; fwd.y = 0; fwd.Normalize();
        float kmh = car != null ? Mathf.Abs(car.SpeedKmh) : 0f;
        float sp = Mathf.Clamp01(kmh / 300f);
        if (!init) { heading = fwd; smoothPos = target.position; vel = Vector3.zero; init = true; }

        switch (view)
        {
            case View.TVPod:
                transform.position = target.TransformPoint(tvPodOffset);
                transform.rotation = target.rotation * Quaternion.Euler(tvPodPitch, 0, 0);
                SetFov(baseFov + 4f + 14f * sp);
                break;
            case View.Cockpit:
                transform.position = target.TransformPoint(cockpitOffset);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(target.forward, target.up), 1f - Mathf.Exp(-20f * dt));
                SetFov(baseFov + 6f + 12f * sp);
                break;
            default:
            {
                bool far = view == View.FarChase;
                // Heading lags the car a little: in a corner the camera swings wide and shows the car's side
                heading = Vector3.Slerp(heading, fwd, 1f - Mathf.Exp(-(far ? 3.5f : 4.5f) * dt)).normalized;
                float dist = (far ? 9f : distance) + sp * (far ? 2f : 1.2f);
                float hgt = (far ? 2.6f : height) + sp * 0.25f;
                // Smooth the follow point (absorbs bumps without losing the car)
                smoothPos = Vector3.SmoothDamp(smoothPos, target.position, ref vel, 0.04f);
                Vector3 pivot = smoothPos + Vector3.up * 0.9f;
                Vector3 desired = smoothPos - heading * dist + Vector3.up * hgt;
                // Keep the camera out of walls and grandstands
                Vector3 dir = desired - pivot;
                int n = Physics.SphereCastNonAlloc(pivot, 0.25f, dir.normalized, hits, dir.magnitude, ~0, QueryTriggerInteraction.Ignore);
                float allowed = dir.magnitude;
                for (int i = 0; i < n; i++)
                {
                    if (hits[i].collider.attachedRigidbody != null || hits[i].distance <= 0f) continue;
                    allowed = Mathf.Min(allowed, hits[i].distance - 0.1f);
                }
                transform.position = pivot + dir.normalized * Mathf.Max(1.5f, allowed);
                Vector3 look = smoothPos + Vector3.up * (far ? 0.6f : 0.75f) + fwd * (far ? lookAhead + 2f : lookAhead);
                transform.rotation = Quaternion.LookRotation(look - transform.position);
                SetFov(baseFov - 4f + 18f * sp);
                break;
            }
        }

        // Fine vibration at speed (chassis buzz), stronger on the onboards
        if (shake && kmh > 150f)
        {
            float amp = (kmh - 150f) / 200f * (view == View.Chase || view == View.FarChase ? 0.004f : 0.008f);
            transform.position += new Vector3(Mathf.PerlinNoise(Time.time * 40f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 43f) - 0.5f, 0f) * amp * 2f;
        }
        // Subtle kerb rumble
        if (shake && car is F1Car f1c && f1c.OnKerb && kmh > 30f)
        {
            float kAmp = Mathf.Clamp01(kmh / 180f) * (view == View.Chase || view == View.FarChase ? 0.012f : 0.018f);
            transform.position += new Vector3(0f, Mathf.Sin(Time.time * 65f) * kAmp, 0f);
        }
        // Impact shake, fading out over about half a second
        if (shake && car != null && car.LastImpactTime > 0f)
        {
            float age = Time.time - car.LastImpactTime;
            float amp = Mathf.Clamp01(car.LastImpactStrength / 20f) * 0.22f * Mathf.Exp(-age * 6f);
            if (amp > 0.002f)
            {
                transform.position += Random.insideUnitSphere * amp;
                transform.rotation *= Quaternion.Euler(Random.Range(-1f, 1f) * amp * 25f, Random.Range(-1f, 1f) * amp * 25f, 0);
            }
        }
    }

    void SetFov(float f)
    {
        if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, f, 0.1f);
    }
}
