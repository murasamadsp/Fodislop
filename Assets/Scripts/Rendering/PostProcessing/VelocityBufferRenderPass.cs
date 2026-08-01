#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Fodinae.Rendering.PostProcessing;

public class VelocityBufferRenderPass : ScriptableRenderPass
{
    private const string PASS_NAME = "VelocityBufferPass";
    private static readonly int VelocityTexID = Shader.PropertyToID("_CameraVelocityTexture");
    private static readonly int VelocityPropID = Shader.PropertyToID("_Velocity");

    private readonly Material _velocityMaterial;
    private readonly MaterialPropertyBlock _propertyBlock = new();

    public VelocityBufferRenderPass(Shader velocityShader)
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        _velocityMaterial = new Material(velocityShader);
    }

#if !UNITY_6000_0_OR_NEWER
    private RTHandle? _velocityRT;

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.colorFormat = RenderTextureFormat.RGHalf;
        desc.depthBufferBits = 0;
        RenderingUtils.ReAllocIfNeeded(ref _velocityRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraVelocityTexture");

        cmd.SetGlobalTexture(VelocityTexID, _velocityRT);
        ConfigureTarget(_velocityRT);
        ConfigureClear(ClearFlag.Color, Color.clear);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_velocityRT == null || _velocityMaterial == null) return;

        var cmd = CommandBufferPool.Get(PASS_NAME);
        cmd.Clear();
        cmd.SetRenderTarget(_velocityRT);
        cmd.ClearRenderTarget(false, true, Color.clear);

        Camera cam = renderingData.cameraData.camera;
        float orthographicSize = cam.orthographic ? cam.orthographicSize : 5f;
        float screenHeightWorld = orthographicSize * 2f;

        IReadOnlyList<MotionBlurTag> tags = MotionBlurTag.ActiveTags;
        for (int i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (tag == null || !tag.gameObject.activeInHierarchy) continue;

            var renderer = tag.GetComponent<SpriteRenderer>();
            if (renderer == null || !renderer.enabled) continue;

            Vector2 uvVelocity = tag.Velocity / Mathf.Max(screenHeightWorld, 0.001f);
            _propertyBlock.SetVector(VelocityPropID, new Vector4(uvVelocity.x, uvVelocity.y, 0f, 0f));

            cmd.DrawRenderer(renderer, _velocityMaterial, 0, 0);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
#endif

#if UNITY_6000_0_OR_NEWER
    private class PassData
    {
        public Camera camera = null!;
        public Material velocityMaterial = null!;
        public MaterialPropertyBlock propertyBlock = null!;
        public TextureHandle velocityTexture;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_velocityMaterial == null) return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        var desc = cameraData.cameraTargetDescriptor;
        desc.colorFormat = RenderTextureFormat.RGHalf;
        desc.depthBufferBits = 0;

        TextureHandle velocityTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_CameraVelocityTexture", true);

        using (var builder = renderGraph.AddUnsafePass<PassData>(PASS_NAME, out var passData, profilingSampler))
        {
            passData.camera = cameraData.camera;
            passData.velocityMaterial = _velocityMaterial;
            passData.propertyBlock = _propertyBlock;
            passData.velocityTexture = velocityTexture;

            builder.UseTexture(passData.velocityTexture, AccessFlags.Write);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.SetRenderTarget(data.velocityTexture);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetGlobalTexture(VelocityTexID, data.velocityTexture);

                float orthographicSize = data.camera.orthographic ? data.camera.orthographicSize : 5f;
                float screenHeightWorld = orthographicSize * 2f;

                IReadOnlyList<MotionBlurTag> tags = MotionBlurTag.ActiveTags;
                for (int i = 0; i < tags.Count; i++)
                {
                    var tag = tags[i];
                    if (tag == null || !tag.gameObject.activeInHierarchy) continue;

                    var renderer = tag.GetComponent<SpriteRenderer>();
                    if (renderer == null || !renderer.enabled) continue;

                    Vector2 uvVelocity = tag.Velocity / Mathf.Max(screenHeightWorld, 0.001f);
                    data.propertyBlock.SetVector(VelocityPropID, new Vector4(uvVelocity.x, uvVelocity.y, 0f, 0f));

                    cmd.DrawRenderer(renderer, data.velocityMaterial, 0, 0);
                }
            });
        }
    }
#endif

    public void Dispose()
    {
#if !UNITY_6000_0_OR_NEWER
        _velocityRT?.Release();
#endif
        if (_velocityMaterial != null) Object.DestroyImmediate(_velocityMaterial);
    }
}
