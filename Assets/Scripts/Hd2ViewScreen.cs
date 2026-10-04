using UnityEngine;

/// <summary>
/// Shows a scene camera on a screen. The camera is not tied to the headset.
/// </summary>
public class Hd2ViewScreen : MonoBehaviour
{
    public Camera viewCamera;
    public Renderer screen;
    public int textureWidth = 1280;
    public int textureHeight = 960;

    RenderTexture texture;
    Material material;

    void Awake()
    {
        if (viewCamera == null || screen == null)
            return;

        texture = new RenderTexture(textureWidth, textureHeight, 16);
        texture.name = "PlatformView";
        viewCamera.targetTexture = texture;
        viewCamera.enabled = true;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        material = new Material(shader);
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        screen.sharedMaterial = material;
    }

    void OnDestroy()
    {
        if (viewCamera != null)
            viewCamera.targetTexture = null;
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }

        if (material != null)
            Destroy(material);
    }
}
