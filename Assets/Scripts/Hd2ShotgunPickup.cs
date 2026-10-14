using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Brass pad with the sawed-off inside the sphere. A hand inside the sphere
/// swaps that hand's gun for the shotgun while that hand's grip is held.
/// </summary>
[ExecuteAlways]
public class Hd2ShotgunPickup : MonoBehaviour
{
    public GameObject shotgunModel;
    public float cooldown = 10f;

    static readonly Color pickupColor = new Color(0.78f, 0.52f, 0.16f, 1f);
    static readonly Vector3 equippedPosition = new Vector3(0f, 0.01f, 0.02f);
    static readonly Quaternion equippedRotation = Quaternion.Euler(0f, 180f, 0f);
    const float equippedScale = 0.85f;
    const float displayScale = 0.9f;
    static readonly Vector3 displayEuler = new Vector3(-90f, 20f, 0f);

    Transform sphere;
    Transform mini;
    LineRenderer ring;
    bool ready = true;
    float cooldownLeft;
    InputAction rightGrip;
    InputAction leftGrip;

    void OnEnable()
    {
        BuildVisuals();
        RepairSavedMaterials();
        Hd2PickupGlow.Ensure(transform, pickupColor);
        SetReady(true);
        if (!Application.isPlaying)
            return;

        rightGrip = GripAction("RightHand");
        leftGrip = GripAction("LeftHand");
    }

    public void EnsureDisplay()
    {
        if (transform.Find("Pad") == null)
            BuildVisuals();
        else
        {
            sphere = transform.Find("Sphere");
            mini = transform.Find("Sphere/Mini");
            Transform ringTransform = transform.Find("Ring");
            ring = ringTransform != null ? ringTransform.GetComponent<LineRenderer>() : null;
        }

        if (mini == null)
            BuildMini();
    }

    void OnDisable()
    {
        Dispose(ref rightGrip);
        Dispose(ref leftGrip);
    }

    static InputAction GripAction(string node)
    {
        var action = new InputAction("Hd2ShotgunPickup" + node, InputActionType.Button);
        action.AddBinding("<XRController>{" + node + "}/gripPressed");
        action.Enable();
        return action;
    }

    void Update()
    {
        if (mini == null && sphere != null && shotgunModel != null)
            BuildMini();

        var view = Camera.main;
        if (ring != null && view != null)
            ring.transform.rotation = Quaternion.LookRotation(ring.transform.position - view.transform.position);

        if (mini != null && ready)
            mini.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);

        if (!ready)
        {
            cooldownLeft -= Time.deltaTime;
            SetRing(1f - Mathf.Clamp01(cooldownLeft / Mathf.Max(0.01f, cooldown)));
            if (cooldownLeft <= 0f)
                SetReady(true);
            return;
        }

        if (!Application.isPlaying || sphere == null)
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
        if (hand.GetComponentInChildren<Hd2Shotgun>(true) != null)
            return;

        float distance = (hand.position - sphere.position).sqrMagnitude;
        if (distance > reachSqr || distance >= bestDistance)
            return;

        best = hand;
        bestDistance = distance;
    }

    static Transform FindNamed(Transform root, string handName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
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
        if (shotgunModel == null || hand.GetComponentInChildren<Hd2Shotgun>(true) != null)
            return false;

        DestroyGuns<Hd2Pistol>(hand);
        DestroyGuns<Hd2Smg>(hand);
        DestroyGuns<Hd2RocketLauncher>(hand);
        DestroyGuns<Hd2Shocks>(hand);
        DestroyGuns<Hd2LaserPistol>(hand);
        DestroyGuns<Hd2Shotgun>(hand);

        var gun = Instantiate(shotgunModel, hand);
        gun.name = "Shotgun";
        gun.transform.localPosition = equippedPosition;
        gun.transform.localRotation = equippedRotation;
        gun.transform.localScale = Vector3.one * equippedScale;
        StripColliders(gun);

        if (hand.name != "LeftHand")
            MirrorGrip(gun.transform);

        var shotgun = gun.AddComponent<Hd2Shotgun>();
        shotgun.SetHand(hand.name == "LeftHand" ? Hd2Pistol.Hand.Left : Hd2Pistol.Hand.Right);
        Transform muzzle = FindNamed(gun.transform, "Barrel");
        if (muzzle == null)
        {
            muzzle = FindNamed(gun.transform, "Muzzle");
            if (muzzle != null)
                muzzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        if (muzzle != null)
            shotgun.muzzle = muzzle;

        return true;
    }

    static void DestroyGuns<T>(Transform hand) where T : Component
    {
        T[] guns = hand.GetComponentsInChildren<T>(true);
        for (int i = 0; i < guns.Length; i++)
        {
            if (guns[i] != null)
                Destroy(guns[i].gameObject);
        }
    }

    public void RepairSavedMaterials()
    {
        EnsureMaterial(transform.Find("Pad"), pickupColor, false);
        EnsureMaterial(transform.Find("Sphere"), new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        EnsureLine(transform.Find("Ring"));
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

    void EnsureLine(Transform target)
    {
        if (target == null)
            return;
        var line = target.GetComponent<LineRenderer>();
        if (line == null || (line.sharedMaterial != null && line.sharedMaterial.shader != null))
            return;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = pickupColor;
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
            mini = transform.Find("Sphere/Mini");
            Transform ringTransform = transform.Find("Ring");
            ring = ringTransform != null ? ringTransform.GetComponent<LineRenderer>() : null;
            return;
        }

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Pad";
        pad.transform.SetParent(transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        pad.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
        RemoveCollider(pad);
        Paint(pad, pickupColor, false);

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Sphere";
        ball.transform.SetParent(transform, false);
        ball.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        ball.transform.localScale = Vector3.one * 0.42f;
        RemoveCollider(ball);
        Paint(ball, new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        sphere = ball.transform;

        var ringObject = new GameObject("Ring");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = false;
        ring.widthMultiplier = 0.025f;
        ring.positionCount = 0;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ring.startColor = ring.endColor = pickupColor;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.enabled = false;
        BuildMini();
    }

    void BuildMini()
    {
        if (sphere == null || sphere.Find("Mini") != null || shotgunModel == null)
            return;

        var display = Instantiate(shotgunModel, sphere);
        display.name = "Mini";
        display.transform.localPosition = Vector3.zero;
        display.transform.localRotation = Quaternion.Euler(displayEuler);
        display.transform.localScale = Vector3.one * displayScale;
        StripColliders(display);
        HideGrip(display.transform);
        mini = display.transform;
    }

    static void HideGrip(Transform display)
    {
        Transform[] all = display.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == "Hand" || all[i].name == "HandSkin")
                all[i].gameObject.SetActive(false);
        }
    }

    static void MirrorGrip(Transform root)
    {
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

    static void StripColliders(GameObject target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
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
        Shader shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
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

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }
}
