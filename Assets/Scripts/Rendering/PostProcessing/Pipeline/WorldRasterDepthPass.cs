#nullable enable

using System;
using Kern.Rendering.UrpIntegration;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing;

// Renderer2D's raster depth must follow the world color attachment.
internal sealed class WorldRasterDepthPass : ScriptableRenderPass2D
{
    public WorldRasterDepthPass()
    {
        renderPassEvent2D = RenderPassEvent2D.BeforeRendering;
        ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
    {
        PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup?.Invoke(graph, frameData);
        UniversalResourceData resources = frameData.Get<UniversalResourceData>();
        TextureHandle color = resources.activeColorTexture;
        if (!color.IsValid())
        {
            throw new InvalidOperationException("World raster requires a valid color attachment.");
        }

        // Validate the attachment Renderer2D will actually bind. Its active
        // selector can reference display depth independently of cameraDepth.
        TextureHandle depth = resources.activeDepthTexture;
        if (!depth.IsValid())
        {
            return;
        }

        RenderTargetInfo colorInfo = graph.GetRenderTargetInfo(color);
        // Imported depth attachments can expose RenderTargetInfo without a
        // creation descriptor. Query attachment facts before deriving a resource.
        RenderTargetInfo depthInfo = graph.GetRenderTargetInfo(depth);
        if (colorInfo.width == depthInfo.width && colorInfo.height == depthInfo.height &&
            colorInfo.volumeDepth == depthInfo.volumeDepth && colorInfo.msaaSamples == depthInfo.msaaSamples)
        {
            return;
        }

        // Imported handles may have no creation descriptor. The renderer's
        // frame descriptor supplies allocation flags; graph facts supply the
        // actual extent, samples and depth/stencil format.
        var depthDesc = new TextureDesc(frameData.Get<UniversalCameraData>().cameraTargetDescriptor);
        depthDesc.name = "_WorldRasterDepth";
        depthDesc.width = colorInfo.width;
        depthDesc.height = colorInfo.height;
        depthDesc.slices = colorInfo.volumeDepth;
        depthDesc.msaaSamples = (MSAASamples)colorInfo.msaaSamples;
        depthDesc.format = depthInfo.format;
        depthDesc.bindTextureMS = depthInfo.bindMS && depthInfo.msaaSamples == colorInfo.msaaSamples;
        depthDesc.enableRandomWrite = false;
        depthDesc.useMipMap = false;
        depthDesc.autoGenerateMips = false;
        depthDesc.clearBuffer = true;
        WorldRasterAttachmentBinding.SetCameraDepth(resources, graph.CreateTexture(depthDesc));
    }
}
