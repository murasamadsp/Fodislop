#nullable enable

using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Kern.World.Terrain;

namespace Kern.World.Lighting;

internal sealed class GeometryLightingSolver
{
    private readonly LightingResourceManager _resources;

    public GeometryLightingSolver(LightingResourceManager resources)
    {
        _resources = resources;
    }

    public void RecordMaterialField(
        CommandBuffer commandBuffer,
        TerrainRenderer terrainRenderer,
        LightingGeometryRegistry geometryRegistry,
        Vector4 worldRect)
    {
        commandBuffer.BeginSample("Kern.Lighting.MaterialField");
        terrainRenderer.RenderLightingMaterialFields(
            commandBuffer,
            _resources.MaterialField!,
            _resources.StaticEmissionField!,
            worldRect);
        if (geometryRegistry.HasContributors)
        {
            geometryRegistry.RenderLightingFields(
                commandBuffer,
                _resources.MaterialField!,
                _resources.StaticEmissionField!,
                worldRect,
                clearFields: false);
        }

        commandBuffer.EndSample("Kern.Lighting.MaterialField");
    }

    public void RecordAmbientOcclusionField(
        CommandBuffer commandBuffer,
        TerrainRenderer terrainRenderer,
        LightingGeometryRegistry geometryRegistry,
        Vector4 worldRect)
    {
        RenderTexture ambientOcclusionField = _resources.AmbientOcclusionField!;
        RenderTexture ambientOcclusionScratch = _resources.AmbientOcclusionScratch!;

        commandBuffer.BeginSample("Kern.Lighting.AmbientOcclusionField");
        terrainRenderer.RenderLightingMaterialFields(
            commandBuffer,
            ambientOcclusionField,
            ambientOcclusionScratch,
            worldRect);
        if (geometryRegistry.HasContributors)
        {
            geometryRegistry.RenderLightingFields(
                commandBuffer,
                ambientOcclusionField,
                ambientOcclusionScratch,
                worldRect,
                clearFields: false);
        }

        // The visible terrain samples this independent occupancy pyramid.
        // Keeping it separate prevents PerBlock lighting from collapsing a
        // rounded or alpha-cutout block to one solid square texel.
        commandBuffer.GenerateMips(ambientOcclusionField);
        commandBuffer.EndSample("Kern.Lighting.AmbientOcclusionField");
    }

    public void PrepareCaches(CommandBuffer commandBuffer, bool materialFieldRebuilt)
    {
        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture cellSolidMask = _resources.CellSolidMask!;
        int buildMaskKernel = _resources.BuildCellSolidMaskKernel;

        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.CellGridSizeID,
            _resources.CellGridWidth,
            _resources.CellGridHeight);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveCascadeKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveDynamicLightingKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.TraceDynamicPolarKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.ResolveTransmissionDebugKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);

        if (!materialFieldRebuilt && _resources.GeometryCachesValid)
        {
            return;
        }

        commandBuffer.BeginSample("Kern.Lighting.GeometryCaches");
        BindFieldTextures(commandBuffer, compute, buildMaskKernel);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildMaskKernel,
            LightingComputeBinder.CellSolidMaskOutputID,
            cellSolidMask);

        commandBuffer.DispatchCompute(
            compute,
            buildMaskKernel,
            LightingComputeBinder.DispatchGroups(_resources.CellGridWidth),
            LightingComputeBinder.DispatchGroups(_resources.CellGridHeight),
            1);

        commandBuffer.EndSample("Kern.Lighting.GeometryCaches");
        _resources.GeometryCachesValid = true;
    }

    private void BindFieldTextures(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel)
    {
        LightingComputeBinder.BindFieldTextures(
            commandBuffer,
            compute,
            kernel,
            _resources.MaterialField!,
            _resources.StaticEmissionField!,
            _resources.LightingCounters);
    }
}
