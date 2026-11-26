using UnityEngine;
using UnityEditor;

// Tools > Race > Simulate AI Race: runs the AI racers through a full race in the editor
// (no Play mode) and logs lap times, finishing order and any car overlaps. Restores the grid afterwards.
public static class RaceSimTest
{
    [MenuItem("Tools/Race/Simulate AI Race")]
    public static void Simulate()
    {
        var manager = Object.FindFirstObjectByType<RaceManager>();
        if (manager == null) { Debug.LogError("No RaceManager in scene."); return; }
        var track = manager.track;
        var ais = manager.ais;
        int n = ais.Length;

        var startProgress = new float[n];
        var startLane = new float[n];
        var startPos = new Vector3[n];
        var startRot = new Quaternion[n];
        manager.racers.Clear();
        var racers = new RaceManager.Racer[n];
        for (int i = 0; i < n; i++)
        {
            startProgress[i] = ais[i].progress;
            startLane[i] = ais[i].lane;
            startPos[i] = ais[i].transform.position;
            startRot[i] = ais[i].transform.rotation;
            ais[i].manager = manager;
            racers[i] = new RaceManager.Racer { name = ais[i].driverName, ai = ais[i], progress = ais[i].progress, lateral = ais[i].lane };
            manager.racers.Add(racers[i]);
        }

        const float dt = 0.02f;
        var finish = new float[n];
        var lapStart = new float[n];
        var log = new System.Text.StringBuilder();
        int overlaps = 0, finishedCount = 0;
        float time = 0, topSpeed = 0, cooldown = 20f, fastest = float.MaxValue;   // keep simulating after the finish to check parking
        for (int i = 0; i < n; i++) finish[i] = -1;
        while (time < 2000f)
        {
            time += dt;
            bool allDone = true;
            for (int i = 0; i < n; i++)
            {
                float before = ais[i].progress;
                ais[i].Step(dt, true, finish[i] >= 0, manager.aiSkill);
                racers[i].progress = ais[i].progress;
                racers[i].lateral = ais[i].lane;
                racers[i].speed = ais[i].Speed;
                topSpeed = Mathf.Max(topSpeed, ais[i].Speed);
                int lapBefore = Mathf.FloorToInt(before / track.length), lapAfter = Mathf.FloorToInt(ais[i].progress / track.length);
                if (lapAfter > lapBefore && lapAfter >= 1 && finish[i] < 0)
                {
                    float lapTime = time - lapStart[i];
                    if (i == 0) log.AppendLine($"  {ais[i].driverName} lap {lapAfter}: {lapTime:0.00}s");
                    if (lapAfter > 1) fastest = Mathf.Min(fastest, lapTime);
                    lapStart[i] = time;
                }
                if (finish[i] < 0 && ais[i].progress >= manager.laps * track.length)
                {
                    finish[i] = time;
                    if (manager.pitEntryDistance > 0f)
                    {
                        // Same as the race: cool down and leave through the pit entry
                        ais[i].parkProgress = Mathf.Floor(ais[i].progress / track.length) * track.length + manager.pitEntryDistance;
                        ais[i].parkLane = manager.pitLaneOffset;
                        ais[i].leaveAfterFinish = true;
                    }
                    else ais[i].parkProgress = manager.laps * track.length + 95f - 11f * finishedCount++;
                }
                if (finish[i] < 0) allDone = false;
            }
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    if (finish[a] < 0 && finish[b] < 0 && Mathf.Abs(racers[a].progress - racers[b].progress) < 4.4f && Mathf.Abs(racers[a].lateral - racers[b].lateral) < 1.8f) overlaps++;
            if (allDone) { if (cooldown <= 0) break; cooldown -= dt; }
        }

        log.Insert(0, $"AI race sim ({manager.laps} laps, {track.length:0} m/lap, top speed {topSpeed * 3.6f:0} km/h, fastest lap {fastest:0.00}s, overlap frames {overlaps}):\n");
        for (int i = 0; i < n; i++)
            log.AppendLine($"  FINISH {ais[i].driverName}: {finish[i]:0.00}s, stopped {ais[i].progress - manager.laps * track.length:0.0} m past line, side offset {ais[i].lane:0.0} m, left track: {!ais[i].gameObject.activeSelf} (parked flag {ais[i].Parked}, leave {ais[i].leaveAfterFinish}, park lane {ais[i].parkLane})");
        Debug.Log(log.ToString());

        for (int i = 0; i < n; i++)
        {
            ais[i].progress = startProgress[i];
            ais[i].lane = startLane[i];
            ais[i].parkProgress = float.NaN;
            ais[i].leaveAfterFinish = false;
            ais[i].gameObject.SetActive(true);
            ais[i].transform.SetPositionAndRotation(startPos[i], startRot[i]);
        }
        manager.racers.Clear();
    }
}
