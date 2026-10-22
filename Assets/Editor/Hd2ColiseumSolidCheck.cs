using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reports whether the Rome Coliseum mesh collider lines up with the visible mesh.
/// Runs only while .utmp/coliseum_solid_check.requested exists.
/// </summary>
[InitializeOnLoad]
public static class Hd2ColiseumSolidCheck
{
    const string RequestName = "coliseum_solid_check.requested";
    const string ReportName = "coliseum_solid_check.txt";

    static bool running;
    static bool scheduled;

    static Hd2ColiseumSolidCheck()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (running || scheduled || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(ProjectFile(RequestName)))
            return;
        scheduled = true;
        EditorApplication.delayCall += Run;
    }

    static string ProjectFile(string name)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".utmp", name));
    }

    static void Run()
    {
        scheduled = false;
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (File.Exists(ProjectFile(RequestName)))
            {
                scheduled = true;
                EditorApplication.delayCall += Run;
            }
            return;
        }

        string request = ProjectFile(RequestName);
        if (!File.Exists(request))
            return;

        running = true;
        try
        {
            var scene = EditorSceneManager.GetActiveScene();
            var report = new StringBuilder();
            report.AppendLine("scene=" + scene.path);
            GameObject coliseum = null;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == "Coliseum")
                    coliseum = roots[i];
            }

            if (coliseum == null)
            {
                Write(report.AppendLine("ERROR no Coliseum").ToString());
                return;
            }

            report.AppendLine("root=" + coliseum.name
                + " pos=" + coliseum.transform.position.ToString("F2")
                + " scale=" + coliseum.transform.lossyScale.ToString("F3"));

            var filters = coliseum.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer visual = null;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                string path = filter.sharedMesh != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "null";
                report.AppendLine("filter " + PathOf(filter.transform, coliseum.transform)
                    + " mesh=" + (filter.sharedMesh != null ? filter.sharedMesh.name : "null")
                    + " path=" + path);
                if (visual == null && path.Replace('\\', '/').Contains("ColiseumNoFloors"))
                    visual = filter.GetComponent<MeshRenderer>();
            }

            var cols = coliseum.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                MeshCollider col = cols[i];
                string path = col.sharedMesh != null ? AssetDatabase.GetAssetPath(col.sharedMesh) : "null";
                report.AppendLine("collider " + PathOf(col.transform, coliseum.transform)
                    + " enabled=" + col.enabled
                    + " convex=" + col.convex
                    + " trigger=" + col.isTrigger
                    + " mesh=" + (col.sharedMesh != null ? col.sharedMesh.name : "NULL")
                    + " path=" + path
                    + " bounds=" + col.bounds.ToString("F1"));
            }

            if (visual != null)
            {
                MeshFilter filter = visual.GetComponent<MeshFilter>();
                bool added = visual.GetComponent<MeshCollider>() == null;
                MeshCollider onMesh = visual.GetComponent<MeshCollider>();
                if (onMesh == null)
                    onMesh = Undo.AddComponent<MeshCollider>(visual.gameObject);
                Undo.RecordObject(onMesh, "Coliseum collider");
                onMesh.sharedMesh = null;
                onMesh.sharedMesh = filter.sharedMesh;
                onMesh.convex = false;
                onMesh.isTrigger = false;
                onMesh.enabled = true;
                EditorUtility.SetDirty(onMesh);
                PrefabUtility.RecordPrefabInstancePropertyModifications(onMesh);

                int removed = 0;
                cols = coliseum.GetComponentsInChildren<MeshCollider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] == null || cols[i] == onMesh)
                        continue;
                    string path = cols[i].sharedMesh != null ? AssetDatabase.GetAssetPath(cols[i].sharedMesh) : "";
                    path = path.Replace('\\', '/');
                    if (!path.EndsWith("Assets/Models/Coliseum/ColiseumNoFloors.fbx", StringComparison.OrdinalIgnoreCase)
                        && !path.EndsWith("Assets/Models/Coliseum/Coliseum.fbx", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Undo.DestroyObjectImmediate(cols[i]);
                    removed++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                report.AppendLine("fixed added=" + added + " removed=" + removed + " saved=" + saved);
                report.AppendLine("meshScale=" + visual.transform.localScale.ToString("F3"));
                report.AppendLine("colliderBounds=" + onMesh.bounds.ToString("F1"));
                report.AppendLine("visualBounds=" + visual.bounds.ToString("F1"));
                Probe(report, "out+X", visual.bounds.center, Vector3.right, visual.bounds.extents.x + 8f);
                Probe(report, "out+Z", visual.bounds.center, Vector3.forward, visual.bounds.extents.z + 8f);
                Vector3 outside = visual.bounds.center + Vector3.right * (visual.bounds.extents.x + 4f);
                Probe(report, "in-X", outside, Vector3.left, visual.bounds.extents.x + 8f);
            }

            Write(report.ToString());
        }
        catch (Exception ex)
        {
            Write("ERROR " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            running = false;
            TryDelete(request);
        }
    }

    static void Probe(StringBuilder report, string label, Vector3 origin, Vector3 dir, float distance)
    {
        if (Physics.Raycast(origin, dir, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            report.AppendLine("ray " + label + " HIT " + hit.collider.name + " dist=" + hit.distance.ToString("F2"));
        else
            report.AppendLine("ray " + label + " MISS");
    }

    static string PathOf(Transform t, Transform root)
    {
        if (t == root)
            return t.name;
        var names = t.name;
        while (t.parent != null && t.parent != root)
        {
            t = t.parent;
            names = t.name + "/" + names;
        }
        return root.name + "/" + names;
    }

    static void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(ReportName)));
        File.WriteAllText(ProjectFile(ReportName), text);
        Debug.Log("Hd2ColiseumSolidCheck\n" + text);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
