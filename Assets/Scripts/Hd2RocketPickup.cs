using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orange pad with the rocket launcher inside the sphere. A hand inside the sphere
/// swaps that hand's gun for the launcher only while that hand's grip is held.
/// Either hand can take it. A hand that already holds one does nothing.
/// </summary>
[ExecuteAlways]
public class Hd2RocketPickup : MonoBehaviour
{
    public GameObject rocketModel;
    public float cooldown = 10f;

    static readonly Color pickupColor = new Color(0.95f, 0.42f, 0.1f, 1f);
    static readonly Vector3 equippedPosition = new Vector3(0f, 0.01f, 0.05f);
    static readonly Vector3 equippedEuler = new Vector3(0f, 90f, 90f);
    const float equippedScale = 0.27f;
    const float displayFit = 0.42f;

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
        var action = new InputAction("Hd2RocketPickup" + node, InputActionType.Button);
        action.AddBinding("<XRController>{" + node + "}/gripPressed");
        action.Enable();
        return action;
    }

    void Update()
    {
        if (mini == null && rocketModel != null && sphere != null)
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

        if (!Application.isPlaying || sphere == null || rocketModel == null)
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
        if (hand.GetComponentInChildren<Hd2RocketLauncher>(true) != null)
            return;

        float distance = (hand.position - sphere.position).sqrMagnitude;
        if (distance > reachSqr || distance >= bestDistance)
            return;

        best = hand;
        bestDistance = distance;
    }

    static Transform FindNamed(Transform root, string name)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == name)
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
        if (rocketModel == null || hand.GetComponentInChildren<Hd2RocketLauncher>(true) != null)
            return false;

        DestroyAll<Hd2Pistol>(hand);
        DestroyAll<Hd2Smg>(hand);
        DestroyAll<Hd2RocketLauncher>(hand);

        var gun = Instantiate(rocketModel, hand);
        gun.name = "RocketLauncher";
        gun.transform.localPosition = equippedPosition;
        gun.transform.localRotation = Quaternion.Euler(equippedEuler);
        if (hand.name == "LeftHand")
            gun.transform.localRotation *= Quaternion.AngleAxis(180f, Vector3.up);
        gun.transform.localScale = Vector3.one * equippedScale;
        StripColliders(gun);

        var launcher = gun.GetComponent<Hd2RocketLauncher>();
        if (launcher == null)
            launcher = gun.AddComponent<Hd2RocketLauncher>();
        launcher.SetHand(hand.name == "LeftHand" ? Hd2Pistol.Hand.Left : Hd2Pistol.Hand.Right);
        if (hand.name != "LeftHand")
            MirrorAcrossLocalX(gun.transform);
        return true;
    }

    static void MirrorAcrossLocalX(Transform root)
    {
        SkinnedMeshRenderer[] skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var worlds = new Vector3[skins.Length][];
        var normalWorlds = new Vector3[skins.Length][];
        for (int s = 0; s < skins.Length; s++)
        {
            Mesh source = skins[s].sharedMesh;
            if (source == null)
                continue;
            Vector3[] verts = source.vertices;
            Vector3[] sourceNormals = source.normals;
            worlds[s] = new Vector3[verts.Length];
            normalWorlds[s] = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                worlds[s][i] = skins[s].transform.TransformPoint(verts[i]);
                Vector3 normal = sourceNormals != null && i < sourceNormals.Length ? sourceNormals[i] : Vector3.up;
                normalWorlds[s][i] = skins[s].transform.TransformDirection(normal);
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

        for (int s = 0; s < skins.Length; s++)
        {
            if (worlds[s] == null || skins[s].sharedMesh == null)
                continue;

            Mesh mesh = Instantiate(skins[s].sharedMesh);
            mesh.name = skins[s].sharedMesh.name + " Right";
            Vector3[] verts = new Vector3[worlds[s].Length];
            Vector3[] normals = new Vector3[worlds[s].Length];
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = skins[s].transform.InverseTransformPoint(MirrorPoint(root, worlds[s][i]));
                normals[i] = skins[s].transform.InverseTransformDirection(MirrorDirection(root, normalWorlds[s][i])).normalized;
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
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                    continue;
                bindposes[i] = bones[i].worldToLocalMatrix * skins[s].transform.localToWorldMatrix;
            }

            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
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

    static void DestroyAll<T>(Transform hand) where T : Component
    {
        T[] found = hand.GetComponentsInChildren<T>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                Destroy(found[i].gameObject);
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
            ring = transform.Find("Ring") != null ? transform.Find("Ring").GetComponent<LineRenderer>() : null;
            if (mini != null)
                PrepareDisplay(mini.gameObject);
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
        if (rocketModel == null || sphere == null || sphere.Find("Mini") != null)
            return;

        var display = Instantiate(rocketModel, sphere);
        display.name = "Mini";
        display.transform.localPosition = Vector3.zero;
        display.transform.localRotation = Quaternion.Euler(-20f, 30f, 0f);
        display.transform.localScale = Vector3.one;
        StripColliders(display);
        PrepareDisplay(display);
        mini = display.transform;
    }

    static void PrepareDisplay(GameObject display)
    {
        HideGrip(display.transform);
        FitDisplay(display);
    }

    static void HideGrip(Transform display)
    {
        Transform[] all = display.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == display)
                continue;
            if (all[i].name == "Hand" || all[i].name == "hand" || all[i].name == "HandSkin")
                all[i].gameObject.SetActive(false);
        }
    }

    static void FitDisplay(GameObject display)
    {
        Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds bounds = new Bounds(display.transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled || !renderers[i].gameObject.activeInHierarchy)
                continue;
            if (!any)
            {
                bounds = renderers[i].bounds;
                any = true;
            }
            else
                bounds.Encapsulate(renderers[i].bounds);
        }

        if (!any)
            return;

        float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (size < 0.001f)
            return;

        float parent = display.transform.parent != null ? display.transform.parent.lossyScale.x : 1f;
        float world = size;
        float want = displayFit * Mathf.Max(0.01f, parent);
        // bounds are already in world space, so scale the local size toward the sphere.
        display.transform.localScale = Vector3.one * (want / world);
        Vector3 shift = display.transform.position - bounds.center;
        display.transform.position += shift * (want / world);
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
