using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Hd2Body))]
public class Hd2BodyPivotEditor : Editor
{
    void OnSceneGUI()
    {
        var body = (Hd2Body)target;
        if (Application.isPlaying)
            return;

        Draw(body.shoulderPivotL, "LeftShoulderPivot");
        Draw(body.shoulderPivotR, "RightShoulderPivot");
        Draw(body.gripPivotL, "LeftGripPivot");
        Draw(body.gripPivotR, "RightGripPivot");
    }

    static void Draw(Transform pivot, string label)
    {
        if (pivot == null)
            return;

        bool left = label.Contains("Left");
        Handles.color = label.Contains("Grip")
            ? (left ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 0.85f, 0.3f))
            : (left ? new Color(0.3f, 0.75f, 1f) : new Color(1f, 0.55f, 0.2f));
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(pivot.position, pivot.rotation);
        if (!EditorGUI.EndChangeCheck())
        {
            Handles.Label(pivot.position, label);
            return;
        }

        Undo.RecordObject(pivot, "Move pivot");
        pivot.position = moved;
        EditorUtility.SetDirty(pivot);
    }
}
