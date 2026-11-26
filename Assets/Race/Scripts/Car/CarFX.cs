using UnityEngine;

// Runtime car effects, built in code (no texture/material assets):
//  - tyre smoke from the wheels when sliding, locking or spinning
//  - dust / stones when running on gravel or grass
//  - carbon debris burst on hard impacts (called by F1Car)
[RequireComponent(typeof(F1Car))]
public class CarFX : MonoBehaviour
{
    F1Car car;
    ParticleSystem smoke, dust, debris;
    static Material smokeMat, debrisMat;
    static Texture2D puff;

    void Start()
    {
        car = GetComponent<F1Car>();
        EnsureMaterials();
        smoke = MakeSystem("FX_TyreSmoke", smokeMat, new Color(0.85f, 0.85f, 0.87f, 0.55f), 1.6f, 1.8f, 0.6f, -0.05f);
        dust = MakeSystem("FX_Dust", smokeMat, new Color(0.62f, 0.55f, 0.42f, 0.6f), 1.2f, 2.4f, 1.2f, 0.25f);
        debris = MakeSystem("FX_Debris", debrisMat, new Color(0.08f, 0.08f, 0.09f, 1f), 1.6f, 0.09f, 6f, 1.6f);
        var dm = debris.main; dm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.16f); dm.startRotation3D = true;
        var dShape = debris.shape; dShape.shapeType = ParticleSystemShapeType.Sphere; dShape.radius = 0.4f;
    }

    static void EnsureMaterials()
    {
        if (smokeMat != null) return;
        // Soft round puff texture, generated once
        puff = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FX_Puff" };
        var px = new Color32[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                byte a = (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255f);
                px[y * 64 + x] = new Color32(255, 255, 255, a);
            }
        puff.SetPixels32(px); puff.Apply();
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        smokeMat = new Material(sh) { name = "FX_Smoke" };
        smokeMat.SetTexture("_BaseMap", puff);
        smokeMat.SetFloat("_Surface", 1f); smokeMat.SetFloat("_Blend", 0f);
        smokeMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        smokeMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        smokeMat.SetFloat("_ZWrite", 0f);
        smokeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        smokeMat.renderQueue = 3000;
        debrisMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit") ?? sh) { name = "FX_Debris" };
    }

    ParticleSystem MakeSystem(string n, Material m, Color c, float life, float size, float speed, float gravity)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
        main.startColor = c;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.6f, 0.1f, 0.3f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    void Update()
    {
        if (car == null || smoke == null) return;
        float v = Mathf.Abs(car.Speed);
        // Smoke at the rear wheels (or fronts when locking)
        bool lockup = car.WheelLocking;
        smoke.transform.localPosition = new Vector3(0f, 0.15f, lockup ? 1.7f : -1.7f);
        float slip = Mathf.Max(Mathf.Clamp01((car.TyreSlip - 0.45f) * 1.6f), lockup || car.WheelSpinning ? 0.8f : 0f);
        var se = smoke.emission; se.rateOverTime = v > 3f ? slip * 70f : 0f;
        // Dust / stones off-track
        dust.transform.localPosition = new Vector3(0f, 0.1f, -1.9f);
        bool off = car.OnGravel || car.OnGrass;
        var de = dust.emission; de.rateOverTime = off && v > 3f ? Mathf.Clamp01(v / 40f) * (car.OnGravel ? 90f : 35f) : 0f;
    }

    public void Debris(Vector3 point, int count)
    {
        if (debris == null) return;
        debris.transform.position = point;
        debris.Emit(count);
    }
}
