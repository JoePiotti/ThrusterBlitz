using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Quest runs at 72 Hz with fixed foveation. Vsync stays off so the
/// headset runtime paces frames. Skinning stays at four bones.
/// </summary>
static class Hd2QuestPerformance
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        QualitySettings.skinWeights = SkinWeights.FourBones;
        Application.targetFrameRate = 72;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Foveate()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        for (int i = 0; i < displays.Count; i++)
            displays[i].foveatedRenderingLevel = 1f;
    }
}
