using UnityEngine;

/// <summary>
/// Touching this volume kills the player. The body checks it while moving,
/// because locomotion is not a rigidbody. A splash plays at the surface.
/// </summary>
public class Hd2Water : MonoBehaviour
{
    static Material dropletMaterial;

    public static void Splash(Vector3 point, bool body)
    {
        var splash = new GameObject(body ? "RobotSplash" : "BulletSplash");
        splash.transform.SetPositionAndRotation(point + Vector3.up * 0.04f, Quaternion.LookRotation(Vector3.up));

        float scale = body ? 1f : 0.42f;
        Burst(splash, scale, body ? 28 : 10, body ? 18 : 6);
        PlaySplash(splash, body);
        float life = 1.6f;
        var source = splash.GetComponent<AudioSource>();
        if (source != null && source.clip != null)
            life = Mathf.Max(life, source.clip.length + 0.15f);
        Object.Destroy(splash, life);
    }

    static void Burst(GameObject splash, float scale, int dropCount, int mistCount)
    {
        var drops = splash.AddComponent<ParticleSystem>();
        Configure(drops, scale, dropCount, false);
        var mistObject = new GameObject("Mist");
        mistObject.transform.SetParent(splash.transform, false);
        var mist = mistObject.AddComponent<ParticleSystem>();
        Configure(mist, scale, mistCount, true);
        drops.Play();
        mist.Play();
    }

    static void Configure(ParticleSystem particles, float scale, int count, bool mist)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = mist ? 0.15f : 1.35f;
        main.maxParticles = count + 4;
        if (mist)
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.6f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * scale, 0.2f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.82f, 0.94f, 1f, 0.45f),
                new Color(1f, 1f, 1f, 0.22f));
        }
        else
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f * scale, 5.5f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f * scale, 0.07f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.72f, 0.9f, 1f, 0.95f),
                Color.white);
        }

        var emission = particles.emission;
        emission.rateOverTime = 0f;
        short burst = (short)Mathf.Max(1, count);
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burst, burst) });

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = mist ? 70f : 42f;
        shape.radius = mist ? 0.12f * scale : 0.05f * scale;

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        Material droplets = DropletMaterial();
        if (droplets != null)
            renderer.material = droplets;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static void PlaySplash(GameObject splash, bool body)
    {
        AudioClip clip = Resources.Load<AudioClip>(body ? "Audio/RobotSplash" : "Audio/BulletSplash");
        if (clip == null)
            return;

        var source = splash.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.volume = 1f;
        source.minDistance = body ? 2f : 1f;
        source.maxDistance = body ? 24f : 16f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.clip = clip;
        source.Play();
    }

    static Material DropletMaterial()
    {
        if (dropletMaterial != null)
            return dropletMaterial;

        dropletMaterial = Hd2Shot.ParticleMaterial();
        return dropletMaterial;
    }
}
