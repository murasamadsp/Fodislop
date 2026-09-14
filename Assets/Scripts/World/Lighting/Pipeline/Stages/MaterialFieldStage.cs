#nullable enable

using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages;
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

        // Mips serve contact AO and its debug view. Light transport reads only
        // base-level physical occupancy; averaging mass would make walls leak.
        commandBuffer.GenerateMips(context.MaterialField);

        commandBuffer.EndSample("Fodinae.Lighting.MaterialField");
    }
}
