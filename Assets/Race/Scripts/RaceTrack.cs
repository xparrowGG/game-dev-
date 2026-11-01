using UnityEngine;

// Evenly spaced centreline of a closed circuit (points carry the road height).
// Distance 0 is the start/finish line and increases in the racing direction.
// "Right" is the driver's right-hand side.
public class RaceTrack : MonoBehaviour
{
    public Vector3[] points;
    public float spacing = 2f;
    public float length;
    public float halfWidth = 7f;
    public string trackName = "Circuit";

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

    public Vector3 RightAt(float d)
    {
        Vector3 t = TangentAt(d); t.y = 0;
        return Vector3.Cross(Vector3.up, t).normalized;
    }

    Vector3 SegmentDir(int i)
    {
        i = (i % Count + Count) % Count;
        return (points[(i + 1) % Count] - points[i]).normalized;
    }

    // Distance along the track of the centreline point closest to p (horizontal distance).
    public float ClosestDistance(Vector3 p) => ClosestInRange(p, 0, Count);

    // Faster search around a known previous distance; falls back to a full search if the result is far away.
    public float ClosestDistance(Vector3 p, float hint, int window = 60)
    {
        int c = Mathf.FloorToInt(Mathf.Repeat(hint, length) / spacing);
        float d = ClosestInRange(p, c - window, window * 2);
        Vector3 q = PointAt(d) - p; q.y = 0;
        return q.sqrMagnitude < 30f * 30f ? d : ClosestDistance(p);
    }

    float ClosestInRange(Vector3 p, int start, int count)
    {
        float best = float.MaxValue, bestD = 0;
        for (int k = 0; k < count; k++)
        {
            int i = ((start + k) % Count + Count) % Count;
            Vector3 a = points[i], b = points[(i + 1) % Count];
            Vector3 ab = b - a; ab.y = 0;
            Vector3 ap = p - a; ap.y = 0;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            float dist = (ap - ab * t).sqrMagnitude;
            // Prefer the section at the right height where the circuit passes over itself
            dist += Mathf.Pow(Mathf.Max(0f, Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) - 3f), 2f) * 4f;
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

    // Approximate corner radius around distance d (in plan view).
    public float RadiusAt(float d)
    {
        Vector3 a = TangentAt(d - 6f), b = TangentAt(d + 6f);
        a.y = 0; b.y = 0;
        float angle = Vector3.Angle(a, b) * Mathf.Deg2Rad;
        return angle < 0.001f ? 10000f : 12f / angle;
    }
}
