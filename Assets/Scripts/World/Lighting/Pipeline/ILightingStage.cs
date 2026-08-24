#nullable enable

using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline
{
    /// <summary>
    /// One self-contained step of the radiance-cascade solve: binds whatever
    /// resources it needs and records its own dispatch(es) into the shared
    /// command buffer. A stage owns no GPU resources itself - the engine
    /// allocates/releases those - it only knows how to record work against
    /// resources handed to it through <see cref="LightingFrameContext"/>.
    /// </summary>
    public interface ILightingStage
    {
        void Record(CommandBuffer commandBuffer, in LightingFrameContext context);
    }
}
