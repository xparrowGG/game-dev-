using UnityEditor;
using UnityEngine;

// Import settings for the real circuit / car packs under Assets/F1Assets.
public class F1AssetImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/F1Assets/")) return;
        var mi = (ModelImporter)assetImporter;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.bakeAxisConversion = true;
        mi.importBlendShapes = false;
        mi.generateSecondaryUV = false;
        mi.indexFormat = ModelImporterIndexFormat.Auto;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.importNormals = ModelImporterNormals.Import;
        mi.isReadable = assetPath.Contains("/Tracks/") || assetPath.Contains("/Car/");
        mi.meshCompression = ModelImporterMeshCompression.Off;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/F1Assets/") && !assetPath.StartsWith("Assets/Resources/F1UI/")) return;
        var ti = (TextureImporter)assetImporter;
        string n = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        if (assetPath.StartsWith("Assets/Resources/F1UI/"))
        {
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 1024;
            ti.sRGBTexture = true;
            return;
        }
        if (n.EndsWith("_Normal")) ti.textureType = TextureImporterType.NormalMap;
        if (n.EndsWith("_Metallic") || n.EndsWith("_Roughness") || n.EndsWith("_AO")) ti.sRGBTexture = false;
        if (n.EndsWith("_Metallic") || n.EndsWith("_Roughness")) ti.isReadable = true;
        ti.maxTextureSize = 2048;
    }
}
