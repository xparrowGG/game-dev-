using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

// Tools > XPARROW > Save All: saves open scenes, all assets and project settings (incl. the build scene list),
// then warns about any project file that is close to GitHub's 100 MiB per-file push limit.
public static class SaveAll
{
    const long WarnBytes = 90L * 1024 * 1024;     // GitHub rejects files over 100 MiB

    [MenuItem("Tools/XPARROW/Save All %#&s")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("File/Save Project");
        UnityEngine.Debug.Log("XPARROW: everything saved.");
        CheckGitHubSizes();
    }

    [MenuItem("Tools/XPARROW/Check File Sizes for GitHub")]
    public static void CheckGitHubSizes()
    {
        int n = 0;
        foreach (var f in new DirectoryInfo("Assets").GetFiles("*", SearchOption.AllDirectories))
        {
            if (f.Length < WarnBytes) continue;
            n++;
            string msg = $"GitHub size check: {f.FullName.Replace('\\', '/')} is {f.Length / (1024f * 1024f):0.0} MiB";
            if (f.Length >= 100L * 1024 * 1024) UnityEngine.Debug.LogError(msg + " - OVER the 100 MiB limit, the push will be rejected.");
            else UnityEngine.Debug.LogWarning(msg + " - close to the 100 MiB limit; keep re-exports smaller.");
        }
        if (n == 0) UnityEngine.Debug.Log("GitHub size check: all files under 90 MiB.");
    }
}
