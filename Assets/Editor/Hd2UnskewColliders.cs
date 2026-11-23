using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > HD2 > Unskew Colliders In Open Scene.
/// An object rotated by an angle that isn't a multiple of 90 degrees, under a parent that
/// is scaled unevenly (e.g. the arena at X 0.799), is drawn skewed, but physics can't skew
/// a collider, so its collision box no longer matches what you see: shots land off the
/// surface and the body can walk into the visible wall. This moves each such object with a
/// collider to an unscaled copy of its parent ("arena (unscaled)"), keeping its position
/// and rotation. It then becomes a plain rotated box whose collider matches its mesh.
/// Undo works. Save the scene afterwards.
/// </summary>
static class Hd2UnskewColliders
{
    [MenuItem("Tools/HD2/Unskew Colliders In Open Scene")]
    static void Run()
    {
        var moved = new List<string>();
        var holders = new Dictionary<Transform, Transform>();
        var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var done = new HashSet<Transform>();
        foreach (Collider collider in colliders)
        {
            Transform target = collider.transform;
            if (done.Contains(target) || EditorUtility.IsPersistent(target) || !Skewed(target))
                continue;

            Transform scaled = UnevenAncestor(target);
            if (scaled == null)
                continue;

            if (!holders.TryGetValue(scaled, out Transform holder))
            {
                holder = FindOrMakeHolder(scaled);
                holders[scaled] = holder;
            }

            // The direct child of the scaled parent that carries this object.
            Transform top = target;
            while (top.parent != scaled)
                top = top.parent;
            if (done.Contains(top))
                continue;

            Undo.SetTransformParent(top, holder, true, "Unskew colliders");
            done.Add(top);
            done.Add(target);
            moved.Add(top.name);
        }

        if (moved.Count == 0)
        {
            Debug.Log("Unskew colliders: nothing skewed in the open scene.");
            return;
        }

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("Unskew colliders: moved " + moved.Count + " object(s) out of unevenly scaled parents: "
            + string.Join(", ", moved) + ". Save the scene.");
    }

    static bool Skewed(Transform t)
    {
        Matrix4x4 m = t.localToWorldMatrix;
        Vector3 x = ((Vector3)m.GetColumn(0)).normalized;
        Vector3 y = ((Vector3)m.GetColumn(1)).normalized;
        Vector3 z = ((Vector3)m.GetColumn(2)).normalized;
        const float tolerance = 0.002f;
        return Mathf.Abs(Vector3.Dot(x, y)) > tolerance
            || Mathf.Abs(Vector3.Dot(y, z)) > tolerance
            || Mathf.Abs(Vector3.Dot(x, z)) > tolerance;
    }

    static Transform UnevenAncestor(Transform t)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            Vector3 s = p.localScale;
            if (Mathf.Abs(s.x - s.y) > 0.0001f || Mathf.Abs(s.y - s.z) > 0.0001f)
                return p;
        }

        return null;
    }

    static Transform FindOrMakeHolder(Transform scaled)
    {
        string holderName = scaled.name + " (unscaled)";
        Transform home = scaled.parent;
        if (home != null)
        {
            Transform found = home.Find(holderName);
            if (found != null)
                return found;
        }
        else
        {
            foreach (GameObject root in scaled.gameObject.scene.GetRootGameObjects())
            {
                if (root.name == holderName)
                    return root.transform;
            }
        }

        var holder = new GameObject(holderName);
        Undo.RegisterCreatedObjectUndo(holder, "Unskew colliders");
        SceneManagerMove(holder, scaled);
        holder.transform.SetParent(home, false);
        holder.transform.SetPositionAndRotation(scaled.position, scaled.rotation);
        holder.transform.localScale = Vector3.one;
        holder.transform.SetSiblingIndex(scaled.GetSiblingIndex() + 1);
        holder.isStatic = scaled.gameObject.isStatic;
        holder.layer = scaled.gameObject.layer;
        return holder.transform;
    }

    static void SceneManagerMove(GameObject holder, Transform like)
    {
        if (holder.scene != like.gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, like.gameObject.scene);
    }
}
