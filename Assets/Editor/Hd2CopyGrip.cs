using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies the SMG's grip (hand pose and fingers) onto the other GripWeapons prefabs as a starting point.
/// The hand is placed relative to each gun's own trigger and barrel, so it lands in roughly the same
/// spot on every gun: same distance from the trigger, same angle to the barrel. Fine-tune each gun after.
/// Run it from HD2 > Copy SMG Grip To Other Guns. Writes a report to Logs/Hd2CopyGrip.txt.
/// </summary>
static class Hd2CopyGrip
{
    const string Folder = "Assets/Prefabs/GripWeapons/";
    const string Source = "GripSmg";
    static readonly string[] Targets = { "GripPistol", "GripBurst", "GripLaser", "GripShock", "GripShotgun", "GripRocket", "GripDropShield" };

    [MenuItem("HD2/Copy SMG Grip To Other Guns")]
    static void RunFromMenu()
    {
        if (EditorUtility.DisplayDialog("Copy SMG grip",
                "Replace the hand pose on GripPistol, GripBurst, GripLaser, GripShock, GripShotgun, GripRocket and GripDropShield with the SMG's?",
                "Copy", "Cancel"))
            Run();
    }

    static void Run()
    {
        var report = new StringBuilder();
        GameObject sourceRoot = PrefabUtility.LoadPrefabContents(Folder + Source + ".prefab");
        try
        {
            var sourcePose = sourceRoot.GetComponent<Hd2GripPose>();
            Transform sourceWrist = Hd2GripFingers.Find(sourceRoot.transform, "Wrist");
            if (sourcePose == null || !sourcePose.HasFingers || sourceWrist == null)
            {
                report.AppendLine("GripSmg has no posed grip yet. Nothing copied.");
                Finish(report);
                return;
            }

            if (!TryFrame(sourceRoot.transform, out Vector3 sourceTrigger, out Quaternion sourceFrame, out string why))
            {
                report.AppendLine("GripSmg: " + why + ". Nothing copied.");
                Finish(report);
                return;
            }

            // The hand relative to the SMG's trigger, in the SMG's barrel frame. Meters, not scaled:
            // the hand is the same size on every gun.
            Quaternion toFrame = Quaternion.Inverse(sourceFrame);
            Vector3 offset = toFrame * (sourceWrist.position - sourceTrigger);
            Quaternion turn = toFrame * sourceWrist.rotation;
            report.AppendLine($"GripSmg: hand {offset * 100f} cm from the trigger (barrel frame).");

            foreach (string name in Targets)
                CopyTo(name, offset, turn, sourcePose, report);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(sourceRoot);
        }

        Finish(report);
    }

    static void CopyTo(string name, Vector3 offset, Quaternion turn, Hd2GripPose sourcePose, StringBuilder report)
    {
        string path = Folder + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            report.AppendLine(name + ": prefab not found, skipped.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!TryFrame(root.transform, out Vector3 trigger, out Quaternion frame, out string why))
            {
                report.AppendLine(name + ": " + why + ", skipped.");
                return;
            }

            Transform wrist = Hd2GripFingers.Find(root.transform, "Wrist");
            if (wrist == null)
            {
                wrist = new GameObject("Wrist").transform;
                wrist.SetParent(root.transform, false);
            }

            wrist.SetPositionAndRotation(trigger + frame * offset, frame * turn);

            var pose = root.GetComponent<Hd2GripPose>();
            if (pose == null)
                pose = root.AddComponent<Hd2GripPose>();
            pose.baked = true;
            pose.fingers = (Quaternion[])sourcePose.fingers.Clone();

            Transform markers = Hd2GripFingers.Find(root.transform, "GripFingers");
            if (markers != null)
                Object.DestroyImmediate(markers.gameObject);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            report.AppendLine(name + ": copied.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // A frame from the gun's own shape: forward from the trigger toward the muzzle,
    // up toward the middle of the gun's biggest mesh (the body sits above the trigger).
    static bool TryFrame(Transform root, out Vector3 trigger, out Quaternion frame, out string why)
    {
        trigger = Vector3.zero;
        frame = Quaternion.identity;
        why = null;

        Transform triggerPart = FindMesh(root, "Trigger");
        if (triggerPart == null)
        {
            why = "no Trigger mesh";
            return false;
        }

        Transform muzzle = Hd2GripFingers.Find(root, "Muzzle");
        if (muzzle == null)
        {
            why = "no Muzzle";
            return false;
        }

        trigger = MeshCenter(triggerPart, out _);
        Vector3 body = BiggestMeshCenter(root, triggerPart);
        Vector3 forward = muzzle.position - trigger;
        Vector3 up = Vector3.ProjectOnPlane(body - trigger, forward);
        if (forward.sqrMagnitude < 0.000001f || up.sqrMagnitude < 0.000001f)
        {
            why = "can't tell which way the barrel points";
            return false;
        }

        frame = Quaternion.LookRotation(forward.normalized, up.normalized);
        return true;
    }

    static Transform FindMesh(Transform root, string name)
    {
        if (string.Equals(root.name, name, System.StringComparison.OrdinalIgnoreCase) && MeshOf(root) != null)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindMesh(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static Mesh MeshOf(Transform part)
    {
        var filter = part.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
            return filter.sharedMesh;
        var skin = part.GetComponent<SkinnedMeshRenderer>();
        return skin != null ? skin.sharedMesh : null;
    }

    static Vector3 MeshCenter(Transform part, out float size)
    {
        Mesh mesh = MeshOf(part);
        size = 0f;
        if (mesh == null)
            return part.position;
        Bounds bounds = mesh.bounds;
        size = Vector3.Scale(bounds.size, part.lossyScale).sqrMagnitude;
        return part.TransformPoint(bounds.center);
    }

    static Vector3 BiggestMeshCenter(Transform root, Transform skip)
    {
        Vector3 best = root.position;
        float bestSize = -1f;
        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
        {
            if (part == skip || part.GetComponent<MeshFilter>() == null)
                continue;
            Vector3 center = MeshCenter(part, out float size);
            if (size > bestSize)
            {
                bestSize = size;
                best = center;
            }
        }

        return best;
    }

    static void Finish(StringBuilder report)
    {
        string text = report.ToString().TrimEnd();
        Debug.Log("Copy SMG grip:\n" + text);
        try
        {
            File.WriteAllText("Logs/Hd2CopyGrip.txt", text);
        }
        catch (IOException)
        {
        }

        AssetDatabase.Refresh();
    }
}
