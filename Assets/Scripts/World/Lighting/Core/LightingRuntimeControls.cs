#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.World.Lighting;

internal sealed class LightingRuntimeControls
{
    private readonly IClientConfigManager _clientConfig;
    private readonly LightingQualityController _qualityController;
    private readonly LightingRuntimeState _runtimeState;
    private readonly DynamicLightManager _dynamicLightManager;
    private readonly Action _publishTerrainRequirements;

    public LightingRuntimeControls(
        IClientConfigManager clientConfig,
        LightingQualityController qualityController,
        LightingRuntimeState runtimeState,
        DynamicLightManager dynamicLightManager,
        Action publishTerrainRequirements)
    {
        _clientConfig = clientConfig;
        _qualityController = qualityController;
        _runtimeState = runtimeState;
        _dynamicLightManager = dynamicLightManager;
        _publishTerrainRequirements = publishTerrainRequirements;
    }

    public void ApplyClientConfig()
    {
        _qualityController.Apply(
            _clientConfig.Config.GraphicsPreset,
            _clientConfig.Config.GraphicsQualitySettings);
        _publishTerrainRequirements();
        LightingRuntimeInvalidation.ResetFieldAndRadiance(_runtimeState);
        _dynamicLightManager.IncrementGeneration();
        _dynamicLightManager.MarkDirty();
        Debug.Log(
            $"[LightingEngine] Applied client config (Preset={_clientConfig.Config.GraphicsPreset})");
    }

    public void SetDebugView(
        ref LightingEngine.DebugView current,
        LightingEngine.DebugView requested)
    {
        if (current == requested)
        {
            return;
        }

        current = requested;
        _runtimeState.HasRenderedLightState = false;
        _runtimeState.HasStaticRadianceState = false;
        _runtimeState.HasDynamicRadianceState = false;
        _runtimeState.CompositeDirty = true;
        Debug.Log($"[LightingEngine] SetDebugView: {requested}");
    }

    public void InvalidateRadiance() =>
        LightingRuntimeInvalidation.ResetRadiance(_runtimeState);

    public void ResetRuntimeLightingPreferences()
    {
        _qualityController.Apply(
            _clientConfig.Config.GraphicsPreset,
            _clientConfig.Config.GraphicsQualitySettings);
        _publishTerrainRequirements();
        LightingRuntimeInvalidation.ResetFieldAndRadiance(_runtimeState);
    }
}
