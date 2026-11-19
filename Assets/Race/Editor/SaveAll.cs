using UnityEditor;
using UnityEditor.SceneManagement;

// Tools > XPARROW > Save All: saves open scenes, all assets and project settings (incl. the build scene list).
public static class SaveAll
{
    [MenuItem("Tools/XPARROW/Save All %#&s")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("File/Save Project");
        UnityEngine.Debug.Log("XPARROW: everything saved.");
    }
}
