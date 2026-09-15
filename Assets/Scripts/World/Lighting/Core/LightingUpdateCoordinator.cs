#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Interfaces.Diagnostics;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Diagnostics;
using Fodinae.World.Lighting.Quality;
using Fodinae.World.Terrain;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting;

/// <summary>
/// Owns per-frame invalidation, command recording, execution and journal state.
/// </summary>
internal sealed class LightingUpdateCoordinator
{
    private static readonly ProfilerMarker UpdateMarker =
        new("Fodinae.Lighting.UpdateLighting.CPU");
    private static readonly AllocationLedger.Entry AllocationEntry =
        AllocationLedger.Register("Свет — обновление");
    private static readonly ProfilerMarker BuildCommandsMarker =
        new("Fodinae.Lighting.BuildCommands.CPU");
    private static readonly ProfilerMarker ExecuteCommandsMarker =
        new("Fodinae.Lighting.ExecuteCommands.CPU");

    private readonly LightingResourceManager _resources;
    private readonly LightingRuntimeState _state;
    private readonly LightingGpuLifecycle _gpuLifecycle;
    private readonly LightingFrameExecutor _frameExecutor;
    private readonly LightingPresentation _presentation;
    private readonly LightingGeometryRegistry _geometryRegistry;
    private readonly DynamicLightManager _dynamicLightManager;
    private readonly IFrameTelemetry _telemetry;
    private readonly LightingInvalidationJournal _journal;
    private readonly List<string> _executedStages = new();

    public LightingUpdateCoordinator(
        LightingResourceManager resources,
        LightingRuntimeState state,
        LightingGpuLifecycle gpuLifecycle,
        LightingFrameExecutor frameExecutor,
        LightingPresentation presentation,
        LightingGeometryRegistry geometryRegistry,
        DynamicLightManager dynamicLightManager,
        IFrameTelemetry telemetry,
        LightingInvalidationJournal journal)
    {
        _resources = resources;
        _state = state;
        _gpuLifecycle = gpuLifecycle;
        _frameExecutor = frameExecutor;
        _presentation = presentation;
        _geometryRegistry = geometryRegistry;
        _dynamicLightManager = dynamicLightManager;
        _telemetry = telemetry;
        _journal = journal;
    }

    public void Update(
        int visibleMinX,
        int visibleMinY,
        int visibleWidth,
        int visibleHeight,
        Camera camera,
        IWorldDataStorage? storage,
        MapManager? mapManager,
        TerrainRenderer terrainRenderer,
        GraphicsQualitySettings qualitySettings,
        LightingQualityMode qualityMode,
        LightingEngine.DebugView debugView,
        bool bypassLightingCompute)
    {
        using var updateMarker = UpdateMarker.Auto();
        using var allocationScope = AllocationLedger.Measure(AllocationEntry);
        if (terrainRenderer == null)
        {
            throw new ArgumentNullException(nameof(terrainRenderer));
        }
        if (visibleWidth <= 0 || visibleHeight <= 0 || camera == null ||
            storage == null || mapManager == null)
        {
            return;
        }

        if (!camera.orthographic)
        {
            return;
        }


        if (bypassLightingCompute || qualityMode == LightingQualityMode.Off)
        {
            bool enteringBypass = !_state.WasLightingBypassed;
            _state.WasLightingBypassed = true;
            _presentation.PublishDisabled();
            if (enteringBypass)
            {
                _gpuLifecycle.ReleaseResources();
            }

            return;
        }

        if (_state.WasLightingBypassed || _presentation.IsDisabledStatePublished)
        {
            _state.WasLightingBypassed = false;
            _presentation.MarkEnabled();
            Shader.EnableKeyword(LightingPresentation.WorldLightingKeyword);
            InvalidateAll();
        }

        _gpuLifecycle.EnsurePipeline();
        Vector4 previousLightingRegion = _state.LastVisibleRegion;
        Vector4 lightingRegion = LightingRegionCalculator.GetStableLightingRegion(
            visibleMinX,
            visibleMinY,
            visibleWidth,
            visibleHeight,
            _state.LastVisibleRegion);
        bool regionChanged = lightingRegion != _state.LastVisibleRegion;
        if (regionChanged)
        {
            _telemetry.LightingRegionChangeCount++;
        }
        _state.LastVisibleRegion = lightingRegion;

        _state.ActivatePendingRegionIfVisible(
            new RectInt(
                visibleMinX,
                visibleMinY,
                visibleWidth,
                visibleHeight));

        int gridWidth = Mathf.RoundToInt(lightingRegion.z);
        int gridHeight = Mathf.RoundToInt(lightingRegion.w);
        bool resourcesResized = EnsureResources(
            gridWidth,
            gridHeight,
            camera,
            qualitySettings,
            qualityMode);
        if (resourcesResized)
        {
            _state.ClearPendingRegionInvalidation();
            _state.FieldDirty = true;
            _state.HasRenderedLightState = false;
            _state.HasStaticRadianceState = false;
            _state.HasDynamicRadianceState = false;
        }

        if (regionChanged)
        {
            // A region rebuild redraws the whole material field, so any
            // queued chunk invalidation inside the old window is already
            // represented by this solve.
            _state.ClearPendingRegionInvalidation();
        }

        bool dynamicLightsDirty = !_state.HasRenderedLightState || _dynamicLightManager.IsDirty;
        ulong contributorGeometryRevision = _geometryRegistry.GeometryRevision;
        bool contributorGeometryChanged =
            _state.LastContributorGeometryRevision != contributorGeometryRevision;
        bool geometryChanged =
            _state.LastTerrainContentRevision != terrainRenderer.TerrainContentRevision ||
            contributorGeometryChanged;
        if (geometryChanged)
        {
            _telemetry.LightingGeometryChangeCount++;
        }
        if (!_state.FieldDirty && !regionChanged && !dynamicLightsDirty && !geometryChanged &&
            !_state.CompositeDirty && !_state.BounceDirty)
        {
            return;
        }

        bool geometryUpdateRequired = _state.FieldDirty || regionChanged || geometryChanged;
        if (geometryUpdateRequired || _state.BounceDirty || _state.CompositeDirty)
        {
            _state.DynamicSolveInProgress = false;
        }

        const float cellSize = ProjectRuntimeContracts.World.CellSize;
        Vector4 worldRect = new(
            lightingRegion.x * cellSize,
            lightingRegion.y * cellSize,
            lightingRegion.z * cellSize,
            lightingRegion.w * cellSize);
        CommandBuffer commandBuffer = _resources.LightingCommandBuffer ??
            throw new InvalidOperationException(
                "Radiance Cascades command buffer is not initialized.");
        commandBuffer.Clear();
        int dynamicLightCount;
        bool dynamicLightsChanged;
        bool rebuildFields = _state.FieldDirty || regionChanged || geometryChanged;
        Vector2Int regionDelta = regionChanged && !float.IsNaN(previousLightingRegion.x)
            ? new Vector2Int(
                Mathf.RoundToInt(lightingRegion.x - previousLightingRegion.x),
                Mathf.RoundToInt(lightingRegion.y - previousLightingRegion.y))
            : Vector2Int.zero;
        bool allowStaticDependencyMask = !resourcesResized &&
            !regionChanged &&
            !contributorGeometryChanged &&
            _state.ActiveRegionInvalidations.Count > 0;
        bool reuseStaticAtlas = false;
        if (rebuildFields)
        {
            _telemetry.LightingFieldRebuildCount++;
        }
        try
        {
            long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
            using (BuildCommandsMarker.Auto())
            {
                commandBuffer.BeginSample("Fodinae.RadianceCascades");
                if (_state.DynamicSolveInProgress)
                {
                    dynamicLightCount = _dynamicLightManager.UploadedCount;
                    dynamicLightsChanged = false;
                }
                else
                {
                    dynamicLightCount = _frameExecutor.UploadDynamicLights(
                        commandBuffer,
                        worldRect,
                        cellSize,
                        out dynamicLightsChanged);
                }

                if (!rebuildFields && !dynamicLightsChanged &&
                    !_state.CompositeDirty && !_state.BounceDirty)
                {
                    commandBuffer.EndSample("Fodinae.RadianceCascades");
                    RememberDynamicLightState();
                    return;
                }

                _frameExecutor.ConfigureSharedComputeParameters(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    _resources.StaticEmissionField!,
                    qualityMode,
                    debugView);
                bool staticRadianceChanged = rebuildFields || !_state.HasStaticRadianceState;
                bool dynamicRadianceChanged = dynamicLightCount > 0 &&
                    (dynamicLightsChanged || staticRadianceChanged || !_state.HasDynamicRadianceState);
                LightingInvalidationFlags invalidations = RecordLightingFrame(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    dynamicLightCount,
                    rebuildFields,
                    dynamicLightsChanged,
                    staticRadianceChanged,
                    reuseStaticAtlas,
                    regionDelta,
                    _state.ActiveRegionInvalidations,
                    allowStaticDependencyMask,
                    dynamicRadianceChanged,
                    qualityMode,
                    debugView,
                    terrainRenderer);
                _state.HasStaticRadianceState |= staticRadianceChanged;
                _state.HasDynamicRadianceState = dynamicLightCount > 0 &&
                    (dynamicRadianceChanged || _state.HasDynamicRadianceState);
                if (staticRadianceChanged)
                {
                    _telemetry.LightingStaticSolveCount++;
                    _telemetry.LightingStaticSolveFrameCount++;
                }

                if (dynamicRadianceChanged)
                {
                    _telemetry.LightingDynamicSolveCount++;
                }

                commandBuffer.EndSample("Fodinae.RadianceCascades");
                _telemetry.LightingBuildCommandsTimeMs =
                    (float)((System.Diagnostics.Stopwatch.GetTimestamp() - buildStart) *
                        1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _telemetry.LightingCommandBufferBytes = commandBuffer.sizeInBytes;
                _telemetry.ActiveDynamicLights = dynamicLightCount;
                long executeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                using (ExecuteCommandsMarker.Auto())
                {
                    Graphics.ExecuteCommandBuffer(commandBuffer);
                }

                _telemetry.LightingExecuteCommandsTimeMs =
                    (float)((System.Diagnostics.Stopwatch.GetTimestamp() - executeStart) *
                        1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _presentation.Publish(
                    debugView,
                    qualityMode,
                    _state.LastVisibleRegion,
                    cellSize);
                string reason = rebuildFields
                    ? "Geometry or region updated"
                    : dynamicLightsChanged
                        ? "Dynamic lights updated"
                        : "Lightmap refreshed";
                _journal.Record(
                    _state.SolveCount,
                    invalidations,
                    reason,
                    _executedStages.ToArray(),
                    Array.Empty<string>());
                _state.SolveCount++;
                _state.FieldDirty = false;
                _state.CompositeDirty = false;
                _state.BounceDirty = false;
                _state.LastTerrainContentRevision = terrainRenderer.TerrainContentRevision;
                _state.LastContributorGeometryRevision = contributorGeometryRevision;
                _state.CompleteActiveRegionInvalidation();
                RememberDynamicLightState();
            }
        }
        finally
        {
            commandBuffer.Clear();
        }
    }

    private static bool IsReusableRegionDelta(Vector2Int delta)
    {
        return delta != Vector2Int.zero &&
            (delta.x % 8) == 0 &&
            (delta.y % 8) == 0;
    }


    private bool EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        GraphicsQualitySettings qualitySettings,
        LightingQualityMode qualityMode)
    {
        _state.RequestedPixelsPerCell = qualityMode == LightingQualityMode.PerBlock
            ? 1
            : Mathf.Clamp(qualitySettings.LightingMinimumPixelsPerCell, 1, 16);
        bool textureDimensionLimited;
        bool cascadeBudgetLimited;
        bool resized = _gpuLifecycle.EnsureResources(
            gridWidth,
            gridHeight,
            camera,
            in qualitySettings,
            qualityMode,
            out textureDimensionLimited,
            out cascadeBudgetLimited,
            out int effectivePixelsPerCell);
        _state.TextureDimensionLimited = textureDimensionLimited;
        _state.CascadeBudgetLimited = cascadeBudgetLimited;
        _state.EffectivePixelsPerCell = effectivePixelsPerCell;
        _telemetry.LightingEstimatedCascadeRayWorkUnits =
            _resources.EstimatedCascadeRayWorkUnits;
        _telemetry.LightingEstimatedCascadeDispatchThreads =
            _resources.EstimatedCascadeDispatchThreads;
        return resized;
    }

    private LightingInvalidationFlags RecordLightingFrame(
        CommandBuffer commandBuffer,
        Vector4 worldRect,
        float cellSize,
        int dynamicLightCount,
        bool rebuildFields,
        bool dynamicLightsChanged,
        bool staticRadianceChanged,
        bool reuseStaticAtlas,
        Vector2Int regionDelta,
        IReadOnlyList<RectInt> dirtyRegions,
        bool allowStaticDependencyMask,
        bool dynamicRadianceChanged,
        LightingQualityMode qualityMode,
        LightingEngine.DebugView debugView,
        TerrainRenderer terrainRenderer)
    {
        LightingFrameResult result = _frameExecutor.Record(
            commandBuffer,
            new LightingFrameRequest(
                worldRect,
                cellSize,
                dynamicLightCount,
                rebuildFields,
                dynamicLightsChanged,
                staticRadianceChanged,
                reuseStaticAtlas,
                regionDelta,
                dirtyRegions,
                allowStaticDependencyMask,
                dynamicRadianceChanged,
                dynamicLightCount == 0 &&
                    (dynamicLightsChanged || staticRadianceChanged || _state.HasDynamicRadianceState),
                _state.BounceDirty,
                _state.CompositeDirty,
                qualityMode,
                debugView),
            terrainRenderer,
            _resources.StaticEmissionField!,
            _resources.StaticDirectTexture!);
        _executedStages.Clear();
        _executedStages.AddRange(result.ExecutedStages);
        return result.Invalidations |
            (_state.BounceDirty ? LightingInvalidationFlags.BounceDirty : LightingInvalidationFlags.None) |
            (_state.CompositeDirty ? LightingInvalidationFlags.CompositeDirty : LightingInvalidationFlags.None);
    }

    private void RememberDynamicLightState()
    {
        _state.HasRenderedLightState = true;
        _dynamicLightManager.ClearDirty();
    }

    private void InvalidateAll()
    {
        _state.FieldDirty = true;
        _state.CompositeDirty = true;
        _state.BounceDirty = true;
        _state.HasRenderedLightState = false;
        _state.HasStaticRadianceState = false;
        _state.HasDynamicRadianceState = false;
        _state.LastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
    }

}
