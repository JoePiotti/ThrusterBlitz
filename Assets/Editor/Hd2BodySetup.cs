using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class Hd2BodyImporter : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (assetPath != Hd2BodySetup.RobotPath)
            return;

        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.optimizeBones = false;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != Hd2BodySetup.RobotPath)
            return;

        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i] == null || skins[i].sharedMesh == null || skins[i].name == "Jetpack")
                continue;
            if (skins[i].sharedMesh.vertexCount < 1000)
                continue;
            CapSegmentHoles(skins[i]);
        }
    }

    // The robot was cut into shells. Shoulders, arms, and legs are open at those cuts.
    static void CapSegmentHoles(SkinnedMeshRenderer skin)
    {
        Mesh mesh = skin.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int subCount = mesh.subMeshCount;
        var tris = new List<int>[subCount];
        var faceDir = new HashSet<long>();
        var edgeSub = new Dictionary<long, int>();
        var edgeCount = new Dictionary<long, int>();
        for (int s = 0; s < subCount; s++)
        {
            int[] indices = mesh.GetTriangles(s);
            tris[s] = new List<int>(indices);
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = indices[t];
                int b = indices[t + 1];
                int c = indices[t + 2];
                NoteEdge(faceDir, edgeSub, edgeCount, a, b, s);
                NoteEdge(faceDir, edgeSub, edgeCount, b, c, s);
                NoteEdge(faceDir, edgeSub, edgeCount, c, a, s);
            }
        }

        var neighbors = new Dictionary<int, List<int>>();
        foreach (var pair in edgeCount)
        {
            if (pair.Value != 1)
                continue;
            int a = (int)(pair.Key >> 32);
            int b = (int)pair.Key;
            AddNeighbor(neighbors, a, b);
            AddNeighbor(neighbors, b, a);
        }

        var seen = new HashSet<int>();
        var loops = new List<int[]>();
        foreach (int start in neighbors.Keys)
        {
            if (!seen.Add(start))
                continue;
            var loop = new List<int> { start };
            int prev = -1;
            int cur = start;
            bool closed = false;
            while (neighbors.TryGetValue(cur, out List<int> nexts))
            {
                int nxt = -1;
                for (int i = 0; i < nexts.Count; i++)
                {
                    if (nexts[i] != prev && !seen.Contains(nexts[i]))
                    {
                        nxt = nexts[i];
                        break;
                    }
                }

                if (nxt < 0)
                {
                    closed = nexts.Contains(start) && loop.Count >= 3;
                    break;
                }

                loop.Add(nxt);
                seen.Add(nxt);
                prev = cur;
                cur = nxt;
            }

            if (closed)
                loops.Add(loop.ToArray());
        }

        Vector3[] world = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            world[i] = skin.transform.TransformPoint(vertices[i]);

        var centers = new Vector3[loops.Count];
        var radii = new float[loops.Count];
        for (int i = 0; i < loops.Count; i++)
        {
            Vector3 center = Vector3.zero;
            int[] loop = loops[i];
            for (int v = 0; v < loop.Length; v++)
                center += world[loop[v]];
            center /= loop.Length;
            float radius = 0f;
            for (int v = 0; v < loop.Length; v++)
                radius += (world[loop[v]] - center).magnitude;
            centers[i] = center;
            radii[i] = radius / loop.Length;
        }

        Transform neck = FindBone(skin, "Neck");
        float neckY = neck != null ? neck.position.y : float.PositiveInfinity;
        int capped = 0;
        for (int i = 0; i < loops.Count; i++)
        {
            if (!IsSegmentHole(skin, loops[i], centers[i], radii[i], centers, radii, neckY))
                continue;

            int[] loop = OrientHole(loops[i], faceDir);
            int sub = MajoritySubmesh(loop, edgeSub);
            if (sub < 0)
                continue;

            for (int t = 1; t + 1 < loop.Length; t++)
            {
                tris[sub].Add(loop[0]);
                tris[sub].Add(loop[t]);
                tris[sub].Add(loop[t + 1]);
            }

            capped++;
        }

        Debug.Log("Capped " + capped + " of " + loops.Count + " open loops on " + skin.name);
        if (capped == 0)
            return;

        for (int s = 0; s < subCount; s++)
            mesh.SetTriangles(tris[s], s);
        mesh.RecalculateBounds();
    }

    static void NoteEdge(HashSet<long> faceDir, Dictionary<long, int> edgeSub, Dictionary<long, int> edgeCount, int a, int b, int sub)
    {
        faceDir.Add(((long)a << 32) | (uint)b);
        long undirected = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        edgeSub[undirected] = sub;
        edgeCount.TryGetValue(undirected, out int count);
        edgeCount[undirected] = count + 1;
    }

    static void AddNeighbor(Dictionary<int, List<int>> neighbors, int a, int b)
    {
        if (!neighbors.TryGetValue(a, out List<int> list))
        {
            list = new List<int>(2);
            neighbors.Add(a, list);
        }

        list.Add(b);
    }

    static int[] OrientHole(int[] loop, HashSet<long> faceDir)
    {
        int withFace = 0;
        int againstFace = 0;
        for (int i = 0; i < loop.Length; i++)
        {
            int a = loop[i];
            int b = loop[(i + 1) % loop.Length];
            if (faceDir.Contains(((long)a << 32) | (uint)b))
                withFace++;
            if (faceDir.Contains(((long)b << 32) | (uint)a))
                againstFace++;
        }

        if (withFace <= againstFace)
            return loop;

        var reversed = new int[loop.Length];
        for (int i = 0; i < loop.Length; i++)
            reversed[i] = loop[loop.Length - 1 - i];
        return reversed;
    }

    static int MajoritySubmesh(int[] loop, Dictionary<long, int> edgeSub)
    {
        var votes = new Dictionary<int, int>();
        int best = -1;
        int bestCount = 0;
        for (int i = 0; i < loop.Length; i++)
        {
            int a = loop[i];
            int b = loop[(i + 1) % loop.Length];
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!edgeSub.TryGetValue(key, out int sub))
                continue;
            votes.TryGetValue(sub, out int count);
            count++;
            votes[sub] = count;
            if (count > bestCount)
            {
                bestCount = count;
                best = sub;
            }
        }

        return best;
    }

    static bool IsSegmentHole(SkinnedMeshRenderer skin, int[] loop, Vector3 center, float radius, Vector3[] centers, float[] radii, float neckY)
    {
        if (loop.Length < 10 || radius < 0.04f)
            return false;
        if (center.y > neckY - 0.02f)
            return false;

        string bone = NearestBone(skin, center, out float boneDistance);
        bool limb = IsLimbBone(bone);
        bool nearJoint = NearJoint(skin, center, 0.2f);
        bool paired = HasFacingPair(center, radius, centers, radii);
        if (!limb && !nearJoint)
            return false;
        if (radius >= 0.05f && loop.Length >= 14)
            return true;
        if (paired && radius >= 0.04f)
            return true;
        if (nearJoint && radius >= 0.07f)
            return true;
        return boneDistance < 0.04f && radius >= 0.045f;
    }

    static bool HasFacingPair(Vector3 center, float radius, Vector3[] centers, float[] radii)
    {
        for (int i = 0; i < centers.Length; i++)
        {
            float gap = (centers[i] - center).magnitude;
            if (gap < 0.004f || gap > 0.055f)
                continue;
            if (Mathf.Abs(radii[i] - radius) < Mathf.Max(0.03f, radius * 0.45f))
                return true;
        }

        return false;
    }

    static bool NearJoint(SkinnedMeshRenderer skin, Vector3 center, float reach)
    {
        var bones = skin.bones;
        if (bones == null)
            return false;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null)
                continue;
            string name = bones[i].name;
            if (name != "Arm.L" && name != "Arm.R" && name != "Thigh.L" && name != "Thigh.R")
                continue;
            if ((bones[i].position - center).sqrMagnitude <= reach * reach)
                return true;
        }

        return false;
    }

    static string NearestBone(SkinnedMeshRenderer skin, Vector3 center, out float distance)
    {
        distance = float.MaxValue;
        string best = "";
        var bones = skin.bones;
        if (bones == null)
            return best;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null)
                continue;
            float d = (bones[i].position - center).magnitude;
            if (d < distance)
            {
                distance = d;
                best = bones[i].name;
            }
        }

        return best;
    }

    static bool IsLimbBone(string name)
    {
        return name.StartsWith("Arm") || name.StartsWith("Thigh") || name.StartsWith("Shin")
            || name.StartsWith("Hand") || name.StartsWith("Foot");
    }

    static Transform FindBone(SkinnedMeshRenderer skin, string name)
    {
        var bones = skin.bones;
        if (bones == null)
            return null;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null && bones[i].name == name)
                return bones[i];
        }

        return null;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        for (int i = 0; i < imported.Length; i++)
        {
            if (imported[i] == Hd2BodySetup.RobotPath)
                EditorApplication.delayCall += Hd2BodySetup.AttachIfNeeded;
        }
    }
}

[InitializeOnLoad]
public static class Hd2BodySetup
{
    public const string RobotPath = "Assets/Models/Bot/HumanoidRobot.fbx";
    const string TexturePath = "Assets/Models/Bot/HumanoidRobot_BaseColor.png";
    const string MaterialPath = "Assets/Models/Bot/HumanoidRobot.mat";
    const string PlayerPath = "Assets/Prefabs/VRPlayer.prefab";
    const string SessionKey = "HD2_BODY_RIG_V13";
    static int rigTries;

    static Hd2BodySetup()
    {
    }

    public static void AttachIfNeeded()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var robot = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (robot == null || player == null)
            return;

        var importer = AssetImporter.GetAtPath(RobotPath) as ModelImporter;
        if (importer != null && (!importer.isReadable || importer.animationType != ModelImporterAnimationType.Generic))
        {
            importer.isReadable = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.optimizeBones = false;
            importer.SaveAndReimport();
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var existing = root.transform.Find("Body");
            if (existing != null && !UsesModel(existing.gameObject, RobotPath))
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(robot, root.transform);
                instance.name = "Body";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                AssignPartMaterials(instance);
                existing = instance.transform;
            }

            var body = root.GetComponent<Hd2Body>();
            if (body == null)
                body = root.AddComponent<Hd2Body>();

            var locomotion = root.GetComponent<Hd2Locomotion>();
            if (locomotion != null)
            {
                body.head = locomotion.head;
                body.leftHand = locomotion.leftHand;
                body.rightHand = locomotion.rightHand;
            }

            var upperArmL = FindChild(existing, "Arm.L");
            if (upperArmL == null)
            {
                if (rigTries++ < 30)
                    EditorApplication.delayCall += AttachIfNeeded;
                return;
            }

            body.body = existing;
            body.bodyRestEuler = Vector3.zero;
            body.scaleWholeBody = true;
            body.upperArmL = upperArmL;
            body.forearmL = FindChild(existing, "Arm.L.002");
            body.handL = FindChild(existing, "Hand.L");
            body.upperArmR = FindChild(existing, "Arm.R");
            body.forearmR = FindChild(existing, "Arm.R.002");
            body.handR = FindChild(existing, "Hand.R");
            var chest = FindChild(existing, "Chest");
            body.shoulderPivotL = EnsurePivot(chest, upperArmL, "LeftShoulderPivot");
            body.shoulderPivotR = EnsurePivot(chest, body.upperArmR, "RightShoulderPivot");
            var locomotionHands = root.GetComponent<Hd2Locomotion>();
            if (locomotionHands != null)
            {
                body.gripPivotL = EnsureGripPivot(locomotionHands.leftHand, false);
                body.gripPivotR = EnsureGripPivot(locomotionHands.rightHand, true);
            }

            AssignPartMaterials(existing.gameObject);

            // Boxes are hand-tuned after the rig settles. Only fill them in
            // when this body has none yet, and never replace ones already there.
            if (existing.GetComponentInChildren<Collider>() == null)
                Hd2PracticeBot.FitHitboxes(existing);

            SetActive(root.transform, "LeftHandVisual", false);
            SetActive(root.transform, "RightHandVisual", false);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Debug.Log("HD2 humanoid robot attached to VRPlayer.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        SessionState.SetBool(SessionKey, true);
    }

    static Transform EnsurePivot(Transform chest, Transform arm, string name)
    {
        if (chest == null || arm == null)
            return null;

        var pivot = chest.Find(name);
        if (pivot == null)
        {
            var marker = new GameObject(name);
            pivot = marker.transform;
            pivot.SetParent(chest, false);
            pivot.SetPositionAndRotation(arm.position, arm.rotation);
        }

        if (pivot.GetComponent<Hd2ShoulderPivot>() == null)
            pivot.gameObject.AddComponent<Hd2ShoulderPivot>();
        return pivot;
    }

    static Transform EnsureGripPivot(Transform hand, bool right)
    {
        if (hand == null)
            return null;

        string name = right ? "RightGripPivot" : "LeftGripPivot";
        var pivot = FindChild(hand, name);
        var gun = hand.GetComponentInChildren<Hd2Pistol>(true);
        if (pivot == null)
        {
            var marker = new GameObject(name);
            pivot = marker.transform;
            pivot.SetParent(gun != null ? gun.transform : hand, false);
            float side = right ? -1f : 1f;
            pivot.localPosition = gun != null
                ? new Vector3(0.014f * side, 0.101f, 0.088f)
                : new Vector3(0f, 0.01f, 0.05f);
            pivot.localRotation = Quaternion.identity;
        }

        if (pivot.GetComponent<Hd2ShoulderPivot>() == null)
            pivot.gameObject.AddComponent<Hd2ShoulderPivot>();
        return pivot;
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChild(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static bool UsesModel(GameObject instance, string assetPath)
    {
        var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance);
        return source != null && AssetDatabase.GetAssetPath(source) == assetPath;
    }

    static void AssignPartMaterials(GameObject instance)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer renderer = null;
        int best = 0;
        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i].sharedMesh == null)
                continue;
            if (skins[i].sharedMesh.subMeshCount <= best)
                continue;
            best = skins[i].sharedMesh.subMeshCount;
            renderer = skins[i];
        }
        if (renderer == null || renderer.sharedMesh == null || shader == null)
            return;

        int count = renderer.sharedMesh.subMeshCount;
        var materials = new Material[count];
        for (int i = 0; i < count; i++)
        {
            string texturePath = "Assets/Models/Bot/Parts/tripo_part_" + i + ".png";
            materials[i] = PartMaterial(i, texturePath, shader);
        }

        renderer.sharedMaterials = materials;
        AssignJetpackMaterial(instance, shader);
    }

    static void AssignJetpackMaterial(GameObject instance, Shader shader)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].name != "Jetpack")
                continue;
            renderers[i].sharedMaterial = PartMaterial(100, "Assets/Models/Bot/Jetpack_BaseColor.png", shader);
        }
    }

    static Material PartMaterial(int index, string texturePath, Shader shader)
    {
        const string folder = "Assets/Models/Bot/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Models/Bot", "Materials");

        string path = folder + "/Part_" + index + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static void AssignBotMaterial(GameObject instance)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null && shader != null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        if (material == null)
            return;

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        EditorUtility.SetDirty(material);
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = material;
    }

    static void AssignStandInMaterials(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            string name = renderers[i].name;
            string colorPath = null;
            string emissionPath = null;
            if (name.Contains("Top"))
                colorPath = "Assets/Models/StandIn/Textures/Primary_Top.009_Colored_t.png";
            else if (name.Contains("Bottom"))
                colorPath = "Assets/Models/StandIn/Textures/Primary_Bottom.009_Colored_t.png";
            else if (name.Contains("Secondary"))
            {
                colorPath = "Assets/Models/StandIn/Textures/Secondary_t.png";
                emissionPath = "Assets/Models/StandIn/Textures/Secondary_e.png";
            }

            if (colorPath == null)
                continue;

            renderers[i].sharedMaterial = StandInMaterial(renderers[i].name, colorPath, emissionPath);
        }
    }

    static Material StandInMaterial(string assetName, string colorPath, string emissionPath)
    {
        const string folder = "Assets/Models/StandIn/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Models/StandIn", "Materials");

        string path = folder + "/" + assetName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null && shader != null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        if (material == null)
            return null;

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        if (color != null)
            material.SetTexture("_BaseMap", color);
        var emission = emissionPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath) : null;
        if (emission != null)
        {
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", Color.white);
            material.EnableKeyword("_EMISSION");
        }

        return material;
    }

    static void SetActive(Transform root, string name, bool active)
    {
        var child = FindChild(root, name);
        if (child != null)
            child.gameObject.SetActive(active);
    }
}
