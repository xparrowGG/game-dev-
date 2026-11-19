using UnityEngine;

// Measured layout of the 2026 car model (filled in by the car prefab builder): axle positions,
// half tracks and tyre radius, so physics, AI and cameras match the model exactly.
public class CarGeometry : MonoBehaviour
{
    public float frontAxleZ = 1.7f, rearAxleZ = -1.7f;
    public float frontTrack = 0.81f, rearTrack = 0.76f;
    public float tyreRadius = 0.36f;
    public float headZ = -0.2f, headY = 0.95f;     // driver helmet (camera placement)
}
