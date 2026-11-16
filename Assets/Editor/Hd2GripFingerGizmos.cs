using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws GripFingers like skeleton bones in the Scene view and prefab stage.
/// Nothing from this file is included in a player build.
/// </summary>
[InitializeOnLoad]
static class Hd2GripFingerGizmos
{
    static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

    static readonly Color[] Colors =
    {
        new Color(1f, 0.82f, 0.25f),
        new Color(0.35f, 0.85f, 1f),
        new Color(0.45f, 0.95f, 0.45f),
        new Color(1f, 0.55f, 0.25f),
        new Color(0.85f, 0.45f, 1f),
    };

    static Hd2GripFingerGizmos()
    {
        SceneView.duringSceneGui += Draw;
    }

    static void Draw(SceneView view)
    {
        if (EditorApplication.isPlaying)
            return;

        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
        {
            DrawUnder(stage.prefabContentsRoot.transform);
            return;
        }

        Transform[] selected = Selection.transforms;
        for (int i = 0; i < selected.Length; i++)
        {
            if (selected[i] != null)
                DrawUnder(selected[i]);
        }
    }

    static void DrawUnder(Transform root)
    {
        if (root == null)
            return;
        if (root.name == "GripFingers")
        {
            DrawRig(root);
            return;
        }

        for (int i = 0; i < root.childCount; i++)
            DrawUnder(root.GetChild(i));
    }

    static void DrawRig(Transform grip)
    {
        CompareFunction previous = Handles.zTest;
        Handles.zTest = CompareFunction.Always;
        for (int i = 0; i < Fingers.Length; i++)
        {
            Transform bone = grip.Find(Fingers[i]);
            if (bone == null)
                continue;

            Transform mid = bone.Find(Fingers[i] + ".001");
            Transform tip = mid != null ? mid.Find(Fingers[i] + ".Tip") : null;
            Handles.color = Colors[i];
            if (mid != null)
                DrawBone(bone.position, mid.position);
            if (mid != null && tip != null)
                DrawBone(mid.position, tip.position);
            DrawJoint(bone.position);
            if (mid != null)
                DrawJoint(mid.position);
            if (tip != null)
            {
                DrawJoint(tip.position);
                Handles.Label(tip.position, Fingers[i]);
            }
        }

        Handles.zTest = previous;
    }

    static void DrawBone(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length < 0.0001f)
            return;

        Vector3 direction = delta / length;
        Vector3 side = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.right : Vector3.up);
        if (side.sqrMagnitude < 0.0001f)
            side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(direction, side);

        float width = Mathf.Max(length * 0.22f, 0.006f);
        Vector3 neck = start + direction * (length * 0.22f);
        Vector3 a = neck + up * width;
        Vector3 b = neck - up * width;
        Vector3 c = neck + side * width;
        Vector3 d = neck - side * width;

        ThickLine(start, a);
        ThickLine(start, b);
        ThickLine(start, c);
        ThickLine(start, d);
        ThickLine(a, c);
        ThickLine(c, b);
        ThickLine(b, d);
        ThickLine(d, a);
        ThickLine(a, end);
        ThickLine(b, end);
        ThickLine(c, end);
        ThickLine(d, end);
    }

    static void ThickLine(Vector3 a, Vector3 b)
    {
        Handles.DrawAAPolyLine(3f, a, b);
    }

    static void DrawJoint(Vector3 position)
    {
        float size = HandleUtility.GetHandleSize(position) * 0.08f;
        Handles.SphereHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
    }
}
