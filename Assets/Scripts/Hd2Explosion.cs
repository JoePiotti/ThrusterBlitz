using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pooled particle bursts for rocket and shock explosions. A few small unlit
/// particles stand in for the old growing spheres.
/// </summary>
public class Hd2Explosion : MonoBehaviour
{
    const int PoolCap = 4;

    public ParticleSystem root;
    public ParticleSystem smoke;
    public ParticleSystem[] tinted;
    public Transform globe;
    public Material globeMaterial;
    public float life = 0.9f;
    public bool rocket;

    float age;
    bool pooled;
    static readonly List<Hd2Explosion> rockets = new List<Hd2Explosion>();
    static readonly List<Hd2Explosion> shocks = new List<Hd2Explosion>();
    static int rocketMade;
    static int shockMade;
    static Material shared;

    public static void Rocket(Vector3 point, bool impact)
    {
        Hd2Explosion burst = Borrow(rockets, ref rocketMade, CreateRocket);
        if (burst == null)
            return;
        burst.Play(point, impact);
    }

    public static void Shock(Vector3 point, Color color)
    {
        Hd2Explosion burst = Borrow(shocks, ref shockMade, CreateShock);
        if (burst == null)
            return;
        burst.Tint(color);
        burst.Play(point, false);
    }

    void Play(Vector3 point, bool smokeOn)
    {
        transform.position = point;
        transform.rotation = Quaternion.identity;
        if (smoke != null)
            smoke.gameObject.SetActive(smokeOn);
        pooled = false;
        age = 0f;
        if (globe != null)
        {
            globe.gameObject.SetActive(rocket);
            globe.localScale = Vector3.one * 0.3f;
        }

        gameObject.SetActive(true);
        if (root == null)
            return;
        root.Clear(true);
        root.Play(true);
    }

    void Tint(Color color)
    {
        if (tinted == null)
            return;
        Color hot = Color.Lerp(Color.white, color, 0.25f);
        var gradient = new ParticleSystem.MinMaxGradient(hot, color);
        for (int i = 0; i < tinted.Length; i++)
        {
            if (tinted[i] == null)
                continue;
            var main = tinted[i].main;
            main.startColor = gradient;
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        if (rocket && globe != null)
        {
            float t = age / 0.45f;
            globe.localScale = Vector3.one * Mathf.Lerp(0.35f, 3.2f, Mathf.Clamp01(t));
            float alpha = t < 0.4f ? 0.9f : Mathf.Clamp01(1f - (t - 0.4f) / 0.7f);
            globe.gameObject.SetActive(alpha > 0.02f);
            if (globeMaterial != null)
            {
                Color color = new Color(1f, 1f, 1f, alpha);
                if (globeMaterial.HasProperty("_BaseColor"))
                    globeMaterial.SetColor("_BaseColor", color);
                if (globeMaterial.HasProperty("_Color"))
                    globeMaterial.SetColor("_Color", color);
            }
        }

        if (age < life)
            return;
        Recycle();
    }

    void Recycle()
    {
        if (pooled)
            return;
        pooled = true;
        if (root != null)
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
        (rocket ? rockets : shocks).Add(this);
    }

    static Hd2Explosion Borrow(List<Hd2Explosion> pool, ref int made, System.Func<Hd2Explosion> create)
    {
        int last = pool.Count - 1;
        if (last >= 0)
        {
            Hd2Explosion idle = pool[last];
            pool.RemoveAt(last);
            return idle;
        }

        if (made >= PoolCap)
            return null;
        made++;
        return create();
    }

    static Hd2Explosion CreateRocket()
    {
        var burst = new GameObject("RocketBurst");
        burst.hideFlags = HideFlags.HideAndDontSave;
        var flash = AddSystem(burst, false);
        ConfigureFlash(flash, 28, 0.16f, 0.36f, 2.2f, 6.5f, 0.7f, 1.8f,
            Color.white, new Color(1f, 0.92f, 0.7f));
        var sparks = AddSystem(Child(burst.transform, "Sparks"), true);
        ConfigureSparks(sparks, 56, 0.28f, 0.62f, 6f, 15f, 0.45f, 0.06f, 0.16f,
            Color.white, new Color(1f, 0.55f, 0.12f));
        var smokeObject = Child(burst.transform, "Smoke");
        var smoke = AddSystem(smokeObject, false);
        ConfigureSmoke(smoke);
        var explosion = burst.AddComponent<Hd2Explosion>();
        explosion.root = flash;
        explosion.smoke = smoke;
        explosion.globe = AddGlobe(burst.transform, out explosion.globeMaterial);
        explosion.rocket = true;
        explosion.life = 1.2f;
        burst.SetActive(false);
        return explosion;
    }

    static Hd2Explosion CreateShock()
    {
        var burst = new GameObject("ShockBurst");
        burst.hideFlags = HideFlags.HideAndDontSave;
        var flash = AddSystem(burst, false);
        ConfigureFlash(flash, 6, 0.2f, 0.4f, 1.4f, 3.6f, 0.45f, 1.05f,
            Color.white, new Color(1f, 0.35f, 0.12f));
        var sparks = AddSystem(Child(burst.transform, "Sparks"), true);
        ConfigureSparks(sparks, 14, 0.22f, 0.48f, 6f, 13f, 0.35f, 0.05f, 0.11f,
            Color.white, new Color(1f, 0.22f, 0.06f));
        var explosion = burst.AddComponent<Hd2Explosion>();
        explosion.root = flash;
        explosion.tinted = new[] { flash, sparks };
        explosion.life = 0.7f;
        burst.SetActive(false);
        return explosion;
    }

    static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    static ParticleSystem AddSystem(GameObject host, bool stretch)
    {
        var particles = host.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (stretch)
        {
            renderer.lengthScale = 2.2f;
            renderer.velocityScale = 0.04f;
        }
        Material material = SharedMaterial();
        if (material != null)
            renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    static void ConfigureFlash(
        ParticleSystem particles,
        int count,
        float lifeMin,
        float lifeMax,
        float speedMin,
        float speedMax,
        float sizeMin,
        float sizeMax,
        Color a,
        Color b)
    {
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = 0f;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.04f;
        Fade(particles, 1.4f);
    }

    static void ConfigureSparks(
        ParticleSystem particles,
        int count,
        float lifeMin,
        float lifeMax,
        float speedMin,
        float speedMax,
        float gravity,
        float sizeMin,
        float sizeMax,
        Color a,
        Color b)
    {
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = gravity;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        Fade(particles, 1f);
    }

    static void ConfigureSmoke(ParticleSystem particles)
    {
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 1.15f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.35f, 0.32f, 0.28f, 0.45f),
            new Color(0.55f, 0.5f, 0.44f, 0.28f));
        main.gravityModifier = -0.08f;
        main.maxParticles = 22;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;
        Fade(particles, 1.8f);
    }

    static void Fade(ParticleSystem particles, float grow)
    {
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(0.2f, grow),
            new Keyframe(1f, 0.15f)));
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.7f, 0.25f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;
    }

    static Transform AddGlobe(Transform parent, out Material material)
    {
        var globe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        globe.name = "Flash";
        Collider shell = globe.GetComponent<Collider>();
        if (shell != null)
        {
            if (Application.isPlaying)
                Object.Destroy(shell);
            else
                Object.DestroyImmediate(shell);
        }

        globe.transform.SetParent(parent, false);
        globe.transform.localPosition = Vector3.zero;
        globe.transform.localScale = Vector3.one * 0.3f;
        var renderer = globe.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        material = null;
        Material spark = Resources.Load<Material>("Spark");
        if (spark != null)
        {
            material = new Material(spark);
            Color white = new Color(1f, 1f, 1f, 0.9f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", white);
            renderer.sharedMaterial = material;
        }

        globe.SetActive(false);
        return globe.transform;
    }

    static Material SharedMaterial()
    {
        if (shared != null)
            return shared;
        shared = Hd2Shot.ParticleMaterial();
        return shared;
    }
}
