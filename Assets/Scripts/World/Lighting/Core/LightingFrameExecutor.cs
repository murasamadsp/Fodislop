#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Lighting.Quality;
using Kern.World.Terrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Records the complete lighting transport order into one command buffer.
/// It owns per-transport cache state; resource lifetime remains external.
/// </summary>
internal sealed class LightingFrameExecutor
{
    private readonly LightingResourceManager _resources;
    private readonly GeometryLightingSolver _geometrySolver;
    private readonly StaticLightingSolver _staticSolver;
    private readonly DynamicLightingSolver _dynamicSolver;
    private readonly IndirectLightingSolver _indirectSolver;
    private readonly DynamicLightManager _dynamicLightManager;
    private readonly IFrameTelemetry _telemetry;
    private readonly LightingGeometryRegistry _geometryRegistry;
    private readonly List<string> _executedStages = new();

    public LightingFrameExecutor(
        LightingResourceManager resources,
        GeometryLightingSolver geometrySolver,
        StaticLightingSolver staticSolver,
        DynamicLightingSolver dynamicSolver,
        IndirectLightingSolver indirectSolver,
        DynamicLightManager dynamicLightManager,
        LightingGeometryRegistry geometryRegistry,
        IFrameTelemetry telemetry)
    {
        _resources = resources;
        _geometrySolver = geometrySolver;
        _staticSolver = staticSolver;
        _dynamicSolver = dynamicSolver;
        _indirectSolver = indirectSolver;
        _dynamicLightManager = dynamicLightManager;
        _geometryRegistry = geometryRegistry;
        _telemetry = telemetry;
    }

    public void Release()
    {
        _dynamicSolver.Release();
        _dynamicLightManager.ResetUploadState();
    }

    public void EnsureDynamicLightCapacity(int capacity)
    {
        _dynamicLightManager.EnsureCapacity(capacity);
    }

    public int UploadDynamicLights(
        CommandBuffer commandBuffer,
        Vector4 worldRect,
        float cellSize,
        out bool uploadedLightsChanged)
    {
        return _dynamicLightManager.UploadDynamicLights(
            commandBuffer,
            _resources.DynamicLightBuffer,
            worldRect,
            cellSize,
            out uploadedLightsChanged);
    }

    public void ClearDynamicDirect(CommandBuffer commandBuffer)
    {
        commandBuffer.SetRenderTarget(_resources.DirectTexture!);
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);
    }

    public void ConfigureSharedComputeParameters(
        CommandBuffer commandBuffer,
        Vector4 worldRect,
        float cellSize,
        RenderTexture emissionField,
        LightingQualityMode quality,
        LightingEngine.DebugView debugView)
    {
        LightingComputeBinder.BindSharedParameters(
            commandBuffer,
            _resources.LightingCompute!,
            _resources.FieldWidth,
            _resources.FieldHeight,
            _resources.BounceWidth,
            _resources.BounceHeight,
            worldRect,
            cellSize,
            quality,
            debugView,
            _resources.MaterialField!,
            emissionField,
            _resources.SolveCascadeKernel,
            _resources.ResolveDirectKernel,
            _resources.SolveDiffuseBounceKernel,
            _resources.CompositeLightingKernel);
    }

    public LightingFrameResult Record(
        CommandBuffer commandBuffer,
        LightingFrameRequest request,
        TerrainRenderer terrainRenderer,
        RenderTexture emissionField,
        RenderTexture staticDirectTexture)
    {
        _executedStages.Clear();
        LightingInvalidationFlags invalidations = BuildInvalidations(request);

        if (request.RebuildFields)
        {
            _geometrySolver.RecordMaterialField(
                commandBuffer,
                terrainRenderer,
                _geometryRegistry,
                request.WorldRect);
            _executedStages.Add("MaterialField");
            _geometrySolver.PrepareCaches(commandBuffer, materialFieldRebuilt: true);
            _executedStages.Add("GeometryCache");
        }

        bool staticRadianceChanged = request.StaticRadianceChanged;
        bool dynamicRadianceNeeded = request.DynamicRadianceChanged &&
            request.DynamicLightCount > 0;
        if (request.ClearDynamicRadiance)
        {
            ClearDynamicDirect(commandBuffer);
            _dynamicSolver.InvalidateTiles();
        }

        if (staticRadianceChanged &&
            LightingConfigHolder.EnabledFeatures.HasFlag(LightingFeatureFlags.StaticRC))
        {
            _staticSolver.RecordTrace(
                commandBuffer,
                emissionField,
                request.ReuseStaticAtlas,
                request.RegionDelta,
                request.DirtyRegions,
                request.AllowStaticDependencyMask,
                request.WorldRect);
            _executedStages.Add("CascadeTrace");
            _staticSolver.RecordResolve(
                commandBuffer,
                request.DebugView,
                emissionField,
                staticDirectTexture);
            _executedStages.Add("CascadeMerge");
        }

        if (dynamicRadianceNeeded &&
            LightingConfigHolder.EnabledFeatures.HasFlag(LightingFeatureFlags.DynamicLights))
        {
            _dynamicSolver.Record(
                commandBuffer,
                request.DynamicLightCount,
                request.WorldRect,
                request.CellSize,
                staticRadianceChanged || request.RebuildFields,
                request.DebugView,
                _telemetry);
            _executedStages.Add("DynamicLighting");
        }

        bool bounceRequired = request.BounceDirty ||
            request.DynamicLightsChanged ||
            request.DynamicRadianceChanged ||
            staticRadianceChanged;
        if (request.Quality != LightingQualityMode.PerBlock &&
            LightingConfigHolder.BounceEnabled &&
            LightingConfigHolder.BounceStrength > 0f &&
            LightingConfigHolder.EnabledFeatures.HasFlag(LightingFeatureFlags.DiffuseBounce) &&
            bounceRequired)
        {
            _indirectSolver.RecordBounce(commandBuffer);
            _executedStages.Add("DiffuseBounce");
        }

        if (request.DynamicLightsChanged ||
            request.DynamicRadianceChanged ||
            staticRadianceChanged ||
            request.CompositeDirty)
        {
            _indirectSolver.RecordComposite(commandBuffer);
            _executedStages.Add("Composite");
        }

        return new LightingFrameResult(
            invalidations,
            staticRadianceChanged,
            dynamicRadianceNeeded,
            request.ClearDynamicRadiance,
            _executedStages.ToArray());
    }

    private static LightingInvalidationFlags BuildInvalidations(
        LightingFrameRequest request)
    {
        LightingInvalidationFlags invalidations = LightingInvalidationFlags.None;
        if (request.RebuildFields)
        {
            invalidations |=
                LightingInvalidationFlags.GeometryChanged |
                LightingInvalidationFlags.RegionChanged |
                LightingInvalidationFlags.FieldDirty;
        }

        if (request.DynamicLightsChanged)
        {
            invalidations |= LightingInvalidationFlags.DynamicLightsChanged;
        }

        if (request.StaticRadianceChanged)
        {
            invalidations |= LightingInvalidationFlags.StaticRadianceChanged;
        }

        if (request.DynamicRadianceChanged)
        {
            invalidations |= LightingInvalidationFlags.DynamicRadianceChanged;
        }

        return invalidations;
    }
}
