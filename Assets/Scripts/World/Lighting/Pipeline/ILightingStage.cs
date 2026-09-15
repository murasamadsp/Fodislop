#nullable enable

using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline;
public interface ILightingStage
{
    void Record(CommandBuffer commandBuffer, in LightingFrameContext context);
}
