using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Rebuilds grind rails from splines while editing, when entering play mode, and on a player build.
/// A removed spline drops its rail.
/// </summary>
[InitializeOnLoad]
static class Hd2SplineRailBuild
{
    static bool queued;

    static Hd2SplineRailBuild()
    {
        Spline.Changed += OnSplineChanged;
        SplineContainer.SplineAdded += OnSplineListChanged;
        SplineContainer.SplineRemoved += OnSplineListChanged;
        EditorApplication.hierarchyChanged += Queue;
    }

    static void OnSplineChanged(Spline spline, int index, SplineModification modification)
    {
        Queue();
    }

    static void OnSplineListChanged(SplineContainer container, int index)
    {
        Queue();
    }

    static void Queue()
    {
        if (queued)
            return;
        queued = true;
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            return;
        Hd2SplineRail.SyncLoaded(true);
    }

    [PostProcessScene(50)]
    public static void BakeScene()
    {
        Hd2SplineRail.SyncLoaded(false);
    }
}
