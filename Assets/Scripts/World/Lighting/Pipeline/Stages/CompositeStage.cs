#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages;
public sealed class CompositeStage : ILightingStage
{
    private static readonly int _DirectInputID = Shader.PropertyToID("_DirectInput");
    private static readonly int _StaticDirectInputID = Shader.PropertyToID("_StaticDirectInput");
    private static readonly int _BounceInputID = Shader.PropertyToID("_BounceInput");
    private static readonly int _ResultID = Shader.PropertyToID("_Result");

    private readonly int _kernel;

    public CompositeStage(int kernel)
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
            _BounceInputID,
            context.BounceTexture);
        commandBuffer.SetComputeTextureParam(
            context.Compute,
            _kernel,
            _ResultID,
            context.ResultTexture);
        commandBuffer.DispatchCompute(
            context.Compute,
            _kernel,
            Mathf.CeilToInt(context.FieldWidth / 8f),
            Mathf.CeilToInt(context.FieldHeight / 8f),
            1);
    }
}
