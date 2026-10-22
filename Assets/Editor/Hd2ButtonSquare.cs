using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves the team button that sits in the old east amphitheater onto the open square of St Peter's.
/// Runs only while .utmp/button_square.requested exists. Does not open or reload a scene.
/// </summary>
[InitializeOnLoad]
public static class Hd2ButtonSquare
{
    const string RequestName = "button_square.requested";
    const string ReportName = "button_square_report.txt";
    const float Step = 0.5f;

    static bool running;
    static bool scheduled;

    static Hd2ButtonSquare()
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

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.path.Replace('\\', '/').EndsWith("Assets/Scenes/Rome.unity", StringComparison.OrdinalIgnoreCase))
        {
            Write("ERROR active scene is " + scene.path);
            TryDelete(request);
            return;
        }

        running = true;
        try
        {
            string mode = File.ReadAllText(request).Trim();
            Write(Map(scene, mode == "move"));
            TryDelete(request);
        }
        catch (Exception e)
        {
            Write("ERROR\n" + e);
            TryDelete(request);
        }
        finally
        {
            running = false;
        }
    }

    static string Map(Scene scene, bool move)
    {
        GameObject building = FindRoot(scene, "St Peters");
        if (building == null)
            return "ERROR St Peters is missing";
        MeshCollider collider = building.GetComponentInChildren<MeshCollider>();
        if (collider == null || collider.sharedMesh == null)
            return "ERROR St Peters has no mesh collider";

        Bounds bounds = collider.bounds;
        int nx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / Step));
        int nz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / Step));
        float[] height = new float[nx * nz];
        bool[] hit = new bool[nx * nz];
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        int hits = 0;
        for (int iz = 0; iz < nz; iz++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                float x = bounds.min.x + (ix + 0.5f) * Step;
                float z = bounds.min.z + (iz + 0.5f) * Step;
                var ray = new Ray(new Vector3(x, bounds.max.y + 2f, z), Vector3.down);
                int index = iz * nx + ix;
                if (collider.Raycast(ray, out RaycastHit info, bounds.size.y + 4f))
                {
                    hit[index] = true;
                    height[index] = info.point.y;
                    hits++;
                    if (info.point.y < min)
                        min = info.point.y;
                    if (info.point.y > max)
                        max = info.point.y;
                }
            }
        }

        const float Bin = 0.25f;
        int binCount = Mathf.Max(1, Mathf.CeilToInt((max - min) / Bin) + 1);
        int[] bins = new int[binCount];
        for (int i = 0; i < height.Length; i++)
        {
            if (!hit[i])
                continue;
            int bin = Mathf.Clamp(Mathf.FloorToInt((height[i] - min) / Bin), 0, binCount - 1);
            bins[bin]++;
        }

        float mid = (min + max) * 0.5f;
        int plazaBin = 0;
        int plazaVotes = -1;
        for (int i = 0; i < binCount; i++)
        {
            float center = min + (i + 0.5f) * Bin;
            if (center > mid)
                break;
            if (bins[i] > plazaVotes)
            {
                plazaVotes = bins[i];
                plazaBin = i;
            }
        }

        float plazaY = min + (plazaBin + 0.5f) * Bin;
        bool[] floor = new bool[nx * nz];
        int floorCount = 0;
        for (int i = 0; i < height.Length; i++)
        {
            if (hit[i] && Mathf.Abs(height[i] - plazaY) <= 0.4f)
            {
                floor[i] = true;
                floorCount++;
            }
        }

        int[] labels = new int[nx * nz];
        int bestLabel = 0;
        int bestCount = 0;
        int label = 0;
        var stack = new int[nx * nz];
        for (int start = 0; start < floor.Length; start++)
        {
            if (!floor[start] || labels[start] != 0)
                continue;
            label++;
            int count = 0;
            int top = 0;
            stack[top++] = start;
            labels[start] = label;
            while (top > 0)
            {
                int current = stack[--top];
                count++;
                int cx = current % nx;
                int cz = current / nx;
                TryPush(floor, labels, stack, ref top, nx, nz, cx - 1, cz, label);
                TryPush(floor, labels, stack, ref top, nx, nz, cx + 1, cz, label);
                TryPush(floor, labels, stack, ref top, nx, nz, cx, cz - 1, label);
                TryPush(floor, labels, stack, ref top, nx, nz, cx, cz + 1, label);
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestLabel = label;
            }
        }

        double sumX = 0d;
        double sumZ = 0d;
        float floorY = 0f;
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != bestLabel)
                continue;
            int ix = i % nx;
            int iz = i / nx;
            sumX += bounds.min.x + (ix + 0.5f) * Step;
            sumZ += bounds.min.z + (iz + 0.5f) * Step;
            floorY += height[i];
        }

        Vector3 square = bestCount > 0
            ? new Vector3((float)(sumX / bestCount), floorY / bestCount, (float)(sumZ / bestCount))
            : bounds.center;

        var report = new StringBuilder();
        report.AppendLine("bounds center=" + bounds.center.ToString("F2") + " size=" + bounds.size.ToString("F2") + " minY=" + bounds.min.y.ToString("F2"));
        report.AppendLine("samples=" + (nx * nz) + " hits=" + hits + " height=" + min.ToString("F2") + ".." + max.ToString("F2"));
        report.AppendLine("plaza_y=" + plazaY.ToString("F2") + " votes=" + plazaVotes + " floor_cells=" + floorCount + " largest=" + bestCount + " square=" + square.ToString("F2"));
        report.AppendLine("map +Z up, +X right, low=_ mid=- high=# miss=.");
        for (int iz = nz - 1; iz >= 0; iz--)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                int index = iz * nx + ix;
                if (!hit[index])
                    report.Append('.');
                else if (labels[index] == bestLabel)
                    report.Append('_');
                else if (height[index] < min + (max - min) * 0.45f)
                    report.Append('-');
                else
                    report.Append('#');
            }

            report.AppendLine();
        }

        GameObject button = FindButtonInside(scene, bounds);
        if (button == null)
        {
            report.AppendLine("button=missing");
            return report.ToString();
        }

        float bottomOffset = ButtonBottomOffset(button);
        report.AppendLine("button=" + button.name
            + " pos=" + button.transform.position.ToString("F3")
            + " bottomOffset=" + bottomOffset.ToString("F3"));

        if (move && bestCount >= 8)
        {
            Vector3 spot = square;
            var down = new Ray(new Vector3(square.x, bounds.max.y + 2f, square.z), Vector3.down);
            bool open = collider.Raycast(down, out RaycastHit spotHit, bounds.size.y + 4f) && spotHit.point.y <= plazaY + 0.6f;
            if (!open)
                spot = NearestFloorCell(bounds, labels, bestLabel, height, nx, square);
            else
                spot.y = spotHit.point.y;

            Vector3 position = button.transform.position;
            position.x = spot.x;
            position.z = spot.z;
            position.y = spot.y - bottomOffset + 0.02f;
            button.transform.position = position;
            EditorUtility.SetDirty(button);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            report.AppendLine("surface=" + spot.ToString("F3") + " moved=" + button.transform.position.ToString("F3") + " saved=" + saved);
        }
        else
        {
            report.AppendLine("moved=false");
        }

        return report.ToString();
    }

    static Vector3 NearestFloorCell(Bounds bounds, int[] labels, int bestLabel, float[] height, int nx, Vector3 square)
    {
        float best = float.PositiveInfinity;
        Vector3 spot = square;
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != bestLabel)
                continue;
            int ix = i % nx;
            int iz = i / nx;
            float x = bounds.min.x + (ix + 0.5f) * Step;
            float z = bounds.min.z + (iz + 0.5f) * Step;
            float distance = (x - square.x) * (x - square.x) + (z - square.z) * (z - square.z);
            if (distance < best)
            {
                best = distance;
                spot = new Vector3(x, height[i], z);
            }
        }

        return spot;
    }

    static void TryPush(bool[] floor, int[] labels, int[] stack, ref int top, int nx, int nz, int x, int z, int label)
    {
        if (x < 0 || z < 0 || x >= nx || z >= nz)
            return;
        int index = z * nx + x;
        if (!floor[index] || labels[index] != 0)
            return;
        labels[index] = label;
        stack[top++] = index;
    }

    static float ButtonBottomOffset(GameObject button)
    {
        var renderers = button.GetComponentsInChildren<Renderer>();
        bool any = false;
        float min = 0f;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!any || renderers[i].bounds.min.y < min)
            {
                min = renderers[i].bounds.min.y;
                any = true;
            }
        }

        return any ? min - button.transform.position.y : 0f;
    }

    static GameObject FindButtonInside(Scene scene, Bounds bounds)
    {
        GameObject best = null;
        float bestDistance = float.PositiveInfinity;
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (!roots[i].name.StartsWith("ButtonBase", StringComparison.Ordinal))
                continue;
            Vector3 p = roots[i].transform.position;
            float dx = Mathf.Max(bounds.min.x - p.x, p.x - bounds.max.x);
            float dz = Mathf.Max(bounds.min.z - p.z, p.z - bounds.max.z);
            float outside = Mathf.Max(dx, 0f) + Mathf.Max(dz, 0f);
            if (outside > 2f)
                continue;
            float distance = (new Vector2(p.x, p.z) - new Vector2(bounds.center.x, bounds.center.z)).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = roots[i];
            }
        }

        return best;
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

    static void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(ReportName)));
        File.WriteAllText(ProjectFile(ReportName), text);
        Debug.Log("Hd2ButtonSquare\n" + text);
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
