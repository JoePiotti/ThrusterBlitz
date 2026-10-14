using UnityEngine;

/// <summary>
/// Visible projectile. A normal shot stops on the first collider that is not the shooter.
/// A charged shot bounces off walls and floors, and stops on players, bots, practice targets, and water.
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
    public float maxBoltLength = 2.4f;
    [Tooltip("Shot dies after traveling this far from where it was fired. 0 keeps the old lifetime-only shots.")]
    public float maxTravel;
    [Tooltip("Past this distance from the muzzle, damage is multiplied. 0 leaves damage flat.")]
    public float rangeFalloffStart;
    public float rangeFalloffMultiplier = 1f;

    float age;
    float traveled;
    Vector3 rangeOrigin;
    bool hasRangeOrigin;
    static Material plasmaMaterial;
    MaterialPropertyBlock plasmaBlock;
    Vector3 previous;
    bool hasPrevious;
    bool inPool;
    static Mesh boltMesh;
    static readonly System.Collections.Generic.List<Hd2Shot> shotPool = new System.Collections.Generic.List<Hd2Shot>();
    static readonly RaycastHit[] hitBuffer = new RaycastHit[24];

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
        if (maxTravel > 0f && distance > 0.0001f)
        {
            float room = maxTravel - traveled;
            if (room <= 0.001f)
            {
                Recycle();
                return;
            }

            if (distance > room)
            {
                next = previous + delta / distance * room;
                delta = next - previous;
                distance = room;
            }
        }

        if (distance > 0.0001f && TryResolveHit(previous, delta / distance, distance, dt))
            return;

        traveled += distance;
        transform.position = next;
        previous = next;
        if (maxTravel > 0f && traveled >= maxTravel - 0.001f)
        {
            Recycle();
            return;
        }
        if (plasma)
            PoseBolt(dt);

        if (age >= lifetime)
            Recycle();
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

    public static Mesh BoltMesh()
    {
        if (boltMesh != null)
            return boltMesh;

        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boltMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying)
            Destroy(temp);
        else
            DestroyImmediate(temp);
        return boltMesh;
    }

    public static Hd2Shot Rent()
    {
        Hd2Shot shot;
        int last = shotPool.Count - 1;
        if (last >= 0)
        {
            shot = shotPool[last];
            shotPool.RemoveAt(last);
        }
        else
        {
            var go = new GameObject("Shot");
            go.AddComponent<MeshFilter>().sharedMesh = BoltMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            shot = go.AddComponent<Hd2Shot>();
        }

        shot.inPool = false;
        shot.age = 0f;
        shot.traveled = 0f;
        shot.hasRangeOrigin = false;
        shot.hasPrevious = false;
        shot.maxTravel = 0f;
        shot.rangeFalloffStart = 0f;
        shot.rangeFalloffMultiplier = 1f;
        shot.bouncesRemaining = 0;
        shot.gravel = true;
        shot.plasma = false;
        shot.damage = 10f;
        shot.headMultiplier = 3f;
        shot.lifetime = 0.6f;
        shot.minBoltLength = 0.45f;
        shot.maxBoltLength = 2.4f;
        shot.owner = null;
        shot.velocity = Vector3.zero;
        shot.gameObject.SetActive(true);
        return shot;
    }

    void Recycle()
    {
        if (inPool)
            return;

        inPool = true;
        gameObject.SetActive(false);
        transform.SetParent(null, false);
        shotPool.Add(this);
    }

    public void BeginAt(Vector3 origin)
    {
        rangeOrigin = origin;
        hasRangeOrigin = true;
        traveled = 0f;
        transform.position = origin;
        previous = origin;
        hasPrevious = true;
    }

    public void ReleaseFromMuzzle(Vector3 origin, Vector3 direction)
    {
        const float gap = 1f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();
        float reach = gap + BoltLength(0.016f);
        if (CoverGap(origin, direction, reach))
            return;

        StartAhead(origin, direction, gap);
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
            Recycle();
            MetalSparks(hit.point, hit.normal);
            return true;
        }

        var field = hit.collider.GetComponentInParent<Hd2SpawnField>();
        if (field != null)
        {
            Recycle();
            field.RegisterBlockedShot(hit.point, hit.normal);
            return true;
        }

        var target = hit.collider.GetComponentInParent<Hd2ShootTarget>();
        if (target != null)
        {
            Recycle();
            target.RegisterHit(hit.point, hit.normal);
            return true;
        }

        if (hit.collider.GetComponentInParent<Hd2Water>() != null)
        {
            Recycle();
            Hd2Water.Splash(hit.point, false);
            return true;
        }

        bool rail = hit.collider.GetComponentInParent<Hd2GrindRail>() != null;
        if (bouncesRemaining > 0)
        {
            if (rail)
                MetalSparks(hit.point, hit.normal);
            else
                ScoreMark(hit.point, hit.normal);
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

        Recycle();
        if (rail)
            MetalSparks(hit.point, hit.normal);
        else
        {
            ScoreMark(hit.point, hit.normal);
            if (gravel)
                GravelBurst(hit.point, hit.normal);
        }
        return true;
    }

    static Material sparkMaterial;

    static Material scoreMarkMaterial;
    static Mesh quadMesh;
    static GameObject sparkTemplate;
    static GameObject gravelTemplate;
    static readonly System.Collections.Generic.List<GameObject> scoreMarks = new System.Collections.Generic.List<GameObject>();
    static readonly System.Collections.Generic.List<GameObject> sparkPool = new System.Collections.Generic.List<GameObject>();
    static readonly System.Collections.Generic.List<GameObject> gravelPool = new System.Collections.Generic.List<GameObject>();
    const int MaxScoreMarks = 24;
    const int MaxBursts = 8;

    static void ScoreMark(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;
        normal.Normalize();

        Material material = ScoreMarkMaterial();
        if (material == null)
            return;

        GameObject mark;
        if (scoreMarks.Count >= MaxScoreMarks)
        {
            mark = scoreMarks[0];
            scoreMarks.RemoveAt(0);
        }
        else
        {
            mark = new GameObject("ShotScoreMark");
            mark.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var created = mark.AddComponent<MeshRenderer>();
            created.sharedMaterial = material;
            created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            created.receiveShadows = false;
        }

        scoreMarks.Add(mark);
        Vector3 up = Mathf.Abs(normal.y) > 0.85f ? Vector3.forward : Vector3.up;
        Quaternion face = Quaternion.LookRotation(normal, up);
        face = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * face;
        mark.transform.SetPositionAndRotation(point + normal * 0.02f, face);
        mark.transform.localScale = Vector3.one * 0.11f;
        mark.SetActive(true);
    }

    static Mesh QuadMesh()
    {
        if (quadMesh != null)
            return quadMesh;

        var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying)
            Destroy(temp);
        else
            DestroyImmediate(temp);
        return quadMesh;
    }

    static Material ScoreMarkMaterial()
    {
        if (scoreMarkMaterial != null)
            return scoreMarkMaterial;

        Shader shader = Shader.Find("HD2/ShotScoreMark");
        Texture2D texture = Resources.Load<Texture2D>("Textures/ShotScoreMark");
        if (shader == null || texture == null)
            return null;

        scoreMarkMaterial = new Material(shader);
        scoreMarkMaterial.SetTexture("_MainTex", texture);
        scoreMarkMaterial.SetColor("_Color", Color.white);
        return scoreMarkMaterial;
    }

    static int sparkMade;
    static int gravelMade;

    static void MetalSparks(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;
        PlayBurst(BorrowBurst(sparkPool, ref sparkMade, SparkTemplate), point + normal * 0.015f, normal);
    }

    static void GravelBurst(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;
        PlayBurst(BorrowBurst(gravelPool, ref gravelMade, GravelTemplate), point + normal * 0.02f, normal);
    }

    static void PlayBurst(GameObject burst, Vector3 point, Vector3 normal)
    {
        if (burst == null)
            return;
        burst.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
        burst.SetActive(true);
        var particles = burst.GetComponent<ParticleSystem>();
        if (particles == null)
            return;
        particles.Clear(true);
        particles.Play(true);
    }

    static GameObject BorrowBurst(System.Collections.Generic.List<GameObject> pool, ref int made, System.Func<GameObject> template)
    {
        int last = pool.Count - 1;
        if (last >= 0)
        {
            GameObject idle = pool[last];
            pool.RemoveAt(last);
            return idle;
        }

        if (made >= MaxBursts)
            return null;

        made++;
        return Object.Instantiate(template());
    }

    static GameObject SparkTemplate()
    {
        if (sparkTemplate != null)
            return sparkTemplate;

        var sparks = new GameObject("Sparks");
        var life = sparks.AddComponent<Hd2Burst>();
        life.life = 1.2f;
        life.gravel = false;
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
        main.maxParticles = 24;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 14) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = 0.01f;
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = SparkMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        sparks.SetActive(false);
        sparks.hideFlags = HideFlags.HideAndDontSave;
        sparkTemplate = sparks;
        return sparkTemplate;
    }

    static GameObject GravelTemplate()
    {
        if (gravelTemplate != null)
            return gravelTemplate;

        var burst = new GameObject("Gravel");
        var life = burst.AddComponent<Hd2Burst>();
        life.life = 1.4f;
        life.gravel = true;
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
        main.maxParticles = 16;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = chips.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 10) });
        var shape = chips.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 48f;
        shape.radius = 0.02f;
        var chipRenderer = burst.GetComponent<ParticleSystemRenderer>();
        chipRenderer.sharedMaterial = SparkMaterial();
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
        dustMain.maxParticles = 12;
        dustMain.simulationSpace = ParticleSystemSimulationSpace.World;
        var dustEmission = dust.emission;
        dustEmission.rateOverTime = 0f;
        dustEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 10) });
        var dustShape = dust.shape;
        dustShape.shapeType = ParticleSystemShapeType.Cone;
        dustShape.angle = 36f;
        dustShape.radius = 0.03f;
        var dustRenderer = dustObject.GetComponent<ParticleSystemRenderer>();
        dustRenderer.sharedMaterial = SparkMaterial();
        dustRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dustRenderer.receiveShadows = false;

        burst.SetActive(false);
        burst.hideFlags = HideFlags.HideAndDontSave;
        gravelTemplate = burst;
        return gravelTemplate;
    }

    public static Material ParticleMaterial()
    {
        if (sparkMaterial != null)
            return sparkMaterial;

        sparkMaterial = Resources.Load<Material>("Spark");
        if (sparkMaterial != null)
            return sparkMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return null;

        sparkMaterial = new Material(shader);
        if (sparkMaterial.HasProperty("_BaseColor"))
            sparkMaterial.SetColor("_BaseColor", Color.white);
        return sparkMaterial;
    }

    static Material SparkMaterial()
    {
        return ParticleMaterial();
    }

    bool ClosestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        closest = default;
        int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Transform hitTransform = hitBuffer[i].transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;
            if (owner != null && hitTransform.IsChildOf(owner))
                continue;

            var field = hitBuffer[i].collider.GetComponentInParent<Hd2SpawnField>();
            if (field != null && field.ShotPasses(origin))
                continue;

            if (hitBuffer[i].distance < best)
            {
                best = hitBuffer[i].distance;
                closest = hitBuffer[i];
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
        return Mathf.Clamp(Mathf.Max(travel, minBoltLength), 0.45f, Mathf.Max(0.45f, maxBoltLength));
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
            plasmaMaterial = Resources.Load<Material>("PlasmaBolt");
            if (plasmaMaterial == null)
            {
                Shader shader = Shader.Find("HD2/PlasmaBolt");
                if (shader == null)
                    return;
                plasmaMaterial = new Material(shader);
            }
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
        if (IsHeadshot(hit))
            dealt *= headMultiplier;
        if (rangeFalloffStart > 0f && hasRangeOrigin && Vector3.Distance(rangeOrigin, hit.point) > rangeFalloffStart)
            dealt *= rangeFalloffMultiplier;
        health.MarkHit(hit.point);
        health.ApplyHit(dealt, false);
        if (owner != null && owner.GetComponent<Hd2Locomotion>() != null
            && health.GetComponent<Hd2EnemyBot>() != null)
            PlayHitThunk();
        return true;
    }

    static AudioClip hitThunk;
    static AudioSource hitThunkSource;

    static void PlayHitThunk()
    {
        if (hitThunk == null)
            hitThunk = Resources.Load<AudioClip>("Audio/HitThunk");
        if (hitThunk == null)
            return;

        if (hitThunkSource == null)
        {
            var speaker = new GameObject("HitThunk");
            speaker.hideFlags = HideFlags.DontSave;
            Object.DontDestroyOnLoad(speaker);
            hitThunkSource = speaker.AddComponent<AudioSource>();
            hitThunkSource.playOnAwake = false;
            hitThunkSource.spatialBlend = 0f;
            hitThunkSource.dopplerLevel = 0f;
        }

        hitThunkSource.PlayOneShot(hitThunk);
    }

    static bool IsHeadshot(RaycastHit hit)
    {
        if (IsNamedHead(hit.collider.transform))
            return true;

        Transform head = FindBone(hit.collider.transform, "Head");
        if (head == null)
            return false;

        Vector3 skull = head.position;
        if (head.childCount > 0)
            skull = (head.position + head.GetChild(0).position) * 0.5f;

        float headDist = (hit.point - skull).sqrMagnitude;
        Transform neck = FindBone(hit.collider.transform, "Neck");
        Transform chest = FindBone(hit.collider.transform, "Chest");
        float neckDist = neck != null ? (hit.point - neck.position).sqrMagnitude : float.MaxValue;
        float chestDist = chest != null ? (hit.point - chest.position).sqrMagnitude : float.MaxValue;
        return headDist <= neckDist && headDist <= chestDist;
    }

    static bool IsNamedHead(Transform hitTransform)
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

    static Transform FindBone(Transform start, string boneName)
    {
        Transform root = start;
        while (root.parent != null && root.parent.GetComponent<Hd2Health>() == null)
            root = root.parent;

        return FindNamed(root, boneName);
    }

    static Transform FindNamed(Transform root, string boneName)
    {
        if (root.name == boneName)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), boneName);
            if (found != null)
                return found;
        }

        return null;
    }

    public static void ReturnBurst(GameObject burst, bool gravel)
    {
        if (burst == null)
            return;
        if (gravel)
            gravelPool.Add(burst);
        else
            sparkPool.Add(burst);
    }
}

sealed class Hd2Burst : MonoBehaviour
{
    public float life = 1.2f;
    public bool gravel;
    float age;

    void OnEnable()
    {
        age = 0f;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age < life)
            return;

        gameObject.SetActive(false);
        Hd2Shot.ReturnBurst(gameObject, gravel);
    }
}
