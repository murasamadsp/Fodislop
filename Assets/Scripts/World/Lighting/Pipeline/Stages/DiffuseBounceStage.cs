#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages;
public sealed class DiffuseBounceStage : ILightingStage
{
    private static readonly int _DirectInputID = Shader.PropertyToID("_DirectInput");
    private static readonly int _StaticDirectInputID = Shader.PropertyToID("_StaticDirectInput");
    private static readonly int _BounceTextureID = Shader.PropertyToID("_BounceTexture");

    private readonly int _kernel;

    public DiffuseBounceStage(int kernel)
    {
        _kernel = kernel;
    }

    public void Record(CommandBuffer commandBuffer, in LightingFrameContext context)
    {
        commandBuffer.SetComputeTextureParam(
            context.Compute,
            _kernel,
            _DirectInputID,
            context.DirectTexture);
        commandBuffer.SetComputeTextureParam(
            context.Compute,
            _kernel,
            _StaticDirectInputID,
            context.StaticDirectTexture);
        commandBuffer.SetComputeTextureParam(
            context.Compute,
            _kernel,
            _BounceTextureID,
            context.BounceTexture);
        commandBuffer.DispatchCompute(
            context.Compute,
            _kernel,
            Mathf.CeilToInt(context.BounceWidth / 8f),
            Mathf.CeilToInt(context.BounceHeight / 8f),
            1);
    }
}
