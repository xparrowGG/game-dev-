using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Smooth third-person follow camera. Press C to toggle the in-car view.
public class ChaseCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 7f;
    public float height = 2.6f;
    public float lookAhead = 4f;
    public float followSharpness = 6f;
    public bool cockpitView;

    Camera cam;
    CarController car;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target != null) car = target.GetComponent<CarController>();
    }

    void LateUpdate()
    {
        if (target == null) return;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) cockpitView = !cockpitView;
#else
        if (Input.GetKeyDown(KeyCode.C)) cockpitView = !cockpitView;
#endif
        Vector3 fwd = target.forward;
        fwd.y = 0; fwd.Normalize();

        if (cam != null && car != null)
            cam.fieldOfView = Mathf.Lerp(60f, 78f, car.SpeedKmh / 220f);

        if (cockpitView)
        {
            transform.position = target.TransformPoint(new Vector3(0, 1.22f, 0.25f));
            transform.rotation = Quaternion.LookRotation(fwd);
            return;
        }

        Vector3 desired = target.position - fwd * distance + Vector3.up * height;
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
        transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 1.2f + fwd * lookAhead - transform.position);
    }
}
