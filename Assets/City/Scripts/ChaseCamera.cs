using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Car camera with three views, cycled with C:
//   TV Pod  - fixed onboard camera above the airbox (default)
//   Chase   - smooth third-person follow
//   Cockpit - driver's eye view
// Shakes on impacts reported by the CarController.
public class ChaseCamera : MonoBehaviour
{
    public enum View { TVPod, Chase, Cockpit }

    public Transform target;
    public View view = View.TVPod;
    public float distance = 7f;
    public float height = 2.6f;
    public float lookAhead = 4f;
    public float followSharpness = 6f;
    public Vector3 cockpitOffset = new Vector3(0, 1.22f, 0.25f);   // local to the car
    public Vector3 tvPodOffset = new Vector3(0, 1.28f, -0.55f);    // local to the car
    public float tvPodPitch = 6f;                                    // degrees looking down

    Camera cam;
    VehicleBase car;

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
        if (cycle) view = (View)(((int)view + 1) % 3);

        Vector3 fwd = target.forward;
        fwd.y = 0; fwd.Normalize();
        float kmh = car != null ? car.SpeedKmh : 0f;

        switch (view)
        {
            case View.TVPod:
                transform.position = target.TransformPoint(tvPodOffset);
                transform.rotation = target.rotation * Quaternion.Euler(tvPodPitch, 0, 0);
                if (cam != null) cam.fieldOfView = Mathf.Lerp(68f, 80f, kmh / 250f);
                break;
            case View.Cockpit:
                transform.position = target.TransformPoint(cockpitOffset);
                transform.rotation = Quaternion.LookRotation(fwd);
                if (cam != null) cam.fieldOfView = Mathf.Lerp(65f, 78f, kmh / 250f);
                break;
            default:
                Vector3 desired = target.position - fwd * distance + Vector3.up * height;
                transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
                transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 1.2f + fwd * lookAhead - transform.position);
                if (cam != null) cam.fieldOfView = Mathf.Lerp(60f, 78f, kmh / 220f);
                break;
        }

        // Impact shake, fading out over about half a second
        if (car != null && car.LastImpactTime > 0f)
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
}
