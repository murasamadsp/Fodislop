#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing;

/// <summary>
/// Renderer-owned raster layout. The Camera remains the display/input view;
/// only frame-local attachment descriptors and GPU matrices use the world grid.
/// </summary>
internal sealed class WorldRasterGridPass : ScriptableRenderPass2D
{
    private WorldRenderGridCamera _layout;

    public WorldRasterGridPass(int firstSortingLayerId)
    {
        renderPassEvent2D = RenderPassEvent2D.BeforeRenderingNormals;
        renderPassSortingLayerID = firstSortingLayerId;
        ConfigureInput(ScriptableRenderPassInput.Color);
    }

    public void Prepare(ref CameraData cameraData)
    {
        _layout = WorldRenderGridCamera.Capture(cameraData.camera);
        // AddRenderPasses runs before Renderer2D.CreateResources. Its color,
        // depth, normals and light attachments all derive from this descriptor.
        ref RenderTextureDescriptor descriptor = ref cameraData.cameraTargetDescriptor;
        descriptor.width = _layout.Grid.Width;
        descriptor.height = _layout.Grid.Height;
        descriptor.msaaSamples = 1;
        descriptor.bindMS = false;
        descriptor.useDynamicScale = false;
    }

    private sealed class PassData
    {
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public TextureHandle Color;
    }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
    {
        TextureHandle color = frameData.Get<UniversalResourceData>().activeColorTexture;
        RenderTargetInfo descriptor = graph.GetRenderTargetInfo(color);
        if (descriptor.width != _layout.Grid.Width || descriptor.height != _layout.Grid.Height)
        {
            throw new InvalidOperationException("Renderer2D did not allocate the world-grid raster target.");
        }

        using var builder = graph.AddRasterRenderPass<PassData>("Kern.WorldRaster.Projection", out var data);
        data.View = _layout.ViewMatrix;
        data.Projection = _layout.ProjectionMatrix;
        data.Color = color;
        builder.UseTexture(color, AccessFlags.Read);
        builder.AllowGlobalStateModification(true);
        builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
        {
            bool flipped = SystemInfo.graphicsUVStartsAtTop &&
                context.GetTextureUVOrigin(pass.Color) == TextureUVOrigin.BottomLeft;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(pass.Projection, flipped);
            RenderingUtils.SetViewAndProjectionMatrices(context.cmd, pass.View, projection, true);
        });
    }
}
