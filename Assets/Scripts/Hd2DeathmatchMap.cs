using UnityEngine;

/// <summary>
/// Deathmatch match start. The level itself is built by hand.
/// Children named Manual are placed by hand and must not be edited by scripts.
/// Children named Generated are the only place scripts may add map objects.
/// </summary>
[DefaultExecutionOrder(-100)]
public class Hd2DeathmatchMap : MonoBehaviour
{
    void Awake()
    {
        if (GetComponent<Hd2SpawnDirector>() == null)
            gameObject.AddComponent<Hd2SpawnDirector>();
        if (GetComponent<Hd2Deathmatch>() == null)
            gameObject.AddComponent<Hd2Deathmatch>();
    }
}
