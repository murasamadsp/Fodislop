#nullable enable

using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages
{
    /// <summary>
    /// Rebuilds the material/emission fields from terrain and registered
    /// lighting-geometry contributors, then generates the mip chain the
    /// ray march needs for its far-step occupancy samples. Extracted
    /// verbatim from the "rebuildFields" block inside the engine's former
    /// inline <c>UpdateLighting</c>. The decision of whether a rebuild is
    /// needed this frame (field/region/geometry dirty) stays in
    /// <c>LightingEngine.UpdateLighting</c>, same as every other
    /// extracted stage's dispatch condition.
    /// </summary>
    public sealed class MaterialFieldStage : ILightingStage
    {
        public void Record(CommandBuffer commandBuffer, in LightingFrameContext context)
        {
            commandBuffer.BeginSample("Fodinae.Lighting.MaterialField");
            context.TerrainRenderer.RenderLightingMaterialFields(
                commandBuffer,
                context.MaterialField,
                context.StaticEmissionField,
                context.WorldRect);
            if (context.GeometryRegistry.HasContributors)
            {
                context.GeometryRegistry.RenderLightingFields(
                    commandBuffer,
                    context.MaterialField,
                    context.StaticEmissionField,
                    context.WorldRect,
                    clearFields: false);
            }

            // Mip-цепь обязательна: марш сэмплит occupancy с
            // samplingMip = log2(stepLength) > 0 на дальних шагах,
            // а SampleLevel на текстуре без mip'ов возвращает 0 —
            // свет не поглощался блоками дальше первого шага.
            // GenerateMips строит цепь из mip 0, не затирая contributors.
            commandBuffer.GenerateMips(context.MaterialField);

            commandBuffer.EndSample("Fodinae.Lighting.MaterialField");
        }
    }
}
