using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A short spasm right after a body goes ragdoll, so a fighter killed in the air reads as
/// dead before it hits the ground. A few quick random jolts to the limbs, then it removes
/// itself. Begin adds it to the ragdoll's root.
/// </summary>
public class Hd2DeathTwitch : MonoBehaviour
{
    // Seconds after going limp for each jolt, and how hard each one is.
    static readonly float[] JoltTimes = { 0.06f, 0.16f, 0.27f, 0.4f };
    static readonly float[] JoltStrength = { 1f, 0.8f, 0.6f, 0.4f };
    const float Spin = 14f;   // rad/s added to a limb at full strength
    const float Kick = 1.4f;  // m/s added to a limb at full strength

    List<Rigidbody> bodies;
    float startedAt;
    int next;

    public static void Begin(List<Rigidbody> ragdoll, Transform root)
    {
        if (ragdoll == null || root == null || ragdoll.Count == 0)
            return;
        var twitch = root.GetComponent<Hd2DeathTwitch>();
        if (twitch == null)
            twitch = root.gameObject.AddComponent<Hd2DeathTwitch>();
        twitch.bodies = ragdoll;
        twitch.startedAt = Time.time;
        twitch.next = 0;
        twitch.enabled = true;
    }

    // Jolting the torso would knock the whole body off its flight; only the limbs and head twitch.
    static bool Core(string bone)
    {
        return bone.Contains("Hips") || bone.Contains("Spine") || bone.Contains("Chest");
    }

    void FixedUpdate()
    {
        if (bodies == null || next >= JoltTimes.Length)
        {
            enabled = false;
            return;
        }

        if (Time.time - startedAt < JoltTimes[next])
            return;

        float strength = JoltStrength[next];
        next++;
        for (int i = 0; i < bodies.Count; i++)
        {
            Rigidbody limb = bodies[i];
            if (limb == null || limb.isKinematic || Core(limb.name))
                continue;
            limb.AddTorque(Random.onUnitSphere * (Spin * strength), ForceMode.VelocityChange);
            limb.AddForce(Random.onUnitSphere * (Kick * strength), ForceMode.VelocityChange);
        }
    }
}
