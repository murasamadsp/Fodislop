#nullable enable

using Fodinae.World.Lighting.Quality;
using Fodinae.Rendering;
using UnityEngine;

namespace Fodinae.World.Lighting;

/// <summary>
/// Owns GPU resource sizing and release transitions for the lighting runtime.
/// </summary>
internal sealed class LightingGpuLifecycle
{
    private readonly LightingResourceManager _resources;
    private readonly LightingFrameExecutor _frameExecutor;

    public LightingGpuLifecycle(
        LightingResourceManager resources,
        LightingFrameExecutor frameExecutor)
    {
        _resources = resources;
        _frameExecutor = frameExecutor;
    }

    public bool EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        in GraphicsQualitySettings qualitySettings,
        LightingQualityMode qualityMode,
        out bool textureDimensionLimited,
        out bool cascadeBudgetLimited,
        out int effectivePixelsPerCell)
    {
        int oldFieldWidth = _resources.FieldWidth;
        int oldFieldHeight = _resources.FieldHeight;

        _resources.EnsureResources(
            gridWidth,
            gridHeight,
            camera,
            in qualitySettings,
            qualityMode,
            out textureDimensionLimited,
            out cascadeBudgetLimited,
            out effectivePixelsPerCell);

        _frameExecutor.EnsureDynamicLightCapacity(
            Mathf.Max(1, qualitySettings.LightingMaximumLightCount));

        return oldFieldWidth != _resources.FieldWidth ||
            oldFieldHeight != _resources.FieldHeight;
    }

    public void EnsurePipeline()
    {
        _resources.EnsureGpuPipelineInitialized();
    }

    public void ReleasePipeline()
    {
        _resources.ReleaseGpuPipeline();
        _frameExecutor.Release();
    }

    public void ReleaseResources()
    {
        _resources.ReleaseResources();
        _frameExecutor.Release();
    }
}
