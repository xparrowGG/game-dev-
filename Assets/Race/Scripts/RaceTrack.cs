using UnityEngine;

// Evenly spaced centreline of a closed circuit. Distance 0 is the start/finish line,
// distance increases in the racing direction. "Right" is the driver's right-hand side.
public class RaceTrack : MonoBehaviour
{
    public Vector3[] points;
    public float spacing = 2f;
    public float length;
    public float halfWidth = 7f;

    public int Count => points.Length;

    public Vector3 PointAt(float d)
    {
        d = Mathf.Repeat(d, length);
        float f = d / spacing;
        int i = Mathf.FloorToInt(f) % Count;
        return Vector3.Lerp(points[i], points[(i + 1) % Count], f - Mathf.Floor(f));
    }

    public Vector3 TangentAt(float d)
    {
        d = Mathf.Repeat(d, length);
        float f = d / spacing;
        int i = Mathf.FloorToInt(f) % Count;
        Vector3 a = SegmentDir(i - 1), b = SegmentDir(i), c = SegmentDir(i + 1);
        float t = f - Mathf.Floor(f);
        return (t < 0.5f ? Vector3.Lerp((a + b).normalized, b, t * 2f) : Vector3.Lerp(b, (b + c).normalized, t * 2f - 1f)).normalized;
    }

    public Vector3 RightAt(float d) => Vector3.Cross(Vector3.up, TangentAt(d)).normalized;

    Vector3 SegmentDir(int i)
    {
        i = (i % Count + Count) % Count;
        return (points[(i + 1) % Count] - points[i]).normalized;
    }

    // Distance along the track of the centreline point closest to p.
    public float ClosestDistance(Vector3 p)
    {
        float best = float.MaxValue, bestD = 0;
        for (int i = 0; i < Count; i++)
        {
            Vector3 a = points[i], b = points[(i + 1) % Count];
            Vector3 ab = b - a; ab.y = 0;
            Vector3 ap = p - a; ap.y = 0;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
            float dist = (ap - ab * t).sqrMagnitude;
            if (dist < best) { best = dist; bestD = (i + t) * spacing; }
        }
        return bestD;
    }

    // Signed sideways offset of p from the centreline (positive = right).
    public float LateralOffset(Vector3 p, float d)
    {
        Vector3 delta = p - PointAt(d); delta.y = 0;
        return Vector3.Dot(delta, RightAt(d));
    }

    // Approximate corner radius around distance d.
    public float RadiusAt(float d)
    {
        float angle = Vector3.Angle(TangentAt(d - 6f), TangentAt(d + 6f)) * Mathf.Deg2Rad;
        return angle < 0.001f ? 10000f : 12f / angle;
    }
}
