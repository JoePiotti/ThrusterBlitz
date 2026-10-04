using UnityEngine;

/// <summary>
/// Deathmatch match start. The level itself is built by hand in the scene.
/// At play, each player is sent to their opening spawn.
/// </summary>
[DefaultExecutionOrder(-100)]
public class Hd2DeathmatchMap : MonoBehaviour
{
    void Awake()
    {
        if (GetComponent<Hd2SpawnDirector>() == null)
            gameObject.AddComponent<Hd2SpawnDirector>();
    }

    void Start()
    {
        var director = GetComponent<Hd2SpawnDirector>();
        if (director == null)
            return;

        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        System.Array.Sort(players, (a, b) => string.CompareOrdinal(a.name, b.name));
        for (int i = 0; i < players.Length; i++)
        {
            Transform spawn = director.PickOpening(i);
            director.MoveToSpawn(players[i].transform, spawn);
            var health = players[i].GetComponent<Hd2Health>();
            if (health != null)
                health.RememberSpawn();
        }
    }
}
