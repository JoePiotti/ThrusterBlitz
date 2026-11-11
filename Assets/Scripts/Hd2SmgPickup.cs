using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pink pad with the SMG icon inside the sphere. A hand inside the sphere
/// swaps that hand's gun for an SMG only while that hand's grip is held.
/// The sphere then hides for the cooldown. A hand that already holds an SMG does nothing.
/// </summary>
[ExecuteAlways]
public class Hd2SmgPickup : MonoBehaviour
{
    public Texture2D icon;
    public GameObject smgModel;
    public GameObject grip;
    public GameObject gripLeft;
    public GameObject gripRight;
    public float cooldown = 10f;

    static readonly Color pickupColor = new Color(0.976f, 0.839f, 0.816f, 1f);
    static readonly Vector3 equippedPosition = new Vector3(0f, 0.01f, 0.05f);
    static readonly Vector3 equippedEuler = Vector3.zero;
    static readonly Vector3 gripEquippedEuler = new Vector3(0f, 0f, 180f);

    Transform sphere;
    Transform iconQuad;
    LineRenderer ring;
    bool ready = true;
    float cooldownLeft;
    InputAction rightGrip;
    InputAction leftGrip;

    void OnEnable()
    {
        BuildVisuals();
        Hd2PickupGlow.Ensure(transform, pickupColor);
        SetReady(true);
        if (!Application.isPlaying)
            return;

        rightGrip = GripAction("RightHand");
        leftGrip = GripAction("LeftHand");
    }

    void OnDisable()
    {
        Dispose(ref rightGrip);
        Dispose(ref leftGrip);
    }

    static InputAction GripAction(string node)
    {
        var action = new InputAction("Hd2SmgPickup" + node, InputActionType.Button);
        action.AddBinding("<XRController>{" + node + "}/gripPressed");
        action.Enable();
        return action;
    }

    void LateUpdate()
    {
        Hd2PickupIcon.Face(iconQuad, ring != null ? ring.transform : null);
    }

    void Update()
    {
        if (!ready)
        {
            cooldownLeft -= Time.deltaTime;
            SetRing(1f - Mathf.Clamp01(cooldownLeft / Mathf.Max(0.01f, cooldown)));
            if (cooldownLeft <= 0f)
                SetReady(true);
            return;
        }

        if (!Application.isPlaying || sphere == null || smgModel == null)
            return;

        var player = FindFirstObjectByType<Hd2Locomotion>();
        if (player == null)
            return;

        Transform hand = HandInside(player);
        if (hand != null)
            TryCollect(hand);
    }

    Transform HandInside(Hd2Locomotion player)
    {
        float radius = sphere.lossyScale.x * 0.5f;
        float reach = radius * radius;
        Transform best = null;
        float bestDistance = float.MaxValue;
        Consider(player.transform, "RightHand", ref best, ref bestDistance, reach);
        Consider(player.transform, "LeftHand", ref best, ref bestDistance, reach);
        return best;
    }

    void Consider(Transform root, string handName, ref Transform best, ref float bestDistance, float reachSqr)
    {
        Transform hand = FindNamed(root, handName);
        if (hand == null || !GripHeld(handName))
            return;
        if (hand.GetComponentInChildren<Hd2Smg>(true) != null)
            return;

        float distance = (hand.position - sphere.position).sqrMagnitude;
        if (distance > reachSqr || distance >= bestDistance)
            return;

        best = hand;
        bestDistance = distance;
    }

    static Transform FindNamed(Transform root, string handName)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == handName)
                return transforms[i];
        }

        return null;
    }

    bool GripHeld(string handName)
    {
        InputAction action = handName == "LeftHand" ? leftGrip : rightGrip;
        return action != null && action.IsPressed();
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }

    public bool BotTake(Transform hand)
    {
        if (!ready || hand == null)
            return false;
        TryCollect(hand);
        return !ready;
    }

    void TryCollect(Transform hand)
    {
        if (!Equip(hand))
            return;

        ready = false;
        cooldownLeft = cooldown;
        if (sphere != null)
            sphere.gameObject.SetActive(false);
        if (ring != null)
            ring.enabled = true;
        SetRing(0f);
    }

    bool Equip(Transform hand)
    {
        bool prefabGrip = grip != null;
        bool gripped = prefabGrip || (hand.name == "LeftHand" ? gripLeft != null : gripRight != null);
        GameObject equipped = prefabGrip ? grip : hand.name == "LeftHand" ? gripLeft : gripRight;
        if (equipped == null)
            equipped = smgModel;
        if (equipped == null || hand.GetComponentInChildren<Hd2Smg>(true) != null)
            return false;

        var pistols = hand.GetComponentsInChildren<Hd2Pistol>(true);
        for (int i = 0; i < pistols.Length; i++)
        {
            if (pistols[i] != null)
                Destroy(pistols[i].gameObject);
        }

        var rockets = hand.GetComponentsInChildren<Hd2RocketLauncher>(true);
        for (int i = 0; i < rockets.Length; i++)
        {
            if (rockets[i] != null)
                Destroy(rockets[i].gameObject);
        }

        var shocks = hand.GetComponentsInChildren<Hd2Shocks>(true);
        for (int i = 0; i < shocks.Length; i++)
        {
            if (shocks[i] != null)
                Destroy(shocks[i].gameObject);
        }

        var lasers = hand.GetComponentsInChildren<Hd2LaserPistol>(true);
        for (int i = 0; i < lasers.Length; i++)
        {
            if (lasers[i] != null)
                Destroy(lasers[i].gameObject);
        }

        var shotguns = hand.GetComponentsInChildren<Hd2Shotgun>(true);
        for (int i = 0; i < shotguns.Length; i++)
        {
            if (shotguns[i] != null)
                Destroy(shotguns[i].gameObject);
        }

        var bursts = hand.GetComponentsInChildren<Hd2BurstPistol>(true);
        for (int i = 0; i < bursts.Length; i++)
        {
            if (bursts[i] != null)
                Destroy(bursts[i].gameObject);
        }

        var gun = Instantiate(equipped, hand);
        gun.name = "Smg";
        gun.transform.localPosition = equippedPosition;
        gun.transform.localRotation = Quaternion.Euler(prefabGrip || !gripped ? equippedEuler : gripEquippedEuler);
        StripColliders(gun);
        if (prefabGrip && hand.name != "LeftHand")
            MirrorGrip(gun.transform);

        var smg = gun.AddComponent<Hd2Smg>();
        smg.SetHand(hand.name == "LeftHand" ? Hd2Pistol.Hand.Left : Hd2Pistol.Hand.Right);
        var muzzle = gun.transform.Find("Muzzle");
        if (muzzle != null)
        {
            if (prefabGrip || !gripped)
            {
                muzzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                float side = hand.name == "RightHand" ? -1f : 1f;
                muzzle.localPosition = new Vector3(0.016f * side, 0.43f, 0.21f);
                muzzle.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }

            smg.BindMuzzle(muzzle);
        }

        Hd2Pistol.PulseEquip(hand);
        return true;
    }

    public void RepairSavedMaterials()
    {
        EnsureMaterial(transform.Find("Pad"), pickupColor, false);
        EnsureMaterial(transform.Find("Sphere"), new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        EnsureLine(transform.Find("Ring"), pickupColor);
        Transform iconTransform = transform.Find("Sphere/Icon");
        if (iconTransform == null)
            return;

        var renderer = iconTransform.GetComponent<Renderer>();
        if (renderer != null && (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null))
        {
            var iconMaterial = new Material(Shader.Find("HD2/PickupIcon"));
            if (icon != null)
                iconMaterial.mainTexture = icon;
            renderer.sharedMaterial = iconMaterial;
        }
    }

    static void EnsureMaterial(Transform target, Color color, bool transparent)
    {
        if (target == null)
            return;
        var renderer = target.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null)
            return;
        Paint(target.gameObject, color, transparent);
    }

    static void EnsureLine(Transform target, Color color)
    {
        if (target == null)
            return;
        var line = target.GetComponent<LineRenderer>();
        if (line == null || (line.sharedMaterial != null && line.sharedMaterial.shader != null))
            return;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = color;
    }

    void SetReady(bool value)
    {
        ready = value;
        if (sphere != null)
            sphere.gameObject.SetActive(value);
        if (ring != null)
            ring.enabled = !value;
    }

    void BuildVisuals()
    {
        if (transform.Find("Pad") != null)
        {
            sphere = transform.Find("Sphere");
            iconQuad = transform.Find("Sphere/Icon");
            ring = transform.Find("Ring") != null ? transform.Find("Ring").GetComponent<LineRenderer>() : null;
            if (iconQuad == null)
                BuildIcon();
            return;
        }

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Pad";
        pad.transform.SetParent(transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.015f, 0f);
        pad.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
        RemoveCollider(pad);
        Paint(pad, pickupColor, false);

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Sphere";
        ball.transform.SetParent(transform, false);
        ball.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        ball.transform.localScale = Vector3.one * 0.7f;
        RemoveCollider(ball);
        Paint(ball, new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        sphere = ball.transform;

        var ringObject = new GameObject("Ring");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        ringObject.transform.localScale = Vector3.one * 0.7f;
        ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = false;
        ring.widthMultiplier = 0.025f;
        ring.positionCount = 0;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ring.startColor = ring.endColor = pickupColor;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.enabled = false;

        BuildIcon();
    }

    void BuildIcon()
    {
        if (sphere == null || sphere.Find("Icon") != null)
            return;

        var iconObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        iconObject.name = "Icon";
        iconObject.transform.SetParent(sphere, false);
        iconObject.transform.localPosition = Vector3.zero;
        iconObject.transform.localScale = Vector3.one * 0.48f;
        RemoveCollider(iconObject);
        var iconMaterial = new Material(Shader.Find("HD2/PickupIcon"));
        if (icon != null)
            iconMaterial.mainTexture = icon;
        iconObject.GetComponent<Renderer>().sharedMaterial = iconMaterial;
        iconQuad = iconObject.transform;
    }

    void SetRing(float amount)
    {
        if (ring == null)
            return;

        const int segments = 40;
        int count = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Clamp01(amount)));
        ring.positionCount = count;
        float radius = 0.28f;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : i / (float)(segments - 1);
            float angle = t * Mathf.PI * 2f - Mathf.PI * 0.5f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    static void MirrorGrip(Transform root)
    {
        SkinnedMeshRenderer[] skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var skinWorlds = new Vector3[skins.Length][];
        var skinNormalWorlds = new Vector3[skins.Length][];
        for (int s = 0; s < skins.Length; s++)
        {
            Mesh source = skins[s].sharedMesh;
            if (source == null || !source.isReadable)
                continue;
            Vector3[] verts = source.vertices;
            Vector3[] sourceNormals = source.normals;
            skinWorlds[s] = new Vector3[verts.Length];
            skinNormalWorlds[s] = new Vector3[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                skinWorlds[s][v] = skins[s].transform.TransformPoint(verts[v]);
                Vector3 normal = sourceNormals != null && v < sourceNormals.Length ? sourceNormals[v] : Vector3.up;
                skinNormalWorlds[s][v] = skins[s].transform.TransformDirection(normal);
            }
        }

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        var worlds = new Vector3[filters.Length][];
        var normalWorlds = new Vector3[filters.Length][];
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh source = filters[i].sharedMesh;
            if (source == null || !source.isReadable)
                continue;

            Vector3[] verts = source.vertices;
            Vector3[] sourceNormals = source.normals;
            worlds[i] = new Vector3[verts.Length];
            normalWorlds[i] = new Vector3[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                worlds[i][v] = filters[i].transform.TransformPoint(verts[v]);
                Vector3 normal = sourceNormals != null && v < sourceNormals.Length ? sourceNormals[v] : Vector3.up;
                normalWorlds[i][v] = filters[i].transform.TransformDirection(normal);
            }
        }

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        var mirroredPos = new Vector3[all.Length];
        var mirroredRot = new Quaternion[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            Vector3 local = root.InverseTransformPoint(all[i].position);
            local.x = -local.x;
            mirroredPos[i] = root.TransformPoint(local);
            Quaternion localRot = Quaternion.Inverse(root.rotation) * all[i].rotation;
            localRot = new Quaternion(localRot.x, -localRot.y, -localRot.z, localRot.w);
            mirroredRot[i] = root.rotation * localRot;
        }

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == root)
                continue;
            all[i].SetPositionAndRotation(mirroredPos[i], mirroredRot[i]);
        }

        for (int i = 0; i < filters.Length; i++)
        {
            if (worlds[i] == null || filters[i].sharedMesh == null)
                continue;

            Mesh mesh = Object.Instantiate(filters[i].sharedMesh);
            mesh.name = filters[i].sharedMesh.name + " Right";
            Vector3[] verts = new Vector3[worlds[i].Length];
            Vector3[] normals = new Vector3[worlds[i].Length];
            for (int v = 0; v < verts.Length; v++)
            {
                verts[v] = filters[i].transform.InverseTransformPoint(MirrorPoint(root, worlds[i][v]));
                normals[v] = filters[i].transform.InverseTransformDirection(MirrorDirection(root, normalWorlds[i][v])).normalized;
            }

            mesh.vertices = verts;
            mesh.normals = normals;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] tris = mesh.GetTriangles(sub);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int swap = tris[t + 1];
                    tris[t + 1] = tris[t + 2];
                    tris[t + 2] = swap;
                }

                mesh.SetTriangles(tris, sub);
            }

            mesh.RecalculateBounds();
            filters[i].sharedMesh = mesh;
        }

        for (int s = 0; s < skins.Length; s++)
        {
            if (skinWorlds[s] == null || skins[s].sharedMesh == null)
                continue;

            Mesh mesh = Object.Instantiate(skins[s].sharedMesh);
            mesh.name = skins[s].sharedMesh.name + " Right";
            Vector3[] verts = new Vector3[skinWorlds[s].Length];
            Vector3[] normals = new Vector3[skinWorlds[s].Length];
            for (int v = 0; v < verts.Length; v++)
            {
                verts[v] = skins[s].transform.InverseTransformPoint(MirrorPoint(root, skinWorlds[s][v]));
                normals[v] = skins[s].transform.InverseTransformDirection(MirrorDirection(root, skinNormalWorlds[s][v])).normalized;
            }

            mesh.vertices = verts;
            mesh.normals = normals;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] tris = mesh.GetTriangles(sub);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int swap = tris[t + 1];
                    tris[t + 1] = tris[t + 2];
                    tris[t + 2] = swap;
                }

                mesh.SetTriangles(tris, sub);
            }

            Transform[] bones = skins[s].bones;
            var bindposes = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] == null)
                    continue;
                bindposes[b] = bones[b].worldToLocalMatrix * skins[s].transform.localToWorldMatrix;
            }

            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            skins[s].sharedMesh = mesh;
        }
    }

    static Vector3 MirrorPoint(Transform root, Vector3 world)
    {
        Vector3 local = root.InverseTransformPoint(world);
        local.x = -local.x;
        return root.TransformPoint(local);
    }

    static Vector3 MirrorDirection(Transform root, Vector3 world)
    {
        Vector3 local = root.InverseTransformDirection(world);
        local.x = -local.x;
        return root.TransformDirection(local);
    }

    static void StripColliders(GameObject target)
    {
        var colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            RemoveCollider(colliders[i].gameObject);
    }

    static void RemoveCollider(GameObject target)
    {
        var collider = target.GetComponent<Collider>();
        if (collider == null)
            return;
        if (Application.isPlaying)
            Destroy(collider);
        else
            DestroyImmediate(collider);
    }

    static void Paint(GameObject target, Color color, bool transparent)
    {
        var shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var material = new Material(shader);
        material.color = color;
        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 3000;
        }

        target.GetComponent<Renderer>().sharedMaterial = material;
    }
}
