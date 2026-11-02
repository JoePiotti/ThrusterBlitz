using System.Collections.Generic;
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

        float before = health;
        health = maxHealth;
        NoteCritical(before);
        Changed?.Invoke(this);
        return true;
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || health <= 0f || health >= maxHealth)
            return;

        float before = health;
        health = Mathf.Min(maxHealth, health + amount);
        NoteCritical(before);
        Changed?.Invoke(this);
    }

    /// <summary>
    /// Same team, and not this body. Team None can still hurt anyone.
    /// </summary>
    public bool IsAlly(Component attacker)
    {
        if (attacker == null)
            return false;

        Transform them = attacker.transform;
        if (them == transform || them.IsChildOf(transform) || transform.IsChildOf(them))
            return false;

        Hd2Team mine = Hd2SpawnField.TeamOf(this);
        Hd2Team theirs = Hd2SpawnField.TeamOf(attacker);
        return mine != Hd2Team.None && mine == theirs;
    }

    public void ApplyHit(float amount, bool headshot, bool playerShot = false)
    {
        if (amount <= 0f || health <= 0f)
            return;

        if (playerShot && IsAlly(LastAttacker))
        {
            LastAttacker = null;
            blastArmed = false;
            killPathCount = 0;
            KillLineTime = -1f;
            return;
        }

        if (headshot)
            amount *= 3f;

        float before = health;
        health = Mathf.Max(0f, health - amount);
        if (health <= 0f && blastArmed)
        {
            KilledByBlast = true;
            BlastOrigin = armedBlast;
        }

        blastArmed = false;
        if (playerShot)
            PlayPlayerHit();
        NoteCritical(before);
        Changed?.Invoke(this);
        if (health <= 0f)
        {
            var killer = LastAttacker;
            LastAttacker = null;
            NoteScore(killer);
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
    public Vector3 LastHitPoint { get; private set; }
    public Vector3 LastShotOrigin { get; private set; }
    public float KillLineTime { get; private set; }
    readonly Vector3[] killPath = new Vector3[96];
    int killPathCount;
    public int KillPathCount => killPathCount;

    public Vector3 KillPathPoint(int index)
    {
        return killPath[index];
    }
    public bool KilledByBlast { get; private set; }
    public Vector3 BlastOrigin { get; private set; }
    bool blastArmed;
    Vector3 armedBlast;

    void NoteScore(Transform killer)
    {
        Hd2Locomotion victim = GetComponent<Hd2Locomotion>();
        if (victim != null)
        {
            Hd2Hud hud = victim.GetComponent<Hd2Hud>();
            if (hud != null)
                hud.AddDeath();
        }

        if (killer == null || killer.GetComponentInParent<Hd2EnemyBot>() != null)
            return;

        Hd2Locomotion player = killer.GetComponentInParent<Hd2Locomotion>();
        if (player == null || player == victim)
            return;

        Hd2Team side = Hd2SpawnField.TeamOf(this);
        if (side != Hd2Team.None && player.team != Hd2Team.None && side == player.team)
            return;

        Hd2Hud killerHud = player.GetComponent<Hd2Hud>();
        if (killerHud != null)
            killerHud.AddKill();
        PlayKillWhack();
    }

    public void MarkBlast(Vector3 origin)
    {
        blastArmed = true;
        armedBlast = origin;
    }

    /// <summary>
    /// Gives a fresh ragdoll one shove away from a killing blast.
    /// </summary>
    public static void ThrowFromBlast(List<Rigidbody> bodies, Vector3 origin, Vector3 bodyPosition)
    {
        if (bodies == null)
            return;

        Vector3 away = bodyPosition + Vector3.up - origin;
        if (away.sqrMagnitude < 0.04f)
            away = Vector3.up;
        away.y = Mathf.Max(away.y, 0.2f);
        away.Normalize();
        Vector3 velocity = away * 5.5f;
        for (int i = 0; i < bodies.Count; i++)
        {
            Rigidbody rigidbody = bodies[i];
            if (rigidbody == null)
                continue;
            rigidbody.linearVelocity = velocity;
        }
    }

    public void MarkHit(Vector3 point)
    {
        LastHitPoint = point;
    }

    public void MarkKillLine(Vector3 origin, Vector3 point)
    {
        killPathCount = 0;
        LastShotOrigin = origin;
        LastHitPoint = point;
        KillLineTime = Time.time;
    }

    public void MarkKillPath(Vector3[] source, int count)
    {
        killPathCount = 0;
        if (source == null || count < 2)
            return;

        int n = Mathf.Min(count, killPath.Length);
        if (count <= killPath.Length)
        {
            for (int i = 0; i < count; i++)
                killPath[i] = source[i];
            killPathCount = count;
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                int index = i * (count - 1) / (n - 1);
                killPath[i] = source[index];
            }

            killPathCount = n;
        }

        LastShotOrigin = killPath[0];
        LastHitPoint = killPath[killPathCount - 1];
        KillLineTime = Time.time;
    }

    public void PinSpawn(Vector3 position, Quaternion rotation)
    {
        spawnPosition = position;
        spawnRotation = rotation;
        hasSpawn = true;
        pinSpawn = true;
    }

    bool pinSpawn;

    public void Respawn()
    {
        StopCritical();
        health = maxHealth;
        KilledByBlast = false;
        killPathCount = 0;
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
        // A team base stays pinned. Clearing it made the next death use a
        // deathmatch point, which put a switched player back on the old side.
        if (!pinSpawn && Hd2SpawnDirector.Instance != null)
        {
            var farthest = Hd2SpawnDirector.Instance.PickFarthest(transform);
            if (farthest != null)
            {
                pos = farthest.position;
                rot = farthest.rotation;
            }
        }

        transform.SetPositionAndRotation(pos, rot);
        if (GetComponent<Hd2Locomotion>() != null)
            Hd2TeleportArrive.Play(pos);
    }

    static AudioClip playerHitClip;
    static AudioSource playerHitSource;

    void PlayPlayerHit()
    {
        if (GetComponent<Hd2Locomotion>() == null)
            return;

        if (playerHitClip == null)
            playerHitClip = Resources.Load<AudioClip>("Audio/PlayerHit");
        if (playerHitClip == null)
            return;

        if (playerHitSource == null)
        {
            var speaker = new GameObject("PlayerHit");
            speaker.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(speaker);
            playerHitSource = speaker.AddComponent<AudioSource>();
            playerHitSource.playOnAwake = false;
            playerHitSource.spatialBlend = 0f;
            playerHitSource.dopplerLevel = 0f;
        }

        playerHitSource.PlayOneShot(playerHitClip);
    }

    static AudioClip killWhackClip;
    static AudioSource killWhackSource;

    static void PlayKillWhack()
    {
        if (killWhackClip == null)
            killWhackClip = Resources.Load<AudioClip>("Audio/Whack");
        if (killWhackClip == null)
            return;

        if (killWhackSource == null)
        {
            var speaker = new GameObject("KillWhack");
            speaker.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(speaker);
            killWhackSource = speaker.AddComponent<AudioSource>();
            killWhackSource.playOnAwake = false;
            killWhackSource.spatialBlend = 0f;
            killWhackSource.dopplerLevel = 0f;
        }

        killWhackSource.PlayOneShot(killWhackClip);
    }

    const float CriticalHealth = 10f;
    static AudioClip criticalClip;
    static AudioSource criticalSource;

    void NoteCritical(float before)
    {
        if (GetComponent<Hd2Locomotion>() == null)
            return;

        if (health <= 0f || health > before)
        {
            StopCritical();
            if (health <= 0f)
                PlayDeath();
            return;
        }

        if (before > CriticalHealth && health <= CriticalHealth)
            PlayCritical();
    }

    static void PlayCritical()
    {
        if (criticalClip == null)
            criticalClip = Resources.Load<AudioClip>("Audio/CriticalDamage");
        if (criticalClip == null)
            return;

        if (criticalSource == null)
        {
            var speaker = new GameObject("CriticalDamage");
            speaker.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(speaker);
            criticalSource = speaker.AddComponent<AudioSource>();
            criticalSource.playOnAwake = false;
            criticalSource.spatialBlend = 0f;
            criticalSource.dopplerLevel = 0f;
        }

        criticalSource.Stop();
        criticalSource.clip = criticalClip;
        criticalSource.Play();
    }

    static void StopCritical()
    {
        if (criticalSource != null && criticalSource.isPlaying)
            criticalSource.Stop();
    }

    static AudioClip deathClip;
    static AudioSource deathSource;

    static void PlayDeath()
    {
        if (deathClip == null)
            deathClip = Resources.Load<AudioClip>("Audio/RobotDeath");
        if (deathClip == null)
            return;

        if (deathSource == null)
        {
            var speaker = new GameObject("RobotDeath");
            speaker.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(speaker);
            deathSource = speaker.AddComponent<AudioSource>();
            deathSource.playOnAwake = false;
            deathSource.spatialBlend = 0f;
            deathSource.dopplerLevel = 0f;
        }

        deathSource.Stop();
        deathSource.clip = deathClip;
        deathSource.Play();
    }
}
