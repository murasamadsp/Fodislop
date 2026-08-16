#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Player.Logic;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;

namespace Fodinae.Rendering;

public sealed class GraphicsSettingsController
{
    private readonly IClientConfigManager _clientConfig;
    private readonly TerrariaLightingEngine _lightingEngine;
    private readonly PostProcessController _postProcessController;
    private readonly TerrainRenderer _terrainRenderer;
    private readonly SurfaceRenderer _surfaceRenderer;

    public GraphicsSettingsController(
        IClientConfigManager clientConfig,
        TerrariaLightingEngine lightingEngine,
        PostProcessController postProcessController,
        TerrainRenderer terrainRenderer,
        SurfaceRenderer surfaceRenderer)
    {
        _clientConfig = clientConfig;
        _lightingEngine = lightingEngine;
        _postProcessController = postProcessController;
        _terrainRenderer = terrainRenderer;
        _surfaceRenderer = surfaceRenderer;
    }

    public GraphicsPreset SelectedPreset => _clientConfig.SelectedGraphicsPreset;

    public void MarkCustom()
    {
        _clientConfig.MarkGraphicsAsCustom();
    }

    public void SelectStandardPreset(GraphicsPreset preset)
    {
        _clientConfig.SelectGraphicsPreset(preset);
        ApplyAll();
        _clientConfig.Save();
    }

    public void ApplyCustomWorldMaterialSettings()
    {
        MarkCustom();
        _terrainRenderer.ApplyClientConfig();
        _surfaceRenderer.ApplyClientConfig();
        _clientConfig.Save();
    }

    private void ApplyAll()
    {
        _lightingEngine.ApplyClientConfig();
        _postProcessController.ApplyClientConfig();
        _terrainRenderer.ApplyClientConfig();
        _surfaceRenderer.ApplyClientConfig();
        PlayerMovementController.LocalPlayer?
            .GetComponent<Robot>()?
            .ResetDynamicLightPreferences();
    }
}
