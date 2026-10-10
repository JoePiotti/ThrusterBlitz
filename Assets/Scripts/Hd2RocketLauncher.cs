using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One-handed laser-guided rocket launcher. A slight trigger pull shows the laser.
/// A full pull fires one rocket, which steers toward the live dot.
/// Releasing the trigger turns the laser off and leaves the rocket on its last point.
/// </summary>
public class Hd2RocketLauncher : MonoBehaviour
{
    [Header("Hand")]
    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    [Header("Trigger")]
    [Tooltip("Quest trigger axis. A rest or tiny touch stays below this and does not light the laser.")]
    public float laserTriggerThreshold = 0.2f;
    [Tooltip("Full pull. Must stay clearly past the laser threshold.")]
    public float fireTriggerThreshold = 0.9f;

    [Header("Flight")]
    [Tooltip("Speed as the rocket leaves the tube. It then accelerates up to maxSpeed.")]
    public float launchSpeed = 8f;
    [Tooltip("Acceleration as the rocket leaves the tube.")]
    public float acceleration = 20f;
    [Tooltip("Extra acceleration added for each second the rocket has been flying.")]
    public float accelerationGain = 30f;
    [Tooltip("Placeholder until a headset pass.")]
    public float maxSpeed = 60f;
    public float minTurnRadiusMeters = 10f;
    public float turnRadiusMeters = 20f;
    public float maxFlightSeconds = 6f;
    [Tooltip("How long a fresh rocket takes to slide from the base into the tube, after the reload delay.")]
    public float chamberSeconds = 0.6f;
    [Tooltip("Minimum time between shots.")]
    public float shotInterval = 1.75f;

    [Header("Explosion")]
    public float innerRadiusMeters = 1.5f;
    public float innerDamage = 60f;
    public float outerRadiusMeters = 3f;
    public float outerDamage = 36f;
    [Tooltip("Splash hits the shooter too. Turn this off to spare the owner.")]
    public bool damageOwner = true;

    public Transform muzzle;
    Transform laserSight;

    InputAction triggerAxis;
    InputAction editorFire;
    float editorPull;
    bool firedThisPull;
    float nextShotTime;
    LineRenderer laser;
    Transform dot;
    Mesh rocketMesh;
    Material rocketMaterial;
    Transform rocketBone;
    Vector3 rocketRestScale = Vector3.one;
    const float chamberDelay = 0.8f;
    bool chambering;
    float chamberAge;
    Vector3 chamberStart;
    Vector3 chamberEnd;
    GameObject chamberRocket;

    AudioSource reloadSource;
    AudioClip reloadClip;

    static readonly System.Collections.Generic.List<XRDisplaySubsystem> displays =
        new System.Collections.Generic.List<XRDisplaySubsystem>();
    static readonly RaycastHit[] aimHits = new RaycastHit[24];

    void Awake()
    {
        if (muzzle == null)
        {
            Transform found = FindNamed(transform, "Muzzle");
            if (found != null)
                muzzle = found;
        }

        AimMuzzle();
        PaintSight();
        BuildLaser();
        reloadClip = Resources.Load<AudioClip>("Audio/RocketReload");
        if (reloadClip == null)
            return;

        reloadSource = gameObject.AddComponent<AudioSource>();
        reloadSource.playOnAwake = false;
        reloadSource.loop = false;
        reloadSource.spatialBlend = 1f;
        reloadSource.minDistance = 0.4f;
        reloadSource.maxDistance = 12f;
    }

    void Start()
    {
        CacheRocketMesh();
    }

    void OnEnable()
    {
        BindInputs();
    }

    public void SetHand(Hd2Pistol.Hand value)
    {
        hand = value;
        if (isActiveAndEnabled)
            BindInputs();
    }

    void BindInputs()
    {
        Dispose(ref triggerAxis);
        Dispose(ref editorFire);

        string node = hand == Hd2Pistol.Hand.Left ? "LeftHand" : "RightHand";
        triggerAxis = new InputAction("Hd2RocketPull" + node, InputActionType.Value);
        triggerAxis.expectedControlType = "Axis";
        triggerAxis.AddBinding("<XRController>{" + node + "}/trigger");
        triggerAxis.Enable();

        if (hand == Hd2Pistol.Hand.Right)
        {
            editorFire = new InputAction("Hd2RocketEditor", InputActionType.Button);
            editorFire.AddBinding("<Mouse>/rightButton");
            editorFire.Enable();
        }
    }

    void OnDisable()
    {
        Hd2Rocket.Guide(this, Vector3.zero, false);
        SetLaser(false, transform.position, transform.forward, transform.position, false);
        RestoreLoadedRocket();
        Dispose(ref triggerAxis);
        Dispose(ref editorFire);
    }

    void Update()
    {
        bool menuOpen = ControlMap() != null && ControlMap().MenuOpen;
        float pull = menuOpen ? 0f : ReadPull();
        bool aiming = pull >= laserTriggerThreshold;
        bool full = pull >= Mathf.Max(laserTriggerThreshold + 0.05f, fireTriggerThreshold);
        if (pull < laserTriggerThreshold)
            firedThisPull = false;

        AdvanceChamber();

        Vector3 origin = AimOrigin();
        Vector3 direction = Barrel();
        Vector3 sight = laserSight != null ? laserSight.position : origin;
        Vector3 point = sight + direction * 80f;
        bool hit = aiming && AimPoint(sight, direction, out point);
        SetLaser(aiming, sight, direction, point, hit);
        Hd2Rocket.Guide(this, point, aiming);

        if (!chambering && Time.time >= nextShotTime && aiming && full && !firedThisPull)
        {
            firedThisPull = true;
            nextShotTime = Time.time + Mathf.Max(0.05f, shotInterval);
            Fire(origin, direction, point);
        }
    }

    float ReadPull()
    {
        if (EditorFallback)
        {
            bool held = editorFire != null && editorFire.IsPressed();
            editorPull = Mathf.MoveTowards(editorPull, held ? 1f : 0f, Time.deltaTime / 0.55f);
            return editorPull;
        }

        if (triggerAxis == null)
            return 0f;
        return Mathf.Clamp01(triggerAxis.ReadValue<float>());
    }

    void Fire(Vector3 origin, Vector3 direction, Vector3 point)
    {
        if (rocketMesh == null)
            CacheRocketMesh();
        if (rocketMesh == null || rocketBone == null)
            return;

        Quaternion seated = rocketBone.rotation;
        Vector3 seatedAt = rocketBone.position;
        Vector3 size = rocketBone.lossyScale;
        float slide = 0.32f;
        var body = new GameObject("Rocket");
        body.transform.SetPositionAndRotation(seatedAt, Quaternion.LookRotation(direction));
        AttachRocketMesh(body.transform, Quaternion.Inverse(body.transform.rotation) * seated, size);
        AddThrust(body.transform);

        var rocket = body.AddComponent<Hd2Rocket>();
        rocket.source = this;
        rocket.owner = transform.root;
        rocket.BindOwner(GetComponentInParent<Hd2Health>());
        rocket.aimPoint = point;
        rocket.homing = true;
        rocket.damageOwner = damageOwner;
        rocket.launchSpeed = launchSpeed;
        rocket.acceleration = acceleration;
        rocket.accelerationGain = accelerationGain;
        rocket.maxSpeed = maxSpeed;
        rocket.minTurnRadiusMeters = minTurnRadiusMeters;
        rocket.turnRadiusMeters = turnRadiusMeters;
        rocket.maxFlightSeconds = maxFlightSeconds;
        rocket.innerRadiusMeters = innerRadiusMeters;
        rocket.innerDamage = innerDamage;
        rocket.outerRadiusMeters = outerRadiusMeters;
        rocket.outerDamage = outerDamage;
        rocket.Launch(direction);

        rocketBone.localScale = Vector3.zero;
        BeginChamber(seated, seatedAt, direction, slide, size);
        Pulse(0.55f, 0.06f);
    }

    void BeginChamber(Quaternion seated, Vector3 seatedAt, Vector3 barrel, float slide, Vector3 size)
    {
        if (chamberRocket != null)
            Destroy(chamberRocket);

        if (chamberSeconds < 0.6f)
            chamberSeconds = 0.6f;
        chamberAge = 0f;
        chambering = true;
        chamberRocket = new GameObject("ChamberRocket");
        chamberRocket.transform.SetParent(transform, false);
        chamberRocket.transform.localRotation = Quaternion.Inverse(transform.rotation) * seated;
        chamberRocket.transform.localScale = Vector3.one;
        Vector3 localBarrel = transform.InverseTransformDirection(barrel);
        chamberEnd = transform.InverseTransformPoint(seatedAt);
        chamberStart = chamberEnd - localBarrel * slide;
        chamberRocket.transform.localPosition = chamberStart;
        chamberRocket.SetActive(false);
        AttachRocketMesh(chamberRocket.transform, Quaternion.identity, size);
        if (reloadSource != null && reloadClip != null)
            reloadSource.PlayOneShot(reloadClip, 0.5f);
    }

    void AdvanceChamber()
    {
        if (!chambering || chamberRocket == null)
            return;

        chamberAge += Time.deltaTime;
        if (chamberAge < chamberDelay)
            return;

        if (!chamberRocket.activeSelf)
            chamberRocket.SetActive(true);

        float t = Mathf.SmoothStep(0f, 1f, (chamberAge - chamberDelay) / Mathf.Max(0.05f, chamberSeconds));
        chamberRocket.transform.localPosition = Vector3.Lerp(chamberStart, chamberEnd, t);
        if (t < 1f)
            return;

        RestoreLoadedRocket();
    }

    void RestoreLoadedRocket()
    {
        chambering = false;
        if (rocketBone != null)
            rocketBone.localScale = rocketRestScale;
        if (chamberRocket != null)
            Destroy(chamberRocket);
        chamberRocket = null;
    }

    void AttachRocketMesh(Transform body, Quaternion localRotation, Vector3 size)
    {
        var visual = new GameObject("RocketMesh");
        visual.transform.SetParent(body, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = localRotation;
        Vector3 parentScale = body.lossyScale;
        visual.transform.localScale = new Vector3(
            size.x / Mathf.Max(0.0001f, parentScale.x),
            size.y / Mathf.Max(0.0001f, parentScale.y),
            size.z / Mathf.Max(0.0001f, parentScale.z));
        var filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = rocketMesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = rocketMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    void AddThrust(Transform body)
    {
        float tail = 0f;
        Vector3 ventCenter = Vector3.zero;
        MeshFilter filter = body.GetComponentInChildren<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            Bounds bounds = filter.sharedMesh.bounds;
            Transform visual = filter.transform;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Vector3 centerLocal = body.InverseTransformPoint(visual.TransformPoint(bounds.center));
            float minZ = float.PositiveInfinity;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 local = body.InverseTransformPoint(visual.TransformPoint(center + Vector3.Scale(extents, new Vector3(x, y, z))));
                        if (local.z < minZ)
                            minZ = local.z;
                    }
                }
            }

            if (minZ < float.PositiveInfinity)
                tail = minZ;
            ventCenter = new Vector3(centerLocal.x, centerLocal.y, tail);
        }

        var vent = new GameObject("Thrust");
        vent.transform.SetParent(body, false);
        vent.transform.localPosition = ventCenter;
        vent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var particles = vent.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.85f, 0.35f, 0.9f),
            new Color(1f, 0.45f, 0.08f, 0.85f));
        main.gravityModifier = 0f;
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 70f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.006f;

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(0.35f, 1.2f),
            new Keyframe(1f, 0.2f)));

        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                new GradientColorKey(new Color(1f, 0.4f, 0.05f), 0.35f),
                new GradientColorKey(new Color(0.25f, 0.08f, 0.05f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0.5f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var renderer = vent.GetComponent<ParticleSystemRenderer>();
        renderer.material = ThrustMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
        AddSmoke(vent.transform);
    }

    void AddSmoke(Transform vent)
    {
        var smokeObject = new GameObject("Smoke");
        smokeObject.transform.SetParent(vent, false);
        smokeObject.transform.localPosition = new Vector3(0f, 0f, 0.04f);
        smokeObject.transform.localRotation = Quaternion.identity;

        var particles = smokeObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.78f, 0.78f, 0.78f, 0.45f),
            new Color(0.9f, 0.9f, 0.9f, 0.55f));
        main.gravityModifier = 0.02f;
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 22f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 16f;
        shape.radius = 0.008f;

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(1f, 1.8f)));

        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.9f, 0.9f, 0.9f), 0f),
                new GradientColorKey(new Color(0.72f, 0.72f, 0.72f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.4f, 0f),
                new GradientAlphaKey(0.15f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var renderer = smokeObject.GetComponent<ParticleSystemRenderer>();
        renderer.material = ThrustMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
    }

    static Material thrustMaterial;

    static Material ThrustMaterial()
    {
        if (thrustMaterial != null)
            return thrustMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        thrustMaterial = new Material(shader);
        if (thrustMaterial.HasProperty("_BaseColor"))
            thrustMaterial.SetColor("_BaseColor", Color.white);
        return thrustMaterial;
    }

    void CacheRocketMesh()
    {
        SkinnedMeshRenderer[] skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int s = 0; s < skins.Length; s++)
        {
            Transform[] bones = skins[s].bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || bones[i].name != "rocket")
                    continue;
                rocketBone = bones[i];
                rocketRestScale = rocketBone.localScale;
                rocketMesh = ExtractRocket(skins[s], i);
                Material[] materials = skins[s].sharedMaterials;
                rocketMaterial = i < materials.Length ? materials[i] : skins[s].sharedMaterial;
                return;
            }
        }
    }

    static Mesh ExtractRocket(SkinnedMeshRenderer skin, int boneIndex)
    {
        Mesh source = skin.sharedMesh;
        if (source == null || boneIndex < 0 || boneIndex >= source.bindposes.Length)
            return null;

        BoneWeight[] weights = source.boneWeights;
        Vector3[] verts = source.vertices;
        Vector3[] normals = source.normals;
        Vector2[] uvs = source.uv;
        if (weights == null || weights.Length != verts.Length)
            return null;

        var keep = new bool[verts.Length];
        int count = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            keep[i] = UsesBone(weights[i], boneIndex);
            if (keep[i])
                count++;
        }

        if (count == 0)
            return null;

        var remap = new int[verts.Length];
        var newVerts = new Vector3[count];
        var newNormals = new Vector3[count];
        var newUvs = new Vector2[count];
        Matrix4x4 bind = source.bindposes[boneIndex];
        int next = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            if (!keep[i])
                continue;
            remap[i] = next;
            newVerts[next] = bind.MultiplyPoint3x4(verts[i]);
            Vector3 normal = normals != null && i < normals.Length ? normals[i] : Vector3.up;
            newNormals[next] = bind.MultiplyVector(normal).normalized;
            newUvs[next] = uvs != null && i < uvs.Length ? uvs[i] : Vector2.zero;
            next++;
        }

        var triangles = new System.Collections.Generic.List<int>();
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            int[] tris = source.GetTriangles(sub);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t];
                int b = tris[t + 1];
                int c = tris[t + 2];
                if (!keep[a] || !keep[b] || !keep[c])
                    continue;
                triangles.Add(remap[a]);
                triangles.Add(remap[b]);
                triangles.Add(remap[c]);
            }
        }

        if (triangles.Count == 0)
            return null;

        var mesh = new Mesh();
        mesh.name = "RocketRound";
        mesh.SetVertices(newVerts);
        mesh.SetNormals(newNormals);
        mesh.SetUVs(0, newUvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static bool UsesBone(BoneWeight weight, int bone)
    {
        if (weight.boneIndex0 == bone && weight.weight0 >= 0.5f)
            return true;
        if (weight.boneIndex1 == bone && weight.weight1 >= 0.5f)
            return true;
        if (weight.boneIndex2 == bone && weight.weight2 >= 0.5f)
            return true;
        return weight.boneIndex3 == bone && weight.weight3 >= 0.5f;
    }

    bool AimPoint(Vector3 origin, Vector3 direction, out Vector3 point)
    {
        point = origin + direction * 80f;
        int count = Physics.RaycastNonAlloc(origin, direction, aimHits, 200f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        Transform owner = transform.root;
        for (int i = 0; i < count; i++)
        {
            Collider collider = aimHits[i].collider;
            if (collider == null)
                continue;
            if (collider.transform.IsChildOf(transform) || collider.transform.IsChildOf(owner))
                continue;
            if (aimHits[i].distance >= best)
                continue;
            best = aimHits[i].distance;
            point = aimHits[i].point;
            found = true;
        }

        return found;
    }

    Vector3 AimOrigin()
    {
        Vector3 barrel = Barrel();
        if (!TubeFront(barrel, out Vector3 front))
            return muzzle != null ? muzzle.position : transform.position;
        return front;
    }

    public Vector3 Barrel()
    {
        // The tube runs along local X. The muzzle is the +X end on the left
        // launcher. The right launcher is mirrored, so its muzzle is the -X end.
        Vector3 local = hand == Hd2Pistol.Hand.Right ? Vector3.left : Vector3.right;
        return transform.TransformDirection(local);
    }

    bool TubeFront(Vector3 barrel, out Vector3 front)
    {
        front = transform.position;
        Transform gun = FindNamed(transform, "Gun");
        Transform scope = gun != null ? gun : transform;
        Renderer[] renderers = scope.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (renderer.gameObject.name == "Laser" || renderer.gameObject.name == "LaserDot")
                continue;
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        if (!any)
            return false;

        Vector3 center = bounds.center;
        float reach = 0f;
        Vector3 extents = bounds.extents;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    float along = Vector3.Dot(corner - center, barrel);
                    if (along > reach)
                        reach = along;
                }
            }
        }

        front = center + barrel * reach;
        return true;
    }

    void AimMuzzle()
    {
        if (muzzle == null)
            return;

        Vector3 along = muzzle.position - transform.position;
        if (along.sqrMagnitude < 0.0004f)
            return;
        muzzle.rotation = Quaternion.LookRotation(along.normalized, transform.up);
    }

    void PaintSight()
    {
        Transform gun = FindNamed(transform, "Gun");
        Transform scope = gun != null ? gun : transform;
        Transform[] all = scope.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name != "Laser")
                continue;
            if (all[i].GetComponent<MeshRenderer>() == null)
                continue;
            laserSight = all[i];
            break;
        }

        if (laserSight == null)
            return;

        var renderer = laserSight.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (renderer == null || shader == null)
            return;

        var material = new Material(shader);
        material.color = Color.red;
        material.SetColor("_BaseColor", Color.red);
        renderer.material = material;
    }

    void BuildLaser()
    {
        var lineObject = new GameObject("LaserBeam");
        lineObject.transform.SetParent(transform, false);
        laser = lineObject.AddComponent<LineRenderer>();
        laser.useWorldSpace = true;
        laser.positionCount = 2;
        laser.widthMultiplier = 0.003f;
        laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
            laser.material = new Material(shader);
        laser.startColor = new Color(1f, 0.25f, 0.15f, 0.2f);
        laser.endColor = new Color(1f, 0.25f, 0.15f, 0f);
        laser.enabled = false;

        var mark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mark.name = "LaserDot";
        var collider = mark.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        mark.transform.SetParent(transform, false);
        Vector3 parentScale = transform.lossyScale;
        const float diameter = 0.03f;
        mark.transform.localScale = new Vector3(
            diameter / Mathf.Max(0.0001f, parentScale.x),
            diameter / Mathf.Max(0.0001f, parentScale.y),
            diameter / Mathf.Max(0.0001f, parentScale.z));
        var renderer = mark.GetComponent<Renderer>();
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (renderer != null && unlit != null)
        {
            var material = new Material(unlit);
            material.color = new Color(1f, 0.3f, 0.15f, 1f);
            renderer.sharedMaterial = material;
        }

        dot = mark.transform;
        dot.gameObject.SetActive(false);
    }

    void SetLaser(bool on, Vector3 origin, Vector3 direction, Vector3 point, bool hit)
    {
        const float beamLength = 0.15f;
        if (laser != null)
        {
            laser.enabled = on;
            if (on)
            {
                Vector3 end = origin + direction * beamLength;
                if (hit)
                {
                    float distance = Vector3.Distance(origin, point);
                    if (distance < beamLength)
                        end = point;
                }

                laser.SetPosition(0, origin);
                laser.SetPosition(1, end);
            }
        }

        if (dot != null)
        {
            dot.gameObject.SetActive(on && hit);
            if (on && hit)
                dot.position = point;
        }
    }

    void Pulse(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f)
            return;

        var node = hand == Hd2Pistol.Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
        var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        for (int i = 0; i < devices.Count; i++)
        {
            UnityEngine.XR.InputDevice device = devices[i];
            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }
    }

    Hd2ControlMap controlMap;

    Hd2ControlMap ControlMap()
    {
        if (controlMap == null)
            controlMap = GetComponentInParent<Hd2ControlMap>();
        return controlMap;
    }

    static bool EditorFallback
    {
        get
        {
            if (!Application.isEditor)
                return false;
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].running)
                    return false;
            }

            return true;
        }
    }

    static Transform FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
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
