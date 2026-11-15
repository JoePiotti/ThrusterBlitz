using Fusion;
using UnityEngine;

/// <summary>
/// The other player's body in a networked Shootout. The owner keeps their VR rig
/// and hides this copy. Everyone else sees the robot and can shoot it.
/// </summary>
public class Hd2NetPlayer : NetworkBehaviour
{
    public GameObject bodyPrefab;
    public GameObject pistolPrefab;
    public GameObject smgPrefab;
    public GameObject shotgunPrefab;
    public GameObject laserPrefab;
    public GameObject shockPrefab;
    public GameObject rocketPrefab;
    public GameObject burstPrefab;
    public GameObject handMeshLeftPrefab;
    public GameObject handMeshRightPrefab;
    public Vector3 handMeshEulerOffsetL;
    public Vector3 handMeshEulerOffsetR;
    public Hd2Team team = Hd2Team.None;

    Hd2Health proxyHealth;
    Hd2Locomotion owner;
    Transform view;
    Transform upperL;
    Transform foreL;
    Transform handL;
    Transform upperR;
    Transform foreR;
    Transform handR;
    Transform thighL;
    Transform shinL;
    Transform thighR;
    Transform shinR;
    Quaternion restUpperL;
    Quaternion restForeL;
    Quaternion restUpperR;
    Quaternion restForeR;
    Quaternion restThighL;
    Quaternion restShinL;
    Quaternion restThighR;
    Quaternion restShinR;
    GameObject gunL;
    GameObject gunR;
    int shownL = -1;
    int shownR = -1;
    Vector3 leftPos;
    Quaternion leftRot = Quaternion.identity;
    Vector3 rightPos;
    Quaternion rightRot = Quaternion.identity;
    int leftGun;
    int rightGun;
    Vector3 lastFeet;
    float nextPose;
    float gait;
    bool sawFeet;
    bool posed;
    Transform handVisualL;
    Transform handVisualR;
    Transform remoteWristL;
    Transform remoteWristR;
    Hd2GripHand gripHandL;
    Hd2GripHand gripHandR;

    public override void Spawned()
    {
        var capsule = GetComponent<MeshRenderer>();
        if (capsule != null)
            capsule.enabled = false;

        var solid = GetComponent<CapsuleCollider>();
        if (solid != null)
        {
            solid.center = new Vector3(0f, 1f, 0f);
            solid.height = 2f;
            solid.radius = 0.35f;
        }

        team = TeamFromSpawn();
        BuildBody();

        proxyHealth = gameObject.AddComponent<Hd2Health>();
        proxyHealth.autoRespawn = false;
        proxyHealth.Configure(60f, true);

        if (Object.HasInputAuthority)
        {
            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }

            Renderer[] views = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null)
                    views[i].enabled = false;
            }

            ClaimLocalPlayer();
            Hd2Session.NoteLocalBody();
        }
    }

    public void RelayHit(float amount, Hd2Team killerTeam)
    {
        if (amount <= 0f || Object == null || !Object.IsValid || Object.HasInputAuthority)
            return;
        RPC_TakeHit(amount, (int)killerTeam);
    }

    public override void Render()
    {
        if (Object.HasInputAuthority)
        {
            SendPose();
            return;
        }

        if (posed)
            PoseRemote();

        if (proxyHealth != null && proxyHealth.IsDead && sawFeet)
        {
            if ((transform.position - lastFeet).sqrMagnitude > 9f)
                proxyHealth.HealToFull();
        }

        lastFeet = transform.position;
        sawFeet = true;
    }

    void SendPose()
    {
        if (Time.unscaledTime < nextPose)
            return;
        nextPose = Time.unscaledTime + 0.05f;
        if (owner == null)
            owner = UnityEngine.Object.FindFirstObjectByType<Hd2Locomotion>();
        if (owner == null)
            return;
        if (view == null)
        {
            Camera[] cameras = owner.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == null)
                    continue;
                if (cameras[i].GetComponentInParent<Hd2NetPlayer>() != null)
                    continue;
                view = cameras[i].transform;
                break;
            }

            if (view == null)
                view = owner.transform;
        }

        Transform left = owner.leftHand;
        Transform right = owner.rightHand;
        Vector3 feet = view.position;
        feet.y -= 1.6f;
        RPC_SubmitPose(
            feet,
            view.eulerAngles.y,
            left != null ? left.position : feet,
            left != null ? left.rotation : Quaternion.identity,
            right != null ? right.position : feet,
            right != null ? right.rotation : Quaternion.identity,
            GunId(left),
            GunId(right));
    }

    static int GunId(Transform hand)
    {
        if (hand == null)
            return 0;
        if (hand.GetComponentInChildren<Hd2DropShield>(false) != null)
            return 8;
        if (hand.GetComponentInChildren<Hd2RocketLauncher>(false) != null)
            return 6;
        if (hand.GetComponentInChildren<Hd2Shotgun>(false) != null)
            return 3;
        if (hand.GetComponentInChildren<Hd2Shocks>(false) != null)
            return 5;
        if (hand.GetComponentInChildren<Hd2LaserPistol>(false) != null)
            return 4;
        if (hand.GetComponentInChildren<Hd2BurstPistol>(false) != null)
            return 7;
        if (hand.GetComponentInChildren<Hd2Smg>(false) != null)
            return 2;
        if (hand.GetComponentInChildren<Hd2Pistol>(false) != null)
            return 1;
        return 0;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    void RPC_SubmitPose(
        Vector3 feet,
        float yaw,
        Vector3 leftPosition,
        Quaternion leftRotation,
        Vector3 rightPosition,
        Quaternion rightRotation,
        int leftWeapon,
        int rightWeapon)
    {
        if (Object.HasInputAuthority)
            return;

        transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        leftPos = leftPosition;
        leftRot = leftRotation;
        rightPos = rightPosition;
        rightRot = rightRotation;
        leftGun = leftWeapon;
        rightGun = rightWeapon;
        posed = true;
    }

    void PoseRemote()
    {
        Vector3 moved = transform.position - lastFeet;
        moved.y = 0f;
        float speed = sawFeet ? moved.magnitude / Mathf.Max(0.01f, Time.deltaTime) : 0f;
        if (speed > 0.2f)
            gait += speed * 1.4f * Time.deltaTime;
        float step = speed > 0.2f ? Mathf.Sin(gait) : 0f;
        SwingLeg(thighL, shinL, restThighL, restShinL, step);
        SwingLeg(thighR, shinR, restThighR, restShinR, -step);
        AimArm(upperL, foreL, handL, restUpperL, restForeL, leftPos);
        AimArm(upperR, foreR, handR, restUpperR, restForeR, rightPos);
        ShowGun(ref gunL, ref shownL, leftPos, leftRot, leftGun, ref remoteWristL, false);
        ShowGun(ref gunR, ref shownR, rightPos, rightRot, rightGun, ref remoteWristR, true);
        RefineRemoteArm(true);
        RefineRemoteArm(false);
        PoseGripFingers(gripHandL, gunL);
        PoseGripFingers(gripHandR, gunR);
    }

    // Baked guns put the hand exactly on the Wrist marker; the fingers follow the markers.
    static void PoseGripFingers(Hd2GripHand grip, GameObject gun)
    {
        if (grip == null)
            return;

        grip.Track(gun != null ? gun.transform : null);
        if (gun == null)
            return;

        grip.PinHand();
        grip.ApplyFingers();
    }

    void CacheLimbs(Transform robot)
    {
        upperL = Find(robot, "Arm.L");
        if (upperL == null)
            upperL = Find(robot, "UpperArm_L");
        foreL = Find(robot, "Arm.L.002");
        if (foreL == null)
            foreL = Find(robot, "Forearm_L");
        handL = Find(robot, "Hand.L");
        if (handL == null)
            handL = Find(robot, "Hand_L");
        upperR = Find(robot, "Arm.R");
        if (upperR == null)
            upperR = Find(robot, "UpperArm_R");
        foreR = Find(robot, "Arm.R.002");
        if (foreR == null)
            foreR = Find(robot, "Forearm_R");
        handR = Find(robot, "Hand.R");
        if (handR == null)
            handR = Find(robot, "Hand_R");
        thighL = Find(robot, "Thigh.L");
        if (thighL == null)
            thighL = Find(robot, "Thigh_L");
        shinL = Find(robot, "Thigh.L.001");
        if (shinL == null)
            shinL = Find(robot, "Shin_L");
        thighR = Find(robot, "Thigh.R");
        if (thighR == null)
            thighR = Find(robot, "Thigh_R");
        shinR = Find(robot, "Thigh.R.001");
        if (shinR == null)
            shinR = Find(robot, "Shin_R");
        restUpperL = Local(upperL);
        restForeL = Local(foreL);
        restUpperR = Local(upperR);
        restForeR = Local(foreR);
        restThighL = Local(thighL);
        restShinL = Local(shinL);
        restThighR = Local(thighR);
        restShinR = Local(shinR);
        if (handL != null && handR != null)
        {
            gripHandL = new Hd2GripHand(handL, false);
            gripHandR = new Hd2GripHand(handR, true);
            Hd2GripHand.BuildMirror(robot, gripHandL, gripHandR);
        }

        EnsureRemoteHandVisuals();
    }

    void EnsureRemoteHandVisuals()
    {
        if (handMeshLeftPrefab != null && handL != null && handVisualL == null)
        {
            GameObject go = Instantiate(handMeshLeftPrefab, handL);
            go.name = "HandMesh";
            handVisualL = go.transform;
        }

        if (handMeshRightPrefab != null && handR != null && handVisualR == null)
        {
            GameObject go = Instantiate(handMeshRightPrefab, handR);
            go.name = "HandMesh";
            handVisualR = go.transform;
        }
    }

    void RefineRemoteArm(bool left)
    {
        int gunId = left ? leftGun : rightGun;
        GameObject gun = left ? gunL : gunR;
        Transform wrist = left ? remoteWristL : remoteWristR;
        Transform upper = left ? upperL : upperR;
        Transform fore = left ? foreL : foreR;
        Transform hand = left ? handL : handR;
        Transform visual = left ? handVisualL : handVisualR;
        Quaternion restUpper = left ? restUpperL : restUpperR;
        Quaternion restFore = left ? restForeL : restForeR;
        Vector3 offset = left ? handMeshEulerOffsetL : handMeshEulerOffsetR;

        if (gunId == 0 || gun == null || wrist == null)
        {
            RestRemoteHandMesh(visual);
            return;
        }

        AimArm(upper, fore, hand, restUpper, restFore, wrist.position);
        if (visual != null && hand != null)
            visual.SetPositionAndRotation(wrist.position, wrist.rotation * Quaternion.Euler(offset));
    }

    static void RestRemoteHandMesh(Transform visual)
    {
        if (visual == null)
            return;
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
    }

    static Quaternion Local(Transform bone)
    {
        return bone != null ? bone.localRotation : Quaternion.identity;
    }

    static void SwingLeg(Transform thigh, Transform shin, Quaternion thighRest, Quaternion shinRest, float step)
    {
        if (thigh != null)
            thigh.localRotation = thighRest * Quaternion.Euler(step * 28f, 0f, 0f);
        if (shin != null)
            shin.localRotation = shinRest * Quaternion.Euler(Mathf.Max(0f, -step) * 35f, 0f, 0f);
    }

    static void AimArm(
        Transform upper,
        Transform fore,
        Transform hand,
        Quaternion upperRest,
        Quaternion foreRest,
        Vector3 target)
    {
        if (upper != null)
            upper.localRotation = upperRest;
        if (fore != null)
            fore.localRotation = foreRest;
        if (upper == null)
            return;

        Vector3 from = (fore != null ? fore.position : hand != null ? hand.position : upper.position) - upper.position;
        Vector3 to = target - upper.position;
        if (from.sqrMagnitude > 0.0001f && to.sqrMagnitude > 0.0001f)
            upper.rotation = Quaternion.FromToRotation(from.normalized, to.normalized) * upper.rotation;
        if (fore == null || hand == null)
            return;

        Vector3 fromHand = hand.position - fore.position;
        Vector3 toHand = target - fore.position;
        if (fromHand.sqrMagnitude > 0.0001f && toHand.sqrMagnitude > 0.0001f)
            fore.rotation = Quaternion.FromToRotation(fromHand.normalized, toHand.normalized) * fore.rotation;
    }

    void ShowGun(ref GameObject shown, ref int shownId, Vector3 position, Quaternion rotation, int gun, ref Transform wristCache, bool right)
    {
        if (shownId != gun)
        {
            if (shown != null)
                Destroy(shown);
            shown = null;
            shownId = gun;
            wristCache = null;
            GameObject prefab = GunPrefab(gun);
            if (prefab != null)
            {
                shown = Instantiate(prefab);
                shown.name = "RemoteGun";
                MonoBehaviour[] parts = shown.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] != null)
                        parts[i].enabled = false;
                }

                Collider[] cols = shown.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                        cols[i].enabled = false;
                }

                // Grip prefabs are authored for the left hand. Mirror them for the right,
                // the same way the local pickup does, so the finger markers fit that hand.
                if (right && Hd2GripFingers.Find(shown.transform, "GripFingers") != null)
                    Hd2ShotgunPickup.MirrorGrip(shown.transform);
                wristCache = FindWrist(shown.transform);
            }
        }

        if (shown == null)
            return;
        shown.transform.SetPositionAndRotation(position, rotation);
    }

    static Transform FindWrist(Transform gun)
    {
        if (gun == null)
            return null;
        for (int i = 0; i < gun.childCount; i++)
        {
            Transform child = gun.GetChild(i);
            if (child.name == "Wrist")
                return child;
            Transform nested = FindWrist(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    GameObject GunPrefab(int gun)
    {
        switch (gun)
        {
            case 1: return pistolPrefab;
            case 2: return smgPrefab;
            case 3: return shotgunPrefab;
            case 4: return laserPrefab;
            case 5: return shockPrefab;
            case 6: return rocketPrefab;
            case 7: return burstPrefab;
            case 8: return pistolPrefab;
            default: return pistolPrefab;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.InputAuthority)]
    void RPC_TakeHit(float amount, int killerTeam)
    {
        Hd2Locomotion player = UnityEngine.Object.FindFirstObjectByType<Hd2Locomotion>();
        if (player == null)
            return;
        Hd2Health life = player.GetComponent<Hd2Health>();
        if (life == null || life.IsDead)
            return;

        life.ApplyHit(amount, false, true);
        Hd2Team side = (Hd2Team)killerTeam;
        if (life.IsDead && Hd2Shootout.Present && side != Hd2Team.None && side != player.team)
            Hd2Shootout.NoteKill(side);
    }

    void ClaimLocalPlayer()
    {
        Hd2Locomotion player = UnityEngine.Object.FindFirstObjectByType<Hd2Locomotion>();
        if (player == null)
            return;

        player.team = team;
        player.teamColor = Hd2Locomotion.ArmorOf(team);
        Hd2Body avatar = player.GetComponent<Hd2Body>();
        if (avatar != null)
            avatar.SetArmorColor(player.teamColor);
        player.ReturnToBase();
    }

    void BuildBody()
    {
        if (bodyPrefab == null || transform.Find("Body") != null)
            return;

        GameObject robot = Instantiate(bodyPrefab, transform);
        robot.name = "Body";
        robot.transform.localPosition = Vector3.zero;
        robot.transform.localRotation = Quaternion.identity;
        robot.transform.localScale = Vector3.one;
        Camera[] cameras = robot.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null)
                Destroy(cameras[i]);
        }

        AudioListener[] ears = robot.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < ears.Length; i++)
        {
            if (ears[i] != null)
                Destroy(ears[i]);
        }

        Stand(robot.transform);
        Fit(robot.transform);
        Tint(robot.transform, Hd2Locomotion.ArmorOf(team));
        Hd2PracticeBot.FitHitboxes(robot.transform);
        CacheLimbs(robot.transform);
    }

    static void Stand(Transform robot)
    {
        Transform head = Find(robot, "Head");
        Transform hips = Find(robot, "Hips");
        if (head == null || hips == null)
            return;
        if (head.position.y > hips.position.y + 0.25f)
            return;
        robot.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        if (head.position.y > hips.position.y + 0.25f)
            return;
        robot.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    static void Fit(Transform robot)
    {
        Transform head = Find(robot, "Head");
        if (head == null)
            return;
        float height = head.position.y - robot.parent.position.y;
        if (height < 0.2f)
            return;
        robot.localScale = Vector3.one * (1.8f / height);
    }

    static void Tint(Transform root, Color color)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            Material[] materials = renderer.materials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];
                if (material == null)
                    continue;
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", color);
            }
        }
    }

    Hd2Team TeamFromSpawn()
    {
        var points = UnityEngine.Object.FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Hd2SpawnPoint nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null || points[i].team == null)
                continue;
            float gap = (points[i].transform.position - transform.position).sqrMagnitude;
            if (gap >= best)
                continue;
            best = gap;
            nearest = points[i];
        }

        if (nearest == null || nearest.team == null)
            return Hd2Team.Blue;
        return nearest.team.Value;
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
}
