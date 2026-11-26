using UnityEngine;

// Trackside detail built at race start from the circuit's centreline (no scene or asset growth):
// braking distance boards (300 / 200 / 100 m) on the outside of every heavy braking zone.
public static class TrackDetails
{
    public static void Build(RaceTrack track)
    {
        if (track == null || track.points == null || track.points.Length < 10) return;
        var root = new GameObject("TrackDetails").transform;
        var white = Mat(new Color(0.95f, 0.95f, 0.95f));
        var post = Mat(new Color(0.25f, 0.26f, 0.28f));
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // Depth-tested text material (the default font material draws through walls)
        var textMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        textMat.SetTexture("_BaseMap", font.material.mainTexture);
        textMat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
        textMat.SetFloat("_AlphaClip", 1f); textMat.SetFloat("_Cutoff", 0.5f); textMat.EnableKeyword("_ALPHATEST_ON");
        int zones = 0, boards = 0;
        float L = track.length;
        float lastZone = -1000f;
        // A braking zone: a long fast run (radius > 250 m for 300 m) into a slow corner (radius < 90 m)
        for (float d = 0f; d < L; d += 4f)
        {
            if (track.RadiusAt(d) > 90f || d - lastZone < 400f) continue;
            bool fastBefore = true;
            for (float a = 40f; a <= 340f; a += 20f) if (track.RadiusAt(d - a) < 250f) { fastBefore = false; break; }
            if (!fastBefore) continue;
            lastZone = d;
            zones++;
            // Boards on the outside of the corner (opposite the turn direction)
            Vector3 t0 = track.TangentAt(d - 30f), t1 = track.TangentAt(d + 30f);
            float turn = Vector3.SignedAngle(new Vector3(t0.x, 0, t0.z), new Vector3(t1.x, 0, t1.z), Vector3.up);
            float side = turn > 0f ? -1f : 1f;
            foreach (int dist in new[] { 300, 200, 100 })
            {
                float bd = d - dist;
                Vector3 p = track.PointAt(bd) + track.RightAt(bd) * side * (track.HalfWidthAt(bd) + 3.2f);
                if (RaceManager.GroundAt(p, out float gy)) p.y = gy;
                var tan = track.TangentAt(bd); tan.y = 0;
                var b = new GameObject("BrakeBoard_" + dist).transform;
                b.SetParent(root, false);
                b.SetPositionAndRotation(p, Quaternion.LookRotation(-tan.normalized));
                Box(b, post, new Vector3(0, 0.9f, 0), new Vector3(0.12f, 1.8f, 0.12f));
                Box(b, white, new Vector3(0, 1.7f, 0), new Vector3(1.4f, 0.9f, 0.06f));
                var txt = new GameObject("Text").AddComponent<TextMesh>();
                txt.transform.SetParent(b, false);
                // Board faces the approaching cars (local +Z); text sits on that face, readable from the car
                txt.transform.localPosition = new Vector3(0, 1.7f, 0.04f);
                txt.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                txt.text = dist.ToString();
                txt.font = font;
                txt.GetComponent<MeshRenderer>().sharedMaterial = textMat;
                txt.fontSize = 64; txt.characterSize = 0.075f;
                txt.anchor = TextAnchor.MiddleCenter; txt.alignment = TextAlignment.Center;
                txt.color = new Color(0.05f, 0.05f, 0.05f);
                boards++;
            }
        }
        Debug.Log($"TrackDetails: {zones} braking zones, {boards} distance boards");
    }

    static Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.3f);
        return m;
    }

    static void Box(Transform parent, Material m, Vector3 pos, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
