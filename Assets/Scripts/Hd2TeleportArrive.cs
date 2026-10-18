using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Short transporter column at a player's feet. Sparkles rise through a
/// body-tall shimmer, then fade. Pooled, unlit, and only played for the player.
/// </summary>
public class Hd2TeleportArrive : MonoBehaviour
{
    const int PoolCap = 2;
    const float Life = 1.25f;

    ParticleSystem root;
    float age;
    bool pooled;
    static readonly List<Hd2TeleportArrive> idle = new List<Hd2TeleportArrive>();
    static int made;

    public static void Play(Vector3 feet)
    {
        Hd2TeleportArrive burst = Borrow();
        if (burst == null)
            return;
        burst.transform.position = feet;
        burst.Begin();
    }

    void Begin()
    {
        pooled = false;
        age = 0f;
        gameObject.SetActive(true);
        if (root == null)
            return;
        root.Clear(true);
        root.Play(true);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age < Life)
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
        idle.Add(this);
    }

    static Hd2TeleportArrive Borrow()
    {
        int last = idle.Count - 1;
        if (last >= 0)
        {
            Hd2TeleportArrive burst = idle[last];
            idle.RemoveAt(last);
            return burst;
        }

        if (made >= PoolCap)
            return null;
        made++;
        return Create();
    }

    static Hd2TeleportArrive Create()
    {
        var go = new GameObject("TeleportArrive");
        go.hideFlags = HideFlags.HideAndDontSave;
        var shimmer = AddSystem(go, false);
        ConfigureShimmer(shimmer);
        var core = AddSystem(Child(go.transform, "Core"), false);
        ConfigureCore(core);
        var arrive = go.AddComponent<Hd2TeleportArrive>();
        arrive.root = shimmer;
        go.SetActive(false);
        return arrive;
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
        Material material = Hd2Shot.ParticleMaterial();
        if (material != null)
            renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    static void ConfigureShimmer(ParticleSystem particles)
    {
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.82f, 0.96f, 1f, 0.9f),
            new Color(0.35f, 0.82f, 1f, 0.75f));
        main.gravityModifier = 0f;
        main.maxParticles = 36;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.56f, 1.7f, 0.56f);
        shape.position = new Vector3(0f, 0.85f, 0f);

        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);

        Fade(particles);
    }

    static void ConfigureCore(ParticleSystem particles)
    {
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 1f, 0.7f),
            new Color(0.7f, 0.95f, 1f, 0.45f));
        main.gravityModifier = 0f;
        main.maxParticles = 10;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.32f, 1.7f, 0.32f);
        shape.position = new Vector3(0f, 0.85f, 0f);

        Fade(particles);
    }

    static void Fade(ParticleSystem particles)
    {
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.4f),
            new Keyframe(0.2f, 1f),
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
                new GradientAlphaKey(0.15f, 0f),
                new GradientAlphaKey(0.9f, 0.12f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;
    }
}
