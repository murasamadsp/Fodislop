#nullable enable

using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline;
public sealed class LightingPipeline
{
    private readonly ILightingStage _stage;

    public LightingPipeline(ILightingStage stage)
    {
        _stage = stage;
    }

    public void Record(CommandBuffer commandBuffer, in LightingFrameContext context)
    {
        _stage.Record(commandBuffer, context);
    }
}
