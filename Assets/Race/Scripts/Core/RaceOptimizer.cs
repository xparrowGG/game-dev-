using UnityEngine;
using UnityEngine.Rendering;

// Run once at race start. The 2026 car is ~80k triangles at full detail; with 21 rivals bunched up that alone was
// millions of triangles, plus >1000 small shadow casters. This trims the cost without visible loss:
//  - rival cars switch to the lighter LODs much sooner (full detail only in close-up)
//  - on rival cars only the main body casts a shadow; small parts (hubs, wishbones, mirrors, cockpit) do not
//  - the rivals' cockpit interior / steering wheel (never seen from outside) is not drawn
//  - small static scenery (< 2.5 m) and the pit crews stop casting shadows
public static class RaceOptimizer
{
    public static void Apply(RaceManager m)
    {
        if (m == null) return;
        int lodCars = 0, shadowsOff = 0;
        if (m.ais != null)
            foreach (var ai in m.ais)
            {
                if (ai == null) continue;
                var g = ai.GetComponent<LODGroup>();
                if (g != null)
                {
                    var lods = g.GetLODs();
                    if (lods.Length >= 3)
                    {
                        lods[0].screenRelativeTransitionHeight = 0.42f;
                        lods[1].screenRelativeTransitionHeight = 0.13f;
                        lods[2].screenRelativeTransitionHeight = 0.005f;
                        g.SetLODs(lods);
                        lodCars++;
                    }
                }
                foreach (var r in ai.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.name;
                    if (n.Contains("SteeringWheel") || n.Contains("Cockpit_Interior") || n.Contains("Mirror_Glass"))
                    {
                        r.forceRenderingOff = true;
                        continue;
                    }
                    bool body = n.StartsWith("F1_Body") || n.StartsWith("F1_Wheel");
                    if (!body && r.shadowCastingMode != ShadowCastingMode.Off) { r.shadowCastingMode = ShadowCastingMode.Off; shadowsOff++; }
                    if (n.EndsWith("_LOD2") && r.shadowCastingMode != ShadowCastingMode.Off) { r.shadowCastingMode = ShadowCastingMode.Off; shadowsOff++; }
                }
            }

        // Scenery and pit crews: small things don't need shadows
        var carRoots = new System.Collections.Generic.HashSet<Transform>();
        if (m.player != null) carRoots.Add(m.player.transform);
        if (m.ais != null) foreach (var ai in m.ais) if (ai != null) carRoots.Add(ai.transform);
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
            if (IsUnder(r.transform, carRoots)) continue;
            bool crew = r is SkinnedMeshRenderer || r.GetComponentInParent<PitBox>() != null;
            if (crew || r.bounds.size.magnitude < 2.5f)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                shadowsOff++;
            }
        }
        Debug.Log($"RaceOptimizer: {lodCars} rival LOD groups tightened, {shadowsOff} small shadow casters turned off");
    }

    static bool IsUnder(Transform t, System.Collections.Generic.HashSet<Transform> roots)
    {
        for (var p = t; p != null; p = p.parent) if (roots.Contains(p)) return true;
        return false;
    }
}
