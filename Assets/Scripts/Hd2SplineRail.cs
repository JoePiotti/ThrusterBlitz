using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Builds one grind rail for each spline on this object. Points are feet positions.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-50)]
public class Hd2SplineRail : MonoBehaviour
{
    const float SampleStep = 0.5f;

    [Tooltip("Rails for splines after the first. The first spline uses the rail on this object.")]
    public Hd2GrindRail[] extraRails;

    static bool undoEdits;
    static bool creatingChild;

    void Awake()
    {
        Sync();
    }

    public static void SyncLoaded(bool recordUndo)
    {
        bool previous = undoEdits;
        undoEdits = recordUndo;
        var containers = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < containers.Length; i++)
        {
            SplineContainer container = containers[i];
            if (!InScene(container))
                continue;
            Hd2SplineRail link = container.GetComponent<Hd2SplineRail>();
            if (link == null)
                link = AddPart<Hd2SplineRail>(container.gameObject);
            if (link != null)
                link.Sync();
        }

        var links = Object.FindObjectsByType<Hd2SplineRail>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < links.Length; i++)
        {
            Hd2SplineRail link = links[i];
            if (!InScene(link) || link.GetComponent<SplineContainer>() != null)
                continue;
            link.Sync();
        }

        undoEdits = previous;
    }

    public void Sync()
    {
        var container = GetComponent<SplineContainer>();
        if (container == null || container.Splines == null || container.Splines.Count == 0)
        {
            ClearHost();
            TrimExtras(0);
            return;
        }

        int count = container.Splines.Count;
        Hd2GrindRail host = EnsureRail(gameObject);
        ApplySpline(container, 0, host);
        TrimExtras(Mathf.Max(0, count - 1));
        Color color = host != null ? host.color : Color.yellow;
        for (int i = 1; i < count; i++)
        {
            int extra = i - 1;
            if (extraRails[extra] == null)
                extraRails[extra] = CreateExtra(i + 1, color);
            ApplySpline(container, i, extraRails[extra]);
        }
    }

    void ClearHost()
    {
        Hd2GrindRail host = GetComponent<Hd2GrindRail>();
        if (host != null)
            ApplyPath(host, System.Array.Empty<Vector3>());
    }

    void TrimExtras(int keep)
    {
        int current = extraRails != null ? extraRails.Length : 0;
        if (current == keep)
            return;

#if UNITY_EDITOR
        if (undoEdits && !Application.isPlaying)
            UnityEditor.Undo.RecordObject(this, "Rebuild Grind Rail");
#endif
        var next = new Hd2GrindRail[keep];
        if (extraRails != null)
        {
            int copy = Mathf.Min(extraRails.Length, keep);
            for (int i = 0; i < copy; i++)
                next[i] = extraRails[i];
            for (int i = keep; i < extraRails.Length; i++)
            {
                if (extraRails[i] != null)
                    DestroyObject(extraRails[i].gameObject);
            }
        }

        extraRails = next;
        Note(this);
    }

    Hd2GrindRail CreateExtra(int number, Color color)
    {
        var child = new GameObject("Rail " + number);
        child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        creatingChild = true;
        EnsureRail(child);
        creatingChild = false;
        Hd2GrindRail rail = child.GetComponent<Hd2GrindRail>();
        if (rail != null)
            rail.color = color;
#if UNITY_EDITOR
        if (undoEdits && !Application.isPlaying)
            UnityEditor.Undo.RegisterCreatedObjectUndo(child, "Add Grind Rail");
#endif
        return rail;
    }

    static Hd2GrindRail EnsureRail(GameObject go)
    {
        AddPart<MeshFilter>(go);
        AddPart<MeshRenderer>(go);
        MeshCollider solid = AddPart<MeshCollider>(go);
        if (solid != null)
            solid.convex = false;
        return AddPart<Hd2GrindRail>(go);
    }

    static T AddPart<T>(GameObject go) where T : Component
    {
        T existing = go.GetComponent<T>();
        if (existing != null)
            return existing;
#if UNITY_EDITOR
        if (undoEdits && !creatingChild && !Application.isPlaying)
            return UnityEditor.Undo.AddComponent<T>(go);
#endif
        return go.AddComponent<T>();
    }

    void ApplySpline(SplineContainer container, int index, Hd2GrindRail rail)
    {
        if (rail == null || container == null || index < 0 || index >= container.Splines.Count)
            return;

        float length = container.CalculateLength(index);
        if (length < 0.2f)
        {
            ApplyPath(rail, System.Array.Empty<Vector3>());
            return;
        }

        int count = Mathf.Max(2, Mathf.CeilToInt(length / SampleStep) + 1);
        var sampled = new Vector3[count];
        int last = count - 1;
        for (int i = 0; i < count; i++)
        {
            float t = last == 0 ? 0f : i / (float)last;
            Vector3 world = container.EvaluatePosition(index, t);
            sampled[i] = rail.transform.InverseTransformPoint(world);
        }

        if (Same(rail.points, sampled))
            return;
        ApplyPath(rail, sampled);
    }

    static void ApplyPath(Hd2GrindRail rail, Vector3[] sampled)
    {
        if (rail == null)
            return;
        // Clearing a rail that is already empty and off changes nothing. Recording it anyway
        // marked the scene as unsaved after every sync (Play mode exit, compile, hierarchy edit).
        if (sampled.Length == 0 && !rail.enabled && (rail.points == null || rail.points.Length == 0))
            return;
#if UNITY_EDITOR
        if (undoEdits && !Application.isPlaying)
            UnityEditor.Undo.RecordObject(rail, "Rebuild Grind Rail");
#endif
        rail.SetPath(sampled);
        Note(rail);
    }

    static bool Same(Vector3[] current, Vector3[] sampled)
    {
        if (current == null || sampled == null || current.Length != sampled.Length)
            return false;
        for (int i = 0; i < current.Length; i++)
        {
            if ((current[i] - sampled[i]).sqrMagnitude > 0.000001f)
                return false;
        }

        return true;
    }

    static bool InScene(Component component)
    {
        return component != null && component.gameObject.scene.IsValid();
    }

    static void Note(Object target)
    {
#if UNITY_EDITOR
        if (undoEdits && !Application.isPlaying && target != null)
            UnityEditor.EditorUtility.SetDirty(target);
#endif
    }

    static void DestroyObject(GameObject go)
    {
        if (go == null)
            return;
#if UNITY_EDITOR
        if (undoEdits && !Application.isPlaying)
            UnityEditor.Undo.DestroyObjectImmediate(go);
        else if (!Application.isPlaying)
            Object.DestroyImmediate(go);
        else
#endif
            Destroy(go);
    }
}
