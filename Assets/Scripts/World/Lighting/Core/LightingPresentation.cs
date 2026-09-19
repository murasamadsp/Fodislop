#nullable enable

using System;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>
/// Publishes the lighting result and disabled fallback to world shaders.
/// </summary>
internal sealed class LightingPresentation
{
    public const string WorldLightingKeyword = "KERN_WORLD_LIGHTING";

    private static readonly int _worldLightTextureID = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int _worldLightRectID = Shader.PropertyToID("_WorldLightRect");
    private static readonly int _worldLightDebugViewID = Shader.PropertyToID("_WorldLightDebugView");
    private static readonly int _worldLightTextureSizeID = Shader.PropertyToID("_WorldLightTextureSize");
    private static readonly int _worldEmissionScaleID = Shader.PropertyToID("_WorldEmissionScale");
    private static readonly int _worldLightPerBlockID = Shader.PropertyToID("_WorldLightPerBlock");
    private static readonly int _worldAmbientOcclusionTextureID =
        Shader.PropertyToID("_WorldAmbientOcclusionTexture");
    private static readonly int _worldAmbientOcclusionYFlipID =
        Shader.PropertyToID("_WorldAmbientOcclusionYFlip");
    private static readonly int _worldAmbientOcclusionTexelsPerCellID =
        Shader.PropertyToID("_WorldAmbientOcclusionTexelsPerCell");

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
        Shader.SetGlobalTexture(_worldLightTextureID, Texture2D.whiteTexture);
        Shader.SetGlobalVector(_worldLightRectID, new Vector4(-1000f, -1000f, 2000f, 2000f));
        Shader.SetGlobalVector(_worldLightTextureSizeID, new Vector4(1, 1, 1, 1));
        Shader.SetGlobalInteger(_worldLightDebugViewID, 0);
        Shader.SetGlobalInteger(_worldLightPerBlockID, 0);
        Shader.SetGlobalTexture(_worldAmbientOcclusionTextureID, Texture2D.blackTexture);
        Shader.SetGlobalInteger(_worldAmbientOcclusionYFlipID, 0);
        Shader.SetGlobalFloat(_worldAmbientOcclusionTexelsPerCellID, 1f);
        Shader.SetGlobalFloat(_worldEmissionScaleID, LightingConfigHolder.EmissionScale);
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
        RenderTexture ambientOcclusion = _resources.AmbientOcclusionField ??
            throw new InvalidOperationException(
                "Enabled world lighting cannot publish before its ambient-occlusion field exists.");
        if (float.IsNaN(visibleRegion.x))
        {
            throw new InvalidOperationException(
                "Enabled world lighting cannot publish before its region exists.");
        }

        Shader.EnableKeyword(WorldLightingKeyword);
        _disabledStatePublished = false;
        Shader.SetGlobalTexture(_worldLightTextureID, lightmap);
        Shader.SetGlobalTexture(_worldAmbientOcclusionTextureID, ambientOcclusion);
        Shader.SetGlobalInteger(
            _worldAmbientOcclusionYFlipID,
            SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        Shader.SetGlobalFloat(
            _worldAmbientOcclusionTexelsPerCellID,
            ambientOcclusion.width / visibleRegion.z);
        Kern.World.Terrain.TerrainLook.ApplyShaderGlobals();

        Shader.SetGlobalInteger(_worldLightDebugViewID, (int)debugView);
        Shader.SetGlobalInteger(_worldLightPerBlockID, qualityMode == LightingQualityMode.PerBlock ? 1 : 0);
        Shader.SetGlobalFloat(_worldEmissionScaleID, LightingConfigHolder.EmissionScale);
        Shader.SetGlobalVector(
            _worldLightTextureSizeID,
            new Vector4(
                lightmap.width,
                lightmap.height,
                1f / lightmap.width,
                1f / lightmap.height));
        Shader.SetGlobalVector(
            _worldLightRectID,
            new Vector4(
                visibleRegion.x * cellSize,
                visibleRegion.y * cellSize,
                visibleRegion.z * cellSize,
                visibleRegion.w * cellSize));
    }
}
