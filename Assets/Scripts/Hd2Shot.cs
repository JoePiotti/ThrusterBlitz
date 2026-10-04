using UnityEngine;

/// <summary>
/// Visible projectile. A normal shot stops on the first collider that is not the shooter.
/// A charged shot bounces off walls and floors, and stops on players, bots, and practice targets.
/// Hd2SpawnField is one-way: a shot from the room side passes through. A shot from the map side stops.
/// </summary>
public class Hd2Shot : MonoBehaviour
{
    public Vector3 velocity;
    public float lifetime = 0.6f;
    public float damage = 10f;
    [Tooltip("Applied when the shot hits a Head transform. Pistol uses 3. The SMG uses 2 so a 10-damage body hit is 20 on a head.")]
    public float headMultiplier = 3f;
    public int bouncesRemaining;
    [Tooltip("Uncharged shots kick up gravel on the ground and walls. Charged shots leave this off.")]
    public bool gravel = true;
    public Transform owner;
    [Tooltip("Stretch this shot along its travel so it reads as one bolt instead of a row of spheres.")]
    public bool plasma;
    public Color boltColor = new Color(0.45f, 0.9f, 1f, 1f);
    public float boltThickness = 0.07f;
    public float minBoltLength = 0.45f;

    float age;
    static Material plasmaMaterial;
    MaterialPropertyBlock plasmaBlock;
    Vector3 previous;
    bool hasPrevious;

    void Awake()
    {
        previous = transform.position;
        hasPrevious = true;
    }

    void Update()
    {
        if (!hasPrevious)
        {
            previous = transform.position;
            hasPrevious = true;
        }

        float dt = Time.deltaTime;
        age += dt;
        Vector3 next = transform.position + velocity * dt;
        Vector3 delta = next - previous;
        float distance = delta.magnitude;

        if (distance > 0.0001f && TryResolveHit(previous, delta / distance, distance, dt))
            return;

        transform.position = next;
        previous = next;
        if (plasma)
            PoseBolt(dt);

        if (age >= lifetime)
            Destroy(gameObject);
    }

    public bool CoverGap(Vector3 origin, Vector3 direction, float range)
    {
        if (direction.sqrMagnitude < 0.0001f || range <= 0f)
            return false;

        direction.Normalize();
        if (!TryResolveHit(origin, direction, range, 0.016f))
            return false;

        return true;
    }

    public void StartAhead(Vector3 origin, Vector3 direction, float gap)
    {
        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();
        float length = BoltLength(0.016f);
        transform.rotation = Quaternion.LookRotation(direction);
        transform.position = origin + direction * (gap + length * 0.5f);
        previous = transform.position;
        hasPrevious = true;
        if (plasma)
            PoseBolt(0.016f);
    }

    bool TryResolveHit(Vector3 origin, Vector3 direction, float distance, float dt)
    {
        if (!ClosestHit(origin, direction, distance, out RaycastHit hit))
            return false;

        if (TryHitBody(hit))
        {
            if (hit.collider.GetComponentInParent<Hd2PracticeBot>() != null)
                MetalSparks(hit.point, hit.normal);
            Destroy(gameObject);
            return true;
        }

        var field = hit.collider.GetComponentInParent<Hd2SpawnField>();
        if (field != null)
        {
            field.RegisterBlockedShot(hit.point, hit.normal);
            Destroy(gameObject);
            return true;
        }

        var target = hit.collider.GetComponentInParent<Hd2ShootTarget>();
        if (target != null)
        {
            target.RegisterHit(hit.point, hit.normal);
            Destroy(gameObject);
            return true;
        }

        if (hit.collider.GetComponentInParent<Hd2GrindRail>() != null)
            MetalSparks(hit.point, hit.normal);

        if (bouncesRemaining > 0)
        {
            bouncesRemaining--;
            velocity = Vector3.Reflect(velocity, hit.normal);
            Vector3 contact = hit.point + hit.normal * 0.04f;
            transform.position = contact;
            previous = contact;
            hasPrevious = true;
            if (plasma)
                PoseBolt(dt);
            return true;
        }

        if (gravel && hit.collider.GetComponentInParent<Hd2GrindRail>() == null)
            GravelBurst(hit.point, hit.normal);

        Destroy(gameObject);
        return true;
    }

    static Material sparkMaterial;

    static void MetalSparks(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;

        var sparks = new GameObject("Sparks");
        sparks.transform.SetPositionAndRotation(point + normal * 0.015f, Quaternion.LookRotation(normal));
        var particles = sparks.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.duration = 0.15f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.95f, 0.8f),
            new Color(1f, 0.72f, 0.28f));
        main.gravityModifier = 1.5f;
        main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12, 20) });

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = 0.01f;

        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.material = SparkMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
        Destroy(sparks, 1.2f);
    }

    static Mesh chipMesh;

    static void GravelBurst(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;

        var burst = new GameObject("Gravel");
        burst.transform.SetPositionAndRotation(point + normal * 0.02f, Quaternion.LookRotation(normal));

        var chips = burst.AddComponent<ParticleSystem>();
        chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = chips.main;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.032f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.32f, 0.28f, 0.24f),
            new Color(0.58f, 0.5f, 0.38f));
        main.gravityModifier = 2.4f;
        main.maxParticles = 24;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = chips.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 14) });

        var shape = chips.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 48f;
        shape.radius = 0.02f;

        var spin = chips.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
        spin.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

        var chipRenderer = burst.GetComponent<ParticleSystemRenderer>();
        chipRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        chipRenderer.mesh = ChipMesh();
        chipRenderer.material = SparkMaterial();
        chipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        chipRenderer.receiveShadows = false;

        var dustObject = new GameObject("Dust");
        dustObject.transform.SetParent(burst.transform, false);
        var dust = dustObject.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var dustMain = dust.main;
        dustMain.playOnAwake = false;
        dustMain.duration = 0.2f;
        dustMain.loop = false;
        dustMain.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
        dustMain.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.5f);
        dustMain.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        dustMain.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.5f, 0.46f, 0.4f, 0.55f),
            new Color(0.7f, 0.64f, 0.54f, 0.35f));
        dustMain.gravityModifier = 0.35f;
        dustMain.maxParticles = 20;
        dustMain.simulationSpace = ParticleSystemSimulationSpace.World;

        var dustEmission = dust.emission;
        dustEmission.rateOverTime = 0f;
        dustEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 14) });

        var dustShape = dust.shape;
        dustShape.shapeType = ParticleSystemShapeType.Cone;
        dustShape.angle = 36f;
        dustShape.radius = 0.03f;

        var dustRenderer = dustObject.GetComponent<ParticleSystemRenderer>();
        dustRenderer.material = SparkMaterial();
        dustRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dustRenderer.receiveShadows = false;

        chips.Play();
        dust.Play();
        Destroy(burst, 1.4f);
    }

    static Mesh ChipMesh()
    {
        if (chipMesh != null)
            return chipMesh;

        chipMesh = new Mesh { name = "GravelChip" };
        const float s = 0.5f;
        chipMesh.vertices = new[]
        {
            new Vector3(-s, -s, -s), new Vector3(s, -s, -s), new Vector3(s, s, -s), new Vector3(-s, s, -s),
            new Vector3(-s, -s, s), new Vector3(s, -s, s), new Vector3(s, s, s), new Vector3(-s, s, s)
        };
        chipMesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            2, 3, 7, 2, 7, 6,
            1, 2, 6, 1, 6, 5,
            3, 0, 4, 3, 4, 7
        };
        chipMesh.RecalculateNormals();
        return chipMesh;
    }

    static Material SparkMaterial()
    {
        if (sparkMaterial != null)
            return sparkMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        sparkMaterial = new Material(shader);
        if (sparkMaterial.HasProperty("_BaseColor"))
            sparkMaterial.SetColor("_BaseColor", Color.white);
        return sparkMaterial;
    }

    bool ClosestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        closest = default;
        var hits = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;
            if (owner != null && hitTransform.IsChildOf(owner))
                continue;

            var field = hits[i].collider.GetComponentInParent<Hd2SpawnField>();
            if (field != null && field.ShotPasses(origin))
                continue;

            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                closest = hits[i];
                found = true;
            }
        }

        return found;
    }

    void PoseBolt(float dt)
    {
        if (velocity.sqrMagnitude < 0.01f)
            return;

        transform.rotation = Quaternion.LookRotation(velocity);
        float length = BoltLength(dt);
        float width = Mathf.Max(0.02f, boltThickness);
        transform.localScale = new Vector3(width, width, length);
    }

    float BoltLength(float dt)
    {
        float travel = velocity.magnitude * Mathf.Max(dt, 0.008f);
        return Mathf.Clamp(Mathf.Max(travel, minBoltLength), 0.45f, 2.4f);
    }

    public void UsePlasma(Color color, float thickness)
    {
        plasma = true;
        boltColor = color;
        boltThickness = thickness;
        var renderer = GetComponent<MeshRenderer>();
        if (renderer == null)
            return;

        if (plasmaMaterial == null)
        {
            Shader shader = Shader.Find("HD2/PlasmaBolt");
            if (shader == null)
                return;
            plasmaMaterial = new Material(shader);
        }

        renderer.sharedMaterial = plasmaMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (plasmaBlock == null)
            plasmaBlock = new MaterialPropertyBlock();
        plasmaBlock.SetColor("_Color", boltColor);
        plasmaBlock.SetColor("_Core", Color.white);
        renderer.SetPropertyBlock(plasmaBlock);
        PoseBolt(0.016f);
    }

    bool TryHitBody(RaycastHit hit)
    {
        var health = hit.collider.GetComponentInParent<Hd2Health>();
        if (health == null)
            return false;

        if (owner != null)
            health.LastAttacker = owner;
        float dealt = damage;
        if (IsHead(hit.collider.transform))
            dealt *= headMultiplier;
        health.ApplyHit(dealt, false);
        return true;
    }

    static bool IsHead(Transform hitTransform)
    {
        Transform current = hitTransform;
        while (current != null)
        {
            if (current.name == "Head")
                return true;
            current = current.parent;
        }

        return false;
    }
}
