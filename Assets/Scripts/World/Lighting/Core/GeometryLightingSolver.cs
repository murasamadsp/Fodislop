#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
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

    public void PrepareCaches(CommandBuffer commandBuffer, bool materialFieldRebuilt)
    {
        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture cellSolidMask = _resources.CellSolidMask!;
        ComputeBuffer bounceTaps = _resources.BounceTaps!;
        ComputeBuffer bounceFilterWeights = _resources.BounceFilterWeights!;
        int buildMaskKernel = _resources.BuildCellSolidMaskKernel;
        int buildTapsKernel = _resources.BuildBounceTapsKernel;
        int buildFilterKernel = _resources.BuildBounceFilterKernel;

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
        commandBuffer.SetComputeTextureParam(
            compute,
            buildTapsKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildFilterKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeBufferParam(
            compute,
            buildTapsKernel,
            LightingComputeBinder.BounceTapsID,
            bounceTaps);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.SolveDiffuseBounceKernel,
            LightingComputeBinder.BounceTapsID,
            bounceTaps);
        commandBuffer.SetComputeBufferParam(
            compute,
            buildFilterKernel,
            LightingComputeBinder.BounceFilterWeightsID,
            bounceFilterWeights);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.CompositeLightingKernel,
            LightingComputeBinder.BounceFilterWeightsID,
            bounceFilterWeights);

        if (!materialFieldRebuilt && _resources.GeometryCachesValid)
        {
            return;
        }

        commandBuffer.BeginSample("Kern.Lighting.GeometryCaches");
        BindFieldTextures(commandBuffer, compute, buildMaskKernel);
        BindFieldTextures(commandBuffer, compute, buildTapsKernel);
        BindFieldTextures(commandBuffer, compute, buildFilterKernel);
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
        if (LightingConfigHolder.BounceEnabled)
        {
            commandBuffer.DispatchCompute(
                compute,
                buildTapsKernel,
                LightingComputeBinder.DispatchGroups(_resources.BounceWidth),
                LightingComputeBinder.DispatchGroups(_resources.BounceHeight),
                1);
            commandBuffer.DispatchCompute(
                compute,
                buildFilterKernel,
                LightingComputeBinder.DispatchGroups(_resources.FieldWidth),
                LightingComputeBinder.DispatchGroups(_resources.FieldHeight),
                1);
        }

        commandBuffer.EndSample("Kern.Lighting.GeometryCaches");
        _resources.GeometryCachesValid = true;
    }

    // Jump-flooded SDF сидов ближайшего твёрдого текселя. Бежит только при
    // перестройке поля (там же, где RecordMaterialField): occupancy меняется
    // лишь тогда. Чётное число проходов гарантирует финал всегда в SeedA,
    // которую композит читает.
    public void RecordDistanceField(CommandBuffer commandBuffer, IFrameTelemetry telemetry)
    {
        ComputeShader compute = _resources.LightingCompute ??
            throw new InvalidOperationException("Distance field cannot build before the lighting compute exists.");
        RenderTexture? seedA = _resources.DistanceSeedA;
        RenderTexture? seedB = _resources.DistanceSeedB;
        RenderTexture? materialField = _resources.MaterialField;
        if (seedA == null || seedB == null || materialField == null)
        {
            throw new InvalidOperationException("Distance field cannot build before its targets exist.");
        }

        int fieldWidth = _resources.FieldWidth;
        int fieldHeight = _resources.FieldHeight;
        int seedKernel = _resources.SeedDistanceFieldKernel;
        int stepKernel = _resources.JumpFloodStepKernel;

        commandBuffer.BeginSample("Kern.Lighting.DistanceField");
        LightingComputeBinder.BindFieldTextures(commandBuffer, compute, seedKernel, materialField, materialField);
        LightingComputeBinder.BindFieldTextures(commandBuffer, compute, stepKernel, materialField, materialField);
        commandBuffer.SetComputeTextureParam(compute, seedKernel, LightingComputeBinder.DistanceSeedID, seedA);
        commandBuffer.DispatchCompute(
            compute,
            seedKernel,
            LightingComputeBinder.DispatchGroups(fieldWidth),
            LightingComputeBinder.DispatchGroups(fieldHeight),
            1);
        telemetry.LightingSdfDispatchPixels += (long)fieldWidth * fieldHeight;

        int maxExtent = Math.Max(fieldWidth, fieldHeight);
        int step = 1;
        while (step * 2 < maxExtent)
        {
            step *= 2;
        }

        RenderTexture read = seedA;
        RenderTexture write = seedB;
        int passCount = 0;
        for (; step >= 1; step /= 2)
        {
            commandBuffer.SetComputeTextureParam(compute, stepKernel, LightingComputeBinder.DistanceSeedID, write);
            commandBuffer.SetComputeTextureParam(compute, stepKernel, LightingComputeBinder.DistanceSeedInputID, read);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.JumpStepID, step);
            commandBuffer.DispatchCompute(
                compute,
                stepKernel,
                LightingComputeBinder.DispatchGroups(fieldWidth),
                LightingComputeBinder.DispatchGroups(fieldHeight),
                1);
            telemetry.LightingSdfDispatchPixels += (long)fieldWidth * fieldHeight;
            passCount++;
            (read, write) = (write, read);
        }

        if (passCount % 2 == 1)
        {
            // Доводка шагом 1 корректность не ломает, а чётность возвращает
            // финал в SeedA детерминированно при любом размере поля.
            commandBuffer.SetComputeTextureParam(compute, stepKernel, LightingComputeBinder.DistanceSeedID, write);
            commandBuffer.SetComputeTextureParam(compute, stepKernel, LightingComputeBinder.DistanceSeedInputID, read);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.JumpStepID, 1);
            commandBuffer.DispatchCompute(
                compute,
                stepKernel,
                LightingComputeBinder.DispatchGroups(fieldWidth),
                LightingComputeBinder.DispatchGroups(fieldHeight),
                1);
            telemetry.LightingSdfDispatchPixels += (long)fieldWidth * fieldHeight;
        }

        commandBuffer.EndSample("Kern.Lighting.DistanceField");
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
