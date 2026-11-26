using UnityEditor;

// Menu shortcuts for the verification routines inside CircuitBuilder (normally only run by the full build).
public static class VerifyMenus
{
    [MenuItem("Tools/XPARROW/Debug/Reimport Circuit FBX")]
    static void ReimportCircuits()
    {
        foreach (var id in new[] { "Monaco", "Baku", "Monza", "Spa", "Suzuka" })
            AssetDatabase.ImportAsset($"Assets/F1Assets/Tracks/{id}/{id}_GP.fbx", ImportAssetOptions.ForceUpdate);
    }

    // Re-apply import settings (texture streaming, audio compression) without touching the big circuit models
    [MenuItem("Tools/XPARROW/Debug/Reimport Textures + Audio")]
    static void ReimportTexAudio()
    {
        int n = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D t:AudioClip", new[] { "Assets/F1Assets", "Assets/Resources" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".fbx")) continue;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); n++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        UnityEngine.Debug.Log("Reimported " + n + " textures/audio clips");
    }

    [MenuItem("Tools/XPARROW/Debug/Verify Pit Lanes (5 circuits)")]
    static void Pits() => CircuitBuilder.PitLaneVerifyTest();

    [MenuItem("Tools/XPARROW/Debug/Verify Barrier Containment (5 circuits)")]
    static void Barriers() => CircuitBuilder.VerifyBarrierContainment();
}
