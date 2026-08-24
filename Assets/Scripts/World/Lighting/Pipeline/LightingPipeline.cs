#nullable enable

using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline
{
    /// <summary>
    /// Records a single <see cref="ILightingStage"/> against the shared
    /// command buffer. Not a multi-stage sequencer: the engine's own
    /// dirty-flag orchestration decides which stage runs when and in what
    /// order (that control flow is load-bearing and stays in
    /// <c>TerrariaLightingEngine.UpdateLighting</c>) - this type exists so a
    /// stage is invoked the same way regardless of which one it is, which is
    /// what makes swapping a stage's implementation a one-line change at the
    /// call site instead of an edit to the orchestration itself.
    /// </summary>
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
}
