#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages
{
    /// <summary>
    /// Rasterizes the dynamic light sources into their own emission field,
    /// which holds nothing else. Extracted verbatim from the engine's former
    /// private <c>ComposeDynamicEmissionField</c>.
    /// </summary>
    /// <remarks>
    /// Separate from the terrain's emission on purpose. Light transport is
    /// linear in emission and the medium is the same either way, so the two
    /// can be solved apart and summed at the end for exactly the result a
    /// combined solve would give. That is what lets the terrain half be
    /// cached until geometry changes while only this half is re-solved as
    /// lamps move.
    ///
    /// It also keeps the lights out of the ray march itself. They used to be
    /// evaluated inside SampleEmission, which the march calls once per ray
    /// step - one lamp cost as many iterations as the solve had steps, around
    /// 238 million on the measured configuration, and a second lamp doubled
    /// it. Here each source costs one small quad.
    /// </remarks>
    public sealed class DynamicEmissionCompositionStage : ILightingStage
    {
        private static readonly int DynamicLightsId = Shader.PropertyToID("_DynamicLights");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");

        public void Record(CommandBuffer commandBuffer, in LightingFrameContext context)
        {
            commandBuffer.BeginSample("Fodinae.Lighting.ComposeEmission");

            // Cleared every time rather than accumulated: a lamp that moved
            // must leave nothing behind at its previous position.
            commandBuffer.SetRenderTarget(context.DynamicEmissionField);
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);
            if (context.DynamicLightCount > 0 && context.DynamicLightBuffer != null)
            {
                Vector4 worldRect = context.WorldRect;
                Matrix4x4 projection = Matrix4x4.Ortho(
                    worldRect.x,
                    worldRect.x + worldRect.z,
                    worldRect.y,
                    worldRect.y + worldRect.w,
                    -100f,
                    100f);
                commandBuffer.SetViewProjectionMatrices(
                    Matrix4x4.identity,
                    GL.GetGPUProjectionMatrix(projection, renderIntoTexture: true));
                // Set on the material, not as global shader state. A global
                // _CellSize would be visible to every shader that happens to
                // declare that name, and this pass has no business changing
                // what the rest of the frame sees.
                context.DynamicEmissionMaterial.SetBuffer(DynamicLightsId, context.DynamicLightBuffer);
                context.DynamicEmissionMaterial.SetFloat(CellSizeId, context.CellSize);
                commandBuffer.DrawProcedural(
                    Matrix4x4.identity,
                    context.DynamicEmissionMaterial,
                    0,
                    MeshTopology.Triangles,
                    6,
                    context.DynamicLightCount);
            }

            commandBuffer.EndSample("Fodinae.Lighting.ComposeEmission");
        }
    }
}
