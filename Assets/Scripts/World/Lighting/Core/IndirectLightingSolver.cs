#nullable enable

using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class IndirectLightingSolver
{
    private static readonly ProfilerMarker CompositeMarker =
        new("Kern.Lighting.Composite.Record.CPU");

    private readonly LightingResourceManager _resources;

    public IndirectLightingSolver(LightingResourceManager resources)
    {
        _resources = resources;
    }

    public void RecordBounce(CommandBuffer commandBuffer)
    {
        ComputeShader compute = _resources.LightingCompute!;
        int kernel = _resources.SolveDiffuseBounceKernel;
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.DirectInputID,
            _resources.DirectTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.StaticDirectInputID,
            _resources.StaticDirectTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.BounceTextureID,
            _resources.BounceTexture!);
        commandBuffer.DispatchCompute(
            compute,
            kernel,
            LightingComputeBinder.DispatchGroups(_resources.BounceWidth),
            LightingComputeBinder.DispatchGroups(_resources.BounceHeight),
            1);
    }

    public void RecordComposite(CommandBuffer commandBuffer)
    {
        using var compositeMarker = CompositeMarker.Auto();
        commandBuffer.BeginSample("Kern.Lighting.Composite");
        ComputeShader compute = _resources.LightingCompute!;
        int kernel = _resources.CompositeLightingKernel;
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.DirectInputID,
            _resources.DirectTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.StaticDirectInputID,
            _resources.StaticDirectTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.BounceInputID,
            _resources.BounceTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.ResultID,
            _resources.LightmapTexture!);
        commandBuffer.DispatchCompute(
            compute,
            kernel,
            LightingComputeBinder.DispatchGroups(_resources.FieldWidth),
            LightingComputeBinder.DispatchGroups(_resources.FieldHeight),
            1);
        commandBuffer.EndSample("Kern.Lighting.Composite");
    }
}
