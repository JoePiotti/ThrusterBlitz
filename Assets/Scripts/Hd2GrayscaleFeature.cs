using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Turns the view black and white while Active (a player out for the rest of an
/// Elimination round). Added to URP-Renderer. It adds no pass at all while inactive,
/// so it costs nothing in normal play on Quest.
/// </summary>
public class Hd2GrayscaleFeature : ScriptableRendererFeature
{
    public static bool Active;

    class GrayscalePass : ScriptableRenderPass
    {
        readonly Material material;

        public GrayscalePass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "Hd2Grayscale";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), "Hd2 Grayscale");
            resources.cameraColor = destination;
        }
    }

    GrayscalePass pass;

    public override void Create()
    {
        Material material = Resources.Load<Material>("Grayscale");
        pass = material != null ? new GrayscalePass(material) : null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Active || pass == null)
            return;
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;
        renderer.EnqueuePass(pass);
    }
}
