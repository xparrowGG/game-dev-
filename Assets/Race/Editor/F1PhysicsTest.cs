using System.Text;
using UnityEngine;
using UnityEditor;

// Tools > Race > Test F1 Physics: drives an F1Car on a flat test pad far below the scene using
// script-stepped physics (no Play mode) and logs acceleration, top speed, braking and cornering grip.
public static class F1PhysicsTest
{
    const float Dt = 0.01f;

    [MenuItem("Tools/Race/Test F1 Physics")]
    public static void Run()
    {
        var prevMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        var log = new StringBuilder("F1 physics test:\n");
        GameObject pad = null, carGo = null;
        try
        {
            pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "PhysicsTestPad";
            pad.transform.position = new Vector3(0, -5000.5f, 0);
            pad.transform.localScale = new Vector3(30000, 1, 30000);

            carGo = F1ModelBuilder.Build("PhysicsTestCar");
            carGo.AddComponent<Rigidbody>();
            var car = carGo.AddComponent<F1Car>();
            car.Init();
            car.externalInput = true;
            var rb = carGo.GetComponent<Rigidbody>();

            // 1. Settle, then full throttle
            Place(car, new Vector3(0, -4999.95f, -12000f));
            Run(car, 1.5f, 0, 0, 0, null);
            float t = 0, t100 = -1, t200 = -1, t300 = -1, maxUpTilt = 1;
            while (t < 45f)
            {
                Step(car, 1, 0, 0);
                t += Dt;
                float kmh = car.SpeedKmh;
                if (t100 < 0 && kmh >= 100) t100 = t;
                if (t200 < 0 && kmh >= 200) t200 = t;
                if (t300 < 0 && kmh >= 300) t300 = t;
                maxUpTilt = Mathf.Min(maxUpTilt, carGo.transform.up.y);
            }
            log.AppendLine($"  0-100 km/h {t100:0.00}s, 0-200 {t200:0.00}s, 0-300 {t300:0.00}s, top speed after 45s {car.SpeedKmh:0} km/h (gear {car.Gear}, {car.Rpm:0} rpm), min up.y {maxUpTilt:0.000}");

            // 2. Brake from current speed to a stop
            Vector3 p0 = carGo.transform.position;
            float v0 = car.SpeedKmh, bt = 0;
            while (car.SpeedKmh > 1f && bt < 20f) { Step(car, 0, 1, 0); bt += Dt; }
            log.AppendLine($"  Braking {v0:0}->0 km/h: {Vector3.Distance(p0, carGo.transform.position):0} m in {bt:0.00}s");

            // 3. Steady cornering at fixed speeds: hold speed with throttle, full steer
            foreach (float target in new[] { 15f, 30f, 55f })
            {
                Place(car, new Vector3(0, -4999.95f, 0));
                Run(car, 1f, 0, 0, 0, null);
                float tt = 0, maxLat = 0, spinOut = 0;
                while (tt < 25f)
                {
                    float err = target - car.Speed;
                    float thr = Mathf.Clamp01(err * 0.5f + 0.1f), brk = Mathf.Clamp01(-err * 0.3f);
                    float steer = tt < 8f ? 0 : 1f;
                    Step(car, thr, brk, steer);
                    tt += Dt;
                    if (tt > 14f)
                    {
                        float yaw = rb.angularVelocity.y;
                        maxLat = Mathf.Max(maxLat, Mathf.Abs(yaw * car.Speed) / 9.81f);
                        Vector3 v = rb.linearVelocity; v.y = 0;
                        if (v.sqrMagnitude > 1f) spinOut = Mathf.Max(spinOut, Vector3.Angle(v, Flat(carGo.transform.forward)));
                    }
                }
                float radius = Mathf.Abs(car.Speed / Mathf.Max(0.001f, rb.angularVelocity.y));
                log.AppendLine($"  Corner @ {target * 3.6f:0} km/h target: speed {car.SpeedKmh:0} km/h, lateral {maxLat:0.00} g, radius {radius:0.0} m, max slide angle {spinOut:0.0}°, up.y {carGo.transform.up.y:0.000}");
            }

            // 4. Lift-off / no-assist lockup check
            car.abs = false;
            Place(car, new Vector3(0, -4999.95f, 6000f));
            Run(car, 1f, 0, 0, 0, null);
            Run(car, 20f, 1, 0, 0, null);
            float vBefore = car.SpeedKmh; Vector3 pB = carGo.transform.position; float tb = 0;
            while (car.SpeedKmh > 1f && tb < 20f) { Step(car, 0, 1, 0); tb += Dt; }
            log.AppendLine($"  No-ABS braking {vBefore:0}->0: {Vector3.Distance(pB, carGo.transform.position):0} m, stable up.y {carGo.transform.up.y:0.000}");

            bool nan = float.IsNaN(carGo.transform.position.x);
            log.AppendLine(nan ? "  ERROR: NaN position" : "  No NaN values.");
        }
        finally
        {
            if (carGo != null) Object.DestroyImmediate(carGo);
            if (pad != null) Object.DestroyImmediate(pad);
            Physics.simulationMode = prevMode;
        }
        Debug.Log(log.ToString());
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }

    static void Place(F1Car car, Vector3 pos)
    {
        car.Teleport(pos, Quaternion.identity);
        Physics.SyncTransforms();
    }

    static void Run(F1Car car, float seconds, float thr, float brk, float steer, System.Action each)
    {
        for (float t = 0; t < seconds; t += Dt) { Step(car, thr, brk, steer); each?.Invoke(); }
    }

    static void Step(F1Car car, float thr, float brk, float steer)
    {
        car.extThrottle = thr;
        car.extBrake = brk;
        car.extSteer = steer;
        car.SimStep(Dt);
        Physics.Simulate(Dt);
    }
}
