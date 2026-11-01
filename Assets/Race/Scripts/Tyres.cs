using UnityEngine;

public enum Compound { Soft, Medium, Hard }

// Slick tyre compounds: grip, life and wear behaviour, plus sidewall colours.
public static class Tyres
{
    public static readonly string[] Names = { "Soft", "Medium", "Hard" };
    public static readonly string[] Letters = { "S", "M", "H" };
    public static readonly Color[] Colors = { new Color(0.95f, 0.15f, 0.15f), new Color(1f, 0.82f, 0.1f), new Color(0.95f, 0.95f, 0.95f) };
    public static readonly float[] Grip = { 1.05f, 1.0f, 0.955f };
    public static readonly float[] LifeMeters = { 16000f, 26000f, 40000f };   // distance to 100% wear at normal pace

    // Grip left at a wear level (0 new .. 1 worn out): a gentle fade, then the "cliff" past ~70%.
    public static float WearGrip(float wear)
    {
        wear = Mathf.Clamp(wear, 0f, 1.3f);
        return 1f - 0.06f * wear - 0.22f * Mathf.Pow(Mathf.Clamp01(wear), 3f) - Mathf.Max(0f, wear - 1f) * 0.4f;
    }

    public static float GripFactor(Compound c, float wear) => Grip[(int)c] * WearGrip(wear);

    // Recolour the sidewall stripes (material "F1_Stripe") without creating material instances.
    public static void Paint(GameObject car, Compound c)
    {
        var block = new MaterialPropertyBlock();
        foreach (var r in car.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null || !r.sharedMaterial.name.StartsWith("F1_Stripe")) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", Colors[(int)c]);
            r.SetPropertyBlock(block);
        }
    }

    // Hide or show the tyres while the crew changes them.
    public static void SetTyresVisible(GameObject car, bool visible)
    {
        var wheels = car.transform.Find("Wheels");
        if (wheels == null) return;
        foreach (var r in wheels.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }
}
