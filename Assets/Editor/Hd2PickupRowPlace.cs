using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places one instance of each pickup prefab that is not already in the open
/// Towers scene. Runs only while .utmp/pickup_row_place.requested exists.
/// Does not open or reload a scene.
/// </summary>
[InitializeOnLoad]
public static class Hd2PickupRowPlace
{
    const string PickupDir = "Assets/Prefabs/Pickups";
    const string RequestName = "pickup_row_place.requested";
    const string ReportName = "pickup_row_place_report.txt";
    const float Spacing = 3f;
    const float Clearance = 1.5f;

    static int tries;
    static bool running;
    static bool scheduled;
    static readonly List<GameObject> Spawned = new List<GameObject>();

    static Hd2PickupRowPlace()
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
            if (EditorApplication.isPlayingOrWillChangePlaymode && ++tries > 30)
            {
                WriteReport("ERROR play mode is active; left the scene untouched");
                TryDelete(request);
                return;
            }
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.path.Replace('\\', '/').EndsWith("Assets/Scenes/Towers.unity", StringComparison.OrdinalIgnoreCase))
        {
            WriteReport("ERROR active scene is '" + scene.path + "'; did not open or reload a scene");
            TryDelete(request);
            return;
        }

        if (SceneJustWritten(scene))
        {
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        var prefabs = LoadPrefabs();
        if (prefabs.Count < 7)
        {
            if (++tries > 400)
            {
                WriteReport("ERROR timed out waiting for pickup prefabs. found=" + prefabs.Count);
                TryDelete(request);
                return;
            }
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        running = true;
        Spawned.Clear();
        try
        {
            Place(scene, prefabs);
            TryDelete(request);
        }
        catch (Exception e)
        {
            for (int i = 0; i < Spawned.Count; i++)
            {
                if (Spawned[i] != null)
                    UnityEngine.Object.DestroyImmediate(Spawned[i]);
            }
            Spawned.Clear();
            WriteReport("ERROR\n" + e);
            TryDelete(request);
        }
        finally
        {
            running = false;
        }
    }

    static void Place(Scene scene, List<GameObject> prefabs)
    {
        var existing = FindPickupInstances(scene);
        var before = SnapshotWatched(scene, existing);

        var missing = new List<GameObject>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            string path = AssetDatabase.GetAssetPath(prefabs[i]);
            if (!existing.ContainsKey(path))
                missing.Add(prefabs[i]);
        }

        var sb = new StringBuilder();
        if (missing.Count == 0)
        {
            sb.AppendLine("OK");
            sb.AppendLine("scene=" + scene.path + " saved=False dirty=" + scene.isDirty);
            sb.AppendLine("added=0");
            AppendExisting(existing, sb);
            AppendWatched(before, SnapshotWatched(scene, existing), sb);
            WriteReport(sb.ToString());
            return;
        }

        var coliseum = FindNamed(scene, "Coliseum");
        if (coliseum == null || !TryBounds(coliseum, out Bounds coliseumBounds))
            throw new InvalidOperationException("Coliseum was not in the open Towers scene");

        var blockers = CollectBlockers(scene);
        if (!TryFindRow(coliseumBounds, missing.Count, blockers, out float originX, out float rowZ, out string search))
            throw new InvalidOperationException("No clear row north of the Coliseum\n" + search);

        var placed = new List<GameObject>();
        for (int i = 0; i < missing.Count; i++)
        {
            float x = originX + (i - (missing.Count - 1) * 0.5f) * Spacing;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(missing[i], scene);
            Spawned.Add(go);
            go.name = missing[i].name;
            if (go.transform.parent != null)
                go.transform.SetParent(null, true);
            SeatPad(go, x, rowZ);
            placed.Add(go);
        }

        for (int i = 0; i < placed.Count; i++)
        {
            if (!TrySample(placed[i].transform.position.x, placed[i].transform.position.z, out float ground))
                throw new InvalidOperationException("Terrain missing under " + placed[i].name);
            float bottom = PadBottom(placed[i]);
            if (bottom < ground - 0.05f || bottom > ground + 0.05f)
                throw new InvalidOperationException(placed[i].name + " pad bottom " + bottom.ToString("F3") + " terrain " + ground.ToString("F3"));
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(placed[i]))
                throw new InvalidOperationException(placed[i].name + " is not a prefab instance");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);

        sb.AppendLine("OK");
        sb.AppendLine("scene=" + scene.path + " saved=" + saved + " dirty=" + scene.isDirty);
        sb.AppendLine("row_origin_x=" + originX.ToString("F3", CultureInfo.InvariantCulture) + " row_z=" + rowZ.ToString("F3", CultureInfo.InvariantCulture) + " spacing=" + Spacing.ToString("F2", CultureInfo.InvariantCulture));
        sb.AppendLine("coliseum_center=" + coliseumBounds.center.ToString("F3") + " coliseum_max=" + coliseumBounds.max.ToString("F3"));
        sb.AppendLine("search");
        sb.AppendLine(search);
        sb.AppendLine("added=" + placed.Count);
        for (int i = 0; i < placed.Count; i++)
            AppendInstance("ADDED", placed[i], sb);
        AppendExisting(existing, sb);
        AppendWatched(before, SnapshotWatched(scene, existing), sb);
        WriteReport(sb.ToString());
    }

    static void SeatPad(GameObject go, float x, float z)
    {
        if (!TrySample(x, z, out float ground))
            throw new InvalidOperationException("No terrain at " + go.name);
        go.transform.position = new Vector3(x, ground, z);
        float bottom = PadBottom(go);
        float dy = ground - bottom;
        if (Mathf.Abs(dy) > 0.0001f)
            go.transform.position = new Vector3(x, go.transform.position.y + dy, z);
    }

    static float PadBottom(GameObject go)
    {
        var transforms = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name != "Pad")
                continue;
            var renderer = transforms[i].GetComponent<Renderer>();
            if (renderer != null)
                return renderer.bounds.min.y;
        }
        float min = float.PositiveInfinity;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            min = Mathf.Min(min, renderers[i].bounds.min.y);
        if (float.IsPositiveInfinity(min))
            return go.transform.position.y;
        return min;
    }

    static bool TryFindRow(Bounds coliseum, int count, List<Obstacle> blockers, out float originX, out float rowZ, out string search)
    {
        originX = coliseum.center.x;
        rowZ = 0f;
        var log = new StringBuilder();
        float zBegin = coliseum.max.z + 3f;
        float zEnd = coliseum.max.z + 28f;
        float templeCap = float.PositiveInfinity;
        for (int i = 0; i < blockers.Count; i++)
        {
            if (blockers[i].name.IndexOf("Temple", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (blockers[i].bounds.min.z <= coliseum.max.z)
                continue;
            if (blockers[i].bounds.max.x < coliseum.center.x - 40f || blockers[i].bounds.min.x > coliseum.center.x + 40f)
                continue;
            templeCap = Mathf.Min(templeCap, blockers[i].bounds.min.z - 3f);
        }
        if (!float.IsPositiveInfinity(templeCap))
            zEnd = Mathf.Min(zEnd, templeCap);
        log.AppendLine("  zBegin=" + zBegin.ToString("F2") + " zEnd=" + zEnd.ToString("F2"));

        float[] shifts = { 0f, 3f, -3f, 6f, -6f, 9f, -9f, 12f, -12f };
        for (float z = zBegin; z <= zEnd + 0.01f; z += 1f)
        {
            for (int s = 0; s < shifts.Length; s++)
            {
                float x0 = coliseum.center.x + shifts[s];
                if (RowClear(x0, z, count, blockers, out string hit))
                {
                    originX = x0;
                    rowZ = z;
                    log.AppendLine("  chosen x0=" + x0.ToString("F2") + " z=" + z.ToString("F2"));
                    search = log.ToString();
                    return true;
                }
                if (s == 0 && Mathf.Abs(z - zBegin) < 0.01f)
                    log.AppendLine("  blocked z=" + z.ToString("F2") + " by " + hit);
            }
        }
        search = log.ToString();
        return false;
    }

    static bool RowClear(float originX, float z, int count, List<Obstacle> blockers, out string hit)
    {
        hit = "";
        for (int i = 0; i < count; i++)
        {
            float x = originX + (i - (count - 1) * 0.5f) * Spacing;
            if (!TrySample(x, z, out float ground))
            {
                hit = "no terrain";
                return false;
            }
            var box = new Bounds(new Vector3(x, ground + 1f, z), new Vector3(1.2f, 2.5f, 1.2f));
            for (int b = 0; b < blockers.Count; b++)
            {
                float gap = Gap(box, blockers[b].bounds);
                if (gap < Clearance)
                {
                    hit = blockers[b].name + " gap=" + gap.ToString("F2");
                    return false;
                }
            }
        }
        return true;
    }

    static float Gap(Bounds a, Bounds b)
    {
        float dx = IntervalGap(a.min.x, a.max.x, b.min.x, b.max.x);
        float dy = IntervalGap(a.min.y, a.max.y, b.min.y, b.max.y);
        float dz = IntervalGap(a.min.z, a.max.z, b.min.z, b.max.z);
        bool overlap = dx < 0f && dy < 0f && dz < 0f;
        if (overlap)
            return -Mathf.Min(-dx, Mathf.Min(-dy, -dz));
        float px = Mathf.Max(0f, dx);
        float py = Mathf.Max(0f, dy);
        float pz = Mathf.Max(0f, dz);
        return Mathf.Sqrt(px * px + py * py + pz * pz);
    }

    static float IntervalGap(float a0, float a1, float b0, float b1)
    {
        if (a1 < b0)
            return b0 - a1;
        if (b1 < a0)
            return a0 - b1;
        return -Mathf.Min(a1, b1) + Mathf.Max(a0, b0);
    }

    struct Obstacle
    {
        public string name;
        public Bounds bounds;
    }

    static List<Obstacle> CollectBlockers(Scene scene)
    {
        var list = new List<Obstacle>();
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || t.gameObject.scene != scene)
                continue;
            if (!IsBlocker(t.name))
                continue;
            if (!TryBounds(t.gameObject, out Bounds bounds))
                continue;
            if (bounds.size.x > 200f || bounds.size.y > 200f || bounds.size.z > 200f)
                continue;
            list.Add(new Obstacle { name = t.name, bounds = bounds });
        }
        return list;
    }

    static bool IsBlocker(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return Contains(name, "Temple")
            || Contains(name, "Shield")
            || Contains(name, "Aqueduct")
            || Contains(name, "Amphitheater")
            || Contains(name, "Coliseum")
            || Contains(name, "Obelisk")
            || Contains(name, "Pickup");
    }

    static bool Contains(string name, string word)
    {
        return name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool TryBounds(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;
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
        if (any)
            return true;
        var colliders = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;
            if (!any)
            {
                bounds = colliders[i].bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
        }
        return any;
    }

    static Dictionary<string, List<GameObject>> FindPickupInstances(Scene scene)
    {
        var map = new Dictionary<string, List<GameObject>>(StringComparer.OrdinalIgnoreCase);
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var go = transforms[i].gameObject;
            if (go.scene != scene)
                continue;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path))
                continue;
            path = path.Replace('\\', '/');
            if (!path.StartsWith(PickupDir, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!map.TryGetValue(path, out var list))
            {
                list = new List<GameObject>();
                map[path] = list;
            }
            list.Add(go);
        }
        return map;
    }

    static List<GameObject> LoadPrefabs()
    {
        var list = new List<GameObject>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PickupDir });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                list.Add(prefab);
        }
        list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        return list;
    }

    static GameObject FindNamed(Scene scene, string name)
    {
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].gameObject.scene == scene && transforms[i].name == name)
                return transforms[i].gameObject;
        }
        return null;
    }

    static bool TrySample(float x, float z, out float height)
    {
        height = 0f;
        var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            var terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null)
                continue;
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z)
                continue;
            height = terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y;
            return true;
        }
        return false;
    }

    static Dictionary<string, string> SnapshotWatched(Scene scene, Dictionary<string, List<GameObject>> existing)
    {
        var map = new Dictionary<string, string>();
        string[] names =
        {
            "Coliseum", "Temple Red", "Temple Blue", "Amphitheater East", "Amphitheater West",
            "Aqueduct East", "Aqueduct West", "Obelisk Northwest", "Obelisk Northeast", "Terrain"
        };
        for (int i = 0; i < names.Length; i++)
        {
            var go = FindNamed(scene, names[i]);
            if (go != null)
                map[names[i]] = DescribeTransform(go.transform);
        }
        foreach (var pair in existing)
        {
            for (int i = 0; i < pair.Value.Count; i++)
            {
                var go = pair.Value[i];
                if (go == null)
                    continue;
                map["pickup:" + go.name + "#" + go.GetInstanceID()] = DescribeTransform(go.transform);
            }
        }
        return map;
    }

    static string DescribeTransform(Transform t)
    {
        Vector3 p = t.position;
        Vector3 e = t.rotation.eulerAngles;
        Vector3 s = t.localScale;
        return string.Format(
            CultureInfo.InvariantCulture,
            "({0:F4},{1:F4},{2:F4}) rot=({3:F3},{4:F3},{5:F3}) scale=({6:F4},{7:F4},{8:F4})",
            p.x, p.y, p.z, e.x, e.y, e.z, s.x, s.y, s.z);
    }

    static void AppendInstance(string label, GameObject go, StringBuilder sb)
    {
        Vector3 p = go.transform.position;
        Vector3 e = go.transform.rotation.eulerAngles;
        Vector3 s = go.transform.localScale;
        TrySample(p.x, p.z, out float ground);
        float bottom = PadBottom(go);
        Transform icon = null;
        var transforms = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == "Icon")
            {
                icon = transforms[i];
                break;
            }
        }
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
        sb.AppendLine(label + " " + go.name);
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  position=({0:F4}, {1:F4}, {2:F4}) terrain_y={3:F4} pad_bottom={4:F4}",
            p.x, p.y, p.z, ground, bottom));
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  rotation=({0:F3}, {1:F3}, {2:F3}) scale=({3:F4}, {4:F4}, {5:F4}) icon_scale={6}",
            e.x, e.y, e.z, s.x, s.y, s.z,
            icon != null ? icon.localScale.ToString("F4") : "none"));
        sb.AppendLine("  prefab=" + path);
    }

    static void AppendExisting(Dictionary<string, List<GameObject>> existing, StringBuilder sb)
    {
        sb.AppendLine("already=" + Count(existing));
        foreach (var pair in existing)
        {
            for (int i = 0; i < pair.Value.Count; i++)
            {
                if (pair.Value[i] != null)
                    AppendInstance("ALREADY", pair.Value[i], sb);
            }
        }
    }

    static int Count(Dictionary<string, List<GameObject>> existing)
    {
        int n = 0;
        foreach (var pair in existing)
            n += pair.Value.Count;
        return n;
    }

    static void AppendWatched(Dictionary<string, string> before, Dictionary<string, string> after, StringBuilder sb)
    {
        sb.AppendLine("watched");
        foreach (var pair in before)
        {
            after.TryGetValue(pair.Key, out string now);
            sb.AppendLine(pair.Key + " before=" + pair.Value + " after=" + now + " same=" + (pair.Value == now));
        }
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

    static bool SceneJustWritten(Scene scene)
    {
        if (string.IsNullOrEmpty(scene.path))
            return false;
        string full = Path.GetFullPath(scene.path);
        if (!File.Exists(full))
            return false;
        return (DateTime.UtcNow - File.GetLastWriteTimeUtc(full)).TotalSeconds < 2.5;
    }

    static void WriteReport(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(ReportName)));
        File.WriteAllText(ProjectFile(ReportName), text);
        Debug.Log("Hd2PickupRowPlace\n" + text);
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
