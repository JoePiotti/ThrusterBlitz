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
/// Raises one soft hill under each Towers temple and seats the temple on it.
/// Runs only while .utmp/temple_hill.requested exists. Does not open or reload a scene.
/// Parks the request while another .utmp/*.requested job is running so the two saves
/// do not block each other.
/// </summary>
[InitializeOnLoad]
public static class Hd2TempleHills
{
    const string RequestName = "temple_hill.requested";
    const string HoldName = "temple_hill.hold";
    const string ReportName = "temple_hill_report.txt";
    const float DefaultProminence = 10f;
    const float CrownMargin = 1.25f;
    const float PreferredBlend = 26f;
    const float ProtectPad = 2f;

    static bool running;
    static bool scheduled;

    static Hd2TempleHills()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (running || scheduled || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(ProjectFile(RequestName)) && !File.Exists(ProjectFile(HoldName)))
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

        if (!File.Exists(ProjectFile(RequestName)) && !File.Exists(ProjectFile(HoldName)))
            return;

        if (OthersBusy())
        {
            Park();
            return;
        }

        if (File.Exists(ProjectFile(HoldName)) && !File.Exists(ProjectFile(RequestName)))
        {
            try
            {
                File.Move(ProjectFile(HoldName), ProjectFile(RequestName));
            }
            catch (Exception)
            {
                scheduled = true;
                EditorApplication.delayCall += Run;
                return;
            }
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            WriteReport("ERROR play mode is active; left the scene untouched");
            ClearRequests();
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.path.Replace('\\', '/').EndsWith("Assets/Scenes/Towers.unity", StringComparison.OrdinalIgnoreCase))
        {
            WriteReport("ERROR active scene is '" + scene.path + "'; did not open or reload a scene");
            ClearRequests();
            return;
        }

        if (SceneJustWritten(scene))
        {
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        running = true;
        try
        {
            Place(scene);
        }
        catch (Exception e)
        {
            WriteReport("ERROR\n" + e);
        }
        finally
        {
            ClearRequests();
            running = false;
        }
    }

    static void Place(Scene scene)
    {
        var red = FindRoot(scene, "Temple Red");
        var blue = FindRoot(scene, "Temple Blue");
        if (red == null || blue == null)
            throw new InvalidOperationException("Temple Red or Temple Blue was not in the open scene");

        Terrain terrain = null;
        var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null && terrains[i].gameObject.scene == scene && terrains[i].name == "Terrain")
                terrain = terrains[i];
        }
        if (terrain == null || terrain.terrainData == null)
            throw new InvalidOperationException("Terrain was not in the open scene");

        var data = terrain.terrainData;
        int res = data.heightmapResolution;
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;
        float[,] heights = data.GetHeights(0, 0, res, res);

        float flat = FlatLevel(heights, origin.y, size.y, res, out float globalMax, out int samples);
        var peaks = FindPeaks(heights, origin, size, res, flat);
        float prominence = ChooseProminence(peaks, red.transform.position, blue.transform.position, flat);

        var blocks = CollectBlocks(scene);
        var sb = new StringBuilder();
        sb.AppendLine("terrain_origin=" + origin.ToString("F3") + " size=" + size.ToString("F3") + " res=" + res);
        sb.AppendLine("flat_y=" + flat.ToString("F3") + " global_max_y=" + globalMax.ToString("F3") + " samples=" + samples);
        sb.AppendLine("peaks=" + peaks.Count + " prominence_m=" + prominence.ToString("F3"));
        for (int i = 0; i < peaks.Count && i < 12; i++)
        {
            var p = peaks[i];
            sb.AppendLine("  peak " + p.x.ToString("F1") + "," + p.z.ToString("F1") + " y=" + p.y.ToString("F2") + " above=" + (p.y - flat).ToString("F2"));
        }

        var watched = SnapshotWatched(scene);
        sb.AppendLine("blocks=" + blocks.Count);
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            sb.AppendLine("  block " + b.name + " x=" + b.minX.ToString("F1") + ".." + b.maxX.ToString("F1") + " z=" + b.minZ.ToString("F1") + ".." + b.maxZ.ToString("F1"));
        }
        Bounds redBounds = BuildingBounds(red);
        Bounds blueBounds = BuildingBounds(blue);
        DescribeBuilding("Temple Red before", red, redBounds, heights, origin, size, res, sb);
        DescribeBuilding("Temple Blue before", blue, blueBounds, heights, origin, size, res, sb);

        var player = FindRoot(scene, "VRPlayer");
        var opening = FindInScene(scene, "Spawn Opening");
        Vector3 playerBefore = player != null ? player.transform.position : Vector3.zero;
        Vector3 openingBefore = opening != null ? opening.transform.position : Vector3.zero;
        bool playerParented = player != null && player.transform.IsChildOf(blue.transform);
        bool openingParented = opening != null && opening.transform.IsChildOf(blue.transform);
        sb.AppendLine("player_before=" + (player != null ? playerBefore.ToString("F4") : "missing") + " parented=" + playerParented);
        sb.AppendLine("opening_before=" + (opening != null ? openingBefore.ToString("F4") : "missing") + " parented=" + openingParented);

        bool redUsed = HillCovers(redBounds, heights, origin, size, res, flat, out float redMin, out float redMax, out float redPeakDist);
        bool blueUsed = HillCovers(blueBounds, heights, origin, size, res, flat, out float blueMin, out float blueMax, out float bluePeakDist);
        sb.AppendLine("red_existing=" + redUsed + " min=" + redMin.ToString("F3") + " max=" + redMax.ToString("F3") + " peakDist=" + redPeakDist.ToString("F2"));
        sb.AppendLine("blue_existing=" + blueUsed + " min=" + blueMin.ToString("F3") + " max=" + blueMax.ToString("F3") + " peakDist=" + bluePeakDist.ToString("F2"));

        float redCrown = redUsed ? redMax : Mathf.Max(flat + prominence, redMax);
        float blueCrown = blueUsed ? blueMax : Mathf.Max(flat + prominence, blueMax);

        int changed = 0;
        if (!redUsed)
            changed += RaiseHill(heights, origin, size, res, redBounds, redCrown, blocks, "Temple Red", sb);
        else
            sb.AppendLine("Temple Red sculpt=existing");
        if (!blueUsed)
            changed += RaiseHill(heights, origin, size, res, blueBounds, blueCrown, blocks, "Temple Blue", sb);
        else
            sb.AppendLine("Temple Blue sculpt=existing");

        if (changed > 0)
        {
            data.SetHeightsDelayLOD(0, 0, heights);
            data.SyncHeightmap();
            terrain.Flush();
            EditorUtility.SetDirty(data);
        }

        string redSeat = Seat(red, terrain, out float redDelta, out float redSupport, out float redBase);
        string blueSeat = Seat(blue, terrain, out float blueDelta, out float blueSupport, out float blueBase);
        sb.AppendLine("Temple Red seat " + redSeat + " support=" + redSupport.ToString("F4") + " base=" + redBase.ToString("F4") + " dy=" + redDelta.ToString("F4"));
        sb.AppendLine("Temple Blue seat " + blueSeat + " support=" + blueSupport.ToString("F4") + " base=" + blueBase.ToString("F4") + " dy=" + blueDelta.ToString("F4"));

        bool playerMoved = false;
        if (player != null && !playerParented)
        {
            if (InsideXZ(blueBounds, player.transform.position, 8f))
            {
                player.transform.position += new Vector3(0f, blueDelta, 0f);
                playerMoved = Mathf.Abs(blueDelta) > 0.0001f;
            }
        }

        bool openingMoved = false;
        if (opening != null && !openingParented)
        {
            if (InsideXZ(blueBounds, opening.transform.position, 8f))
            {
                opening.transform.position += new Vector3(0f, blueDelta, 0f);
                openingMoved = Mathf.Abs(blueDelta) > 0.0001f;
            }
        }

        var pickupMoves = new List<string>();
        LiftBuriedPickups(scene, terrain, pickupMoves);
        for (int i = 0; i < pickupMoves.Count; i++)
            sb.AppendLine(pickupMoves[i]);
        if (pickupMoves.Count == 0)
            sb.AppendLine("pickups_moved=0");

        EditorSceneManager.MarkSceneDirty(scene);
        bool sceneSaved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssetIfDirty(data);
        AssetDatabase.SaveAssets();
        bool terrainDirty = EditorUtility.IsDirty(data);
        bool sceneDirty = scene.isDirty;

        Bounds redAfter = BuildingBounds(red);
        Bounds blueAfter = BuildingBounds(blue);
        DescribeBuilding("Temple Red after", red, redAfter, heights, origin, size, res, sb);
        DescribeBuilding("Temple Blue after", blue, blueAfter, heights, origin, size, res, sb);
        sb.AppendLine("player_after=" + (player != null ? player.transform.position.ToString("F4") : "missing")
            + " parented=" + playerParented + " moved=" + playerMoved);
        sb.AppendLine("opening_after=" + (opening != null ? opening.transform.position.ToString("F4") : "missing")
            + " parented=" + openingParented + " moved=" + openingMoved);
        sb.AppendLine("changed_samples=" + changed);
        sb.AppendLine("scene_saved=" + sceneSaved + " scene_dirty=" + sceneDirty + " terrain_dirty=" + terrainDirty);

        var watchedAfter = SnapshotWatched(scene);
        sb.AppendLine("watched");
        foreach (var kv in watched)
        {
            watchedAfter.TryGetValue(kv.Key, out string now);
            sb.AppendLine(kv.Key + " before=" + kv.Value + " after=" + now + " same=" + (kv.Value == now));
        }

        sb.Insert(0, "OK\n");
        WriteReport(sb.ToString());
    }

    static int RaiseHill(float[,] heights, Vector3 origin, Vector3 size, int res, Bounds footprint, float crown, List<Block> blocks, string label, StringBuilder sb)
    {
        float halfX = footprint.extents.x + CrownMargin;
        float halfZ = footprint.extents.z + CrownMargin;
        Vector3 center = footprint.center;
        float[] blend = BuildBlend(center, halfX, halfZ, blocks, out float minRoom);
        int changed = 0;
        float reach = Mathf.Max(halfX, halfZ) + PreferredBlend + 2f;
        int ix0 = WorldToIndex(center.x - reach, origin.x, size.x, res);
        int ix1 = WorldToIndex(center.x + reach, origin.x, size.x, res);
        int iz0 = WorldToIndex(center.z - reach, origin.z, size.z, res);
        int iz1 = WorldToIndex(center.z + reach, origin.z, size.z, res);
        for (int iz = iz0; iz <= iz1; iz++)
        {
            for (int ix = ix0; ix <= ix1; ix++)
            {
                float wx = origin.x + ix * size.x / (res - 1f);
                float wz = origin.z + iz * size.z / (res - 1f);
                if (GapToBlocks(wx, wz, blocks) < ProtectPad)
                    continue;
                float dx = wx - center.x;
                float dz = wz - center.z;
                float outside = OutsideRect(dx, dz, halfX, halfZ);
                int bin = AngleBin(dx, dz);
                float span = blend[bin];
                float weight = Weight(outside, span);
                if (weight <= 0.0001f)
                    continue;
                float existing = heights[iz, ix] * size.y + origin.y;
                float desired = existing + (crown - existing) * weight;
                if (desired <= existing + 0.001f)
                    continue;
                float n = Mathf.Clamp01((desired - origin.y) / Mathf.Max(size.y, 0.0001f));
                if (n > heights[iz, ix])
                {
                    heights[iz, ix] = n;
                    changed++;
                }
            }
        }
        sb.AppendLine(label + " sculpt=raised crown_y=" + crown.ToString("F3")
            + " min_room_m=" + minRoom.ToString("F2")
            + " samples=" + changed);
        return changed;
    }

    static float[] BuildBlend(Vector3 center, float halfX, float halfZ, List<Block> blocks, out float minRoom)
    {
        var blend = new float[72];
        minRoom = PreferredBlend;
        for (int i = 0; i < 72; i++)
        {
            float ang = i * 5f * Mathf.Deg2Rad;
            float dx = Mathf.Sin(ang);
            float dz = Mathf.Cos(ang);
            float crown = RayExitBox(halfX, halfZ, dx, dz);
            float hit = RayHitBlock(center, dx, dz, blocks, crown + PreferredBlend + ProtectPad + 4f);
            float room = hit - crown - ProtectPad;
            if (room < minRoom)
                minRoom = room;
            blend[i] = Mathf.Clamp(room, 0.5f, PreferredBlend);
        }
        return blend;
    }

    static float RayExitBox(float halfX, float halfZ, float dx, float dz)
    {
        float tx = Mathf.Abs(dx) > 0.0001f ? halfX / Mathf.Abs(dx) : 10000f;
        float tz = Mathf.Abs(dz) > 0.0001f ? halfZ / Mathf.Abs(dz) : 10000f;
        return Mathf.Min(tx, tz);
    }

    static float RayHitBlock(Vector3 center, float dx, float dz, List<Block> blocks, float limit)
    {
        float best = limit;
        for (float t = 0f; t <= limit; t += 1f)
        {
            float x = center.x + dx * t;
            float z = center.z + dz * t;
            if (GapToBlocks(x, z, blocks) <= ProtectPad)
                return t;
        }
        return best;
    }

    static float Weight(float outside, float span)
    {
        if (outside <= 0f)
            return 1f;
        if (span <= 0.05f)
            return 0f;
        float t = Mathf.Clamp01(outside / span);
        return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * t);
    }

    static float OutsideRect(float dx, float dz, float halfX, float halfZ)
    {
        float ax = Mathf.Max(0f, Mathf.Abs(dx) - halfX);
        float az = Mathf.Max(0f, Mathf.Abs(dz) - halfZ);
        return Mathf.Sqrt(ax * ax + az * az);
    }

    static int AngleBin(float dx, float dz)
    {
        float ang = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        if (ang < 0f)
            ang += 360f;
        int bin = Mathf.RoundToInt(ang / 5f) % 72;
        if (bin < 0)
            bin += 72;
        return bin;
    }

    static bool HillCovers(Bounds footprint, float[,] heights, Vector3 origin, Vector3 size, int res, float flat, out float minY, out float maxY, out float peakDist)
    {
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;
        float peakX = footprint.center.x;
        float peakZ = footprint.center.z;
        for (float z = footprint.min.z; z <= footprint.max.z + 0.01f; z += 2f)
        {
            for (float x = footprint.min.x; x <= footprint.max.x + 0.01f; x += 2f)
            {
                float y = SampleArray(heights, origin, size, res, x, z);
                if (y < minY)
                    minY = y;
                if (y > maxY)
                {
                    maxY = y;
                    peakX = x;
                    peakZ = z;
                }
            }
        }
        peakDist = Horizontal(footprint.center, new Vector3(peakX, 0f, peakZ));
        if (float.IsInfinity(minY))
            return false;
        float above = maxY - flat;
        if (above < 3f)
            return false;
        if (maxY - minY > 0.6f)
            return false;
        if (minY < flat + above * 0.55f)
            return false;
        float centerY = SampleArray(heights, origin, size, res, footprint.center.x, footprint.center.z);
        if (centerY < flat + above * 0.7f)
            return false;
        return true;
    }

    static string Seat(GameObject go, Terrain terrain, out float delta, out float support, out float baseY)
    {
        Physics.SyncTransforms();
        Bounds before = BuildingBounds(go);
        baseY = before.min.y;
        support = FootprintSupport(terrain, before);
        delta = support - baseY;
        if (Mathf.Abs(delta) > 0.0001f)
            go.transform.position += new Vector3(0f, delta, 0f);
        Physics.SyncTransforms();
        Bounds after = BuildingBounds(go);
        float gap = FootprintSupport(terrain, after) - after.min.y;
        return "gap=" + gap.ToString("F4") + " pos=" + go.transform.position.ToString("F4");
    }

    static float FootprintSupport(Terrain terrain, Bounds footprint)
    {
        float min = float.PositiveInfinity;
        for (float z = footprint.min.z; z <= footprint.max.z + 0.01f; z += 1f)
        {
            for (float x = footprint.min.x; x <= footprint.max.x + 0.01f; x += 1f)
            {
                float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                if (y < min)
                    min = y;
            }
        }
        float cx = terrain.SampleHeight(new Vector3(footprint.center.x, 0f, footprint.center.z)) + terrain.transform.position.y;
        if (cx < min)
            min = cx;
        return min;
    }

    static void LiftBuriedPickups(Scene scene, Terrain terrain, List<string> log)
    {
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || t.gameObject.scene != scene)
                continue;
            if (t.name.IndexOf("Pickup", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (t.parent != null && t.parent.name.IndexOf("Pickup", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            float bottom = PadBottom(t.gameObject);
            float ground = terrain.SampleHeight(new Vector3(t.position.x, 0f, t.position.z)) + terrain.transform.position.y;
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var b = renderers[r].bounds;
                ground = Mathf.Max(ground, terrain.SampleHeight(new Vector3(b.min.x, 0f, b.min.z)) + terrain.transform.position.y);
                ground = Mathf.Max(ground, terrain.SampleHeight(new Vector3(b.max.x, 0f, b.max.z)) + terrain.transform.position.y);
                ground = Mathf.Max(ground, terrain.SampleHeight(new Vector3(b.center.x, 0f, b.center.z)) + terrain.transform.position.y);
            }
            if (ground > bottom + 0.02f)
            {
                float dy = ground - bottom;
                t.position += new Vector3(0f, dy, 0f);
                log.Add("pickup_moved " + t.name + " dy=" + dy.ToString("F3") + " pos=" + t.position.ToString("F3"));
            }
        }
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

    static float FlatLevel(float[,] heights, float originY, float sizeY, int res, out float globalMax, out int samples)
    {
        globalMax = float.NegativeInfinity;
        var values = new List<float>(17000);
        int stride = Mathf.Max(1, res / 128);
        for (int iz = 0; iz < res; iz += stride)
        {
            for (int ix = 0; ix < res; ix += stride)
            {
                float y = heights[iz, ix] * sizeY + originY;
                if (y > globalMax)
                    globalMax = y;
                values.Add(y);
            }
        }
        samples = values.Count;
        if (samples == 0)
            return originY;
        values.Sort();
        return values[samples / 2];
    }

    struct Peak
    {
        public float x, y, z;
    }

    static List<Peak> FindPeaks(float[,] heights, Vector3 origin, Vector3 size, int res, float flat)
    {
        var list = new List<Peak>();
        float step = Mathf.Max(size.x, size.z) / (res - 1f);
        int stride = Mathf.Max(1, Mathf.RoundToInt(6f / Mathf.Max(step, 0.01f)));
        int radius = Mathf.Max(stride, Mathf.RoundToInt(10f / Mathf.Max(step, 0.01f)));
        for (int iz = radius; iz < res - radius; iz += stride)
        {
            for (int ix = radius; ix < res - radius; ix += stride)
            {
                float y = heights[iz, ix] * size.y + origin.y;
                if (y < flat + 2.5f)
                    continue;
                bool max = true;
                for (int nz = iz - radius; nz <= iz + radius && max; nz += stride)
                {
                    for (int nx = ix - radius; nx <= ix + radius; nx += stride)
                    {
                        if (nx == ix && nz == iz)
                            continue;
                        if (heights[nz, nx] * size.y + origin.y > y + 0.05f)
                        {
                            max = false;
                            break;
                        }
                    }
                }
                if (!max)
                    continue;
                list.Add(new Peak
                {
                    x = origin.x + ix * size.x / (res - 1f),
                    z = origin.z + iz * size.z / (res - 1f),
                    y = y
                });
            }
        }
        list.Sort((a, b) => b.y.CompareTo(a.y));
        return list;
    }

    static float ChooseProminence(List<Peak> peaks, Vector3 red, Vector3 blue, float flat)
    {
        var near = new List<float>();
        var all = new List<float>();
        for (int i = 0; i < peaks.Count; i++)
        {
            float above = peaks[i].y - flat;
            all.Add(above);
            float d = Mathf.Min(Horizontal(red, new Vector3(peaks[i].x, 0f, peaks[i].z)), Horizontal(blue, new Vector3(peaks[i].x, 0f, peaks[i].z)));
            if (d < 220f)
                near.Add(above);
        }
        var use = near.Count > 0 ? near : all;
        if (use.Count == 0)
            return DefaultProminence;
        use.Sort();
        float median = use[use.Count / 2];
        if (median < 4f || median > 30f)
            return DefaultProminence;
        return median;
    }

    static float SampleArray(float[,] heights, Vector3 origin, Vector3 size, int res, float x, float z)
    {
        int ix = WorldToIndex(x, origin.x, size.x, res);
        int iz = WorldToIndex(z, origin.z, size.z, res);
        return heights[iz, ix] * size.y + origin.y;
    }

    static int WorldToIndex(float world, float origin, float span, int res)
    {
        float t = (world - origin) / Mathf.Max(span, 0.0001f);
        int i = Mathf.RoundToInt(t * (res - 1));
        if (i < 0)
            i = 0;
        if (i > res - 1)
            i = res - 1;
        return i;
    }

    struct Block
    {
        public string name;
        public float minX, maxX, minZ, maxZ;
    }

    static List<Block> CollectBlocks(Scene scene)
    {
        var list = new List<Block>();
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var seen = new HashSet<int>();
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || t.gameObject.scene != scene || t.parent != null)
                continue;
            if (!Protect(t.name))
                continue;
            if (!seen.Add(t.gameObject.GetInstanceID()))
                continue;
            if (!TryBounds(t.gameObject, out Bounds b))
                continue;
            list.Add(new Block
            {
                name = t.name,
                minX = b.min.x - ProtectPad,
                maxX = b.max.x + ProtectPad,
                minZ = b.min.z - ProtectPad,
                maxZ = b.max.z + ProtectPad
            });
        }
        return list;
    }

    static bool Protect(string name)
    {
        if (name.StartsWith("Coliseum"))
            return true;
        if (name.StartsWith("Aqueduct"))
            return true;
        if (name.StartsWith("Amphitheater"))
            return true;
        if (name.StartsWith("Obelisk"))
            return true;
        if (name.IndexOf("Pickup", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (name == "cover wall")
            return true;
        return false;
    }

    static bool TryBounds(GameObject go, out Bounds bounds)
    {
        bool any = false;
        bounds = new Bounds(go.transform.position, Vector3.zero);
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null)
                continue;
            if (r.transform != go.transform && UnderNamed(r.transform, go.transform, "Pickup"))
                continue;
            Bounds b = r.bounds;
            float ox = b.center.x - go.transform.position.x;
            float oz = b.center.z - go.transform.position.z;
            if (ox * ox + oz * oz > 45f * 45f)
                continue;
            if (b.size.x > 100f || b.size.y > 80f || b.size.z > 100f)
                continue;
            if (!any)
            {
                bounds = b;
                any = true;
            }
            else
            {
                bounds.Encapsulate(b);
            }
        }
        if (!any || bounds.size.x > 100f || bounds.size.z > 100f)
            bounds = new Bounds(go.transform.position, new Vector3(3f, 3f, 3f));
        return true;
    }

    static bool UnderNamed(Transform t, Transform stop, string fragment)
    {
        while (t != null && t != stop)
        {
            if (t.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            t = t.parent;
        }
        return false;
    }

    static float GapToBlocks(float x, float z, List<Block> blocks)
    {
        float best = float.MaxValue;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            float dx = 0f;
            if (x < b.minX)
                dx = b.minX - x;
            else if (x > b.maxX)
                dx = x - b.maxX;
            float dz = 0f;
            if (z < b.minZ)
                dz = b.minZ - z;
            else if (z > b.maxZ)
                dz = z - b.maxZ;
            float d;
            if (dx == 0f && dz == 0f)
            {
                float px = Mathf.Min(x - b.minX, b.maxX - x);
                float pz = Mathf.Min(z - b.minZ, b.maxZ - z);
                d = -Mathf.Min(px, pz);
            }
            else
            {
                d = Mathf.Sqrt(dx * dx + dz * dz);
            }
            if (d < best)
                best = d;
        }
        return best;
    }

    static Bounds BuildingBounds(GameObject go)
    {
        Physics.SyncTransforms();
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
            return renderer.bounds;
        var filters = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].name.StartsWith("Shield") || filters[i].name.StartsWith("Spawn"))
                continue;
            return filters[i].bounds;
        }
        throw new InvalidOperationException(go.name + " has no temple mesh");
    }

    static void DescribeBuilding(string label, GameObject go, Bounds bounds, float[,] heights, Vector3 origin, Vector3 size, int res, StringBuilder sb)
    {
        var t = go.transform;
        float centerY = SampleArray(heights, origin, size, res, bounds.center.x, bounds.center.z);
        sb.AppendLine(label);
        sb.AppendLine("  position=" + t.position.ToString("F4"));
        sb.AppendLine("  rotation=" + t.rotation.eulerAngles.ToString("F3"));
        sb.AppendLine("  scale=" + t.localScale.ToString("F4"));
        sb.AppendLine("  bounds_min=" + bounds.min.ToString("F3") + " bounds_max=" + bounds.max.ToString("F3") + " size=" + bounds.size.ToString("F3"));
        sb.AppendLine("  base_y=" + bounds.min.y.ToString("F4") + " terrain_center_y=" + centerY.ToString("F3"));
    }

    static bool InsideXZ(Bounds bounds, Vector3 point, float pad)
    {
        return point.x >= bounds.min.x - pad && point.x <= bounds.max.x + pad
            && point.z >= bounds.min.z - pad && point.z <= bounds.max.z + pad;
    }

    static float Horizontal(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static Dictionary<string, string> SnapshotWatched(Scene scene)
    {
        var map = new Dictionary<string, string>();
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || t.gameObject.scene != scene || t.parent != null)
                continue;
            if (!Watch(t.name))
                continue;
            var p = t.position;
            var e = t.rotation.eulerAngles;
            var s = t.localScale;
            map[t.name] = string.Format(
                CultureInfo.InvariantCulture,
                "({0:F4},{1:F4},{2:F4}) rot=({3:F3},{4:F3},{5:F3}) scale=({6:F4},{7:F4},{8:F4})",
                p.x, p.y, p.z, e.x, e.y, e.z, s.x, s.y, s.z);
        }
        return map;
    }

    static bool Watch(string name)
    {
        if (name.StartsWith("Coliseum") || name.StartsWith("Aqueduct") || name.StartsWith("Amphitheater"))
            return true;
        if (name.StartsWith("Obelisk"))
            return true;
        if (name.IndexOf("Pickup", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (name == "cover wall" || name == "Terrain")
            return true;
        return false;
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == name && transforms[i].parent == null && transforms[i].gameObject.scene == scene)
                return transforms[i].gameObject;
        }
        return null;
    }

    static GameObject FindInScene(Scene scene, string name)
    {
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == name && transforms[i].gameObject.scene == scene)
                return transforms[i].gameObject;
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

    static bool SceneJustWritten(Scene scene)
    {
        if (string.IsNullOrEmpty(scene.path))
            return false;
        string full = Path.GetFullPath(scene.path);
        if (!File.Exists(full))
            return false;
        return (DateTime.UtcNow - File.GetLastWriteTimeUtc(full)).TotalSeconds < 3.0;
    }

    static void Park()
    {
        string req = ProjectFile(RequestName);
        string hold = ProjectFile(HoldName);
        try
        {
            if (!File.Exists(req))
                return;
            if (File.Exists(hold))
                File.Delete(hold);
            File.Move(req, hold);
        }
        catch (Exception)
        {
        }
    }

    static void ClearRequests()
    {
        TryDelete(ProjectFile(RequestName));
        TryDelete(ProjectFile(HoldName));
    }

    static void WriteReport(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(ReportName)));
        File.WriteAllText(ProjectFile(ReportName), text);
        Debug.Log("Hd2TempleHills\n" + text);
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
