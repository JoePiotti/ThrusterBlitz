using UnityEditor;
using UnityEngine;

/// <summary>
/// Parents a blockout pistol to each hand and adds the Greybox target if it is missing.
/// Does not move or rotate a pistol that is already on a hand.
/// Safe to run more than once.
/// </summary>
[InitializeOnLoad]
public static class Hd2PistolSetup
{
    const string ModelPath = "Assets/Models/Pistol/Pistol.fbx";
    const string PrefabPath = "Assets/Prefabs/VRPlayer.prefab";
    const string ScenePath = "Assets/Scenes/Greybox.unity";
    const string MaterialPath = "Assets/Materials/Pistol.mat";

    static int tries;

    static Hd2PistolSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    public static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        if (tries++ > 40)
            return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        tries = 100;
        EnsurePrefab(model);
        EnsureTarget();
    }

    static void EnsurePrefab(GameObject model)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            bool changed = false;
            changed |= EnsureHandPistol(root, model, "RightHand", Hd2Pistol.Hand.Right);
            changed |= EnsureHandPistol(root, model, "LeftHand", Hd2Pistol.Hand.Left);
            if (!changed)
                return;

            var locomotion = root.GetComponent<Hd2Locomotion>();
            if (locomotion != null)
                locomotion.arcLaunchAngle = 40f;

            var guns = root.GetComponentsInChildren<Hd2Pistol>(true);
            for (int i = 0; i < guns.Length; i++)
                guns[i].shotSpeed = 45f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool EnsureHandPistol(GameObject root, GameObject model, string handName, Hd2Pistol.Hand hand)
    {
        var handTransform = root.transform.Find(handName);
        if (handTransform == null)
            return false;
        if (handTransform.GetComponentInChildren<Hd2Pistol>(true) != null)
            return false;

        var pistol = (GameObject)PrefabUtility.InstantiatePrefab(model, handTransform);
        pistol.name = "Pistol";
        pistol.transform.localPosition = new Vector3(0f, -0.02f, 0.05f);
        pistol.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        pistol.transform.localScale = Vector3.one;

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
        {
            var renderers = pistol.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = material;
        }

        var gun = pistol.AddComponent<Hd2Pistol>();
        gun.hand = hand;
        var muzzle = pistol.transform.Find("Muzzle");
        if (muzzle != null)
        {
            muzzle.localPosition = new Vector3(0f, 0.16f, -0.054f);
            muzzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            gun.muzzle = muzzle;
        }

        return true;
    }

    static void EnsureTarget()
    {
        if (GameObject.Find("ShootTarget") != null || Object.FindAnyObjectByType<Hd2ShootTarget>() != null)
            return;

        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            return;

        var root = new GameObject("ShootTarget");
        root.transform.position = new Vector3(-1.2f, 0f, 7f);
        root.AddComponent<Hd2ShootTarget>();

        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Board";
        board.transform.SetParent(root.transform, false);
        board.transform.localPosition = new Vector3(0f, 1.45f, 0f);
        board.transform.localScale = new Vector3(0.9f, 0.9f, 0.06f);

        var stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stand.name = "Stand";
        stand.transform.SetParent(root.transform, false);
        stand.transform.localPosition = new Vector3(0f, 0.5f, 0.05f);
        stand.transform.localScale = new Vector3(0.08f, 1f, 0.08f);
        var standCollider = stand.GetComponent<Collider>();
        if (standCollider != null)
            Object.DestroyImmediate(standCollider);

        var bullseye = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bullseye.name = "Bullseye";
        bullseye.transform.SetParent(root.transform, false);
        bullseye.transform.localPosition = new Vector3(0f, 1.45f, -0.04f);
        bullseye.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        bullseye.transform.localScale = new Vector3(0.28f, 0.01f, 0.28f);
        var bullCollider = bullseye.GetComponent<Collider>();
        if (bullCollider != null)
            Object.DestroyImmediate(bullCollider);

        var boardMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ShootTarget.mat");
        var bullMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Bullseye.mat");
        if (boardMat != null)
        {
            board.GetComponent<Renderer>().sharedMaterial = boardMat;
            stand.GetComponent<Renderer>().sharedMaterial = boardMat;
        }
        if (bullMat != null)
            bullseye.GetComponent<Renderer>().sharedMaterial = bullMat;

        var target = root.GetComponent<Hd2ShootTarget>();
        target.board = board.GetComponent<Renderer>();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    }
}
