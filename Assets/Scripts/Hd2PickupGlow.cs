using UnityEngine;

/// <summary>
/// A small point light in the pickup's color. It sits in the sphere and
/// washes the nearby floor. No shadows, so it stays cheap on Quest.
/// </summary>
public static class Hd2PickupGlow
{
    public static void Ensure(Transform root, Color color)
    {
        if (root == null)
            return;

        Transform glow = root.Find("Glow");
        Light light;
        if (glow == null)
        {
            var lamp = new GameObject("Glow");
            lamp.transform.SetParent(root, false);
            glow = lamp.transform;
            light = lamp.AddComponent<Light>();
        }
        else
        {
            light = glow.GetComponent<Light>();
            if (light == null)
                light = glow.gameObject.AddComponent<Light>();
        }

        glow.localPosition = new Vector3(0f, 1.05f, 0f);
        glow.localRotation = Quaternion.identity;
        light.type = LightType.Point;
        light.color = new Color(color.r, color.g, color.b, 1f);
        light.shadows = LightShadows.None;
        light.range = 4.5f;
        light.intensity = 40f;
        light.bounceIntensity = 0f;
        light.renderMode = LightRenderMode.ForcePixel;
        light.lightmapBakeType = LightmapBakeType.Realtime;
    }
}
