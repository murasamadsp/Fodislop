#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages
{
    /// <summary>
    /// Dispatches <c>CompositeLighting</c>: sums the dynamic direct texture,
    /// the cached static direct texture and the diffuse-bounce texture into
    /// the final lightmap. Extracted verbatim from the engine's former
    /// private <c>DispatchComposite</c>.
    /// </summary>
    public sealed class CompositeStage : ILightingStage
    {
        private static readonly int DirectInputId = Shader.PropertyToID("_DirectInput");
        private static readonly int StaticDirectInputId = Shader.PropertyToID("_StaticDirectInput");
        private static readonly int BounceInputId = Shader.PropertyToID("_BounceInput");
        private static readonly int ResultId = Shader.PropertyToID("_Result");

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
                DirectInputId,
                context.DirectTexture);
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                StaticDirectInputId,
                context.StaticDirectTexture);
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                BounceInputId,
                context.BounceTexture);
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                ResultId,
                context.ResultTexture);
            commandBuffer.DispatchCompute(
                context.Compute,
                _kernel,
                Mathf.CeilToInt(context.FieldWidth / 8f),
                Mathf.CeilToInt(context.FieldHeight / 8f),
                1);
        }
    }
}
