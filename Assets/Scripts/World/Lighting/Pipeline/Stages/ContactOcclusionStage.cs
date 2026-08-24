#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages
{
    /// <summary>
    /// Dispatches <c>SolveContactOcclusion</c>, writing the contact-AO
    /// texture <see cref="CompositeStage"/> later reads. Extracted verbatim
    /// from the engine's former private <c>DispatchContactOcclusion</c>.
    /// </summary>
    public sealed class ContactOcclusionStage : ILightingStage
    {
        private static readonly int ContactOcclusionTextureId =
            Shader.PropertyToID("_ContactOcclusionTexture");

        private readonly int _kernel;

        public ContactOcclusionStage(int kernel)
        {
            _kernel = kernel;
        }

        public void Record(CommandBuffer commandBuffer, in LightingFrameContext context)
        {
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                ContactOcclusionTextureId,
                context.ContactOcclusionTexture);
            commandBuffer.DispatchCompute(
                context.Compute,
                _kernel,
                Mathf.CeilToInt(context.FieldWidth / 8f),
                Mathf.CeilToInt(context.FieldHeight / 8f),
                1);
        }
    }
}
