using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Laser-guided rocket. It speeds up to a max speed and cannot turn tighter than its radius.
/// A live laser moves the aim point. Releasing the laser keeps the last point.
/// If the owner dies while the laser is still on, it wanders until it explodes.
/// </summary>
public class Hd2Rocket : MonoBehaviour
{
    public Hd2RocketLauncher source;
    public Transform owner;
    public Hd2Health ownerHealth;
    public Vector3 aimPoint;
    public bool homing = true;
    public bool damageOwner = true;
    public bool locked;

    public float launchSpeed = 8f;
    public float acceleration = 20f;
    public float accelerationGain = 30f;
    public float maxSpeed = 60f;
    public float minTurnRadiusMeters = 10f;
    public float turnRadiusMeters = 20f;
    public float maxFlightSeconds = 6f;
    public float innerRadiusMeters = 1.5f;
    public float innerDamage = 60f;
    public float outerRadiusMeters = 3f;
    public float outerDamage = 36f;

    Vector3 velocity;
    float age;
    float traveled;
    bool wander;
    float wanderTimer;
    Vector3 wanderDir = Vector3.forward;
    bool exploded;

    static readonly List<Hd2Rocket> active = new List<Hd2Rocket>();
    static readonly RaycastHit[] hits = new RaycastHit[24];

    public static void Guide(Hd2RocketLauncher launcher, Vector3 point, bool live)
    {
        for (int i = 0; i < active.Count; i++)
        {
            Hd2Rocket rocket = active[i];
            if (rocket.source != launcher || rocket.wander || rocket.locked)
                continue;
            if (live)
            {
                rocket.homing = true;
                rocket.aimPoint = point;
            }
            else if (rocket.homing)
                rocket.homing = false;
        }
    }

    public void Launch(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;
        direction.Normalize();
        velocity = direction * Mathf.Max(0.1f, launchSpeed);
        transform.rotation = Quaternion.LookRotation(direction);
    }

    public void BindOwner(Hd2Health health)
    {
        if (ownerHealth != null)
            ownerHealth.Changed -= OnOwnerHealth;
        ownerHealth = health;
        if (ownerHealth != null)
            ownerHealth.Changed += OnOwnerHealth;
    }

    void OnEnable()
    {
        active.Add(this);
    }

    void OnDisable()
    {
        if (ownerHealth != null)
            ownerHealth.Changed -= OnOwnerHealth;
        active.Remove(this);
    }

    void OnOwnerHealth(Hd2Health health)
    {
        if (health == null || !health.IsDead || locked)
            return;

        if (homing)
            wander = true;
        homing = false;
        locked = true;
    }

    void Update()
    {
        if (exploded)
            return;

        float dt = Time.deltaTime;
        age += dt;
        if (age >= Mathf.Max(0.2f, maxFlightSeconds))
        {
            Explode(transform.position);
            return;
        }

        NoteDeath();
        Steer(dt);
        Vector3 next = transform.position + velocity * dt;
        if (HitAlong(transform.position, next, out Vector3 point))
        {
            Explode(point);
            return;
        }

        traveled += Vector3.Distance(transform.position, next);
        transform.position = next;
    }

    void NoteDeath()
    {
        if (locked || wander || ownerHealth == null || !ownerHealth.IsDead)
            return;

        if (homing)
            wander = true;
        homing = false;
        locked = true;
    }

    void Steer(float dt)
    {
        float speed = velocity.magnitude;
        Vector3 direction = speed > 0.05f ? velocity / speed : transform.forward;
        Vector3 desired = direction;
        if (wander)
        {
            wanderTimer -= dt;
            if (wanderTimer <= 0f)
            {
                wanderDir = Random.onUnitSphere;
                if (wanderDir.sqrMagnitude < 0.0001f)
                    wanderDir = transform.forward;
                wanderTimer = Random.Range(0.25f, 0.7f);
            }

            desired = wanderDir;
        }
        else
        {
            Vector3 toAim = aimPoint - transform.position;
            if (toAim.sqrMagnitude > 0.04f)
                desired = toAim.normalized;
        }

        float speedT = Mathf.InverseLerp(launchSpeed, Mathf.Max(launchSpeed + 0.01f, maxSpeed), speed);
        float radius = Mathf.Lerp(minTurnRadiusMeters, turnRadiusMeters, speedT);
        radius = Mathf.Max(0.05f, radius);
        float maxRadians = speed / radius * dt;
        direction = Vector3.RotateTowards(direction, desired, maxRadians, 0f);
        float accel = acceleration + accelerationGain * age;
        speed = Mathf.MoveTowards(speed, Mathf.Max(launchSpeed, maxSpeed), Mathf.Max(0f, accel) * dt);
        velocity = direction * speed;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction);
    }

    bool HitAlong(Vector3 from, Vector3 to, out Vector3 point)
    {
        point = to;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return false;

        Vector3 direction = delta / distance;
        int count = Physics.RaycastNonAlloc(from, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || Skip(collider))
                continue;
            if (hits[i].distance >= best)
                continue;
            best = hits[i].distance;
            point = hits[i].point;
            found = true;
        }

        return found;
    }

    bool Skip(Collider collider)
    {
        if (collider.transform.IsChildOf(transform))
            return true;

        if (owner == null)
            return false;

        if (!collider.transform.IsChildOf(owner))
            return false;

        // The tube is in the owner's hand. Let it clear before the owner can stop it.
        return traveled < 0.6f;
    }

    void Explode(Vector3 point)
    {
        if (exploded)
            return;

        exploded = true;
        Transform thrust = transform.Find("Thrust");
        if (thrust != null)
        {
            thrust.SetParent(null, true);
            var particles = thrust.GetComponent<ParticleSystem>();
            if (particles != null)
                particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(thrust.gameObject, 1.2f);
        }
        var seen = new HashSet<Hd2Health>();
        Collider[] colliders = Physics.OverlapSphere(point, outerRadiusMeters, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;

            Hd2Health health = colliders[i].GetComponentInParent<Hd2Health>();
            if (health == null || !seen.Add(health))
                continue;
            if (!damageOwner && ownerHealth != null && health == ownerHealth)
                continue;

            float distance = Vector3.Distance(colliders[i].ClosestPoint(point), point);
            float amount = 0f;
            if (distance <= innerRadiusMeters)
                amount = innerDamage;
            else if (distance <= outerRadiusMeters)
                amount = outerDamage;
            if (amount <= 0f)
                continue;

            if (owner != null)
                health.LastAttacker = owner;
            health.ApplyHit(amount, false);
        }

        SpawnBurst(point, Color.white, 0.9f, 1.2f);
        SpawnBurst(point, new Color(1f, 0.92f, 0.25f), 0.55f, outerRadiusMeters * 2f);
        Destroy(gameObject);
    }

    void SpawnBurst(Vector3 point, Color color, float alpha, float diameter)
    {
        var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        burst.name = "RocketBurst";
        var collider = burst.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        burst.transform.position = point;
        burst.transform.localScale = Vector3.one * 0.2f;
        var renderer = burst.GetComponent<Renderer>();
        var filter = burst.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            Mesh mesh = Instantiate(filter.sharedMesh);
            var colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Color.white;
            mesh.colors = colors;
            filter.sharedMesh = mesh;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (renderer != null && shader != null)
        {
            var material = new Material(shader);
            Color tint = color;
            tint.a = alpha;
            material.color = tint;
            material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            renderer.sharedMaterial = material;
        }

        var fade = burst.AddComponent<Hd2RocketBurst>();
        fade.diameter = diameter;
        fade.color = color;
        fade.alpha = alpha;
    }
}

/// <summary>
/// Short flash at a rocket explosion. The outer sphere grows to the damage radius.
/// </summary>
public class Hd2RocketBurst : MonoBehaviour
{
    public float diameter = 3f;
    public Color color = Color.white;
    public float alpha = 0.8f;
    float age;
    Material material;

    void Awake()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
            material = renderer.sharedMaterial;
    }

    void Update()
    {
        const float duration = 0.1f;
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / duration);
        transform.localScale = Vector3.one * Mathf.Lerp(0.2f, diameter, t);
        if (material != null)
        {
            Color tint = color;
            tint.a = Mathf.Lerp(alpha, 0f, t);
            material.color = tint;
            material.SetColor("_BaseColor", tint);
        }

        if (age >= duration)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }
}
