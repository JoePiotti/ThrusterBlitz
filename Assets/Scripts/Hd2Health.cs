using UnityEngine;

/// <summary>
/// Health for a player or bot. A normal pistol shot is 10 damage. Head hits count triple.
/// At 0 health the body respawns where it started.
/// </summary>
public class Hd2Health : MonoBehaviour
{
    public float maxHealth = 60f;
    public bool autoRespawn = true;

    public event System.Action<Hd2Health> Changed;

    float health;
    Vector3 spawnPosition;
    Quaternion spawnRotation;
    bool hasSpawn;

    public float Health => health;
    public bool IsDead => health <= 0f;

    void Awake()
    {
        health = maxHealth;
        RememberSpawn();
    }

    public void RememberSpawn()
    {
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        hasSpawn = true;
    }

    public void Configure(float maximum, bool fill)
    {
        maxHealth = Mathf.Max(1f, maximum);
        if (fill || health > maxHealth)
            health = maxHealth;
    }

    public bool HealToFull()
    {
        if (health >= maxHealth)
            return false;

        health = maxHealth;
        Changed?.Invoke(this);
        return true;
    }

    public void ApplyHit(float amount, bool headshot)
    {
        if (amount <= 0f || health <= 0f)
            return;

        if (headshot)
            amount *= 3f;

        health = Mathf.Max(0f, health - amount);
        Changed?.Invoke(this);
        if (health <= 0f)
        {
            var killer = LastAttacker;
            LastAttacker = null;
            if (killer != null)
            {
                var meter = killer.GetComponent<Hd2ThrustMeter>();
                if (meter != null)
                    meter.AddCharge();
            }

            if (autoRespawn)
                Respawn();
        }
    }

    public Transform LastAttacker { get; set; }

    public void Respawn()
    {
        health = maxHealth;
        if (!hasSpawn)
            RememberSpawn();

        var locomotion = GetComponent<Hd2Locomotion>();
        if (locomotion != null)
            locomotion.HaltTravel();

        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter != null)
            meter.ResetToBase();

        Vector3 pos = spawnPosition;
        Quaternion rot = spawnRotation;
        if (Hd2SpawnDirector.Instance != null)
        {
            var farthest = Hd2SpawnDirector.Instance.PickFarthest(transform);
            if (farthest != null)
            {
                pos = farthest.position;
                rot = farthest.rotation;
            }
        }

        transform.SetPositionAndRotation(pos, rot);
    }
}
