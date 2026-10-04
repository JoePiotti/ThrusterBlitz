using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects spawn markers and picks opening / farthest respawn poses.
/// </summary>
public class Hd2SpawnDirector : MonoBehaviour
{
    const float TieEpsilon = 0.5f;

    public static Hd2SpawnDirector Instance { get; private set; }

    Hd2SpawnPoint[] points = System.Array.Empty<Hd2SpawnPoint>();

    void OnEnable()
    {
        Instance = this;
        RefreshPoints();
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    void RefreshPoints()
    {
        points = GetComponentsInChildren<Hd2SpawnPoint>(true);
        if (points == null || points.Length == 0)
            points = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (points == null)
            points = System.Array.Empty<Hd2SpawnPoint>();

        System.Array.Sort(points, (a, b) => a.index.CompareTo(b.index));
    }

    /// <summary>
    /// Match-start spawn: unique point by sorted index, wrapping if more players than points.
    /// </summary>
    public Transform PickOpening(int playerIndex)
    {
        if (points == null || points.Length == 0)
            RefreshPoints();
        if (points.Length == 0)
            return null;

        int i = playerIndex % points.Length;
        if (i < 0)
            i += points.Length;
        return points[i].transform;
    }

    /// <summary>
    /// Respawn: maximize minimum distance to other living players; random among ties (0.5m).
    /// </summary>
    public Transform PickFarthest(Transform self)
    {
        if (points == null || points.Length == 0)
            RefreshPoints();
        if (points.Length == 0)
            return null;

        var living = CollectLivingOthers(self);
        if (living.Count == 0)
            return points[Random.Range(0, points.Length)].transform;

        float bestMin = float.NegativeInfinity;
        var mins = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float minDist = float.PositiveInfinity;
            Vector3 spawnPos = points[i].transform.position;
            for (int j = 0; j < living.Count; j++)
            {
                float d = Vector3.Distance(spawnPos, living[j]);
                if (d < minDist)
                    minDist = d;
            }

            mins[i] = minDist;
            if (minDist > bestMin)
                bestMin = minDist;
        }

        int tieCount = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (mins[i] >= bestMin - TieEpsilon)
                tieCount++;
        }

        int pick = Random.Range(0, tieCount);
        for (int i = 0; i < points.Length; i++)
        {
            if (mins[i] < bestMin - TieEpsilon)
                continue;
            if (pick == 0)
                return points[i].transform;
            pick--;
        }

        return points[0].transform;
    }

    public void MoveToSpawn(Transform body, Transform spawn)
    {
        if (body == null || spawn == null)
            return;

        body.SetPositionAndRotation(spawn.position, spawn.rotation);

        var locomotion = body.GetComponent<Hd2Locomotion>();
        if (locomotion != null)
            locomotion.HaltTravel();
    }

    static List<Vector3> CollectLivingOthers(Transform self)
    {
        var result = new List<Vector3>();
        var healths = FindObjectsByType<Hd2Health>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < healths.Length; i++)
        {
            var h = healths[i];
            if (h == null || h.Health <= 0f)
                continue;
            if (self != null && h.transform == self)
                continue;
            result.Add(h.transform.position);
        }

        return result;
    }
}
