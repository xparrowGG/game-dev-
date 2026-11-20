using System.Collections.Generic;
using UnityEngine;

// A team's pit box with a full stop crew (built at race start from primitives in team colours):
// front and rear jack men, a wheel gunner, a tyre-off and a tyre-on man at each corner, two side
// stabilisers and the release light. Sequence of a stop (scaled to the service time):
//   car arrives -> rear jack in, both jacks lift -> guns loosen -> old tyres pulled off and carried out ->
//   new tyres pushed on -> guns tighten -> jacks drop -> light goes green, car released -> crew back to the garage.
public class PitBox : MonoBehaviour
{
    public int team;
    public float trackDistance;          // where the car stops, along the track
    public Renderer releaseLight;
    public Material lightRed, lightGreen, lightOff;

    enum Role { FrontJack, RearJack, Gunner, TyreOff, TyreOn, Stabiliser }
    class Member
    {
        public Transform t, prop;
        public Role role;
        public int corner;               // 0 FL, 1 FR, 2 RL, 3 RR
        public Vector3 home, ready, work, stack;
    }
    readonly List<Member> crew = new List<Member>();
    readonly Transform[] oldTyre = new Transform[4], newTyre = new Transform[4];

    enum Phase { Garage, Ready, Service, Released }
    Phase phase = Phase.Garage;
    float timer, duration = 2.6f, readyBlend, releaseTimer;
    static readonly Vector3[] Wheels = { new Vector3(-0.81f, 0.36f, 1.7f), new Vector3(0.81f, 0.36f, 1.7f), new Vector3(-0.76f, 0.36f, -1.7f), new Vector3(0.76f, 0.36f, -1.7f) };

    // ---------- Build ----------

    public static PitBox Create(Transform parent, int team, Vector3 pos, Quaternion rot, float garageSide, Color primary, Color secondary)
    {
        var go = new GameObject("PitBox_" + team);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var box = go.AddComponent<PitBox>();
        box.team = team;
        box.Build(garageSide, primary, secondary);
        return box;
    }

    static Material Mat(Color c, float smooth = 0.35f, Color? emission = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); }
        return m;
    }

    static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (rot.HasValue) g.transform.localRotation = rot.Value;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    void Build(float side, Color primary, Color secondary)
    {
        var suit = Mat(Color.Lerp(primary, Color.black, 0.15f));
        var trim = Mat(secondary);
        var helmet = Mat(Color.Lerp(secondary, Color.white, 0.3f), 0.8f);
        var rubber = Mat(new Color(0.05f, 0.05f, 0.055f), 0.25f);
        var steel = Mat(new Color(0.55f, 0.57f, 0.6f), 0.6f);
        float gx = side;                 // garage side: +1 right of the car, -1 left

        Member Add(Role role, int corner, Vector3 work, Vector3 ready, Vector3 home)
        {
            var root = new GameObject(role + (corner >= 0 ? "_" + corner : "")).transform;
            root.SetParent(transform, false);
            Part(PrimitiveType.Capsule, root, new Vector3(0, 0.62f, 0), new Vector3(0.42f, 0.5f, 0.32f), suit);         // legs + torso
            Part(PrimitiveType.Cube, root, new Vector3(0, 0.95f, 0), new Vector3(0.44f, 0.12f, 0.33f), trim);           // chest band
            Part(PrimitiveType.Sphere, root, new Vector3(0, 1.38f, 0), new Vector3(0.27f, 0.29f, 0.29f), helmet);       // helmet
            var m = new Member { t = root, role = role, corner = corner, work = work, ready = ready, home = home };
            root.localPosition = home;
            crew.Add(m);
            return m;
        }

        // Jacks
        var fj = Add(Role.FrontJack, -1, new Vector3(0, 0, 3.55f), new Vector3(0, 0, 4.6f), new Vector3(gx * 6.5f, 0, 3.4f));
        fj.prop = Part(PrimitiveType.Cube, fj.t, new Vector3(0, 0.25f, -0.55f), new Vector3(0.5f, 0.12f, 0.9f), steel);
        var rj = Add(Role.RearJack, -1, new Vector3(0, 0, -3.35f), new Vector3(gx * 2.3f, 0, -3.6f), new Vector3(gx * 6.5f, 0, -3.4f));
        rj.prop = Part(PrimitiveType.Cube, rj.t, new Vector3(0, 0.25f, 0.55f), new Vector3(0.5f, 0.12f, 0.9f), steel);

        for (int c = 0; c < 4; c++)
        {
            Vector3 w = Wheels[c];
            float sx = Mathf.Sign(w.x), front = c < 2 ? 1f : -1f;
            float homeX = gx * (5.5f + (c % 2) * 0.8f);
            var gun = Add(Role.Gunner, c, new Vector3(w.x + sx * 0.62f, 0, w.z), new Vector3(w.x + sx * 1.3f, 0, w.z), new Vector3(homeX, 0, w.z + 0.6f));
            gun.prop = Part(PrimitiveType.Cube, gun.t, new Vector3(-sx * 0.32f, 0.45f, 0), new Vector3(0.36f, 0.12f, 0.12f), steel);
            var off = Add(Role.TyreOff, c, new Vector3(w.x + sx * 0.75f, 0, w.z + front * 0.55f), new Vector3(w.x + sx * 1.5f, 0, w.z + front * 0.7f), new Vector3(homeX, 0, w.z + front * 1.4f));
            off.stack = new Vector3(w.x + sx * 2.4f, 0, w.z + front * 0.9f);
            var on = Add(Role.TyreOn, c, new Vector3(w.x + sx * 0.75f, 0, w.z - front * 0.55f), new Vector3(w.x + sx * 2.2f, 0, w.z - front * 0.2f), new Vector3(homeX, 0, w.z - front * 0.8f));

            // Tyres: the old set (shown while it comes off) and the new set (carried in by the tyre-on man)
            oldTyre[c] = Tyre(rubber, c < 2);
            newTyre[c] = Tyre(rubber, c < 2);
            newTyre[c].gameObject.SetActive(false);
            oldTyre[c].gameObject.SetActive(false);
        }
        Add(Role.Stabiliser, 0, new Vector3(-1.25f, 0, 0.1f), new Vector3(-1.9f, 0, 0.2f), new Vector3(gx * 7.2f, 0, 0.5f));
        Add(Role.Stabiliser, 1, new Vector3(1.25f, 0, 0.1f), new Vector3(1.9f, 0, 0.2f), new Vector3(gx * 7.2f, 0, -0.5f));

        // Release light on a post in front of the box, garage side
        Part(PrimitiveType.Cylinder, transform, new Vector3(gx * 2.6f, 1.2f, 4.2f), new Vector3(0.08f, 1.2f, 0.08f), steel);
        lightOff = Mat(new Color(0.1f, 0.1f, 0.1f));
        lightRed = Mat(new Color(1f, 0.1f, 0.08f), 0.6f, new Color(1f, 0.05f, 0.03f) * 3f);
        lightGreen = Mat(new Color(0.1f, 1f, 0.3f), 0.6f, new Color(0.05f, 1f, 0.2f) * 3f);
        releaseLight = Part(PrimitiveType.Cube, transform, new Vector3(gx * 2.6f, 2.5f, 4.2f), new Vector3(0.5f, 0.35f, 0.2f), lightOff).GetComponent<Renderer>();
        // Box markings on the pit lane floor
        var paint = Mat(new Color(0.95f, 0.95f, 0.95f), 0.2f);
        Part(PrimitiveType.Cube, transform, new Vector3(0, 0.012f, 3.0f), new Vector3(2.2f, 0.01f, 0.12f), paint);
        Part(PrimitiveType.Cube, transform, new Vector3(-1.9f, 0.012f, 0), new Vector3(0.12f, 0.01f, 6f), Mat(primary, 0.2f));
        Part(PrimitiveType.Cube, transform, new Vector3(1.9f, 0.012f, 0), new Vector3(0.12f, 0.01f, 6f), Mat(primary, 0.2f));

        var propsPrefab = Resources.Load<GameObject>("F1Tyres/PitProps_Cluster");
        if (propsPrefab != null)
        {
            var props = Instantiate(propsPrefab, transform);
            props.name = "PitProps";
            props.transform.localPosition = new Vector3(gx * 5.0f, 0f, -2.0f);
            props.transform.localRotation = Quaternion.Euler(0f, gx > 0f ? -90f : 90f, 0f);
            foreach (var col in props.GetComponentsInChildren<Collider>(true))
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }
    }

    Transform Tyre(Material rubber, bool front)
    {
        var prefab = Resources.Load<GameObject>(front ? "F1Tyres/PitTyre_Front" : "F1Tyres/PitTyre_Rear");
        if (prefab != null)
        {
            var inst = Instantiate(prefab, transform);
            inst.name = front ? "PitTyre_F" : "PitTyre_R";
            foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            return inst.transform;
        }
        var t = Part(PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(0.72f, 0.185f, 0.72f), rubber, Quaternion.Euler(0, 0, 90));
        var rim = Part(PrimitiveType.Cylinder, t, Vector3.zero, new Vector3(0.62f, 1.02f, 0.62f), Mat(new Color(0.12f, 0.12f, 0.13f), 0.7f));
        rim.name = "Rim";
        return t;
    }

    public void SetCompounds(Compound current, Compound next)
    {
        ApplyTyreMat(oldTyre, Resources.Load<Material>("F1Tyres/Tyre_" + Tyres.Names[(int)current]));
        ApplyTyreMat(newTyre, Resources.Load<Material>("F1Tyres/Tyre_" + Tyres.Names[(int)next]));
    }

    static void ApplyTyreMat(Transform[] list, Material mat)
    {
        if (mat == null) return;
        foreach (var t in list)
        {
            if (t == null) continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Rim") continue;
                r.sharedMaterial = mat;
            }
        }
    }

    // ---------- Control (called by the race manager) ----------

    // A car of this team is coming down the pit lane: crew out of the garage to their positions
    public void Prepare()
    {
        if (phase == Phase.Garage || phase == Phase.Released) { phase = Phase.Ready; timer = 0f; }
    }

    public void Begin(float serviceTime)
    {
        duration = Mathf.Max(1.6f, serviceTime);
        phase = Phase.Service;
        timer = 0f;
        readyBlend = 1f;
        if (releaseLight != null) releaseLight.sharedMaterial = lightRed;
        for (int c = 0; c < 4; c++)
        {
            oldTyre[c].gameObject.SetActive(false);
            newTyre[c].gameObject.SetActive(true);
        }
    }

    // Old API: on = car stopped (default timing), off = service finished
    public void SetWorking(bool on)
    {
        if (on) { if (phase != Phase.Service) Begin(duration); }
        else if (releaseLight != null) releaseLight.sharedMaterial = lightOff;
    }

    public void ShowGreen()
    {
        if (releaseLight != null) releaseLight.sharedMaterial = lightGreen;
        phase = Phase.Released;
        releaseTimer = 0f;
    }

    // ---------- Animation ----------

    static float S(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    void Update()
    {
        float dt = Time.deltaTime;
        timer += dt;
        switch (phase)
        {
            case Phase.Garage: readyBlend = Mathf.MoveTowards(readyBlend, 0f, dt * 0.6f); break;
            case Phase.Ready: readyBlend = Mathf.MoveTowards(readyBlend, 1f, dt * 0.8f); if (timer > 40f) phase = Phase.Garage; break;
            case Phase.Released:
                releaseTimer += dt;
                if (releaseTimer > 1.6f && releaseLight != null) releaseLight.sharedMaterial = lightOff;
                if (releaseTimer > 2.5f) { phase = Phase.Garage; for (int c = 0; c < 4; c++) { oldTyre[c].gameObject.SetActive(false); newTyre[c].gameObject.SetActive(false); } }
                break;
        }

        float D = duration, t = phase == Phase.Service ? timer : phase == Phase.Released ? D + releaseTimer : 0f;
        bool active = phase == Phase.Service || phase == Phase.Released;
        foreach (var m in crew)
        {
            // Walk between garage and ready spot, then step in to work during the stop
            Vector3 basePos = Vector3.Lerp(m.home, m.ready, readyBlend);
            float inBlend = 0f;
            Vector3 pos = basePos;
            if (active)
            {
                switch (m.role)
                {
                    case Role.FrontJack: inBlend = S(-0.3f, 0.05f, t) * (1f - S(D + 0.05f, D + 0.5f, t)); break;
                    case Role.RearJack: inBlend = S(0f, 0.3f, t) * (1f - S(D - 0.15f, D + 0.2f, t)); break;
                    case Role.Gunner: inBlend = S(0f, 0.18f, t) * (1f - S(D - 0.1f, D + 0.25f, t)); break;
                    case Role.TyreOff: inBlend = S(0.05f, 0.3f, t) * (1f - S(0.7f, 1.0f, t)); break;
                    case Role.TyreOn: inBlend = S(0.55f, D - 0.75f, t) * (1f - S(D - 0.3f, D + 0.1f, t)); break;
                    case Role.Stabiliser: inBlend = S(0f, 0.2f, t) * (1f - S(D - 0.1f, D + 0.3f, t)); break;
                }
                pos = Vector3.Lerp(basePos, m.work, inBlend);
                if (m.role == Role.TyreOff) pos = Vector3.Lerp(pos, m.stack, S(0.75f, 1.15f, t) * (1f - S(D + 0.4f, D + 2.2f, t)));
            }
            // Running bob while moving, kneel at the wheel
            Vector3 prev = m.t.localPosition;
            float speed = (pos - prev).magnitude / Mathf.Max(dt, 1e-4f);
            float bob = Mathf.Clamp01(speed / 3f) * Mathf.Abs(Mathf.Sin(Time.time * 14f + m.corner)) * 0.06f;
            float kneel = (m.role == Role.Gunner || m.role == Role.TyreOn || m.role == Role.TyreOff) ? inBlend * 0.38f : 0f;
            m.t.localPosition = new Vector3(pos.x, bob - kneel, pos.z);
            Vector3 look = (m.role == Role.FrontJack || m.role == Role.RearJack) && inBlend > 0.5f ? -pos : (inBlend > 0.3f ? m.work - m.ready : pos - prev);
            look.y = 0f;
            if (look.sqrMagnitude > 0.0004f) m.t.localRotation = Quaternion.Slerp(m.t.localRotation, Quaternion.LookRotation(look), dt * 10f);
            // Wheel gun rattles while it is on the nut
            if (m.role == Role.Gunner && m.prop != null)
            {
                bool firing = (t > 0.15f && t < 0.45f) || (t > D - 0.55f && t < D - 0.25f);
                m.prop.localRotation = firing ? Quaternion.Euler(Random.Range(-6f, 6f), 0, Random.Range(-6f, 6f)) : Quaternion.identity;
            }
            if ((m.role == Role.FrontJack || m.role == Role.RearJack) && m.prop != null)
            {
                float lift = active ? S(0.2f, 0.35f, t) * (1f - S(D - 0.25f, D - 0.05f, t)) : 0f;
                m.prop.localRotation = Quaternion.Euler(m.role == Role.FrontJack ? -12f * lift : 12f * lift, 0, 0);
            }
        }

        // Tyres: old set comes off and is carried to the stack; new set goes from the tyre-on man to the hub
        for (int c = 0; c < 4; c++)
        {
            var on = crew.Find(m => m.role == Role.TyreOn && m.corner == c);
            var off = crew.Find(m => m.role == Role.TyreOff && m.corner == c);
            Vector3 wheel = Wheels[c];
            float sx = Mathf.Sign(wheel.x);
            if (active)
            {
                bool showOld = t > 0.35f && t < D + 2.3f;
                if (oldTyre[c].gameObject.activeSelf != showOld) oldTyre[c].gameObject.SetActive(showOld);
                Vector3 hand = off.t.localPosition + new Vector3(-sx * 0.35f, 0.45f, 0);
                oldTyre[c].localPosition = Vector3.Lerp(wheel + new Vector3(sx * 0.05f, 0, 0), hand, S(0.4f, 0.75f, t));
                bool showNew = t < D - 0.5f;
                if (newTyre[c].gameObject.activeSelf != showNew) newTyre[c].gameObject.SetActive(showNew);
                Vector3 carry = on.t.localPosition + new Vector3(-sx * 0.35f, 0.45f, 0);
                newTyre[c].localPosition = Vector3.Lerp(carry, wheel, S(D - 1.0f, D - 0.6f, t));
            }
            else if (phase == Phase.Ready)
            {
                // Waiting with the new tyre in hand
                if (!newTyre[c].gameObject.activeSelf) newTyre[c].gameObject.SetActive(true);
                newTyre[c].localPosition = on.t.localPosition + new Vector3(-sx * 0.35f, 0.45f, 0);
            }
            else if (newTyre[c].gameObject.activeSelf && readyBlend < 0.05f) newTyre[c].gameObject.SetActive(false);
        }
    }
}
