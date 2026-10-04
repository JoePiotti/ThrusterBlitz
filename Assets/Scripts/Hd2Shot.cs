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
    public Transform owner;
    [Tooltip("Stretch this shot along its travel so it reads as one bolt instead of a row of spheres.")]
    public bool plasma;
    public Color boltColor = new Color(0.45f, 0.9f, 1f, 1f);
    public float boltThickness = 0.07f;

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
        float length = 0.45f;
        if (velocity.sqrMagnitude > 0.01f)
            length = Mathf.Clamp(velocity.magnitude * 0.016f, 0.45f, 2.4f);
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

        Destroy(gameObject);
        return true;
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
        float length = Mathf.Clamp(velocity.magnitude * Mathf.Max(dt, 0.008f), 0.45f, 2.4f);
        float width = Mathf.Max(0.02f, boltThickness);
        transform.localScale = new Vector3(width, width, length);
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
