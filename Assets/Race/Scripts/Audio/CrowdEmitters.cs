using System.Collections.Generic;
using UnityEngine;

// At race start: places positional crowd sound at the circuit's grandstands and spectator areas (found by name),
// so the crowd is heard loudly when you drive past a stand and cheers as your car goes by. No assets needed.
public static class CrowdEmitters
{
    const int MaxEmitters = 14;
    const float MergeDistance = 70f;

    public static void Build(Transform player)
    {
        var points = new List<Vector3>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            string n = r.name;
            if (!(n.Contains("Crowd") || n.Contains("Grandstand") || n.Contains("Spectator") || n.Contains("Tribune"))) continue;
            // Big stand meshes: spread several points along their length
            var b = r.bounds;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(b.size.x, b.size.z) / 90f), 1, 8);
            Vector3 axis = b.size.x >= b.size.z ? Vector3.right * b.size.x : Vector3.forward * b.size.z;
            for (int i = 0; i < steps; i++)
            {
                Vector3 p = b.center + axis * ((i + 0.5f) / steps - 0.5f);
                p.y = b.min.y + 3f;
                points.Add(p);
            }
        }
        // Merge nearby points, keep the biggest clusters
        var clusters = new List<(Vector3 c, int n)>();
        foreach (var p in points)
        {
            int best = -1;
            for (int i = 0; i < clusters.Count; i++) if ((clusters[i].c - p).sqrMagnitude < MergeDistance * MergeDistance) { best = i; break; }
            if (best < 0) clusters.Add((p, 1));
            else { var c = clusters[best]; clusters[best] = ((c.c * c.n + p) / (c.n + 1), c.n + 1); }
        }
        clusters.Sort((a, b) => b.n.CompareTo(a.n));
        var root = new GameObject("CrowdEmitters").transform;
        int made = 0;
        foreach (var c in clusters)
        {
            if (made >= MaxEmitters) break;
            var go = new GameObject("Crowd_" + made);
            go.transform.SetParent(root, false);
            go.transform.position = c.c;
            go.AddComponent<AudioSource>();
            var amb = go.AddComponent<AmbientAudio>();
            amb.positional = true;
            amb.volume = 0.55f;
            amb.watch = player;
            made++;
        }
        Debug.Log($"CrowdEmitters: {made} grandstand sound sources from {points.Count} crowd points");
    }
}
