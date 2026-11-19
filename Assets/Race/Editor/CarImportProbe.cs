using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CarImportProbe
{
    [MenuItem("Tools/XPARROW/Debug/Probe Car FBX")]
    static void Probe()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/F1Assets/Car/F1_2026_Car.fbx");
        var go = (GameObject)Object.Instantiate(fbx);
        var sb = new System.Text.StringBuilder("PROBE root rot " + go.transform.rotation.eulerAngles + "\n");
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.Contains("LOD1") || t.name.Contains("LOD2")) continue;
            var r = t.GetComponent<Renderer>();
            sb.Append(t.name + " pos " + t.position.ToString("F2") + (r != null ? " bounds c " + r.bounds.center.ToString("F2") : "") + "\n");
        }
        Debug.Log(sb.ToString());
        Object.DestroyImmediate(go);
    }

    // Lists the materials of a circuit FBX: name, colour, textures (to judge what the importer picked up)
    [MenuItem("Tools/XPARROW/Debug/Probe Monza Materials")]
    static void ProbeMats()
    {
        var all = AssetDatabase.LoadAllAssetsAtPath("Assets/F1Assets/Tracks/Monza/Monza_GP.fbx");
        var sb = new System.Text.StringBuilder("MATS ");
        var seen = new HashSet<string>();
        foreach (var o in all)
        {
            if (!(o is Material m) || !seen.Add(m.name)) continue;
            string tex = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "-";
            string col = m.HasProperty("_BaseColor") ? ColorUtility.ToHtmlStringRGB(m.GetColor("_BaseColor")) : "?";
            sb.Append($"{m.name}[{m.shader.name.Replace("Universal Render Pipeline/", "")} #{col} {tex}]; ");
        }
        Debug.Log(sb.ToString());
    }
}
