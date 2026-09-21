using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

public static class Hd2XrSetup
{
    public static void SetupOpenXr()
    {
        EnableOpenXr(BuildTargetGroup.Android);
        EnableOpenXr(BuildTargetGroup.Standalone);
        EnableMetaQuestFeatures(BuildTargetGroup.Android);
        EnableMetaQuestFeatures(BuildTargetGroup.Standalone);
        Debug.Log("HD2 OpenXR setup complete.");
    }

    static void EnableOpenXr(BuildTargetGroup group)
    {
        XRGeneralSettingsPerBuildTarget buildTargetSettings = null;
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out buildTargetSettings);

        if (buildTargetSettings == null)
        {
            buildTargetSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");
            AssetDatabase.CreateAsset(buildTargetSettings, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, buildTargetSettings, true);
        }

        var settings = buildTargetSettings.SettingsForBuildTarget(group);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            AssetDatabase.AddObjectToAsset(settings, buildTargetSettings);
            buildTargetSettings.SetSettingsForBuildTarget(group, settings);
            settings.name = group + " Settings";
        }

        var manager = settings.AssignedSettings;
        if (manager == null)
        {
            manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            AssetDatabase.AddObjectToAsset(manager, buildTargetSettings);
            manager.name = group + " Loaders";
            settings.AssignedSettings = manager;
        }

            XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, group);
        AssetDatabase.SaveAssets();
    }

    static void EnableMetaQuestFeatures(BuildTargetGroup group)
    {
        FeatureHelpers.RefreshFeatures(group);
        EnableFeature(group, MetaQuestFeature.featureId);
        EnableFeature(group, "com.unity.openxr.feature.input.metaquestpro");
        EnableFeature(group, "com.unity.openxr.feature.input.metaquestplus");
        EnableFeature(group, "com.unity.openxr.feature.input.oculustouch");
    }

    static void EnableFeature(BuildTargetGroup group, string featureId)
    {
        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
        if (feature != null)
            feature.enabled = true;
    }
}
