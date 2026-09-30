using UnityEngine;

/// <summary>
/// Turns the robot with the headset and reaches the controllers with both arms.
/// Hands copy the controller rotation. Legs stay on the rig.
/// </summary>
public class Hd2Body : MonoBehaviour
{
    public Transform head;
    public Transform body;
    public Transform leftHand;
    public Transform rightHand;

    public Transform upperArmL;
    public Transform forearmL;
    public Transform handL;
    public Transform upperArmR;
    public Transform forearmR;
    public Transform handR;
    [Tooltip("Meters to shift the body behind the headset so the chest does not block looking down.")]
    public float bodyBackOffset = 0.2f;
    [Tooltip("Extra rotation applied before the body yaws with the headset. The blockout robot needs -90 on X.")]
    public Vector3 bodyRestEuler = new Vector3(-90f, 0f, 0f);
    [Tooltip("Scale the whole skinned body so the head stays with the headset. Blockout legs scale on their own.")]
    public bool scaleWholeBody;

    Transform hips;
    Transform neck;
    Transform thighL;
    Transform thighR;
    Transform footL;
    Transform footR;
    Transform[] chainL;
    Transform[] chainR;
    Vector3 restHipsLocal;
    float restNeckAboveHip;
    float restFootOffset;
    int thighAxisL = 1;
    int thighAxisR = 1;
    bool posed;

    void Awake()
    {
        if (GetComponent<Hd2Health>() == null)
            gameObject.AddComponent<Hd2Health>();
        if (GetComponent<Hd2ThrustMeter>() == null)
            gameObject.AddComponent<Hd2ThrustMeter>();
        if (body != null)
        {
            var animators = body.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
                animators[i].enabled = false;
        }
    }

    void LateUpdate()
    {
        if (head == null || body == null)
            return;

        Vector3 look = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        Vector3 feet = new Vector3(head.position.x, transform.position.y, head.position.z);
        if (look.sqrMagnitude > 0.0001f)
            feet -= look.normalized * bodyBackOffset;
        body.position = feet;

        Vector3 rootForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (look.sqrMagnitude > 0.0001f && rootForward.sqrMagnitude > 0.0001f)
        {
            float yaw = Vector3.SignedAngle(rootForward, look, Vector3.up);
            body.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(bodyRestEuler);
        }

        EnsureBones();
        var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < skins.Length; i++)
            skins[i].updateWhenOffscreen = true;
        if (scaleWholeBody)
            FitWholeBodyToHead();
        else
            FitLegsToHead();
        SolveArmChain(chainL, upperArmL, forearmL, handL, leftHand, -1f);
        SolveArmChain(chainR, upperArmR, forearmR, handR, rightHand, 1f);
    }

    void EnsureBones()
    {
        if (body == null)
            return;

        if (upperArmL == null)
            upperArmL = Find(body, "Arm.L");
        if (upperArmL == null)
            upperArmL = Find(body, "UpperArm_L");
        if (forearmL == null)
            forearmL = Find(body, "Arm.L.002");
        if (forearmL == null)
            forearmL = Find(body, "Forearm_L");
        if (handL == null)
            handL = Find(body, "Hand.L");
        if (upperArmR == null)
            upperArmR = Find(body, "Arm.R");
        if (upperArmR == null)
            upperArmR = Find(body, "UpperArm_R");
        if (forearmR == null)
            forearmR = Find(body, "Arm.R.002");
        if (forearmR == null)
            forearmR = Find(body, "Forearm_R");
        if (handR == null)
            handR = Find(body, "Hand.R");
        if (Find(body, "Arm.L") != null)
            scaleWholeBody = true;
        if (chainL == null)
            chainL = BuildChain(upperArmL, handL);
        if (chainR == null)
            chainR = BuildChain(upperArmR, handR);
    }

    static Transform[] BuildChain(Transform upper, Transform hand)
    {
        if (upper == null || hand == null)
            return null;

        var links = new System.Collections.Generic.List<Transform>();
        Transform bone = hand;
        while (bone != null)
        {
            links.Add(bone);
            if (bone == upper)
                break;
            bone = bone.parent;
        }

        if (links.Count < 2 || links[links.Count - 1] != upper)
            return null;

        links.Reverse();
        return links.ToArray();
    }

    void SolveArmChain(Transform[] chain, Transform upper, Transform forearm, Transform hand, Transform target, float side)
    {
        if (chain == null || chain.Length < 2)
        {
            SolveArm(upper, forearm, hand, target, side);
            return;
        }

        if (target == null || hand == null)
            return;

        Vector3 wrist = target.position - target.forward * 0.08f;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            for (int i = chain.Length - 2; i >= 0; i--)
                PointAt(chain[i], hand, wrist);
        }

        PlaceHandOnGun(hand, target);
    }

    void FitWholeBodyToHead()
    {
        if (neck == null)
            neck = Find(body, "Head");
        if (neck == null || head == null)
            return;

        body.localScale = Vector3.one;
        float current = neck.position.y - body.position.y;
        float target = head.position.y - body.position.y;
        float scale = current > 0.05f ? Mathf.Clamp(target / current, 0.35f, 1.75f) : 1f;
        body.localScale = Vector3.one * scale;

        Vector3 look = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        Vector3 desired = head.position;
        if (look.sqrMagnitude > 0.0001f)
            desired -= look.normalized * bodyBackOffset;
        Vector3 shift = desired - neck.position;
        shift.y = 0f;
        body.position += shift;
    }

    void FitLegsToHead()
    {
        if (hips == null)
            BindLegs();
        if (hips == null || neck == null || thighL == null || thighR == null)
            return;

        hips.localPosition = restHipsLocal;
        SetAxisScale(thighL, thighAxisL, 1f);
        SetAxisScale(thighR, thighAxisR, 1f);

        if (!posed)
        {
            restNeckAboveHip = neck.position.y - hips.position.y;
            restFootOffset = (footL != null ? footL.position.y : hips.position.y - 0.8f) - hips.position.y;
            thighAxisL = LengthAxis(thighL, footL != null ? footL : thighL);
            thighAxisR = LengthAxis(thighR, footR != null ? footR : thighR);
            posed = Mathf.Abs(restNeckAboveHip) > 0.05f && restFootOffset < -0.05f;
            if (!posed)
                return;
        }

        float groundY = body.position.y;
        float targetHipY = head.position.y - restNeckAboveHip;
        targetHipY = Mathf.Clamp(targetHipY, groundY + 0.25f, groundY + 2.1f);

        Vector3 hip = hips.position;
        hip.y = targetHipY;
        hips.position = hip;

        float scale = (groundY - targetHipY) / restFootOffset;
        scale = Mathf.Clamp(scale, 0.35f, 1.75f);
        SetAxisScale(thighL, thighAxisL, scale);
        SetAxisScale(thighR, thighAxisR, scale);
    }

    void BindLegs()
    {
        if (body == null)
            return;

        hips = Find(body, "Hips");
        neck = Find(body, "Head");
        if (neck == null)
            neck = Find(body, "Neck");
        if (neck == null)
            neck = Find(body, "Chest");
        HideHeadFromInside();
        thighL = Find(body, "Thigh_L");
        thighR = Find(body, "Thigh_R");
        footL = Find(body, "Foot_L");
        footR = Find(body, "Foot_R");
        if (hips != null)
            restHipsLocal = hips.localPosition;
    }

    static int LengthAxis(Transform bone, Transform end)
    {
        Vector3 along = bone.InverseTransformVector(end.position - bone.position);
        float ax = Mathf.Abs(along.x);
        float ay = Mathf.Abs(along.y);
        float az = Mathf.Abs(along.z);
        if (ay >= ax && ay >= az)
            return 1;
        if (az >= ax)
            return 2;
        return 0;
    }

    static void SetAxisScale(Transform bone, int axis, float scale)
    {
        Vector3 size = Vector3.one;
        if (axis == 0)
            size.x = scale;
        else if (axis == 1)
            size.y = scale;
        else
            size.z = scale;
        bone.localScale = size;
    }

    void HideHeadFromInside()
    {
        Shader shader = Shader.Find("HD2/AvatarHead");
        if (shader == null || body == null)
            return;

        var material = new Material(shader);
        ApplyHeadShader(body, material);
    }

    static void ApplyHeadShader(Transform root, Material material)
    {
        if (root.name == "Head" || root.name == "Neck")
        {
            var renderer = root.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        for (int i = 0; i < root.childCount; i++)
            ApplyHeadShader(root.GetChild(i), material);
    }

    static Transform Find(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    void SolveArm(Transform upper, Transform forearm, Transform hand, Transform target, float side)
    {
        if (upper == null || forearm == null || hand == null || target == null || body == null)
            return;

        float upperLength = Vector3.Distance(upper.position, forearm.position);
        float foreLength = Vector3.Distance(forearm.position, hand.position);
        if (upperLength < 0.02f || foreLength < 0.02f)
            return;

        Vector3 shoulder = upper.position;
        Vector3 toTarget = target.position - shoulder;
        float reach = upperLength + foreLength - 0.01f;
        float distance = Mathf.Clamp(toTarget.magnitude, 0.05f, reach);
        Vector3 direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : body.forward;
        Vector3 aim = direction * distance;

        Vector3 pole = shoulder + body.right * side + body.forward * -0.35f + Vector3.up * -0.15f;
        Vector3 bend = Vector3.Cross(aim, pole - shoulder);
        if (bend.sqrMagnitude < 0.0001f)
            bend = Vector3.Cross(aim, Vector3.up);
        Vector3 poleDirection = Vector3.Cross(bend, aim).normalized;

        float cosShoulder = Mathf.Clamp(
            (upperLength * upperLength + distance * distance - foreLength * foreLength) / (2f * upperLength * distance),
            -1f,
            1f);
        float sinShoulder = Mathf.Sqrt(Mathf.Max(0f, 1f - cosShoulder * cosShoulder));
        Vector3 elbow = shoulder + direction * (upperLength * cosShoulder) + poleDirection * (upperLength * sinShoulder);
        if (Vector3.Dot(elbow - shoulder, pole - shoulder) < 0f)
            elbow = shoulder + direction * (upperLength * cosShoulder) - poleDirection * (upperLength * sinShoulder);

        PointAt(upper, forearm, elbow);
        PointAt(forearm, hand, target.position - target.forward * 0.08f);
        PlaceHandOnGun(hand, target);
    }

    static void PlaceHandOnGun(Transform hand, Transform target)
    {
        // The pistol mesh is yawed 180 on the controller, so the barrel is controller
        // forward. The robot hand bone points the other way unless it is flipped.
        hand.rotation = target.rotation * Quaternion.Euler(0f, 180f, 0f);
        Vector3 grip = target.position;
        for (int i = 0; i < hand.childCount; i++)
        {
            Transform finger = hand.GetChild(i);
            if (finger.childCount == 0)
                continue;
            PointAt(finger, finger.GetChild(0), grip);
            Transform mid = finger.GetChild(0);
            if (mid.childCount > 0)
                PointAt(mid, mid.GetChild(0), grip);
        }
    }

    static void PointAt(Transform bone, Transform child, Vector3 worldPoint)
    {
        Vector3 current = child.position - bone.position;
        Vector3 desired = worldPoint - bone.position;
        if (current.sqrMagnitude < 0.0000001f || desired.sqrMagnitude < 0.0000001f)
            return;

        bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
    }
}
