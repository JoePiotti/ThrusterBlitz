using UnityEngine;

/// <summary>Keeps the mixer volumes applied and routes new sounds to Game (see Hd2Audio).</summary>
public class Hd2AudioRunner : MonoBehaviour
{
    float nextSweep;

    void Start()
    {
        // Mixer values set before the first frame can be dropped; set them again now.
        Hd2Audio.Apply();
    }

    void Update()
    {
        if (Time.unscaledTime < nextSweep)
            return;
        nextSweep = Time.unscaledTime + 0.5f;
        Hd2Audio.Sweep();
    }
}
