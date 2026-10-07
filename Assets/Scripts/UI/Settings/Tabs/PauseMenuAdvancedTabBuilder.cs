#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Game;
using Kern.Rendering;
using Kern.World.Lighting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class PauseMenuAdvancedTabBuilder
{
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly LightingEngine _lightingEngine;
    private readonly IClientConfigManager _clientConfig;
    private readonly ILocalPlayerState _localPlayer;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
    private readonly Action _addLightingDebugControls;
#endif

    public PauseMenuAdvancedTabBuilder(
        GraphicsSettingsController graphicsSettings,
        LightingEngine lightingEngine,
        IClientConfigManager clientConfig,
        ILocalPlayerState localPlayer,
        ICollection<Action> refreshers,
        ILocalizationService loc
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        , Action addLightingDebugControls
#endif
    )
    {
        _graphicsSettings = graphicsSettings;
        _lightingEngine = lightingEngine;
        _clientConfig = clientConfig;
        _localPlayer = localPlayer;
        _refreshers = refreshers;
        _loc = loc;
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        _addLightingDebugControls = addLightingDebugControls;
#endif
    }

    public VisualElement Build(ScrollView advancedScroll)
    {
        Foldout advancedGraphicsSection = advancedScroll.Q<Foldout>("AdvancedLightingSection") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedLightingSection is missing from PauseMenu.uxml.");
        VisualElement ambientGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupAmbient") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupAmbient is missing from PauseMenu.uxml.");
        VisualElement dynamicGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupDynamic") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupDynamic is missing from PauseMenu.uxml.");
        VisualElement extinctionGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupExtinction") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupExtinction is missing from PauseMenu.uxml.");
        VisualElement boundsGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupBounds") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupBounds is missing from PauseMenu.uxml.");
        VisualElement worldMaterialsSection = advancedScroll.Q<VisualElement>("WorldMaterialsSection") ??
            throw new InvalidOperationException("[PauseMenu] WorldMaterialsSection is missing from PauseMenu.uxml.");

        // Освещение — константы, не настраивается

        void SaveShaderSetting(Action<ClientConfig> update)
        {
            _graphicsSettings.UpdateWorldMaterialSettings(update);
        }

        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.ShimmerSpeedScale),
            _loc,
            () => _clientConfig.Config.Terrain.ShimmerSpeedScale,
            value => SaveShaderSetting(
                config => config.Terrain.ShimmerSpeedScale = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.shimmer_color"),
            () => _clientConfig.Config.Terrain.ShimmerColor,
            value => SaveShaderSetting(config => config.Terrain.ShimmerColor = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.BlinkingSpeedScale),
            _loc,
            () => _clientConfig.Config.Terrain.BlinkingSpeedScale,
            value => SaveShaderSetting(config => config.Terrain.BlinkingSpeedScale = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.TransitGlowStrength),
            _loc,
            () => _clientConfig.Config.Terrain.TransitGlowStrength,
            value => SaveShaderSetting(config => config.Terrain.TransitGlowStrength = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.surface_glow_color"),
            () => _clientConfig.Config.Terrain.TransitGlowColor,
            value => SaveShaderSetting(config => config.Terrain.TransitGlowColor = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.PerspectiveGlowStrength),
            _loc,
            () => _clientConfig.Config.Terrain.PerspectiveGlowStrength,
            value => SaveShaderSetting(
                config => config.Terrain.PerspectiveGlowStrength = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.far_surface_color"),
            () => _clientConfig.Config.Terrain.PerspectiveGlowColor,
            value => SaveShaderSetting(
                config => config.Terrain.PerspectiveGlowColor = value),
            0f,
            8f,
            _refreshers));

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        _addLightingDebugControls();
#endif

        return advancedScroll;
    }

    private Robot? ResolveLocalRobot()
    {
        return _localPlayer.Current?.GetComponent<Robot>();
    }
}
