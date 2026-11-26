using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Tools > XPARROW > Debug: performance probes.
//  Log Performance       - Play mode: frame time and render stats of the Game view
//  Analyse Scene Weight  - edit mode: which objects/materials cost the most triangles, draw calls and shadows
public static class PerfProbe
{
    [MenuItem("Tools/XPARROW/Debug/Log Performance")]
    static void Log()
    {
        Debug.Log($"PERF fps={1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0} frame={Time.smoothDeltaTime * 1000f:0.0} ms, " +
                  $"tris={UnityStats.triangles / 1000}k verts={UnityStats.vertices / 1000}k batches={UnityStats.batches} setPass={UnityStats.setPassCalls} " +
                  $"shadowCasters={UnityStats.shadowCasters} renderTime={UnityStats.renderTime * 1000f:0.0} ms");
    }

    [MenuItem("Tools/XPARROW/Debug/Analyse Scene Weight")]
    static void Analyse()
    {
        var rs = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        long tris = 0; int casters = 0, subMeshes = 0;
        var rows = new List<(string name, int tris, int subs, bool shadow, float size)>();
        var mats = new HashSet<Material>();
        foreach (var r in rs)
        {
            if (!r.enabled) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var m = mf.sharedMesh;
            int t = 0;
            for (int s = 0; s < m.subMeshCount; s++) t += (int)m.GetIndexCount(s) / 3;
            tris += t; subMeshes += m.subMeshCount;
            if (r.shadowCastingMode != ShadowCastingMode.Off) casters++;
            foreach (var mat in r.sharedMaterials) if (mat != null) mats.Add(mat);
            rows.Add((Path(r.transform), t, m.subMeshCount, r.shadowCastingMode != ShadowCastingMode.Off, r.bounds.size.magnitude));
        }
        var sb = new System.Text.StringBuilder($"WEIGHT {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}: renderers={rows.Count} tris={tris / 1000}k submeshes(draws)={subMeshes} shadowCasters={casters} materials={mats.Count}\n");
        foreach (var r in rows.OrderByDescending(x => x.tris).Take(25))
            sb.AppendLine($"  {r.tris / 1000,6}k tris  {r.subs,3} sub  shadow={(r.shadow ? "Y" : "n")} size={r.size,6:0}  {r.name}");
        int small = rows.Count(x => x.size < 3f), smallCasters = rows.Count(x => x.size < 3f && x.shadow);
        sb.AppendLine($"  small (<3 m) renderers: {small}, of which shadow casters: {smallCasters}");
        Debug.Log(sb.ToString());
    }

    static string Path(Transform t)
    {
        string p = t.name;
        for (int i = 0; i < 2 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
