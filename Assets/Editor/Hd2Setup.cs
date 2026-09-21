using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
public static class Hd2Setup
{
    const string PipelinePath = "Assets/Settings/URP-HD2.asset";
    const string RendererPath = "Assets/Settings/URP-Renderer.asset";
    const string ScenePath = "Assets/Scenes/Greybox.unity";
    const string SessionKey = "HD2_URP_SETUP_DONE";

    static Hd2Setup()
    {
        EditorApplication.delayCall += AutoSetupUrpIfNeeded;
    }

    static void AutoSetupUrpIfNeeded()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;
        if (GraphicsSettings.defaultRenderPipeline != null)
        {
            SessionState.SetBool(SessionKey, true);
            return;
        }

        SetupUrp();
        SessionState.SetBool(SessionKey, true);
    }

    public static void SetupUrp()
    {
        EnsureFolder("Assets", "Settings");
        EnsureFolder("Assets", "Scenes");

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
        }

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        var names = QualitySettings.names;
        var current = QualitySettings.GetQualityLevel();
        for (var i = 0; i < names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(current, true);

        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);

        CreateGreyboxSceneIfMissing();
        AssetDatabase.SaveAssets();
        Debug.Log("HD2 URP setup complete.");
    }

    public static void SetupQuestAndXr()
    {
        Hd2QuestPlayerSetup.SetupQuestPlayer();
        Hd2XrSetup.SetupOpenXr();
        AssetDatabase.SaveAssets();
        Debug.Log("HD2 Quest + OpenXR setup complete.");
    }

    static void CreateGreyboxSceneIfMissing()
    {
        if (File.Exists(ScenePath))
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = Vector3.zero;

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "ScaleCube";
        cube.transform.position = new Vector3(0f, 0.5f, 3f);

        var light = Object.FindFirstObjectByType<Light>();
        if (light != null)
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);

        var scenes = EditorBuildSettings.scenes;
        var alreadyListed = false;
        foreach (var listed in scenes)
        {
            if (listed.path == ScenePath)
                alreadyListed = true;
        }

        if (!alreadyListed)
        {
            var next = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(next, 0);
            next[scenes.Length] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = next;
        }
    }

    static void EnsureFolder(string parent, string name)
    {
        var path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, name);
    }
}
