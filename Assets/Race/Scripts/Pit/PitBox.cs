using System.Collections.Generic;
using UnityEngine;

// A team's pit box positioned directly in front of its garage bay:
//   - 16 rigged F1 Pit Crew mechanics (Front/Rear Jack with Prop_PitJack, 4 Wheel Gunners with Prop_WheelGun,
//     4 Tyre-Off, 4 Tyre-On, 2 Side Stabilisers) + 2 Front-Wing Replacement mechanics (carrying Prop_FrontWing_Spare)
//   - 2 rigged Race Engineers (team-colored pit wall attire, headsets, telemetry tablets)
//   - 2 rigged Garage Workers (hi-vis safety vests, headsets, guiding incoming car)
// Animates skeletal bones procedurally (running stride, deep wheel-gun crouch/kneel, tyre lift, jack lever, front wing bolt-on).
public class PitBox : MonoBehaviour
{
    public int team;
    public int liveryIndex;
    public float trackDistance;
    public Renderer releaseLight;
    public Material lightRed, lightGreen, lightOff;

    enum Role { FrontJack, RearJack, Gunner, TyreOff, TyreOn, Stabiliser, WingCrew, Engineer, Worker }

    sealed class CrewRig
    {
        public Transform hips, torso, head;
        public Transform upperArmL, lowerArmL, fistL;
        public Transform upperArmR, lowerArmR, fistR;
        public Transform upperLegL, lowerLegL;
        public Transform upperLegR, lowerLegR;

        Quaternion rTorso, rHead;
        Quaternion rUAL, rLAL, rUAR, rLAR;
        Quaternion rULL, rLLL, rULR, rLLR;
        Vector3 rHipsPos;
        Vector3 hipsUpInParent = Vector3.up;
        Vector3 dirUAL, dirLAL, dirUAR, dirLAR;
        Vector3 dirULL, dirLLL, dirULR, dirLLR;

        public bool valid;

        public void Bind(Transform root)
        {
            hips = FindBone(root, "Body") ?? FindBone(root, "Hips");
            torso = FindBone(root, "Abdomen") ?? FindBone(root, "Torso");
            head = FindBone(root, "Head");
            upperArmL = FindBone(root, "UpperArm.L"); lowerArmL = FindBone(root, "LowerArm.L"); fistL = FindBone(root, "Fist.L");
            upperArmR = FindBone(root, "UpperArm.R"); lowerArmR = FindBone(root, "LowerArm.R"); fistR = FindBone(root, "Fist.R");
            upperLegL = FindBone(root, "UpperLeg.L"); lowerLegL = FindBone(root, "LowerLeg.L");
            upperLegR = FindBone(root, "UpperLeg.R"); lowerLegR = FindBone(root, "LowerLeg.R");

            if (hips != null)
            {
                rHipsPos = hips.localPosition;
                hipsUpInParent = hips.parent != null ? hips.parent.InverseTransformVector(root.up) : Vector3.up;
            }
            if (torso != null) rTorso = torso.localRotation;
            if (head != null) rHead = head.localRotation;

            CaptureLimb(root, upperArmL, lowerArmL, Vector3.left, out rUAL, out dirUAL);
            CaptureLimb(root, lowerArmL, fistL, Vector3.left, out rLAL, out dirLAL);
            CaptureLimb(root, upperArmR, lowerArmR, Vector3.right, out rUAR, out dirUAR);
            CaptureLimb(root, lowerArmR, fistR, Vector3.right, out rLAR, out dirLAR);
            CaptureLimb(root, upperLegL, lowerLegL, Vector3.down, out rULL, out dirULL);
            CaptureLimb(root, lowerLegL, null, Vector3.down, out rLLL, out dirLLL);
            CaptureLimb(root, upperLegR, lowerLegR, Vector3.down, out rULR, out dirULR);
            CaptureLimb(root, lowerLegR, null, Vector3.down, out rLLR, out dirLLR);

            valid = upperArmL != null && upperArmR != null;
        }

        static Transform FindBone(Transform root, string exact)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == exact) return t;
            return null;
        }

        static void CaptureLimb(Transform charRoot, Transform bone, Transform child, Vector3 fallbackRootDir, out Quaternion restRot, out Vector3 restLocalDir)
        {
            restRot = bone != null ? bone.localRotation : Quaternion.identity;
            restLocalDir = Vector3.up;
            if (bone == null || bone.parent == null || charRoot == null) return;
            if (child != null)
            {
                Vector3 v = bone.InverseTransformPoint(child.position);
                if (v.sqrMagnitude > 1e-6f) { restLocalDir = v.normalized; return; }
            }
            restLocalDir = bone.InverseTransformDirection(charRoot.TransformDirection(fallbackRootDir)).normalized;
        }

        static void AimBone(Transform bone, Quaternion restRot, Vector3 restLocalDir, Vector3 desiredDirChar, Transform charRoot, float blendRate = 1f)
        {
            if (bone == null || bone.parent == null || desiredDirChar.sqrMagnitude < 1e-5f) return;
            Quaternion neutralWorld = bone.parent.rotation * restRot;
            Vector3 dFrom = neutralWorld * restLocalDir;
            Vector3 dTo = charRoot.TransformDirection(desiredDirChar.normalized);
            Quaternion targetRot = Quaternion.FromToRotation(dFrom, dTo) * neutralWorld;
            bone.rotation = blendRate >= 0.999f ? targetRot : Quaternion.Slerp(bone.rotation, targetRot, blendRate);
        }

        public void ApplyPose(Transform charRoot, float kneel, float runCycle, float runWeight, Vector3 armTargetDirL, Vector3 armTargetDirR, float torsoLeanDeg, float headYawDeg = 0f, float headPitchDeg = 0f, float blendRate = 1f)
        {
            if (!valid) return;
            kneel = Mathf.Clamp01(kneel);
            runWeight = Mathf.Clamp01(runWeight);

            if (hips != null)
                hips.localPosition = rHipsPos + hipsUpInParent * (-0.32f * kneel + Mathf.Abs(Mathf.Sin(runCycle)) * 0.04f * runWeight);
            if (torso != null && torso.parent != null)
            {
                Quaternion torsoTarget = Quaternion.AngleAxis(torsoLeanDeg, charRoot.right) * (torso.parent.rotation * rTorso);
                torso.rotation = blendRate >= 0.999f ? torsoTarget : Quaternion.Slerp(torso.rotation, torsoTarget, blendRate);
            }
            if (head != null && head.parent != null)
            {
                Quaternion headTarget = Quaternion.AngleAxis(headYawDeg, charRoot.up) * Quaternion.AngleAxis(headPitchDeg, charRoot.right) * (head.parent.rotation * rHead);
                head.rotation = blendRate >= 0.999f ? headTarget : Quaternion.Slerp(head.rotation, headTarget, blendRate);
            }

            // Legs: blend between running stride, standing stance, and deep athletic kneel
            float strideL = Mathf.Sin(runCycle) * 0.65f * runWeight;
            float strideR = -strideL;
            Vector3 kneeFwd = new Vector3(0f, -0.45f, 0.90f);
            Vector3 shinBack = new Vector3(0f, -0.78f, -0.62f);

            Vector3 ulL = Vector3.Lerp(new Vector3(-0.08f, -1f, strideL), kneeFwd + new Vector3(-0.22f, 0f, 0f), kneel);
            Vector3 llL = Vector3.Lerp(new Vector3(0f, -1f, Mathf.Max(0f, -strideL) * 0.8f), shinBack, kneel);
            Vector3 ulR = Vector3.Lerp(new Vector3(0.08f, -1f, strideR), kneeFwd + new Vector3(0.22f, 0f, 0f), kneel);
            Vector3 llR = Vector3.Lerp(new Vector3(0f, -1f, Mathf.Max(0f, -strideR) * 0.8f), shinBack, kneel);

            AimBone(upperLegL, rULL, dirULL, ulL, charRoot, blendRate);
            AimBone(lowerLegL, rLLL, dirLLL, llL, charRoot, blendRate);
            AimBone(upperLegR, rULR, dirULR, ulR, charRoot, blendRate);
            AimBone(lowerLegR, rLLR, dirLLR, llR, charRoot, blendRate);

            // Arms: exact world-space aiming with neutral parent continuation (no double rotation!)
            Vector3 runSwingL = new Vector3(-0.18f, -0.86f, -strideL * 0.7f);
            Vector3 runSwingR = new Vector3(0.18f, -0.86f, -strideR * 0.7f);
            Vector3 dArmL = Vector3.Lerp(armTargetDirL, runSwingL, runWeight * (1f - kneel));
            Vector3 dArmR = Vector3.Lerp(armTargetDirR, runSwingR, runWeight * (1f - kneel));

            Vector3 foreL = Vector3.Lerp(dArmL, new Vector3(-0.08f, -0.18f, 0.98f), 0.55f * (1f - runWeight * (1f - kneel)));
            Vector3 foreR = Vector3.Lerp(dArmR, new Vector3(0.08f, -0.18f, 0.98f), 0.55f * (1f - runWeight * (1f - kneel)));

            AimBone(upperArmL, rUAL, dirUAL, dArmL, charRoot, blendRate);
            AimBone(lowerArmL, rLAL, dirLAL, foreL, charRoot, blendRate);
            AimBone(upperArmR, rUAR, dirUAR, dArmR, charRoot, blendRate);
            AimBone(lowerArmR, rLAR, dirLAR, foreR, charRoot, blendRate);
        }
    }

    class Member
    {
        public Transform t, prop;
        public CrewRig rig;
        public Role role;
        public int corner;               // 0 FL, 1 FR, 2 RL, 3 RR (or index for wing/eng/worker)
        public Vector3 home, ready, work, stack;
    }

    readonly List<Member> crew = new List<Member>();
    readonly Transform[] oldTyre = new Transform[4], newTyre = new Transform[4];
    readonly Quaternion[] tyreBaseRot = new Quaternion[4];
    Transform spareWingProp;
    bool spareWingActiveForStop;

    enum Phase { Garage, Ready, Service, Released }
    Phase phase = Phase.Garage;
    float timer, duration = 2.6f, readyBlend, releaseTimer;
    static readonly Vector3[] Wheels =
    {
        new Vector3(-0.81f, 0.36f, 1.7f),
        new Vector3(0.81f, 0.36f, 1.7f),
        new Vector3(-0.76f, 0.36f, -1.7f),
        new Vector3(0.76f, 0.36f, -1.7f)
    };

    // ---------- Build ----------

    public static PitBox Create(Transform parent, int team, Vector3 pos, Quaternion rot, float garageSide, Color primary, Color secondary, string teamName = "", int liveryIndex = 0)
    {
        var go = new GameObject("PitBox_" + team);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var box = go.AddComponent<PitBox>();
        box.team = team;
        box.liveryIndex = liveryIndex;
        box.Build(garageSide, primary, secondary, teamName);
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
        if (Application.isPlaying) Destroy(g.GetComponent<Collider>()); else DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (rot.HasValue) g.transform.localRotation = rot.Value;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    void Build(float side, Color primary, Color secondary, string teamName)
    {
        var suitColor = Color.Lerp(primary, new Color(0.18f, 0.18f, 0.20f), 0.10f);
        var trimColor = Color.Lerp(secondary, Color.white, 0.12f);
        var helmetColor = Color.Lerp(primary, Color.white, 0.08f);
        var steel = Mat(new Color(0.55f, 0.57f, 0.62f), 0.65f);
        var gantryDark = Mat(new Color(0.10f, 0.11f, 0.13f), 0.45f);
        var rubber = Mat(new Color(0.05f, 0.05f, 0.055f), 0.25f);
        float gx = side >= 0f ? 1f : -1f;   // garage apron side: +1 right of car, -1 left

        var prefabMech = Resources.Load<GameObject>("F1Crew/PitCrew_Mechanic");
        var prefabEng = Resources.Load<GameObject>("F1Crew/Race_Engineer");
        var prefabWork = Resources.Load<GameObject>("F1Crew/Pit_Worker");
        var prefabGun = Resources.Load<GameObject>("F1Crew/Prop_WheelGun");
        var prefabJack = Resources.Load<GameObject>("F1Crew/Prop_PitJack");
        var prefabWing = Resources.Load<GameObject>("F1Crew/Prop_FrontWing_Spare");

        // Shared team-tinted materials cloned safely from prefab textures so all 20 crew members keep 100% textured weave!
        void ApplyTeamTints(GameObject charInst, bool isMechanic)
        {
            foreach (var r in charInst.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                var sm = r.sharedMaterials;
                bool chg = false;
                for (int i = 0; i < sm.Length; i++)
                {
                    var m = sm[i];
                    if (m == null) continue;
                    string mn = m.name;
                    if (mn.Contains("Crew_Suit") || mn.Contains("Engineer_Jacket"))
                    {
                        var inst = new Material(m);
                        inst.SetColor("_BaseColor", suitColor);
                        sm[i] = inst; chg = true;
                    }
                    else if (mn.Contains("Crew_Trim"))
                    {
                        var inst = new Material(m);
                        inst.SetColor("_BaseColor", trimColor);
                        sm[i] = inst; chg = true;
                    }
                    else if (isMechanic && mn.Contains("Crew_Helmet") && !mn.Contains("Visor"))
                    {
                        var inst = new Material(m);
                        inst.SetColor("_BaseColor", helmetColor);
                        inst.SetFloat("_Smoothness", 0.85f);
                        sm[i] = inst; chg = true;
                    }
                    else if (mn.Contains("Crew_Visor"))
                    {
                        var inst = new Material(m);
                        inst.SetColor("_BaseColor", new Color(0.02f, 0.025f, 0.035f));
                        inst.SetFloat("_Metallic", 0.35f);
                        inst.SetFloat("_Smoothness", 0.92f);
                        sm[i] = inst; chg = true;
                    }
                }
                if (chg) r.sharedMaterials = sm;
            }
        }

        Member Add(Role role, int corner, Vector3 work, Vector3 ready, Vector3 home)
        {
            GameObject prefab = role == Role.Engineer ? prefabEng : (role == Role.Worker ? prefabWork : prefabMech);
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, transform);
                go.name = role + (corner >= 0 ? "_" + corner : "");
                ApplyTeamTints(go, role != Role.Engineer && role != Role.Worker);
            }
            else
            {
                go = new GameObject(role + (corner >= 0 ? "_" + corner : ""));
                go.transform.SetParent(transform, false);
                Part(PrimitiveType.Capsule, go.transform, new Vector3(0, 0.85f, 0), new Vector3(0.45f, 0.65f, 0.35f), Mat(suitColor));
            }
            var rig = new CrewRig();
            rig.Bind(go.transform);

            var m = new Member { t = go.transform, rig = rig, role = role, corner = corner, work = work, ready = ready, home = home };
            go.transform.localPosition = home;
            crew.Add(m);
            return m;
        }

        Transform AttachTool(Member m, GameObject prefab, Transform handBone, Vector3 localOff, Quaternion localRot, float scale = 1f)
        {
            Transform parentTf = handBone != null ? handBone : m.t;
            GameObject p;
            if (prefab != null)
            {
                p = Instantiate(prefab, parentTf);
                p.transform.localPosition = localOff;
                p.transform.localRotation = localRot;
                p.transform.localScale = Vector3.one * scale;
            }
            else
            {
                p = Part(PrimitiveType.Cube, parentTf, localOff, new Vector3(0.25f, 0.1f, 0.25f), steel).gameObject;
            }
            foreach (var col in p.GetComponentsInChildren<Collider>(true))
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            return p.transform;
        }

        // Front & Rear Jack Men with Prop_PitJack (RearJack offset to rear-quarter so chase camera stays clear)
        var fj = Add(Role.FrontJack, -1, new Vector3(0, 0, 3.65f), new Vector3(0, 0, 4.6f), new Vector3(gx * 2.50f, 0, 4.05f));
        fj.prop = AttachTool(fj, prefabJack, fj.t, new Vector3(0f, 0f, 0.85f), Quaternion.Euler(0, 180f, 0));

        var rj = Add(Role.RearJack, -1, new Vector3(gx * 0.85f, 0, -4.05f), new Vector3(gx * 2.15f, 0, -4.15f), new Vector3(gx * 2.50f, 0, -4.35f));
        rj.prop = AttachTool(rj, prefabJack, rj.t, new Vector3(0f, 0f, 0.85f), Quaternion.Euler(0, 180f, 0));

        // 4 Corners: Gunner (Prop_WheelGun), TyreOff, TyreOn (staggered two-row home formation >= 0.70m apart)
        float[] homeGunZ = { 2.65f, 1.95f, -1.95f, -2.65f };
        float[] homeOffZ = { 3.55f, 1.45f, -2.15f, -3.55f };
        float[] homeOnZ  = { 0.75f, 0.05f, -0.65f, -1.35f };
        for (int c = 0; c < 4; c++)
        {
            Vector3 w = Wheels[c];
            float sx = Mathf.Sign(w.x), front = c < 2 ? 1f : -1f;
            bool onGarageSide = Mathf.Sign(sx) == Mathf.Sign(gx);
            float fastSideOff = 0.72f;
            float fastReadyOff = 0.95f;
            var gun = Add(Role.Gunner, c, new Vector3(w.x + sx * (onGarageSide ? 0.88f : fastSideOff), 0, w.z), new Vector3(onGarageSide ? gx * 2.15f : w.x + sx * fastReadyOff, 0, w.z), new Vector3(gx * 2.50f, 0, homeGunZ[c]));
            gun.prop = AttachTool(gun, prefabGun, gun.rig.fistR ?? gun.t, gun.rig.fistR != null ? new Vector3(0f, 0.08f, 0f) : new Vector3(0, 0.52f, 0.35f), Quaternion.identity);

            var off = Add(Role.TyreOff, c, new Vector3(w.x + sx * (onGarageSide ? 0.90f : fastSideOff), 0, w.z + front * 0.65f), new Vector3(onGarageSide ? gx * 2.25f : w.x + sx * (fastReadyOff + 0.15f), 0, w.z + front * 0.78f), new Vector3(gx * 3.05f, 0, homeOffZ[c]));
            off.stack = new Vector3(gx * 2.35f, 0, w.z + front * 0.90f);

            Add(Role.TyreOn, c, new Vector3(w.x + sx * (onGarageSide ? 0.90f : fastSideOff), 0, w.z - front * 0.65f), new Vector3(onGarageSide ? gx * 2.30f : w.x + sx * (fastReadyOff + 0.30f), 0, w.z - front * 0.25f), new Vector3(gx * 3.05f, 0, homeOnZ[c]));

            oldTyre[c] = Tyre(rubber, c < 2);
            newTyre[c] = Tyre(rubber, c < 2);
            tyreBaseRot[c] = oldTyre[c].localRotation;
            newTyre[c].gameObject.SetActive(false);
            oldTyre[c].gameObject.SetActive(false);
        }

        // Side Stabilisers
        Add(Role.Stabiliser, 0, new Vector3(-1.35f, 0, 0.1f), new Vector3(-2.0f, 0, 0.2f), new Vector3(gx * 2.50f, 0, 1.15f));
        Add(Role.Stabiliser, 1, new Vector3(1.35f, 0, 0.1f), new Vector3(2.0f, 0, 0.2f), new Vector3(gx * 2.50f, 0, -1.15f));

        // 2 Front Wing Replacement Mechanics + Spare Wing prop textured with team livery
        var fw0 = Add(Role.WingCrew, 0, new Vector3(-1.05f, 0, 2.55f), new Vector3(gx * 2.25f, 0, 2.6f), new Vector3(gx * 3.05f, 0, 2.85f));
        var fw1 = Add(Role.WingCrew, 1, new Vector3(1.05f, 0, 2.55f), new Vector3(gx * 2.25f, 0, 2.1f), new Vector3(gx * 3.05f, 0, 2.15f));
        if (prefabWing != null)
        {
            var wingGo = Instantiate(prefabWing, transform);
            wingGo.name = "Spare_FrontWing";
            var liveryMat = Resources.Load<Material>("F1Liveries/Livery_" + liveryIndex.ToString("00"));
            if (liveryMat != null)
                foreach (var r in wingGo.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = liveryMat;
            foreach (var col in wingGo.GetComponentsInChildren<Collider>(true))
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            spareWingProp = wingGo.transform;
            spareWingProp.localPosition = new Vector3(gx * 2.35f, 0.4f, 2.2f);
            spareWingProp.gameObject.SetActive(false);
        }

        // 2 Race Engineers (tablets + headsets) & 2 Garage Workers (hi-vis vests) stationed cleanly on tarmac apron
        Add(Role.Engineer, 0, new Vector3(gx * 2.55f, 0, 0.38f), new Vector3(gx * 2.55f, 0, 0.38f), new Vector3(gx * 2.50f, 0, 0.38f));
        Add(Role.Engineer, 1, new Vector3(gx * 2.55f, 0, -0.38f), new Vector3(gx * 2.55f, 0, -0.38f), new Vector3(gx * 2.50f, 0, -0.38f));
        Add(Role.Worker, 0, new Vector3(gx * 2.35f, 0, 3.35f), new Vector3(gx * 2.25f, 0, 3.35f), new Vector3(gx * 2.50f, 0, 3.35f));
        Add(Role.Worker, 1, new Vector3(gx * 2.35f, 0, -3.35f), new Vector3(gx * 2.25f, 0, -3.35f), new Vector3(gx * 2.50f, 0, -3.35f));

        // Team Overhead Pit Gantry & Release Light directly over/beside box on garage side (clear of Worker 0)
        Part(PrimitiveType.Cylinder, transform, new Vector3(gx * 2.65f, 1.9f, 4.35f), new Vector3(0.09f, 1.9f, 0.09f), gantryDark);
        Part(PrimitiveType.Cube, transform, new Vector3(gx * 1.35f, 3.65f, 4.35f), new Vector3(2.9f, 0.22f, 0.24f), Mat(primary, 0.45f));
        Part(PrimitiveType.Cube, transform, new Vector3(0.0f, 3.42f, 4.35f), new Vector3(1.1f, 0.14f, 0.18f), Mat(secondary, 0.45f));

        lightOff = Mat(new Color(0.1f, 0.1f, 0.1f));
        lightRed = Mat(new Color(1f, 0.1f, 0.08f), 0.6f, new Color(1f, 0.05f, 0.03f) * 3.5f);
        lightGreen = Mat(new Color(0.1f, 1f, 0.3f), 0.6f, new Color(0.05f, 1f, 0.2f) * 3.5f);
        releaseLight = Part(PrimitiveType.Cube, transform, new Vector3(0.0f, 3.22f, 4.37f), new Vector3(0.65f, 0.32f, 0.22f), lightOff).GetComponent<Renderer>();

        // High-visibility Team Pit Box Floor Markings on the garage working apron
        var paintW = Mat(new Color(0.95f, 0.95f, 0.95f), 0.2f);
        var paintP = Mat(primary, 0.25f);
        var paintS = Mat(secondary, 0.25f);
        Part(PrimitiveType.Cube, transform, new Vector3(0, 0.015f, 2.85f), new Vector3(2.3f, 0.012f, 0.16f), paintW);
        Part(PrimitiveType.Cube, transform, new Vector3(-1.35f, 0.015f, 0), new Vector3(0.14f, 0.012f, 5.8f), paintP);
        Part(PrimitiveType.Cube, transform, new Vector3(1.35f, 0.015f, 0), new Vector3(0.14f, 0.012f, 5.8f), paintP);
        Part(PrimitiveType.Cube, transform, new Vector3(0, 0.014f, -2.85f), new Vector3(2.3f, 0.012f, 0.14f), paintS);
        Part(PrimitiveType.Cube, transform, new Vector3(0f, 0.011f, 0f), new Vector3(2.2f, 0.01f, 4.8f), Mat(new Color(primary.r, primary.g, primary.b, 0.55f), 0.6f, primary * 0.35f));

        var propsPrefab = Resources.Load<GameObject>("F1Tyres/PitProps_Cluster");
        if (propsPrefab != null)
        {
            var props = Instantiate(propsPrefab, transform);
            props.name = "PitProps";
            props.transform.localPosition = new Vector3(gx * 3.15f, 0f, -3.85f);
            props.transform.localRotation = Quaternion.identity;
            props.transform.localScale = Vector3.one * 0.52f;
            foreach (var col in props.GetComponentsInChildren<Collider>(true))
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }

        // Pose all idle crew into natural arms-down posture right at creation
        EvaluateAnimation(0f);
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

    public void Prepare()
    {
        if (phase == Phase.Garage || phase == Phase.Released) { phase = Phase.Ready; timer = 0f; }
    }

    public void Begin(float serviceTime, bool replaceFrontWing = false)
    {
        duration = Mathf.Max(1.6f, serviceTime);
        spareWingActiveForStop = replaceFrontWing;
        phase = Phase.Service;
        timer = 0f;
        readyBlend = 1f;
        if (releaseLight != null) releaseLight.sharedMaterial = lightRed;
        for (int c = 0; c < 4; c++)
        {
            oldTyre[c].gameObject.SetActive(false);
            newTyre[c].gameObject.SetActive(true);
        }
        if (spareWingProp != null) spareWingProp.gameObject.SetActive(replaceFrontWing);
    }

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
        if (spareWingProp != null) spareWingProp.gameObject.SetActive(false);
    }

    // EditMode verification helper for deterministic batchmode renders
    public void PreviewServiceAt(float elapsed, float totalService, bool replaceFrontWing)
    {
        Begin(totalService, replaceFrontWing);
        timer = elapsed;
        EvaluateAnimation(0.02f);
    }

    // ---------- Animation ----------

    static float S(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    // Cached crew lookup (no per-frame lambda allocations)
    Member FindCrew(Role role, int corner)
    {
        for (int i = 0; i < crew.Count; i++) { var m = crew[i]; if (m.role == role && m.corner == corner) return m; }
        return null;
    }

    // Idle crews far from the camera are hidden and not animated (they are 20+ skinned figures per box)
    Renderer[] allRenderers;
    float visCheck;
    bool hiddenFar;
    const float FarHide = 160f;

    bool UpdateDistanceCulling()
    {
        visCheck -= Time.deltaTime;
        if (visCheck > 0f) return hiddenFar;
        visCheck = 0.4f;
        var cam = Camera.main;
        bool idle = phase == Phase.Garage && readyBlend <= 0.001f;
        bool far = cam != null && (cam.transform.position - transform.position).sqrMagnitude > FarHide * FarHide;
        bool hide = idle && far;
        if (hide != hiddenFar)
        {
            hiddenFar = hide;
            if (allRenderers == null) allRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                if (r == null || r == releaseLight) continue;
                r.forceRenderingOff = hide;
            }
        }
        return hiddenFar;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (UpdateDistanceCulling()) return;
        timer += dt;
        switch (phase)
        {
            case Phase.Garage: readyBlend = Mathf.MoveTowards(readyBlend, 0f, dt * 0.7f); break;
            case Phase.Ready: readyBlend = Mathf.MoveTowards(readyBlend, 1f, dt * 0.9f); if (timer > 40f) phase = Phase.Garage; break;
            case Phase.Released:
                releaseTimer += dt;
                if (releaseTimer > 1.6f && releaseLight != null) releaseLight.sharedMaterial = lightOff;
                if (releaseTimer > 2.6f)
                {
                    phase = Phase.Garage;
                    spareWingActiveForStop = false;
                    for (int c = 0; c < 4; c++) { oldTyre[c].gameObject.SetActive(false); newTyre[c].gameObject.SetActive(false); }
                }
                break;
        }
        EvaluateAnimation(dt);
    }

    void EvaluateAnimation(float dt)
    {
        float D = duration, t = phase == Phase.Service ? timer : phase == Phase.Released ? D + releaseTimer : 0f;
        bool active = phase == Phase.Service || phase == Phase.Released;
        float wavePhase = Application.isPlaying ? Time.time : timer;
        float poseBlend = dt > 0.0005f && Application.isPlaying ? 1f - Mathf.Exp(-20f * dt) : 1f;
        float posBlend = dt > 0.0005f && Application.isPlaying ? 1f - Mathf.Exp(-18f * dt) : 1f;

        foreach (var m in crew)
        {
            Vector3 basePos = Vector3.Lerp(m.home, m.ready, readyBlend);
            float inBlend = 0f;
            Vector3 pos = basePos;

            if (active)
            {
                switch (m.role)
                {
                    case Role.FrontJack:  inBlend = S(-0.25f, 0.05f, t) * (1f - S(D + 0.05f, D + 0.5f, t)); break;
                    case Role.RearJack:   inBlend = S(0f, 0.28f, t) * (1f - S(D - 0.15f, D + 0.25f, t)); break;
                    case Role.Gunner:     inBlend = S(0f, 0.18f, t) * (1f - S(D - 0.10f, D + 0.25f, t)); break;
                    case Role.TyreOff:    inBlend = S(0.02f, 0.26f, t) * (1f - S(0.60f, 1.05f, t)); break;
                    case Role.TyreOn:     inBlend = S(0.42f, Mathf.Max(0.85f, D - 0.72f), t) * (1f - S(D - 0.22f, D + 0.12f, t)); break;
                    case Role.Stabiliser: inBlend = S(0f, 0.20f, t) * (1f - S(D - 0.10f, D + 0.30f, t)); break;
                    case Role.WingCrew:
                        if (spareWingActiveForStop)
                            inBlend = S(0.15f, 0.75f, t) * (1f - S(D - 0.35f, D + 0.05f, t));
                        break;
                    case Role.Engineer:
                    case Role.Worker:
                        inBlend = S(0f, 0.25f, t) * (1f - S(D, D + 0.4f, t));
                        break;
                }
                if (m.role == Role.TyreOff)
                {
                    if (t < 0.62f) pos = Vector3.Lerp(basePos, m.work, S(0.02f, 0.30f, t));
                    else if (t < D + 0.4f) pos = Vector3.Lerp(m.work, m.stack, S(0.62f, 1.22f, t));
                    else pos = Vector3.Lerp(m.stack, basePos, S(D + 0.4f, D + 2.2f, t));
                }
                else
                {
                    pos = Vector3.Lerp(basePos, m.work, inBlend);
                }
            }

            Vector3 prev = m.t.localPosition;
            Vector3 targetPos = new Vector3(pos.x, 0f, pos.z);
            Vector3 nextPos = posBlend >= 0.999f ? targetPos : Vector3.Lerp(prev, targetPos, posBlend);
            float moveSpeed = dt > 1e-4f ? (targetPos - prev).magnitude / dt : 0f;
            float runWeight = Mathf.Clamp01(moveSpeed / 2.2f);
            float runCycle = wavePhase * 11f + m.corner * 1.1f;

            // Keep feet on asphalt (y = 0); CrewRig handles hip crouch height cleanly!
            m.t.localPosition = nextPos;

            Vector3 look;
            if (m.role == Role.Engineer || m.role == Role.Worker)
                look = new Vector3(-Mathf.Sign(m.home.x), 0f, 0.35f);
            else if (m.role == Role.FrontJack || m.role == Role.RearJack)
                look = -pos;
            else if (inBlend > 0.20f && m.role == Role.WingCrew)
                look = new Vector3(0f, 0f, 2.52f) - new Vector3(pos.x, 0f, pos.z);
            else if (inBlend > 0.20f && (m.role == Role.Gunner || m.role == Role.TyreOn || m.role == Role.TyreOff || m.role == Role.Stabiliser) && m.corner >= 0 && m.corner < 4)
                look = Wheels[m.corner] - new Vector3(pos.x, Wheels[m.corner].y, pos.z);
            else
                look = (pos - prev).sqrMagnitude > 1e-4f ? (pos - prev) : (m.work - m.ready);
            look.y = 0f;
            if (look.sqrMagnitude > 0.0004f)
            {
                Quaternion targetRot = Quaternion.LookRotation(look.normalized);
                m.t.localRotation = dt > 0.001f ? Quaternion.Slerp(m.t.localRotation, targetRot, Mathf.Min(1f, dt * 12f)) : targetRot;
            }

            // Apply anatomical skeletal bone posing on the downloaded Quaternius rig
            if (m.rig != null && m.rig.valid)
            {
                float kneel = 0f, lean = 4f, headPitch = 0f, headYaw = 0f;
                Vector3 armL = new Vector3(-0.22f, -0.88f, 0.22f);
                Vector3 armR = new Vector3(0.22f, -0.88f, 0.22f);

                switch (m.role)
                {
                    case Role.Gunner:
                        kneel = inBlend * 0.88f;
                        lean = Mathf.Lerp(4f, 18f, inBlend);
                        headPitch = Mathf.Lerp(0f, 16f, inBlend);
                        float midClear = active ? S(0.36f, 0.56f, t) * (1f - S(D - 0.68f, D - 0.44f, t)) : 0f;
                        float gunDone = active ? S(D - 0.22f, D - 0.04f, t) * (1f - S(D + 0.08f, D + 0.30f, t)) : 0f;
                        Vector3 gunWorkL = Vector3.Lerp(new Vector3(-0.14f, -0.45f, 0.88f), new Vector3(-0.18f, -0.32f, 0.68f), midClear);
                        Vector3 gunWorkR = Vector3.Lerp(new Vector3(0.14f, -0.45f, 0.88f), new Vector3(0.18f, -0.32f, 0.68f), midClear);
                        gunWorkR = Vector3.Lerp(gunWorkR, new Vector3(0.22f, 0.65f, 0.42f), gunDone);
                        armL = Vector3.Lerp(armL, gunWorkL, inBlend);
                        armR = Vector3.Lerp(armR, gunWorkR, inBlend);
                        break;
                    case Role.TyreOff:
                    case Role.TyreOn:
                        kneel = inBlend * 0.78f;
                        lean = Mathf.Lerp(4f, 22f, inBlend);
                        armL = Vector3.Lerp(armL, new Vector3(-0.35f, -0.25f, 0.90f), inBlend);
                        armR = Vector3.Lerp(armR, new Vector3(0.35f, -0.25f, 0.90f), inBlend);
                        break;
                    case Role.FrontJack:
                    case Role.RearJack:
                        kneel = inBlend * 0.35f;
                        lean = Mathf.Lerp(6f, 18f, inBlend);
                        float liftArm = active ? S(0.15f, 0.35f, t) * (1f - S(D - 0.25f, D, t)) : 0f;
                        armL = Vector3.Lerp(new Vector3(-0.20f, -0.55f, 0.75f), new Vector3(-0.20f, -0.25f, 0.85f), liftArm);
                        armR = Vector3.Lerp(new Vector3(0.20f, -0.55f, 0.75f), new Vector3(0.20f, -0.25f, 0.85f), liftArm);
                        break;
                    case Role.Stabiliser:
                        lean = Mathf.Lerp(4f, 14f, inBlend);
                        armL = Vector3.Lerp(armL, new Vector3(-0.28f, -0.20f, 0.92f), inBlend);
                        armR = Vector3.Lerp(armR, new Vector3(0.28f, -0.20f, 0.92f), inBlend);
                        break;
                    case Role.WingCrew:
                        kneel = inBlend * 0.82f;
                        lean = Mathf.Lerp(5f, 24f, inBlend);
                        armL = Vector3.Lerp(armL, new Vector3(-0.26f, -0.35f, 0.90f), inBlend);
                        armR = Vector3.Lerp(armR, new Vector3(0.26f, -0.35f, 0.90f), inBlend);
                        break;
                    case Role.Engineer:
                        lean = 10f;
                        headPitch = 14f;
                        armL = new Vector3(-0.18f, -0.78f, 0.35f);
                        armR = new Vector3(0.14f, -0.35f, 0.88f); // holding telemetry tablet cleanly forward-up!
                        break;
                    case Role.Worker:
                        // Wave arm gracefully when car is approaching/ready
                        float wave = phase == Phase.Ready ? Mathf.Sin(wavePhase * 7f + m.corner) * 0.35f : 0f;
                        armL = new Vector3(-0.22f, -0.82f, 0.20f);
                        armR = phase == Phase.Ready ? new Vector3(0.35f, 0.55f + wave, 0.45f) : new Vector3(0.22f, -0.82f, 0.20f);
                        break;
                }
                m.rig.ApplyPose(m.t, kneel, runCycle, runWeight, armL, armR, lean, headYaw, headPitch, poseBlend);
            }

            if (m.role == Role.Gunner && m.prop != null)
            {
                bool firing = active && ((t > 0.12f && t < 0.42f) || (t > D - 0.52f && t < D - 0.20f));
                m.prop.localRotation = firing ? Quaternion.Euler(Mathf.Sin(wavePhase * 60f) * 5f, 0f, Mathf.Cos(wavePhase * 52f) * 5f) : Quaternion.identity;
            }
            if ((m.role == Role.FrontJack || m.role == Role.RearJack) && m.prop != null)
            {
                float lift = active ? S(0.18f, 0.35f, t) * (1f - S(D - 0.28f, D - 0.05f, t)) : 0f;
                m.prop.localRotation = Quaternion.Euler(-14f * lift, 180f, 0f);
            }
        }

        // Tyres & Spare Front Wing prop animations (3-phase axial hub slide + arc lift)
        for (int c = 0; c < 4; c++)
        {
            var on = FindCrew(Role.TyreOn, c);
            var off = FindCrew(Role.TyreOff, c);
            if (on == null || off == null) continue;
            Vector3 wheel = Wheels[c];
            float sx = Mathf.Sign(wheel.x);
            Vector3 hubClear = wheel + new Vector3(sx * 0.32f, 0.02f, 0f);
            Quaternion upright = tyreBaseRot[c];
            Quaternion carryRot = Quaternion.Euler(14f, sx * 22f, 0f) * upright;
            if (active)
            {
                bool showOld = t > 0.30f && t < D + 2.3f;
                if (oldTyre[c].gameObject.activeSelf != showOld) oldTyre[c].gameObject.SetActive(showOld);
                Vector3 hand = off.t.localPosition + new Vector3(-sx * 0.36f, 0.48f, 0.06f);
                if (t < 0.52f)
                {
                    oldTyre[c].localPosition = Vector3.Lerp(wheel, hubClear, S(0.30f, 0.52f, t));
                    oldTyre[c].localRotation = upright;
                }
                else
                {
                    float carryT = S(0.52f, 1.15f, t);
                    Vector3 arcPos = Vector3.Lerp(hubClear, hand, carryT);
                    arcPos.y += Mathf.Sin(carryT * Mathf.PI) * 0.10f;
                    oldTyre[c].localPosition = arcPos;
                    oldTyre[c].localRotation = Quaternion.Slerp(upright, carryRot, carryT);
                }

                bool showNew = t < D - 0.38f;
                if (newTyre[c].gameObject.activeSelf != showNew) newTyre[c].gameObject.SetActive(showNew);
                Vector3 carry = on.t.localPosition + new Vector3(-sx * 0.36f, 0.48f, 0.06f);
                float mountSplit = Mathf.Max(0.95f, D - 0.72f);
                float mountDone = Mathf.Max(1.25f, D - 0.38f);
                if (t < mountSplit)
                {
                    float bringT = S(0.45f, mountSplit, t);
                    Vector3 bringPos = Vector3.Lerp(carry, hubClear, bringT);
                    bringPos.y += Mathf.Sin(bringT * Mathf.PI) * 0.10f;
                    newTyre[c].localPosition = bringPos;
                    newTyre[c].localRotation = Quaternion.Slerp(carryRot, upright, bringT);
                }
                else
                {
                    newTyre[c].localPosition = Vector3.Lerp(hubClear, wheel, S(mountSplit, mountDone, t));
                    newTyre[c].localRotation = upright;
                }
            }
            else if (phase == Phase.Ready)
            {
                if (!newTyre[c].gameObject.activeSelf) newTyre[c].gameObject.SetActive(true);
                newTyre[c].localPosition = on.t.localPosition + new Vector3(-sx * 0.36f, 0.48f, 0.06f);
                newTyre[c].localRotation = carryRot;
            }
            else if (newTyre[c].gameObject.activeSelf && readyBlend < 0.05f)
            {
                newTyre[c].gameObject.SetActive(false);
            }
        }

        if (spareWingProp != null && spareWingActiveForStop && active)
        {
            var fw0 = FindCrew(Role.WingCrew, 0);
            var fw1 = FindCrew(Role.WingCrew, 1);
            bool mounted = t >= D - 0.65f;
            if (spareWingProp.gameObject.activeSelf == mounted) spareWingProp.gameObject.SetActive(!mounted);
            if (!mounted && fw0 != null && fw1 != null)
            {
                Vector3 carryCenter = (fw0.t.localPosition + fw1.t.localPosition) * 0.5f + new Vector3(0f, 0.42f, 0.15f);
                Vector3 noseMount = new Vector3(0f, 0.10f, 2.52f);
                spareWingProp.localPosition = Vector3.Lerp(carryCenter, noseMount, S(0.65f, D - 0.75f, t));
                spareWingProp.localRotation = Quaternion.identity;
            }
        }
    }
}
