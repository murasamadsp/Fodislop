#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages
{
    /// <summary>
    /// Dispatches <c>SolveAutomaticNormals</c>, writing the screen-space
    /// occupancy-gradient normal field the resolve/composite kernels sample
    /// for their Lambertian term. Extracted verbatim from the engine's
    /// former private <c>DispatchAutomaticNormals</c>.
    /// </summary>
    public sealed class AutomaticNormalsStage : ILightingStage
    {
        private static readonly int AutomaticNormalFieldId =
            Shader.PropertyToID("_AutomaticNormalField");

        private readonly int _kernel;

        public AutomaticNormalsStage(int kernel)
        {
            _kernel = kernel;
        }

        public void Record(CommandBuffer commandBuffer, in LightingFrameContext context)
        {
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                AutomaticNormalFieldId,
                context.AutomaticNormalField);
            commandBuffer.DispatchCompute(
                context.Compute,
                _kernel,
                Mathf.CeilToInt(context.FieldWidth / 8f),
                Mathf.CeilToInt(context.FieldHeight / 8f),
                1);
        }
    }
}
