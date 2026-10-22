using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Swaps the Rome Coliseum mesh for the no-floors model and keeps the stone material.
/// Runs only while .utmp/coliseum_nofloors.requested exists. Does not open or reload a scene.
/// </summary>
[InitializeOnLoad]
public static class Hd2ColiseumNoFloors
{
    const string MeshPath = "Assets/Models/Coliseum/ColiseumNoFloors.fbx";
    const string MaterialPath = "Assets/Models/Coliseum/Coliseum.mat";
    const string RequestName = "coliseum_nofloors.requested";
    const string ReportName = "coliseum_nofloors_report.txt";

    static bool running;
    static bool scheduled;
    static double started;

    static Hd2ColiseumNoFloors()
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
        if (running)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        string request = ProjectFile(RequestName);
        if (!File.Exists(request))
            return;

        if (OthersBusy() || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        if (started <= 0d)
            started = EditorApplication.timeSinceStartup;

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.path.Replace('\\', '/').EndsWith("Assets/Scenes/Rome.unity", StringComparison.OrdinalIgnoreCase))
        {
            Fail(request, "ERROR active scene is " + scene.path);
            return;
        }

        AssetDatabase.ImportAsset(MeshPath, ImportAssetOptions.ForceUpdate);
        Mesh mesh = LoadMesh();
        if (mesh == null)
        {
            if (EditorApplication.timeSinceStartup - started < 45d)
            {
                scheduled = true;
                EditorApplication.delayCall += Run;
                return;
            }

            Fail(request, "ERROR mesh did not import from " + MeshPath);
            return;
        }

        GameObject coliseum = FindRoot(scene, "Coliseum");
        if (coliseum == null)
        {
            Fail(request, "ERROR Coliseum was not in the open Rome scene");
            return;
        }

        var stone = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (stone == null || stone.GetTexture("_BaseMap") == null)
        {
            Fail(request, "ERROR Coliseum stone material is missing its base texture");
            return;
        }

        var filters = coliseum.GetComponentsInChildren<MeshFilter>(true);
        MeshFilter target = null;
        for (int i = 0; i < filters.Length; i++)
        {
            if (IsCurrentColiseum(filters[i].sharedMesh))
            {
                target = filters[i];
                break;
            }
        }

        if (target == null)
        {
            Fail(request, "ERROR Coliseum mesh filter was not on " + coliseum.name);
            return;
        }

        Mesh oldMesh = target.sharedMesh;
        Vector3 oldSize = oldMesh.bounds.size;
        Vector3 newSize = mesh.bounds.size;
        if (Relative(oldSize.x, newSize.x) > 0.05f
            || Relative(oldSize.y, newSize.y) > 0.08f
            || Relative(oldSize.z, newSize.z) > 0.05f)
        {
            Fail(request, "ERROR mesh size drifted old=" + oldSize.ToString("F4") + " new=" + newSize.ToString("F4"));
            return;
        }

        running = true;
        try
        {
            Undo.SetCurrentGroupName("Swap Coliseum no-floors mesh");
            int undo = Undo.GetCurrentGroup();

            Undo.RecordObject(target, "Swap Coliseum mesh");
            target.sharedMesh = mesh;
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);

            var renderer = target.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Undo.RecordObject(renderer, "Assign Coliseum stone");
                renderer.sharedMaterial = stone;
                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }

            int colliders = PlaceCollider(coliseum, target, mesh);

            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);

            var report = new StringBuilder();
            report.AppendLine("OK");
            report.AppendLine("scene=" + scene.path + " saved=" + saved);
            report.AppendLine("pos=" + coliseum.transform.position.ToString("F3") + " scale=" + coliseum.transform.localScale.ToString("F3"));
            report.AppendLine("old=" + oldMesh.name + " verts=" + oldMesh.vertexCount + " tris=" + Tris(oldMesh) + " size=" + oldSize.ToString("F4"));
            report.AppendLine("new=" + mesh.name + " verts=" + mesh.vertexCount + " tris=" + Tris(mesh) + " size=" + newSize.ToString("F4"));
            report.AppendLine("filters=1 colliders=" + colliders);
            AppendRenderer(renderer, report);
            AppendRing(report, "Ring");
            AppendRing(report, "Ring (1)");
            AppendRing(report, "Ring (2)");
            var world = renderer != null ? renderer.bounds : default;
            report.AppendLine("world_bounds center=" + world.center.ToString("F2") + " size=" + world.size.ToString("F2"));
            WriteReport(report.ToString());
            TryDelete(request);
        }
        catch (Exception ex)
        {
            Fail(request, "ERROR " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            running = false;
            started = 0d;
        }
    }

    public static void RunFromBatch()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Rome.unity", OpenSceneMode.Single);
        string result = Swap(scene);
        WriteReport(result);
        if (!result.StartsWith("OK", StringComparison.Ordinal))
            throw new InvalidOperationException(result);
    }

    static string Swap(Scene scene)
    {
        AssetDatabase.ImportAsset(MeshPath, ImportAssetOptions.ForceUpdate);
        Mesh mesh = LoadMesh();
        if (mesh == null)
            return "ERROR mesh did not import from " + MeshPath;

        GameObject coliseum = FindRoot(scene, "Coliseum");
        if (coliseum == null)
            return "ERROR Coliseum was not in Rome";

        var stone = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (stone == null || stone.GetTexture("_BaseMap") == null)
            return "ERROR Coliseum stone material is missing its base texture";

        var filters = coliseum.GetComponentsInChildren<MeshFilter>(true);
        MeshFilter target = null;
        for (int i = 0; i < filters.Length; i++)
        {
            if (IsCurrentColiseum(filters[i].sharedMesh))
            {
                target = filters[i];
                break;
            }
        }

        if (target == null)
            return "ERROR Coliseum mesh filter was not on " + coliseum.name;

        Mesh oldMesh = target.sharedMesh;
        Vector3 oldSize = oldMesh.bounds.size;
        Vector3 newSize = mesh.bounds.size;
        if (Relative(oldSize.x, newSize.x) > 0.05f
            || Relative(oldSize.y, newSize.y) > 0.08f
            || Relative(oldSize.z, newSize.z) > 0.05f)
            return "ERROR mesh size drifted old=" + oldSize.ToString("F4") + " new=" + newSize.ToString("F4");

        target.sharedMesh = mesh;
        EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);

        var renderer = target.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = stone;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        int colliders = PlaceCollider(coliseum, target, mesh);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);

        var report = new StringBuilder();
        report.AppendLine("OK");
        report.AppendLine("scene=" + scene.path + " saved=" + saved);
        report.AppendLine("pos=" + coliseum.transform.position.ToString("F3") + " scale=" + coliseum.transform.localScale.ToString("F3"));
        report.AppendLine("old=" + oldMesh.name + " verts=" + oldMesh.vertexCount + " tris=" + Tris(oldMesh) + " size=" + oldSize.ToString("F4"));
        report.AppendLine("new=" + mesh.name + " verts=" + mesh.vertexCount + " tris=" + Tris(mesh) + " size=" + newSize.ToString("F4"));
        report.AppendLine("filters=1 colliders=" + colliders);
        AppendRenderer(renderer, report);
        AppendRing(report, "Ring");
        AppendRing(report, "Ring (1)");
        AppendRing(report, "Ring (2)");
        var world = renderer != null ? renderer.bounds : default;
        report.AppendLine("world_bounds center=" + world.center.ToString("F2") + " size=" + world.size.ToString("F2"));
        return report.ToString();
    }

    static Mesh LoadMesh()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(MeshPath);
        Mesh best = null;
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Mesh candidate && candidate.vertexCount > 0)
                best = candidate;
        }

        return best;
    }

    static int PlaceCollider(GameObject root, MeshFilter target, Mesh mesh)
    {
        MeshCollider placed = target.GetComponent<MeshCollider>();
        if (placed == null)
            placed = target.gameObject.AddComponent<MeshCollider>();
        placed.sharedMesh = null;
        placed.sharedMesh = mesh;
        placed.convex = false;
        placed.isTrigger = false;
        EditorUtility.SetDirty(placed);
        PrefabUtility.RecordPrefabInstancePropertyModifications(placed);

        var cols = root.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || cols[i] == placed || !IsCurrentColiseum(cols[i].sharedMesh))
                continue;
            UnityEngine.Object.DestroyImmediate(cols[i]);
        }

        return 1;
    }

    static bool IsCurrentColiseum(Mesh mesh)
    {
        if (mesh == null)
            return false;
        string path = AssetDatabase.GetAssetPath(mesh).Replace('\\', '/');
        return path.EndsWith("Assets/Models/Coliseum/Coliseum.fbx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Assets/Models/Coliseum/ColiseumNoFloors.fbx", StringComparison.OrdinalIgnoreCase);
    }

    static int Tris(Mesh mesh)
    {
        int count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            count += (int)mesh.GetIndexCount(i) / 3;
        return count;
    }

    static float Relative(float oldValue, float newValue)
    {
        return Mathf.Abs(newValue - oldValue) / Mathf.Max(0.0001f, Mathf.Abs(oldValue));
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == name)
                return roots[i];
        }

        return null;
    }

    static void AppendRenderer(MeshRenderer renderer, StringBuilder report)
    {
        if (renderer == null)
        {
            report.AppendLine("renderer=missing");
            return;
        }

        Material mat = renderer.sharedMaterial;
        Texture baseMap = mat != null ? mat.GetTexture("_BaseMap") : null;
        Texture normal = mat != null ? mat.GetTexture("_BumpMap") : null;
        Texture mask = mat != null ? mat.GetTexture("_MetallicGlossMap") : null;
        report.AppendLine(
            "material=" + (mat != null ? mat.name : "none")
            + " shader=" + (mat != null && mat.shader != null ? mat.shader.name : "none")
            + " base=" + Describe(baseMap)
            + " normal=" + Describe(normal)
            + " mask=" + Describe(mask));
    }

    static string Describe(Texture texture)
    {
        if (texture == null)
            return "MISSING";
        return texture.name + " " + texture.width + "x" + texture.height;
    }

    static void AppendRing(StringBuilder report, string name)
    {
        var ring = GameObject.Find(name);
        if (ring == null)
        {
            report.AppendLine(name + "=missing");
            return;
        }

        Transform parent = ring.transform.parent;
        report.AppendLine(
            name
            + " parent=" + (parent != null ? parent.name : "none")
            + " local=" + ring.transform.localPosition.ToString("F4"));
    }

    static bool OthersBusy()
    {
        string dir = Path.GetDirectoryName(ProjectFile(RequestName));
        if (!Directory.Exists(dir))
            return false;
        string[] files = Directory.GetFiles(dir, "*.requested");
        for (int i = 0; i < files.Length; i++)
        {
            if (!files[i].EndsWith(RequestName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static void Fail(string request, string message)
    {
        WriteReport(message);
        TryDelete(request);
        started = 0d;
    }

    static void WriteReport(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(ReportName)));
        File.WriteAllText(ProjectFile(ReportName), text);
        Debug.Log("Hd2ColiseumNoFloors\n" + text);
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
