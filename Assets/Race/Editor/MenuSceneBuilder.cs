using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Tools > Build Main Menu: creates Assets/Scenes/Main Menu.unity — a dark showroom with a turntable,
// spotlights and an F1 car that spins behind the menu — and makes it the first scene in the build.
public static class MenuSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Main Menu.unity";

    [MenuItem("Tools/Build Main Menu")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previous = EditorSceneManager.GetActiveScene().path;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        F1ModelBuilder.Prepare();

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
        RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.16f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = cam.backgroundColor;
        RenderSettings.fogStartDistance = 9f;
        RenderSettings.fogEndDistance = 30f;
        RenderSettings.skybox = null;

        var key = new GameObject("KeyLight").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 0.7f;
        key.transform.rotation = Quaternion.Euler(40f, 25f, 0f);
        key.shadows = LightShadows.Soft;
        Spot("SpotTop", new Vector3(0, 7f, 0), Vector3.zero, 70f, 60f, new Color(1f, 0.97f, 0.92f));
        Spot("SpotFront", new Vector3(-3f, 4f, -6f), new Vector3(0, 0.4f, 0), 45f, 35f, Color.white);
        Spot("RimRed", new Vector3(4f, 2.5f, 5f), new Vector3(0, 0.5f, 0), 25f, 45f, new Color(1f, 0.2f, 0.2f));

        var floorMat = F1ModelBuilder.Mat("Showroom_Floor", new Color(0.05f, 0.055f, 0.07f), 0.3f, 0.85f);
        var discMat = F1ModelBuilder.Mat("Showroom_Turntable", new Color(0.12f, 0.12f, 0.14f), 0.8f, 0.9f);
        var ringMat = F1ModelBuilder.Mat("Showroom_Ring", new Color(0.9f, 0.12f, 0.15f), 0f, 0.5f, new Color(0.9f, 0.1f, 0.12f) * 1.5f);
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

        var car = F1ModelBuilder.Build("ShowroomCar");
        Object.DestroyImmediate(car.GetComponent<Collider>());
        car.transform.position = new Vector3(0, 0.08f, 0);
        car.transform.rotation = Quaternion.Euler(0, 210f, 0);

        var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
        menu.showroomCar = car.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);
        MonacoBuilder.AddSceneToBuild(ScenePath, 0);
        Debug.Log("Main menu scene built: " + ScenePath);

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
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
}
