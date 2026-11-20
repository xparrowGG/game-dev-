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

    // Swap Pirelli sidewall compound material ("Resources/F1Tyres/Tyre_{Soft,Medium,Hard}") or fallback tint.
    public static void Paint(GameObject car, Compound c)
    {
        Material compoundMat = Resources.Load<Material>("F1Tyres/Tyre_" + Names[(int)c]);
        MaterialPropertyBlock block = compoundMat == null ? new MaterialPropertyBlock() : null;
        foreach (var r in car.GetComponentsInChildren<Renderer>(true))
        {
            bool isTyre = r.name.StartsWith("F1_Wheel") ||
                          (r.sharedMaterial != null && (r.sharedMaterial.name.StartsWith("Mat_Tyre") || r.sharedMaterial.name.StartsWith("Tyre_")));
            if (isTyre && compoundMat != null)
            {
                r.sharedMaterial = compoundMat;
                continue;
            }
            if (block != null && r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("F1_Stripe"))
            {
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", Colors[(int)c]);
                r.SetPropertyBlock(block);
            }
        }
    }

    // Hide or show the rubber tyres while the crew changes them (leaving brake hubs visible).
    public static void SetTyresVisible(GameObject car, bool visible)
    {
        var wheels = car.transform.Find("Wheels");
        if (wheels == null) return;
        foreach (var r in wheels.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.StartsWith("F1_Hub")) continue;
            r.enabled = visible;
        }
    }
}
