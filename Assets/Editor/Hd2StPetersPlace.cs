using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts the reduced St Peter's model where Amphitheater East stands, then removes that amphitheater.
/// Runs only while .utmp/stpeters_place.requested exists. Does not open or reload a scene.
/// </summary>
[InitializeOnLoad]
public static class Hd2StPetersPlace
{
    const string MeshPath = "Assets/Models/StPeters/StPeters.fbx";
    const string MaterialPath = "Assets/Models/StPeters/StPeters.mat";
    const string AlbedoPath = "Assets/Models/StPeters/StPeters_Albedo.jpg";
    const string NormalPath = "Assets/Models/StPeters/StPeters_Normal.jpg";
    const string MaskPath = "Assets/Models/StPeters/StPeters_Mask.png";
    const string RequestName = "stpeters_place.requested";
    const string ReportName = "stpeters_place_report.txt";
    const string ObjectName = "St Peters";
    const string ReplacedName = "Amphitheater East";

    static readonly StaticEditorFlags LevelStatic =
        StaticEditorFlags.ContributeGI
        | StaticEditorFlags.OccluderStatic
        | StaticEditorFlags.BatchingStatic
        | StaticEditorFlags.OccludeeStatic
        | StaticEditorFlags.ReflectionProbeStatic;

    static bool running;
    static bool scheduled;
    static bool imported;
    static double started;

    static Hd2StPetersPlace()
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

        if (!imported)
        {
            AssetDatabase.ImportAsset(AlbedoPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(NormalPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(MaterialPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(MeshPath, ImportAssetOptions.ForceUpdate);
            imported = true;
        }

        Mesh mesh = LoadMesh();
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Texture albedo = material != null ? material.GetTexture("_BaseMap") : null;
        if (mesh == null || material == null || albedo == null)
        {
            if (EditorApplication.timeSinceStartup - started < 60d)
            {
                scheduled = true;
                EditorApplication.delayCall += Run;
                return;
            }

            Fail(request, "ERROR import failed mesh=" + (mesh != null) + " material=" + (material != null) + " albedo=" + (albedo != null));
            return;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(MeshPath);
        if (model == null)
        {
            Fail(request, "ERROR model prefab missing at " + MeshPath);
            return;
        }

        running = true;
        try
        {
            GameObject instance = FindRoot(scene, ObjectName);
            string status = "updated";
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                instance.name = ObjectName;
                status = "placed";
            }

            GameObject amphitheater = FindRoot(scene, ReplacedName);
            if (amphitheater == null)
            {
                SeatOnGround(instance);
                Finish(scene, instance, mesh, status + " seated", default, false, 0, request);
                return;
            }
            if (!CombinedBounds(amphitheater, out Bounds amphBounds))
                throw new InvalidOperationException(ReplacedName + " has no renderer bounds");

            // Same upright correction the amphitheater used, including its facing.
            instance.transform.localScale = Vector3.one;
            instance.transform.rotation = amphitheater.transform.rotation;
            instance.transform.position = amphBounds.center;

            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
                EditorUtility.SetDirty(renderers[i]);
            }

            if (!CombinedBounds(instance, out Bounds bounds))
                throw new InvalidOperationException("St Peters has no renderer bounds");
            Vector3 position = instance.transform.position;
            position.x += amphBounds.center.x - bounds.center.x;
            position.z += amphBounds.center.z - bounds.center.z;
            position.y += 0f - bounds.min.y;
            instance.transform.position = position;

            var transforms = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                GameObjectUtility.SetStaticEditorFlags(transforms[i].gameObject, LevelStatic);

            int colliders = 0;
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshCollider collider = filters[i].GetComponent<MeshCollider>();
                if (collider == null)
                    collider = filters[i].gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filters[i].sharedMesh;
                collider.convex = false;
                collider.isTrigger = false;
                EditorUtility.SetDirty(collider);
                colliders++;
            }

            UnityEngine.Object.DestroyImmediate(amphitheater);

            Finish(scene, instance, mesh, status, amphBounds, true, colliders, request);
        }
        catch (Exception e)
        {
            Fail(request, "ERROR\n" + e);
        }
        finally
        {
            running = false;
        }
    }

    static void SeatOnGround(GameObject instance)
    {
        if (!CombinedBounds(instance, out Bounds bounds))
            throw new InvalidOperationException("St Peters has no renderer bounds");
        Vector3 position = instance.transform.position;
        position.y += 0f - bounds.min.y;
        instance.transform.position = position;
    }

    static void Finish(Scene scene, GameObject instance, Mesh mesh, string status, Bounds replaced, bool removedAmphitheater, int colliders, string request)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        var report = new StringBuilder();
        report.AppendLine("OK");
        report.Append(Describe(instance, scene, saved, status));
        report.AppendLine("colliders=" + colliders);
        report.AppendLine("mesh_verts=" + mesh.vertexCount + " mesh_tris=" + Tris(mesh));
        if (removedAmphitheater)
        {
            report.AppendLine("replaced=" + ReplacedName
                + " center=" + replaced.center.ToString("F2")
                + " size=" + replaced.size.ToString("F2")
                + " minY=" + replaced.min.y.ToString("F3"));
        }
        report.AppendLine("amphitheater_removed=" + (FindRoot(scene, ReplacedName) == null));
        WriteReport(report.ToString());
        TryDelete(request);
        started = 0d;
        imported = false;
    }

    static string Describe(GameObject instance, Scene scene, bool saved, string status)
    {
        var report = new StringBuilder();
        report.AppendLine("status=" + status);
        report.AppendLine("scene=" + scene.path + " saved=" + saved);
        report.AppendLine("name=" + instance.name
            + " pos=" + instance.transform.position.ToString("F3")
            + " euler=" + instance.transform.eulerAngles.ToString("F1")
            + " scale=" + instance.transform.localScale.ToString("F3")
            + " static=" + GameObjectUtility.GetStaticEditorFlags(instance));
        if (CombinedBounds(instance, out Bounds bounds))
            report.AppendLine("world_bounds center=" + bounds.center.ToString("F2") + " size=" + bounds.size.ToString("F2") + " minY=" + bounds.min.y.ToString("F3"));
        var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            Texture baseMap = mat != null ? mat.GetTexture("_BaseMap") : null;
            Texture normal = mat != null ? mat.GetTexture("_BumpMap") : null;
            Texture mask = mat != null ? mat.GetTexture("_MetallicGlossMap") : null;
            var filter = renderers[i].GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            report.AppendLine(
                "renderer=" + renderers[i].name
                + " verts=" + (mesh != null ? mesh.vertexCount : 0)
                + " tris=" + (mesh != null ? Tris(mesh) : 0)
                + " material=" + (mat != null ? mat.name : "none")
                + " shader=" + (mat != null && mat.shader != null ? mat.shader.name : "none")
                + " base=" + DescribeTex(baseMap)
                + " normal=" + DescribeTex(normal)
                + " mask=" + DescribeTex(mask));
        }

        return report.ToString();
    }

    static bool CombinedBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!any)
            {
                bounds = renderers[i].bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        return any;
    }

    static Mesh LoadMesh()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(MeshPath);
        Mesh best = null;
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Mesh candidate && candidate.vertexCount > 0 && (best == null || candidate.vertexCount > best.vertexCount))
                best = candidate;
        }

        return best;
    }

    static int Tris(Mesh mesh)
    {
        int count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            count += (int)mesh.GetIndexCount(i) / 3;
        return count;
    }

    static string DescribeTex(Texture texture)
    {
        if (texture == null)
            return "MISSING";
        return texture.name + " " + texture.width + "x" + texture.height;
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
        imported = false;
    }

    static void WriteReport(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(ReportName)));
        File.WriteAllText(ProjectFile(ReportName), text);
        Debug.Log("Hd2StPetersPlace\n" + text);
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
