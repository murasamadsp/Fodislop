#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing;

/// <summary>Point presentation of an already rasterized world, before URP output encoding and UI.</summary>
internal sealed class WorldRasterPresentationPass : ScriptableRenderPass2D
{
    public WorldRasterPresentationPass()
    {
        renderPassEvent2D = RenderPassEvent2D.AfterRenderingPostProcessing + 1;
    }

    private sealed class PassData
    {
        public TextureHandle Source;
        public TextureHandle Destination;
        public Vector4 SourceUv;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
    }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
    {
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalResourceData resources = frameData.Get<UniversalResourceData>();
        if (resources.isActiveTargetBackBuffer)
        {
            throw new InvalidOperationException("World raster presentation requires an intermediate world image.");
        }

        var layout = WorldRenderGridCamera.Capture(cameraData.camera);
        TextureHandle source = resources.activeColorTexture;
        TextureDesc descriptor = source.GetDescriptor(graph);
        descriptor.name = "_WorldRasterPresentation";
        descriptor.sizeMode = TextureSizeMode.Explicit;
        descriptor.width = cameraData.camera.pixelWidth;
        descriptor.height = cameraData.camera.pixelHeight;
        descriptor.depthBufferBits = DepthBits.None;
        descriptor.msaaSamples = MSAASamples.None;
        descriptor.bindTextureMS = false;
        descriptor.enableRandomWrite = false;
        descriptor.filterMode = FilterMode.Point;
        descriptor.clearBuffer = false;
        TextureHandle destination = graph.CreateTexture(descriptor);

        using (var builder = graph.AddRasterRenderPass<PassData>("Kern.WorldRaster.Present", out var data))
        {
            data.Source = source;
            data.Destination = destination;
            data.SourceUv = layout.ViewportToWorldUv;
            data.View = layout.ViewMatrix;
            data.Projection = layout.OriginalProjectionMatrix;
            builder.UseTexture(source, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
            {
                Vector4 uv = pass.SourceUv;
                // The crop is specified in world Y-up coordinates. Account for
                // both graph resource origins, including compute-produced color.
                if (context.GetTextureUVOrigin(pass.Destination) == TextureUVOrigin.TopLeft)
                {
                    uv.w += uv.y;
                    uv.y = -uv.y;
                }

                if (context.GetTextureUVOrigin(pass.Source) == TextureUVOrigin.TopLeft)
                {
                    uv.w = 1f - uv.w;
                    uv.y = -uv.y;
                }

                Blitter.BlitTexture(context.cmd, pass.Source, uv, 0f, false);
                bool flipped = SystemInfo.graphicsUVStartsAtTop &&
                    context.GetTextureUVOrigin(pass.Destination) == TextureUVOrigin.BottomLeft;
                RenderingUtils.SetViewAndProjectionMatrices(context.cmd, pass.View,
                    GL.GetGPUProjectionMatrix(pass.Projection, flipped), true);
            });
        }

        resources.cameraColor = destination;
    }
}
