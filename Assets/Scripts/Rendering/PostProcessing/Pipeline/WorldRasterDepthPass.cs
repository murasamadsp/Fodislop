#nullable enable

using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing;

// Renderer2D's raster depth must follow the world color attachment, rather
// than the display viewport restored by WorldRenderGridBinding for UI.
internal sealed class WorldRasterDepthPass : ScriptableRenderPass2D
{
    public WorldRasterDepthPass()
    {
        renderPassEvent2D = RenderPassEvent2D.BeforeRendering;
    }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
    {
        PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup?.Invoke(graph, frameData);
        UniversalResourceData resources = frameData.Get<UniversalResourceData>();
        TextureHandle color = resources.activeColorTexture;
        TextureHandle depth = resources.activeDepthTexture;
        if (!color.IsValid() || !depth.IsValid())
        {
            throw new InvalidOperationException("World raster requires valid color and depth attachments.");
        }

        TextureDesc colorDesc = color.GetDescriptor(graph);
        TextureDesc depthDesc = depth.GetDescriptor(graph);
        if (colorDesc.width == depthDesc.width && colorDesc.height == depthDesc.height &&
            colorDesc.slices == depthDesc.slices && colorDesc.msaaSamples == depthDesc.msaaSamples)
        {
            return;
        }

        if (depth != resources.cameraDepth)
        {
            throw new InvalidOperationException("World raster depth must be an intermediate camera attachment.");
        }

        // Retain the actual depth/stencil format and allocation flags. Only
        // the raster extent and sample count come from the color attachment.
        depthDesc.name = "_WorldRasterDepth";
        depthDesc.width = colorDesc.width;
        depthDesc.height = colorDesc.height;
        depthDesc.slices = colorDesc.slices;
        depthDesc.msaaSamples = colorDesc.msaaSamples;
        depthDesc.clearBuffer = true;
        resources.cameraDepth = graph.CreateTexture(depthDesc);
    }
}
