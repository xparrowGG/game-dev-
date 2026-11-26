using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > XPARROW > Debug > Audit Materials: for every Final scene, lists materials that look broken after import
// (error shader, missing texture references, plain white untextured surfaces on big meshes).
public static class MaterialAudit
{
    [MenuItem("Tools/XPARROW/Debug/Audit Materials (all scenes)")]
    public static void Run()
    {
        string prev = EditorSceneManager.GetActiveScene().path;
        var sb = new StringBuilder("MATERIAL AUDIT\n");
        var scenes = new List<string> { "Assets/Scenes/Final/Final Main Menu.unity" };
        foreach (var t in GameSession.Tracks) scenes.Add("Assets/Scenes/Final/" + t.scene + ".unity");
        foreach (var path in scenes)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var mats = new Dictionary<Material, float>();   // material -> largest renderer size using it
            int nullSlots = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { nullSlots++; continue; }
                    float s = r.bounds.size.magnitude;
                    if (!mats.TryGetValue(m, out float cur) || s > cur) mats[m] = s;
                }
            int error = 0, missingTex = 0, plainWhite = 0, textured = 0;
            var issues = new StringBuilder();
            foreach (var kv in mats)
            {
                var m = kv.Key;
                if (m.shader == null || m.shader.name.Contains("Error") || m.shader.name.Contains("Hidden/InternalErrorShader")) { error++; issues.Append($"[ERRORSHADER {m.name}] "); continue; }
                bool hasTex = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null;
                if (hasTex) textured++;
                // A texture slot that points at a texture that no longer exists
                foreach (var prop in m.GetTexturePropertyNames())
                {
                    var tex = m.GetTexture(prop);
                    if (tex == null && m.GetTexturePropertyNameIDs() != null && m.HasProperty(prop))
                    {
                        // empty slots are normal; detect dangling references via the serialized data
                    }
                }
                var so = new SerializedObject(m);
                var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
                for (int i = 0; envs != null && i < envs.arraySize; i++)
                {
                    var texProp = envs.GetArrayElementAtIndex(i).FindPropertyRelative("second.m_Texture");
                    if (texProp != null && texProp.objectReferenceValue == null && texProp.objectReferenceInstanceIDValue != 0) { missingTex++; issues.Append($"[MISSING TEX {m.name}] "); break; }
                }
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.gray;
                if (!hasTex && c.r > 0.85f && c.g > 0.85f && c.b > 0.85f && kv.Value > 20f && !m.name.Contains("Line") && !m.name.Contains("White") && !m.name.Contains("Kerb"))
                { plainWhite++; issues.Append($"[PLAIN WHITE {m.name} ({kv.Value:0} m)] "); }
            }
            sb.AppendLine($"  {System.IO.Path.GetFileNameWithoutExtension(path)}: {mats.Count} materials, {textured} textured, error={error}, missingTex={missingTex}, plainWhiteBig={plainWhite}, emptySlots={nullSlots} {issues}");
        }
        Debug.Log(sb.ToString());
        if (!string.IsNullOrEmpty(prev)) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
    }
}
