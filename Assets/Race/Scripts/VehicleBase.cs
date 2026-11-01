using UnityEngine;

// Common surface for player cars (arcade CarController, simulation F1Car) used by the
// race manager, HUD and camera.
public abstract class VehicleBase : MonoBehaviour
{
    public bool inputEnabled = true;             // false holds the car (e.g. on the start grid)
    public float speedMultiplier = 1f;
    public System.Action onRespawnRequested;     // overrides the R key respawn when set

    public float Speed { get; protected set; }              // signed forward speed, m/s
    public float SpeedKmh => Mathf.Abs(Speed) * 3.6f;
    public float DistanceMeters { get; protected set; }
    public float TopSpeedKmh { get; protected set; }
    public float TripTime { get; protected set; }
    public bool Boosting { get; protected set; }
    public float LastImpactStrength { get; protected set; }
    public float LastImpactTime { get; protected set; } = -1f;

    public virtual int Gear => 0;              // -1 reverse, 0 neutral / no gearbox, 1.. forward
    public virtual bool HasGearbox => false;
    public virtual float Rpm => 0f;
    public virtual float MaxRpm => 0f;
    public virtual float ErsCharge => -1f;     // 0..1, negative when the car has no ERS

    public abstract void Teleport(Vector3 pos, Quaternion rot);
}
