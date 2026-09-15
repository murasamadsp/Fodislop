#nullable enable

using System;
using Fodinae.World.Lighting.Quality;
using UnityEngine;

namespace Fodinae.World.Lighting;

/// <summary>
/// Publishes the lighting result and disabled fallback to world shaders.
/// </summary>
internal sealed class LightingPresentation
{
    public const string WorldLightingKeyword = "FODINAE_WORLD_LIGHTING";

    private static readonly int WorldLightTextureID = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int WorldLightRectID = Shader.PropertyToID("_WorldLightRect");
    private static readonly int WorldLightDebugViewID = Shader.PropertyToID("_WorldLightDebugView");
    private static readonly int WorldLightTextureSizeID = Shader.PropertyToID("_WorldLightTextureSize");
    private static readonly int WorldEmissionScaleID = Shader.PropertyToID("_WorldEmissionScale");
    private static readonly int WorldLightPerBlockID = Shader.PropertyToID("_WorldLightPerBlock");
    private static readonly int WorldOccupancyTextureID = Shader.PropertyToID("_WorldOccupancyTexture");
    private static readonly int WorldOccupancyYFlipID = Shader.PropertyToID("_WorldOccupancyYFlip");

    private readonly LightingResourceManager _resources;
    private bool _disabledStatePublished;

    public LightingPresentation(LightingResourceManager resources)
    {
        _resources = resources;
    }

    public bool IsDisabledStatePublished => _disabledStatePublished;

    public void MarkEnabled()
    {
        _disabledStatePublished = false;
    }

    public void PublishDisabled()
    {
        if (_disabledStatePublished)
        {
            return;
        }

        Shader.DisableKeyword(WorldLightingKeyword);
        Shader.SetGlobalTexture(WorldLightTextureID, Texture2D.whiteTexture);
        Shader.SetGlobalVector(WorldLightRectID, new Vector4(-1000f, -1000f, 2000f, 2000f));
        Shader.SetGlobalVector(WorldLightTextureSizeID, new Vector4(1, 1, 1, 1));
        Shader.SetGlobalInteger(WorldLightDebugViewID, 0);
        Shader.SetGlobalInteger(WorldLightPerBlockID, 0);
        Shader.SetGlobalFloat(WorldEmissionScaleID, LightingConfigHolder.EmissionScale);
        _disabledStatePublished = true;
    }

    public void Publish(
        LightingEngine.DebugView debugView,
        LightingQualityMode qualityMode,
        Vector4 visibleRegion,
        float cellSize)
    {
        RenderTexture lightmap = _resources.LightmapTexture ??
            throw new InvalidOperationException(
                "Enabled world lighting cannot publish before its lightmap exists.");
        if (float.IsNaN(visibleRegion.x))
        {
            throw new InvalidOperationException(
                "Enabled world lighting cannot publish before its region exists.");
        }

        Shader.EnableKeyword(WorldLightingKeyword);
        _disabledStatePublished = false;
        Shader.SetGlobalTexture(WorldLightTextureID, lightmap);
        if (_resources.MaterialField != null)
        {
            Shader.SetGlobalTexture(WorldOccupancyTextureID, _resources.MaterialField);
            Shader.SetGlobalInteger(
                WorldOccupancyYFlipID,
                SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        }

        Shader.SetGlobalInteger(WorldLightDebugViewID, (int)debugView);
        Shader.SetGlobalInteger(WorldLightPerBlockID, qualityMode == LightingQualityMode.PerBlock ? 1 : 0);
        Shader.SetGlobalFloat(WorldEmissionScaleID, LightingConfigHolder.EmissionScale);
        Shader.SetGlobalVector(
            WorldLightTextureSizeID,
            new Vector4(
                lightmap.width,
                lightmap.height,
                1f / lightmap.width,
                1f / lightmap.height));
        Shader.SetGlobalVector(
            WorldLightRectID,
            new Vector4(
                visibleRegion.x * cellSize,
                visibleRegion.y * cellSize,
                visibleRegion.z * cellSize,
                visibleRegion.w * cellSize));
    }
}
