using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > XPARROW > Debug > Audit Tracks (lines + pits): opens every Final circuit scene and reports the painted
// line objects (edge lines, centre/lane markings, grid, start line) and the pit-lane set-up.
public static class TrackAudit
{
    [MenuItem("Tools/XPARROW/Debug/Audit Tracks (lines + pits)")]
    public static void Run()
    {
        string prev = EditorSceneManager.GetActiveScene().path;
        var sb = new StringBuilder("TRACK AUDIT\n");
        foreach (var t in GameSession.Tracks)
        {
            string path = "Assets/Scenes/Final/" + t.scene + ".unity";
            if (!System.IO.File.Exists(path)) { sb.AppendLine($"  {t.id}: scene missing"); continue; }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int lines = 0, linesOn = 0; long lineTris = 0; var names = new StringBuilder();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string n = r.name;
                bool isLine = n.Contains("Line") || n.Contains("Marking") || n.Contains("Start_Finish") || n.Contains("Grid_Box") || n.Contains("Liner");
                if (!isLine || n.StartsWith("Track_Limit") || n.StartsWith("AI_")) continue;
                lines++;
                bool on = r.enabled && r.gameObject.activeInHierarchy;
                if (on) linesOn++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) lineTris += mf.sharedMesh.triangles.Length / 3;
                names.Append($"{n}{(on ? "" : "(OFF)")}, ");
            }
            var rm = Object.FindFirstObjectByType<RaceManager>();
            string pit = rm == null ? "no RaceManager" :
                $"boxes={(rm.boxDistances != null ? rm.boxDistances.Length : 0)} profile={(rm.pitProfile != null ? rm.pitProfile.Length : 0)} " +
                $"entry {rm.pitEntryStart:0}-{rm.pitEntryEnd:0} exit {rm.pitExitStart:0}-{rm.pitExitEnd:0} offset {rm.pitLaneOffset:0.0} speed {rm.pitSpeed * 3.6f:0} km/h";
            sb.AppendLine($"  {t.id}: line objects {linesOn}/{lines} visible ({lineTris / 1000}k tris): {names}");
            sb.AppendLine($"        pit: {pit}");
        }
        Debug.Log(sb.ToString());
        if (!string.IsNullOrEmpty(prev)) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
    }
}
