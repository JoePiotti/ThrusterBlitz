using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Skins the gripped rocket from the pose already saved on the prefab.
/// Each piece is weighted fully to its own bone, same as the segmented robot.
/// Finger bones are chained so a knuckle carries the rest of that finger.
/// </summary>
[InitializeOnLoad]
public static class Hd2GripRocketSkin
{
    const string PrefabPath = "Assets/Prefabs/GripRocket.prefab";
    const string TextureFolder = "Assets/Models/Rocket/Textures";
    const string MaterialFolder = "Assets/Models/Rocket/Materials";
    const string MeshFolder = "Assets/Models/Rocket";

    static Hd2GripRocketSkin()
    {
        EditorApplication.delayCall += Ensure;
    }

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        if (AlreadySkinned())
            return;

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            return;

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/hand_gun_and_hand_hand_palm_basecolor.png") == null)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        SkinNow();
    }

    public static void SkinNow()
    {
        if (AlreadySkinned())
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return;

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var hand = FindExact(root.transform, "Hand");
            var gun = FindExact(root.transform, "Gun");
            if (hand == null || gun == null)
            {
                Debug.LogWarning("HD2 grip rocket skin skipped. Hand or Gun was missing.");
                return;
            }

            int handBones = SkinGroup(hand, shader, true);
            int gunBones = SkinGroup(gun, shader, false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("HD2 grip rocket skinned. Hand bones " + handBones + ", gun bones " + gunBones + ".");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool AlreadySkinned()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Transform skin = prefab != null ? FindExact(prefab.transform, "HandSkin") : null;
        if (skin == null)
            return false;

        if (CountNamed(prefab.transform, "HandSkin") != 1 || CountNamed(prefab.transform, "GunSkin") != 1)
            return false;

        var skinned = skin.GetComponent<SkinnedMeshRenderer>();
        if (skinned == null || skinned.bones == null || skinned.bones.Length == 0)
            return false;

        for (int i = 0; i < skinned.bones.Length; i++)
        {
            if (skinned.bones[i] == null || skinned.bones[i].GetComponent<MeshFilter>() != null)
                return false;
        }

        // "middle" used to rank as a mid joint, so the base was left beside the finger.
        Transform middleBase = FindExact(skin, "Hand_middle_base");
        if (middleBase != null && middleBase.childCount == 0)
            return false;

        return true;
    }

    static int SkinGroup(Transform group, Shader shader, bool chainFingers)
    {
        var pieces = new List<MeshRenderer>();
        var renderers = group.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var filter = renderers[i].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (!renderers[i].gameObject.activeInHierarchy)
                continue;
            pieces.Add(renderers[i]);
        }

        if (pieces.Count == 0)
            return 0;

        DestroyNamed(group, group.name + "Skin");

        // Added finger pieces may have been nested by the first pass. Put them
        // back under the group. World pose stays, so the grip does not move.
        for (int i = 0; i < pieces.Count; i++)
        {
            if (!PrefabUtility.IsAddedGameObjectOverride(pieces[i].gameObject))
                continue;
            if (pieces[i].transform.parent == group)
                continue;
            pieces[i].transform.SetParent(group, true);
        }

        var skinObject = new GameObject(group.name + "Skin");
        skinObject.transform.SetParent(group, false);
        var bones = CreateBones(skinObject.transform, pieces, chainFingers);

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var triangles = new List<int[]>();
        var materials = new List<Material>();
        bool hand = chainFingers;
        Matrix4x4 toSkin = skinObject.transform.worldToLocalMatrix;

        for (int i = 0; i < pieces.Count; i++)
        {
            Mesh source = pieces[i].GetComponent<MeshFilter>().sharedMesh;
            int boneIndex = i;
            Matrix4x4 toWorld = pieces[i].transform.localToWorldMatrix;

            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector2[] sourceUvs = source.uv;
            int offset = vertices.Count;
            for (int v = 0; v < sourceVertices.Length; v++)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(sourceVertices[v]);
                vertices.Add(toSkin.MultiplyPoint3x4(world));
                Vector3 normal = sourceNormals != null && v < sourceNormals.Length
                    ? sourceNormals[v]
                    : Vector3.up;
                normals.Add(toSkin.MultiplyVector(toWorld.MultiplyVector(normal)).normalized);
                uvs.Add(sourceUvs != null && v < sourceUvs.Length ? sourceUvs[v] : Vector2.zero);
                var weight = new BoneWeight();
                weight.boneIndex0 = boneIndex;
                weight.weight0 = 1f;
                weights.Add(weight);
            }

            int[] sourceTriangles = source.triangles;
            var shifted = new int[sourceTriangles.Length];
            for (int t = 0; t < sourceTriangles.Length; t++)
                shifted[t] = sourceTriangles[t] + offset;
            triangles.Add(shifted);

            string key = PartKey(source.name);
            if (string.IsNullOrEmpty(FindTexture(key, hand)))
                key = PartKey(pieces[i].name);
            materials.Add(PartMaterial(hand ? "Hand" : "Gun", key, hand, shader));
            pieces[i].enabled = false;
        }

        var mesh = new Mesh();
        mesh.name = group.name + "Skin";
        if (vertices.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Count;
        for (int s = 0; s < triangles.Count; s++)
            mesh.SetTriangles(triangles[s], s);
        mesh.boneWeights = weights.ToArray();

        var bindposes = new Matrix4x4[bones.Length];
        Matrix4x4 skinToWorld = skinObject.transform.localToWorldMatrix;
        for (int i = 0; i < bones.Length; i++)
            bindposes[i] = bones[i].worldToLocalMatrix * skinToWorld;
        mesh.bindposes = bindposes;
        mesh.RecalculateBounds();

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            return 0;

        string meshPath = MeshFolder + "/" + group.name + "Skin.asset";
        var previous = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (previous != null)
            AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var skinned = skinObject.AddComponent<SkinnedMeshRenderer>();
        skinned.sharedMesh = mesh;
        skinned.bones = bones;
        skinned.rootBone = chainFingers ? PalmBone(bones) : skinObject.transform;
        skinned.sharedMaterials = materials.ToArray();
        skinned.updateWhenOffscreen = true;
        skinned.quality = SkinQuality.Bone1;
        return bones.Length;
    }

    static void DestroyNamed(Transform group, string name)
    {
        for (int i = group.childCount - 1; i >= 0; i--)
        {
            Transform child = group.GetChild(i);
            if (child.name == name)
                Object.DestroyImmediate(child.gameObject);
        }
    }

    static int CountNamed(Transform root, string name)
    {
        int count = root.name == name ? 1 : 0;
        for (int i = 0; i < root.childCount; i++)
            count += CountNamed(root.GetChild(i), name);
        return count;
    }

    static Transform[] CreateBones(Transform skin, List<MeshRenderer> pieces, bool chainFingers)
    {
        var bones = new Transform[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            var bone = new GameObject(pieces[i].name);
            bone.transform.SetParent(skin, false);
            bone.transform.position = pieces[i].transform.position;
            bone.transform.rotation = pieces[i].transform.rotation;
            bone.transform.localScale = Vector3.one;
            bones[i] = bone.transform;
        }

        if (chainFingers)
            ChainBones(skin, pieces, bones);
        else
            ParentTrigger(pieces, bones);

        return bones;
    }

    static void ChainBones(Transform skin, List<MeshRenderer> pieces, Transform[] bones)
    {
        int palm = -1;
        for (int i = 0; i < pieces.Count; i++)
        {
            if (FingerOf(pieces[i].name) == "palm" || FingerOf(pieces[i].GetComponent<MeshFilter>().sharedMesh.name) == "palm")
                palm = i;
        }

        Transform chainRoot = palm >= 0 ? bones[palm] : skin;
        string[] fingers = { "thumb", "index", "middle", "ring", "pinky" };
        for (int f = 0; f < fingers.Length; f++)
        {
            var order = new List<int>();
            for (int i = 0; i < pieces.Count; i++)
            {
                string finger = FingerOf(pieces[i].name);
                if (finger == null)
                    finger = FingerOf(pieces[i].GetComponent<MeshFilter>().sharedMesh.name);
                if (finger == fingers[f])
                    order.Add(i);
            }

            order.Sort((a, b) => Rank(pieces[a]).CompareTo(Rank(pieces[b])));
            Transform chainParent = chainRoot;
            int lastRank = int.MinValue;
            for (int n = 0; n < order.Count; n++)
            {
                int index = order[n];
                int rank = Rank(pieces[index]);
                if (rank > lastRank)
                {
                    bones[index].SetParent(chainParent, true);
                    chainParent = bones[index];
                    lastRank = rank;
                }
                else
                {
                    Transform shared = chainParent.parent != null ? chainParent.parent : skin;
                    bones[index].SetParent(shared, true);
                }
            }
        }
    }

    static void ParentTrigger(List<MeshRenderer> pieces, Transform[] bones)
    {
        int trigger = -1;
        int launcher = -1;
        for (int i = 0; i < pieces.Count; i++)
        {
            string name = pieces[i].name.ToLowerInvariant();
            if (name.Contains("trigger"))
                trigger = i;
            else if (name.Contains("launcher"))
                launcher = i;
        }

        if (trigger >= 0 && launcher >= 0)
            bones[trigger].SetParent(bones[launcher], true);
    }

    static Transform PalmBone(Transform[] bones)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            if (FingerOf(bones[i].name) == "palm")
                return bones[i];
        }

        return bones.Length > 0 ? bones[0] : null;
    }

    static string FingerOf(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        string n = name.ToLowerInvariant();
        if (n.Contains("palm"))
            return "palm";
        if (n.Contains("index"))
            return "index";
        if (n.Contains("middle"))
            return "middle";
        if (n.Contains("ring"))
            return "ring";
        if (n.Contains("pinky") || n.Contains("piny"))
            return "pinky";
        if (n.Contains("thumb") || n.Contains("thum"))
            return "thumb";
        return null;
    }

    static int Rank(MeshRenderer piece)
    {
        // The object name is the joint the user kept. The mesh name is only a fallback
        // when a duplicated piece still has the source mesh's name.
        int objectRank = Rank(piece.name);
        string objectName = piece.name.ToLowerInvariant();
        if (objectRank != 0 || objectName.Contains("base") || objectName.Contains("palm"))
            return objectRank;
        return Rank(piece.GetComponent<MeshFilter>().sharedMesh.name);
    }

    static int Rank(string name)
    {
        string n = name.ToLowerInvariant();
        int paren = n.IndexOf(" (");
        if (paren >= 0)
            n = n.Substring(0, paren);
        // "middle" contains "mid", so take the finger name off before ranking the joint.
        n = n.Replace("middle", " ");
        n = n.Replace("thumb", " ").Replace("index", " ").Replace("pinky", " ").Replace("piny", " ").Replace("ring", " ");
        n = n.Replace("thum", " ");
        if (n.Contains("tip"))
            return 50;
        if (n.Contains("joint3"))
            return 40;
        if (n.Contains("mid"))
            return 30;
        if (n.Contains("joint2"))
            return 20;
        if (n.Contains("joint1"))
            return 10;
        if (n.Contains("base"))
            return 5;
        return 0;
    }

    static string PartKey(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        string n = name.ToLowerInvariant();
        int paren = n.IndexOf(" (");
        if (paren >= 0)
            n = n.Substring(0, paren);
        n = n.Replace(' ', '_').Trim('_');
        if (n.StartsWith("hand_"))
            n = n.Substring(5);
        n = n.Replace("piny_", "pinky_");
        if (n == "middle_base")
            n = "middle_joint1";
        if (n == "middle_mid")
            n = "middle_joint2";
        return n.Trim('_');
    }

    static string FindTexture(string key, bool hand)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        string prefix = hand ? "hand_" : "rocket_";
        string needle = key + "_basecolor";
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (file.StartsWith(prefix) && file.Contains(needle))
                return path;
        }

        return null;
    }

    static Material PartMaterial(string side, string key, bool hand, Shader shader)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Models/Rocket", "Materials");

        string safe = string.IsNullOrEmpty(key) ? "part" : key;
        var chars = safe.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                chars[i] = '_';
        }

        safe = new string(chars);

        string path = MaterialFolder + "/" + side + "_" + safe + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.5f);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        string texturePath = FindTexture(key, hand);
        var texture = texturePath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) : null;
        if (texture != null)
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static Transform FindExact(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindExact(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
