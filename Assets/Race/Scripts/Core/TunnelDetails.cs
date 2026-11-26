using UnityEngine;

// Covered sections of track (Monaco's tunnel), found by casting up from the centreline. Built in code at race start:
//  - concrete portal frames (lintel + pillars) at the entry and exit, hiding the raw ceiling edge
//  - the bare rock block above the tunnel gets the circuit's concrete/stucco facade material
//  - warm lights along the inside
//  - eye adaptation on the camera: ambient light drops as you enter and returns as you come out
public static class TunnelDetails
{
    public static void Build(RaceTrack track)
    {
        if (track == null || track.points == null) return;
        float L = track.length, start = -1f, end = -1f;
        int spans = 0;
        var root = new GameObject("TunnelDetails").transform;
        Material concrete = FindMaterial("Monaco_White", "Concrete_Wall", "Stucco_White", "Concrete", "Stucco") ?? NewMat(new Color(0.78f, 0.77f, 0.74f));
        for (float d = 0f; d <= L + 2f; d += 2f)
        {
            bool roof = d < L && Roof(track, d, out _);
            if (roof) { if (start < 0f) start = d; end = d; continue; }
            if (start >= 0f && d - end > 20f)
            {
                if (end - start > 60f)
                {
                    spans++;
                    Portal(root, track, start, concrete, true);
                    Portal(root, track, end, concrete, false);
                    Lights(root, track, start, end);
                }
                start = end = -1f;
            }
        }
        if (spans == 0) { Object.Destroy(root.gameObject); return; }
        // The rock block above the tunnel mouth: concrete facade instead of flat grey
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (r.name.Contains("Tunnel_Mountain"))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.Contains("Rock")) mats[i] = concrete;
                r.sharedMaterials = mats;
            }
        var cam = Camera.main;
        if (cam != null && cam.GetComponent<TunnelAdaptation>() == null) cam.gameObject.AddComponent<TunnelAdaptation>();
        Debug.Log($"TunnelDetails: {spans} covered section(s) dressed");
    }

    static bool Roof(RaceTrack track, float d, out float height)
    {
        height = 0f;
        Vector3 p = track.PointAt(d) + Vector3.up * 1.5f;
        if (Physics.Raycast(p, Vector3.up, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == null)
        { height = hit.distance + 1.5f; return true; }
        return false;
    }

    static float WallDistance(Vector3 from, Vector3 dir)
    {
        return Physics.Raycast(from, dir, out var hit, 25f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 9f;
    }

    static void Portal(Transform root, RaceTrack track, float d, Material m, bool entry)
    {
        Roof(track, d + (entry ? 4f : -4f), out float ceiling);
        if (ceiling < 3f) ceiling = 6.5f;
        Vector3 c = track.PointAt(d);
        Vector3 t = track.TangentAt(d); t.y = 0f; t.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, t);
        Vector3 probe = c + Vector3.up * 1.5f + t * (entry ? 3f : -3f);
        float wl = WallDistance(probe, -right), wr = WallDistance(probe, right);
        float width = wl + wr;
        Vector3 mid = c + right * ((wr - wl) * 0.5f);
        var g = new GameObject(entry ? "TunnelPortal_Entry" : "TunnelPortal_Exit").transform;
        g.SetParent(root, false);
        g.SetPositionAndRotation(mid, Quaternion.LookRotation(entry ? t : -t));
        float clearance = Mathf.Max(4.6f, ceiling - 2.0f);
        // Lintel above the opening (covers the raw ceiling edge) and side pillars over the wall ends
        Block(g, m, new Vector3(0f, clearance + 2.2f, 0.2f), new Vector3(width + 3.0f, 4.4f, 1.6f));
        Block(g, m, new Vector3(-(width * 0.5f + 0.9f), clearance * 0.5f, 0.2f), new Vector3(1.8f, clearance, 1.6f));
        Block(g, m, new Vector3(width * 0.5f + 0.9f, clearance * 0.5f, 0.2f), new Vector3(1.8f, clearance, 1.6f));
        // Dark recess line and a warm light band under the lintel
        Block(g, NewMat(new Color(0.06f, 0.06f, 0.07f)), new Vector3(0f, clearance + 0.05f, 0.9f), new Vector3(width, 0.12f, 0.2f));
        var band = NewMat(new Color(1f, 0.85f, 0.55f));
        band.EnableKeyword("_EMISSION"); band.SetColor("_EmissionColor", new Color(1f, 0.78f, 0.45f) * 2.2f);
        Block(g, band, new Vector3(0f, clearance - 0.05f, 0.6f), new Vector3(width * 0.8f, 0.08f, 0.25f));
    }

    static void Lights(Transform root, RaceTrack track, float a, float b)
    {
        for (float d = a + 12f; d < b - 6f; d += 30f)
        {
            Roof(track, d, out float h);
            var go = new GameObject("TunnelLight");
            go.transform.SetParent(root, false);
            go.transform.position = track.PointAt(d) + Vector3.up * Mathf.Max(3f, h - 0.6f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 20f;
            l.intensity = 5f;
            l.color = new Color(1f, 0.82f, 0.6f);
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
        }
    }

    static void Block(Transform parent, Material m, Vector3 pos, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(g.GetComponent<Collider>());     // visual only: the circuit's own walls stay the collision
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    static Material FindMaterial(params string[] names)
    {
        foreach (var n in names)
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.StartsWith(n) && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m;
        return null;
    }

    static Material NewMat(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.2f);
        return m;
    }
}

// On the race camera: eyes adapting to the tunnel (darker ambient inside, smooth transitions in and out)
public class TunnelAdaptation : MonoBehaviour
{
    float baseAmbient = -1f, inside;
    static readonly RaycastHit[] hits = new RaycastHit[4];

    void LateUpdate()
    {
        if (baseAmbient < 0f) baseAmbient = RenderSettings.ambientIntensity;
        int n = Physics.RaycastNonAlloc(transform.position, Vector3.up, hits, 14f, ~0, QueryTriggerInteraction.Ignore);
        bool covered = false;
        for (int i = 0; i < n; i++) if (hits[i].collider.attachedRigidbody == null) { covered = true; break; }
        inside = Mathf.MoveTowards(inside, covered ? 1f : 0f, Time.deltaTime * (covered ? 1.6f : 1.1f));
        RenderSettings.ambientIntensity = baseAmbient * Mathf.Lerp(1f, 0.42f, inside);
    }

    void OnDisable()
    {
        if (baseAmbient >= 0f) RenderSettings.ambientIntensity = baseAmbient;
    }
}
