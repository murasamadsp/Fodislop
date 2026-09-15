#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages;
public sealed class DynamicEmissionCompositionStage : ILightingStage
{
    private static readonly int _DynamicLightsID = Shader.PropertyToID("_DynamicLights");
    private static readonly int _CellSizeID = Shader.PropertyToID("_CellSize");

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
        // Проекция ставится до ветки по лампам: террейн рисуется в эту же цель
        // независимо от того, есть ли в кадре хоть одна лампа.
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

        if (context.DynamicLightCount > 0 && context.DynamicLightBuffer != null)
        {
            // Set on the material, not as global shader state. A global
            // _CellSize would be visible to every shader that happens to
            // declare that name, and this pass has no business changing
            // what the rest of the frame sees.
            context.DynamicEmissionMaterial.SetBuffer(_DynamicLightsID, context.DynamicLightBuffer);
            context.DynamicEmissionMaterial.SetFloat(_CellSizeID, context.CellSize);
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
