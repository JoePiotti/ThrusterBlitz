using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Turns the view black and white while Active (a player out for the rest of an
/// Elimination round). Players and bots stay in color so their team can still be told
/// apart for call-outs; they are drawn again over the gray image, tested against the
/// scene depth so walls still hide them. Added to URP-Renderer. It adds no pass at all
/// while inactive, so it costs nothing in normal play on Quest.
/// </summary>
public class Hd2GrayscaleFeature : ScriptableRendererFeature
{
    /// <summary>Rendering layer bit given to every player and bot renderer while gray.</summary>
    public const uint CharacterLayer = 1u << 7;

    static bool active;

    public static bool Active
    {
        get { return active; }
        set
        {
            if (value && !active)
                MarkCharacters();
            active = value;
        }
    }

    // Tags players and bots (and what they hold) so the color pass can find them.
    static void MarkCharacters()
    {
        var roots = new List<Transform>();
        foreach (var bot in Object.FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            roots.Add(bot.transform);
        foreach (var player in Object.FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            roots.Add(player.transform);
        foreach (var net in Object.FindObjectsByType<Hd2NetPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            roots.Add(net.transform);

        for (int r = 0; r < roots.Count; r++)
        {
            Renderer[] renderers = roots[r].GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].renderingLayerMask |= CharacterLayer;
        }
    }

    class GrayscalePass : ScriptableRenderPass
    {
        readonly Material material;
        static readonly List<ShaderTagId> tags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        class DepthData
        {
            public TextureHandle source;
            public Material material;
        }

        class ColorData
        {
            public RendererListHandle list;
        }

        public GrayscalePass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
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
            desc.msaaSamples = MSAASamples.None;
            TextureHandle destination = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), "Hd2 Grayscale");
            resources.cameraColor = destination;

            TextureHandle sceneDepth = resources.cameraDepthTexture;
            if (!sceneDepth.IsValid() || !resources.activeDepthTexture.IsValid())
                return;

            // A single-sample copy of the scene depth to test the colored redraw against.
            TextureDesc depthDesc = renderGraph.GetTextureDesc(resources.activeDepthTexture);
            depthDesc.name = "Hd2GrayscaleDepth";
            depthDesc.msaaSamples = MSAASamples.None;
            depthDesc.bindTextureMS = false;
            depthDesc.clearBuffer = false;
            depthDesc.width = desc.width;
            depthDesc.height = desc.height;
            TextureHandle depth = renderGraph.CreateTexture(depthDesc);

            using (var builder = renderGraph.AddRasterRenderPass<DepthData>("Hd2 Grayscale Depth", out var data))
            {
                data.source = sceneDepth;
                data.material = material;
                builder.UseTexture(sceneDepth, AccessFlags.Read);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.SetRenderFunc((DepthData d, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 1));
            }

            var rendering = frameData.Get<UniversalRenderingData>();
            var camera = frameData.Get<UniversalCameraData>();
            var lights = frameData.Get<UniversalLightData>();
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(tags, rendering, camera, lights, SortingCriteria.CommonOpaque);
            var filtering = new FilteringSettings(RenderQueueRange.all, ~0, CharacterLayer);
            RendererListHandle list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));

            using (var builder = renderGraph.AddRasterRenderPass<ColorData>("Hd2 Grayscale Characters", out var data))
            {
                data.list = list;
                builder.UseRendererList(list);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.SetRenderFunc((ColorData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
            }
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
