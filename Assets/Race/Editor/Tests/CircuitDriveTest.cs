using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > XPARROW > Debug > Auto-drive Lap: drives the player car round the open circuit with an autopilot
// (script-stepped physics, no Play mode), and reports falls, missing ground and the lap time.
// Also scans the lap for places where the racing lanes have no drivable surface under them.
public static class CircuitDriveTest
{
    const float Dt = 0.01f;

    [MenuItem("Tools/XPARROW/Debug/Auto-drive Lap (open circuit)")]
    public static void Run()
    {
        RunOpenScene(out _);
    }

    public static bool RunForScene(string scenePath, out string summary)
    {
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        return RunOpenScene(out summary);
    }

    public static bool RunOpenScene(out string summary)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var manager = Object.FindFirstObjectByType<RaceManager>();
        if (manager == null) { summary = "Open a circuit scene first"; Debug.LogError(summary); return false; }
        var track = manager.track;
        var car = manager.player as F1Car;
        var log = new StringBuilder($"DRIVE TEST {scene.name}: lap {track.length:0} m\n");

        // 1. Ground scan along the lap (what the AI and the wheels stand on)
        Physics.SyncTransforms();
        int missing = 0, offHeight = 0; string firstMissing = "";
        for (float d = 0; d < track.length; d += 4f)
            foreach (float lat in new[] { -2.4f, 0f, 2.4f })
            {
                Vector3 p = track.PointAt(d) + track.RightAt(d) * lat;
                if (!RaceManager.GroundAt(p, out float gy))
                {
                    missing++;
                    if (missing <= 4) firstMissing += $" d={d:0}/lat{lat:0.0} {Below(p)};";
                }
                else if (Mathf.Abs(gy - p.y) > 1.5f) offHeight++;
            }
        // Off-track: is there ground beside the circuit, and how far below the road is it?
        int holes = 0, cliffs = 0; string firstHole = "", firstCliff = "";
        for (float d = 0; d < track.length; d += 10f)
            foreach (float side in new[] { -1f, 1f })
                foreach (float off in new[] { 9f, 15f, 25f })
                {
                    Vector3 p = track.PointAt(d) + track.RightAt(d) * side * off;
                    var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
                    float top = float.MinValue; string what = "";
                    foreach (var h in hits) if (h.collider.attachedRigidbody == null && h.point.y > top) { top = h.point.y; what = h.collider.name; }
                    if (hits.Length == 0) { holes++; if (holes <= 4) firstHole += $" d={d:0} off={side * off:0};"; }
                    else if (track.PointAt(d).y - top > 2.5f) { cliffs++; if (cliffs <= 4) firstCliff += $" d={d:0} off={side * off:0} drop={track.PointAt(d).y - top:0.0} ({what});"; }
                }
        log.AppendLine($"  beside the track: {holes} samples with NO collider at all (car would fall forever),{firstHole}  {cliffs} samples >2.5 m below the road,{firstCliff}");
        log.AppendLine($"  ground scan: {missing} lane samples with no surface within 2.5 m above / 3.5 m below the centreline, {offHeight} with surface >1.5 m off the centreline height.{firstMissing}");

        // 2. Autopilot lap
        var prevMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        float travelled = 0f;
        int falls = 0;
        try
        {
            car.Init();
            car.externalInput = true;
            car.inputEnabled = true;
            var rb = car.GetComponent<Rigidbody>();
            float d0 = -10f;
            Vector3 start = track.PointAt(d0) + track.RightAt(d0) * -2.4f;
            if (RaceManager.GroundAt(start, out float sy)) start.y = sy + 0.1f;
            car.Teleport(start, Quaternion.LookRotation(track.TangentAt(d0)));
            float t = 0, prevD = Mathf.Repeat(d0, track.length), maxDrop = 0;
            int airborne = 0, offTrack = 0;
            string events = "";
            while (t < 260f && travelled < track.length + 20f)
            {
                Vector3 pos = car.transform.position;
                float d = track.ClosestDistance(pos, prevD);
                float delta = d - prevD;
                if (delta < -track.length / 2) delta += track.length;
                if (delta > track.length / 2) delta -= track.length;
                travelled += delta; prevD = d;

                float v = Mathf.Max(car.Speed, 0f);
                Vector3 aim = track.PointAt(d + 10f + 0.35f * v);
                Vector3 to = aim - pos; to.y = 0;
                Vector3 fwd = car.transform.forward; fwd.y = 0;
                float ang = Vector3.SignedAngle(fwd, to, Vector3.up);
                float target = 90f;
                for (float a = 0; a < 260f; a += 8f)
                {
                    float vc = Mathf.Min(90f, Mathf.Sqrt(13f * track.RadiusAt(d + a)));
                    target = Mathf.Min(target, Mathf.Sqrt(vc * vc + 2f * 12f * Mathf.Max(0f, a - 15f)));
                }
                float err = target - v;
                car.extThrottle = Mathf.Clamp01(err * 0.4f);
                car.extBrake = Mathf.Clamp01(-err * 0.25f);
                car.extSteer = Mathf.Clamp(ang / 12f, -1f, 1f);
                car.SimStep(Dt);
                Physics.Simulate(Dt);
                t += Dt;

                float lat = track.LateralOffset(car.transform.position, d);
                if (Mathf.Abs(lat) > track.HalfWidthAt(d) + 3f) offTrack++;
                bool hasGround = RaceManager.GroundAt(car.transform.position, out float g);
                float drop = track.PointAt(d).y - car.transform.position.y;
                maxDrop = Mathf.Max(maxDrop, drop);
                if (!hasGround) airborne++;
                if (drop > 3f || rb.linearVelocity.y < -10f)
                {
                    falls++;
                    if (falls <= 3) events += $" FALL t={t:0.0} d={d:0} lat={lat:0.0} y={car.transform.position.y:0.0} centre y={track.PointAt(d).y:0.0} vy={rb.linearVelocity.y:0.0} {Below(car.transform.position)};";
                    if (falls > 50) break;
                }
            }
            log.AppendLine($"  autopilot: travelled {travelled:0} m in {t:0.0} s ({(travelled >= track.length ? "lap done" : "DID NOT FINISH")}), top {car.TopSpeedKmh:0} km/h, " +
                           $"steps without ground {airborne}, off-track steps {offTrack}, falls {falls}, max drop below centreline {maxDrop:0.0} m.{events}");
        }
        finally
        {
            Physics.simulationMode = prevMode;
        }
        summary = log.ToString();
        Debug.Log(summary);
        // Throw away the test changes
        if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.OpenScene(scene.path);
        return missing == 0 && falls == 0 && travelled >= track.length * 0.98f;
    }

    // What is under a point: every collider hit by a long ray from above
    static string Below(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
        var sb = new StringBuilder("[");
        foreach (var h in hits) sb.Append($"{h.collider.name}@{h.point.y:0.0}(n{h.normal.y:0.00}) ");
        return sb.Append("]").ToString();
    }
}
