using UnityEditor;
using UnityEngine;

// Import settings for the real circuit / car packs under Assets/F1Assets.
public class F1AssetImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (assetPath.StartsWith("Assets/Resources/F1Crew/"))
        {
            var cmi = (ModelImporter)assetImporter;
            cmi.importCameras = false;
            cmi.importLights = false;
            cmi.importAnimation = false;
            cmi.animationType = ModelImporterAnimationType.Generic;
            cmi.globalScale = 1f;
            cmi.useFileScale = true;
            cmi.bakeAxisConversion = true;
            cmi.importBlendShapes = false;
            cmi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            cmi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            cmi.importNormals = ModelImporterNormals.Import;
            cmi.isReadable = false;
            return;
        }
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
        if (assetPath.StartsWith("Assets/Resources/F1Crew/"))
        {
            var cti = (TextureImporter)assetImporter;
            string fn = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (fn.EndsWith("_Normal")) cti.textureType = TextureImporterType.NormalMap;
            cti.sRGBTexture = !fn.EndsWith("_Normal");
            cti.maxTextureSize = 1024;
            cti.streamingMipmaps = true;           // only the mip levels the camera needs are kept in VRAM
            return;
        }
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
        ti.maxTextureSize = assetPath.Contains("/Car/Textures/teams/") ? 4096 : 2048;
        if (assetPath.Contains("/Car/Textures/teams/") && n.EndsWith("_BaseColor"))
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        if (ti.mipmapEnabled) ti.streamingMipmaps = true;   // VRAM: load only the detail the camera needs (no visual change up close)
    }

    // Game audio: compressed in the build (Vorbis), small one-shots decompressed on load for instant playback
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Resources/F1Audio/")) return;
        var ai = (AudioImporter)assetImporter;
        var s = ai.defaultSampleSettings;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.6f;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.preloadAudioData = true;
        ai.defaultSampleSettings = s;
        ai.forceToMono = true;
    }

    // Circuit FBX: every node carries the axis-conversion rotation, so objects look tilted in the editor even though
    // the circuit is upright. At import, bake each node's world rotation/scale into its mesh and give every node an
    // identity rotation and scale at the same world position. The world result is exactly unchanged.
    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith("Assets/F1Assets/Tracks/")) return;
        // Blender procedural colours (see OnPostprocessMaterial) on every imported material of this circuit
        var done = new System.Collections.Generic.HashSet<Material>();
        int coloured = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in r.sharedMaterials)
                if (mat != null && done.Add(mat) && ApplyBlenderColour(mat)) coloured++;
        if (coloured > 0) Debug.Log($"F1AssetImport: {coloured} Blender colours applied in {System.IO.Path.GetFileName(assetPath)}");
        Transform reference = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == "Col_Track_Asphalt" || t.name == "Road_Surface") { reference = t; break; }
        if (reference == null)
            foreach (var mf0 in root.GetComponentsInChildren<MeshFilter>(true)) { reference = mf0.transform; break; }
        if (reference == null) return;
        if (Quaternion.Angle(reference.rotation, Quaternion.identity) < 0.01f) return;   // already clean

        var all = root.GetComponentsInChildren<Transform>(true);
        var newPos = new Vector3[all.Length];
        var residual = new Matrix4x4[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            var w = all[i].localToWorldMatrix;
            newPos[i] = all[i].position;                              // same world position
            residual[i] = w;                                          // world rotation/scale goes into the mesh
            residual[i].SetColumn(3, new Vector4(0, 0, 0, 1));        // linear part only
        }
        // Bake residual linear parts (normally identity) into meshes; shared meshes only once
        var baked = new System.Collections.Generic.Dictionary<Mesh, Matrix4x4>();
        var keepRot = new System.Collections.Generic.Dictionary<int, Quaternion>();   // shared-mesh nodes: leftover rotation
        int bakedCount = 0, sharedConflicts = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var mf = all[i].GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var m = residual[i];
            if (IsIdentity(m)) continue;
            var mesh = mf.sharedMesh;
            if (baked.TryGetValue(mesh, out var prev))
            {
                if (!Same(prev, m)) { sharedConflicts++; keepRot[i] = (m * prev.inverse).rotation; }
                continue;
            }
            baked[mesh] = m;
            var v = mesh.vertices; var n = mesh.normals; var tg = mesh.tangents;
            var nm = m.inverse.transpose;
            for (int k = 0; k < v.Length; k++) v[k] = m.MultiplyPoint3x4(v[k]);
            for (int k = 0; k < n.Length; k++) n[k] = nm.MultiplyVector(n[k]).normalized;
            for (int k = 0; k < tg.Length; k++) { var d = m.MultiplyVector(new Vector3(tg[k].x, tg[k].y, tg[k].z)).normalized; tg[k] = new Vector4(d.x, d.y, d.z, tg[k].w); }
            mesh.vertices = v; if (n.Length > 0) mesh.normals = n; if (tg.Length > 0) mesh.tangents = tg;
            mesh.RecalculateBounds();
            bakedCount++;
        }
        // Upright transforms: identity rotation and scale, same world positions
        for (int i = 0; i < all.Length; i++) { all[i].localRotation = Quaternion.identity; all[i].localScale = Vector3.one; }
        for (int i = 0; i < all.Length; i++) all[i].position = newPos[i];
        foreach (var kv in keepRot) all[kv.Key].rotation = kv.Value;
        Debug.Log($"F1AssetImport: {System.IO.Path.GetFileName(assetPath)} transforms cleaned ({all.Length} nodes, {bakedCount} meshes re-baked, {sharedConflicts} shared-mesh conflicts)");
    }

    static bool IsIdentity(Matrix4x4 m)
    {
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) if (Mathf.Abs(m[r, c] - (r == c ? 1f : 0f)) > 1e-4f) return false;
        return true;
    }

    static bool Same(Matrix4x4 a, Matrix4x4 b)
    {
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) if (Mathf.Abs(a[r, c] - b[r, c]) > 1e-4f) return false;
        return true;
    }

    // Blender procedural shaders (noise / mix nodes) can't travel through FBX and import as plain white.
    // <Track>/<id>.blender_colors.json holds the Blender base colours (linear) by material name; apply them at import.
    void OnPostprocessMaterial(Material material) => ApplyBlenderColour(material);

    bool ApplyBlenderColour(Material material)
    {
        if (!assetPath.StartsWith("Assets/F1Assets/Tracks/")) return false;
        string dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
        string id = System.IO.Path.GetFileName(dir);
        string json = dir + "/" + id + ".blender_colors.json";
        if (!System.IO.File.Exists(json)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(json),
            "\"" + System.Text.RegularExpressions.Regex.Escape(material.name) + "\"\\s*:\\s*\\[\\s*([-\\d\\.eE]+)\\s*,\\s*([-\\d\\.eE]+)\\s*,\\s*([-\\d\\.eE]+)");
        if (!m.Success) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var lin = new Color(float.Parse(m.Groups[1].Value, inv), float.Parse(m.Groups[2].Value, inv), float.Parse(m.Groups[3].Value, inv), 1f);
        var c = lin.gamma;                      // Blender stores linear; material colours are authored in sRGB
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
        if (material.HasProperty("_Color")) material.SetColor("_Color", c);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
        return true;
    }
}