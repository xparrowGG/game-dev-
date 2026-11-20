using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Tools > Build Main Menu: creates Assets/Scenes/Final/Final Main Menu.unity — a dark showroom with a turntable,
// spotlights and an F1 car that spins behind the menu — and makes it the first scene in the build.
public static class MenuSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Final/Final Main Menu.unity";
    const string GenDir = "Assets/F1Assets/Generated";

    [MenuItem("Tools/Build Main Menu")]
    public static void Build() => Build(true);

    public static void Build(bool interactive)
    {
        if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previous = EditorSceneManager.GetActiveScene().path;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!AssetDatabase.IsValidFolder("Assets/Scenes/Final"))
            AssetDatabase.CreateFolder("Assets/Scenes", "Final");
        if (!AssetDatabase.IsValidFolder(GenDir))
            AssetDatabase.CreateFolder("Assets/F1Assets", "Generated");

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.04f);
        cam.fieldOfView = 38f;
        // Frames the car in the right part of the screen, beside the menu panel
        camGo.transform.position = new Vector3(-1.0f, 1.9f, -9.5f);
        camGo.transform.LookAt(new Vector3(-2.2f, 0.5f, 0f));

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.35f, 0.40f);
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = CircuitBuilder.GetOrBuildSkyReflectionCube(new Vector3(0.4f, 0.8f, -0.4f).normalized);
        RenderSettings.reflectionIntensity = 1.35f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = cam.backgroundColor;
        RenderSettings.fogStartDistance = 9f;
        RenderSettings.fogEndDistance = 30f;
        RenderSettings.skybox = null;

        var key = new GameObject("KeyLight").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.6f;
        key.transform.rotation = Quaternion.Euler(40f, 25f, 0f);
        key.shadows = LightShadows.Soft;
        Spot("SpotTop", new Vector3(0, 7f, 0), Vector3.zero, 70f, 110f, new Color(1f, 0.97f, 0.92f));
        Spot("SpotFront", new Vector3(-3f, 4f, -6f), new Vector3(0, 0.4f, 0), 50f, 75f, Color.white);
        Spot("SpotFill", new Vector3(5.5f, 3.2f, -3.5f), new Vector3(0, 0.45f, 0), 55f, 50f, new Color(0.85f, 0.92f, 1f));
        Spot("RimRed", new Vector3(4f, 2.5f, 5f), new Vector3(0, 0.5f, 0), 30f, 45f, new Color(1f, 0.25f, 0.25f));

        var floorMat = Mat("Showroom_Floor", new Color(0.05f, 0.055f, 0.07f), 0.3f, 0.85f);
        var discMat = Mat("Showroom_Turntable", new Color(0.12f, 0.12f, 0.14f), 0.8f, 0.9f);
        var ringMat = Mat("Showroom_Ring", new Color(0.9f, 0.12f, 0.15f), 0f, 0.5f, new Color(0.9f, 0.1f, 0.12f) * 1.5f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(6, 1, 6);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "TurntableRing";
        ring.transform.position = new Vector3(0, 0.02f, 0);
        ring.transform.localScale = new Vector3(7.4f, 0.02f, 7.4f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Turntable";
        disc.transform.position = new Vector3(0, 0.04f, 0);
        disc.transform.localScale = new Vector3(7f, 0.04f, 7f);
        disc.GetComponent<MeshRenderer>().sharedMaterial = discMat;

        var car = CircuitBuilder.InstantiateCar("ShowroomCar");
        Object.DestroyImmediate(car.GetComponent<Collider>());
        car.transform.position = new Vector3(0, 0.08f, 0);
        var lod = car.GetComponent<LODGroup>();
        if (lod != null) lod.ForceLOD(0);
        car.transform.rotation = Quaternion.Euler(0, 210f, 0);

        var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
        menu.showroomCar = car.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);
        CircuitBuilder.AddSceneToBuild(ScenePath, 0);
        Debug.Log("Main menu scene built: " + ScenePath);

        if (interactive && !string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
    }

    static void Spot(string name, Vector3 pos, Vector3 target, float angle, float intensity, Color c)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.type = LightType.Spot;
        l.transform.position = pos;
        l.transform.LookAt(target);
        l.spotAngle = angle;
        l.intensity = intensity;
        l.range = 20f;
        l.color = c;
        l.shadows = LightShadows.Soft;
    }

    static Material Mat(string name, Color c, float metal, float smooth, Color? emission = null)
    {
        string path = GenDir + "/" + name + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission.Value);
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }
}
