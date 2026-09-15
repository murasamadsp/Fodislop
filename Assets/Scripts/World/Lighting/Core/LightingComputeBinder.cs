#nullable enable

namespace Fodinae.World.Lighting;

using System;
using Fodinae.Core;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

internal static class LightingComputeBinder
{
    // All lighting kernels use [numthreads(8, 8, 1)]. Keep the GPU execution
    // detail here so a cell-space policy cannot be mistaken for a dispatch
    // threshold.
    public const int ThreadGroupSize = 8;

    public static int DispatchGroups(int extent)
    {
        return (extent + ThreadGroupSize - 1) / ThreadGroupSize;
    }

    public static readonly int MaterialFieldID = Shader.PropertyToID("_MaterialField");
    public static readonly int EmissionFieldID = Shader.PropertyToID("_EmissionField");
    public static readonly int RadianceAtlasID = Shader.PropertyToID("_RadianceAtlas");
    public static readonly int RadianceAtlasInputID = Shader.PropertyToID("_RadianceAtlasInput");
    public static readonly int RadianceAtlasOutputID = Shader.PropertyToID("_RadianceAtlasOutput");
    public static readonly int DirectTextureID = Shader.PropertyToID("_DirectTexture");
    public static readonly int DirectInputID = Shader.PropertyToID("_DirectInput");
    public static readonly int StaticDirectInputID = Shader.PropertyToID("_StaticDirectInput");
    public static readonly int BounceTextureID = Shader.PropertyToID("_BounceTexture");
    public static readonly int BounceInputID = Shader.PropertyToID("_BounceInput");
    public static readonly int ResultID = Shader.PropertyToID("_Result");
    public static readonly int FieldSizeID = Shader.PropertyToID("_FieldSize");
    public static readonly int BounceSizeID = Shader.PropertyToID("_BounceSize");
    public static readonly int WorldRectID = Shader.PropertyToID("_WorldRect");
    public static readonly int AmbientColorID = Shader.PropertyToID("_AmbientColor");
    public static readonly int EmptyExtinctionRGBID = Shader.PropertyToID("_EmptyExtinctionRGB");
    public static readonly int SolidExtinctionRGBID = Shader.PropertyToID("_SolidExtinctionRGB");
    public static readonly int BounceStrengthID = Shader.PropertyToID("_BounceStrength");
    public static readonly int TerrainAmbientOcclusionMipID =
        Shader.PropertyToID("_TerrainAmbientOcclusionMip");
    public static readonly int TerrainAmbientOcclusionStrengthID =
        Shader.PropertyToID("_TerrainAmbientOcclusionStrength");
    public static readonly int EmissionScaleID = Shader.PropertyToID("_EmissionScale");
    public static readonly int MaximumLightMultiplierID = Shader.PropertyToID("_MaximumLightMultiplier");
    public static readonly int CellSizeID = Shader.PropertyToID("_CellSize");
    public static readonly int TransmittanceDebugDistanceCellsID = Shader.PropertyToID("_TransmittanceDebugDistanceCells");
    public static readonly int DebugViewID = Shader.PropertyToID("_DebugView");
    public static readonly int MaterialYFlipID = Shader.PropertyToID("_MaterialYFlip");
    public static readonly int EnableDiffuseBounceID = Shader.PropertyToID("_EnableDiffuseBounce");
    public static readonly int CascadeOffsetID = Shader.PropertyToID("_CascadeOffset");
    public static readonly int CascadeProbeSizeID = Shader.PropertyToID("_CascadeProbeSize");
    public static readonly int CascadeProbeSpacingID = Shader.PropertyToID("_CascadeProbeSpacing");
    public static readonly int CascadeDirectionCountID = Shader.PropertyToID("_CascadeDirectionCount");
    public static readonly int CascadeIntervalID = Shader.PropertyToID("_CascadeInterval");
    public static readonly int FarCascadeOffsetID = Shader.PropertyToID("_FarCascadeOffset");
    public static readonly int FarCascadeProbeSizeID = Shader.PropertyToID("_FarCascadeProbeSize");
    public static readonly int FarCascadeProbeSpacingID = Shader.PropertyToID("_FarCascadeProbeSpacing");
    public static readonly int FarCascadeDirectionCountID = Shader.PropertyToID("_FarCascadeDirectionCount");
    public static readonly int FarCascadeIntervalID = Shader.PropertyToID("_FarCascadeInterval");
    public static readonly int HasFarCascadeID = Shader.PropertyToID("_HasFarCascade");
    public static readonly int CascadeEntryCountID = Shader.PropertyToID("_CascadeEntryCount");
    public static readonly int CascadeDispatchRowWidthID = Shader.PropertyToID("_CascadeDispatchRowWidth");
    public static readonly int CascadeDispatchOriginID = Shader.PropertyToID("_CascadeDispatchOrigin");
    public static readonly int CascadeDispatchSizeID = Shader.PropertyToID("_CascadeDispatchSize");
    public static readonly int ScrollCascadeOffsetID = Shader.PropertyToID("_ScrollCascadeOffset");
    public static readonly int ScrollCascadeEntryCountID = Shader.PropertyToID("_ScrollCascadeEntryCount");
    public static readonly int ScrollProbeSizeID = Shader.PropertyToID("_ScrollProbeSize");
    public static readonly int ScrollProbeSpacingID = Shader.PropertyToID("_ScrollProbeSpacing");
    public static readonly int ScrollDirectionCountID = Shader.PropertyToID("_ScrollDirectionCount");
    public static readonly int ScrollDeltaProbesID = Shader.PropertyToID("_ScrollDeltaProbes");
    public static readonly int DirtyRegionsID = Shader.PropertyToID("_DirtyRegions");
    public static readonly int DirtyRegionCountID = Shader.PropertyToID("_DirtyRegionCount");
    public static readonly int CascadeChangedMaskID = Shader.PropertyToID("_CascadeChangedMask");
    public static readonly int CascadeMaskEnabledID = Shader.PropertyToID("_CascadeMaskEnabled");
    public static readonly int BlockAveragedID = Shader.PropertyToID("_BlockAveraged");
    public static readonly int DynamicLightsID = Shader.PropertyToID("_DynamicLights");
    public static readonly int DynamicLightCountID = Shader.PropertyToID("_DynamicLightCount");
    public static readonly int DynamicDispatchOriginID = Shader.PropertyToID("_DynamicDispatchOrigin");
    public static readonly int DynamicDispatchSizeID = Shader.PropertyToID("_DynamicDispatchSize");
    public static readonly int DynamicLightIndexID = Shader.PropertyToID("_DynamicLightIndex");
    public static readonly int WriteDynamicDirectID = Shader.PropertyToID("_WriteDynamicDirect");
    public static readonly int LampTileOffsetID = Shader.PropertyToID("_LampTileOffset");
    public static readonly int LampTilesID = Shader.PropertyToID("_LampTiles");
    public static readonly int LampTilesInputID = Shader.PropertyToID("_LampTilesInput");
    public static readonly int LampTileInfosID = Shader.PropertyToID("_LampTileInfos");
    public static readonly int LampTileCountID = Shader.PropertyToID("_LampTileCount");
    public static readonly int ComposeOriginID = Shader.PropertyToID("_ComposeOrigin");
    public static readonly int ComposeSizeID = Shader.PropertyToID("_ComposeSize");
    public static readonly int LampPolarID = Shader.PropertyToID("_LampPolar");
    public static readonly int LampPolarInputID = Shader.PropertyToID("_LampPolarInput");
    public static readonly int LampPolarSizeID = Shader.PropertyToID("_LampPolarSize");
    public static readonly int LampPolarPointID = Shader.PropertyToID("_LampPolarPoint");

    // LampEmitterPointsPerAxis squared in WorldLighting.compute: ray fans
    // traced per lamp, one band of rows each in the lamp ray texture.
    public const int LampEmitterPointCount = 9;

    // Must match InvisibleLampRadiance in WorldLighting.compute: absolute
    // radiance below which lamp light cannot move any display level.
    public const float InvisibleLampRadiance = 1e-6f;
    public static readonly int CellGridSizeID = Shader.PropertyToID("_CellGridSize");
    public static readonly int CellSolidMaskID = Shader.PropertyToID("_CellSolidMask");
    public static readonly int CellSolidMaskOutputID = Shader.PropertyToID("_CellSolidMaskOutput");
    public static readonly int BounceTapsID = Shader.PropertyToID("_BounceTaps");
    public static readonly int BounceFilterWeightsID = Shader.PropertyToID("_BounceFilterWeights");
    public static readonly int LightingCountersID = Shader.PropertyToID("_LightingCounters");
    public static readonly int LightingCountersEnabledID = Shader.PropertyToID("_LightingCountersEnabled");

    public static void BindLightingCounters(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel,
        ComputeBuffer counters)
    {
        commandBuffer.SetComputeBufferParam(compute, kernel, LightingCountersID, counters);
    }

    public static float ResolveTransmittanceDebugDistance()
    {
        // Fixed physical distance: changing sigma must change the measured
        // transmission, not silently change the distance in the opposite direction.
        return 1f;
    }

    public static void BindExtinction(CommandBuffer commandBuffer, ComputeShader compute)
    {
        commandBuffer.SetComputeVectorParam(
            compute,
            EmptyExtinctionRGBID,
            LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier);
        commandBuffer.SetComputeVectorParam(
            compute,
            SolidExtinctionRGBID,
            LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier);
    }

    // Weakest extinction of any medium and RGB channel, per cell. Every path
    // is attenuated at least this much per cell of length.
    public static float ResolveMinimumExtinction()
    {
        Color empty = LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier;
        Color solid = LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier;
        return Mathf.Max(0f, Mathf.Min(
            Mathf.Min(empty.r, Mathf.Min(empty.g, empty.b)),
            Mathf.Min(solid.r, Mathf.Min(solid.g, solid.b))));
    }

    public static void BindFieldTextures(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel,
        RenderTexture materialField,
        RenderTexture emissionField,
        ComputeBuffer? lightingCounters = null)
    {
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            MaterialFieldID,
            materialField);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            EmissionFieldID,
            emissionField);
        if (lightingCounters != null)
        {
            BindLightingCounters(commandBuffer, compute, kernel, lightingCounters);
        }
    }

    public static void BindSharedParameters(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int fieldWidth,
        int fieldHeight,
        int bounceWidth,
        int bounceHeight,
        Vector4 worldRect,
        float cellSize,
        LightingQualityMode qualityMode,
        LightingEngine.DebugView debugView,
        RenderTexture materialField,
        RenderTexture emissionField,
        int solveCascadeKernel,
        int resolveDirectKernel,
        int solveDiffuseBounceKernel,
        int compositeLightingKernel)
    {
        commandBuffer.SetComputeIntParams(compute, FieldSizeID, fieldWidth, fieldHeight);
        commandBuffer.SetComputeIntParams(compute, BounceSizeID, bounceWidth, bounceHeight);
        commandBuffer.SetComputeVectorParam(compute, WorldRectID, worldRect);
        commandBuffer.SetComputeVectorParam(
            compute,
            AmbientColorID,
            LightingConfigHolder.AmbientColor * LightingConfigHolder.AmbientIntensity);
        BindExtinction(commandBuffer, compute);
        commandBuffer.SetComputeFloatParam(compute, BounceStrengthID, LightingConfigHolder.BounceStrength);
        commandBuffer.SetComputeFloatParam(
            compute,
            TerrainAmbientOcclusionMipID,
            Fodinae.World.Terrain.TerrainLook.AmbientOcclusionMip);
        commandBuffer.SetComputeFloatParam(
            compute,
            TerrainAmbientOcclusionStrengthID,
            Fodinae.World.Terrain.TerrainLook.AmbientOcclusionStrength);
        commandBuffer.SetComputeFloatParam(compute, EmissionScaleID, LightingConfigHolder.EmissionScale);
        commandBuffer.SetComputeFloatParam(compute, MaximumLightMultiplierID, LightingConfigHolder.MaximumLightMultiplier);
        commandBuffer.SetComputeIntParam(compute, LightingCountersEnabledID, 0);
        commandBuffer.SetComputeFloatParam(compute, CellSizeID, cellSize);
        commandBuffer.SetComputeFloatParam(
            compute,
            TransmittanceDebugDistanceCellsID,
            ResolveTransmittanceDebugDistance());
        commandBuffer.SetComputeIntParam(compute, DebugViewID, (int)debugView);
        commandBuffer.SetComputeIntParam(
            compute,
            MaterialYFlipID,
            SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        commandBuffer.SetComputeIntParam(
            compute,
            EnableDiffuseBounceID,
            LightingConfigHolder.BounceEnabled ? 1 : 0);
        commandBuffer.SetComputeIntParam(
            compute,
            BlockAveragedID,
            qualityMode == LightingQualityMode.PerBlock ? 1 : 0);

        BindFieldTextures(commandBuffer, compute, solveCascadeKernel, materialField, emissionField);
        BindFieldTextures(commandBuffer, compute, resolveDirectKernel, materialField, emissionField);
        BindFieldTextures(commandBuffer, compute, solveDiffuseBounceKernel, materialField, emissionField);
        BindFieldTextures(commandBuffer, compute, compositeLightingKernel, materialField, emissionField);
    }

    public static int ResolveCascadeScrollDelta(int cellDelta, int fieldSize, int cellGridSize, int probeSpacing)
    {
        if (cellGridSize <= 0 || fieldSize <= 0 || fieldSize % cellGridSize != 0 || probeSpacing <= 0)
        {
            throw new ArgumentException("Atlas scrolling requires an integer texel scale and positive probe spacing.");
        }

        long texelDelta = (long)cellDelta * (fieldSize / cellGridSize);
        if (texelDelta % probeSpacing != 0)
        {
            throw new ArgumentException("Atlas scrolling cannot reuse probes with a different world-space phase.");
        }

        return checked((int)(texelDelta / probeSpacing));
    }

    public static void BindCascadeParameters(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        CascadeLayout cascade,
        CascadeLayout farCascade,
        bool hasFarCascade)
    {
        commandBuffer.SetComputeIntParam(compute, CascadeOffsetID, cascade.Offset);
        commandBuffer.SetComputeIntParams(
            compute,
            CascadeProbeSizeID,
            cascade.ProbeWidth,
            cascade.ProbeHeight);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeProbeSpacingID,
            cascade.ProbeSpacing);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeDirectionCountID,
            cascade.DirectionCount);
        commandBuffer.SetComputeVectorParam(
            compute,
            CascadeIntervalID,
            new Vector4(cascade.IntervalStart, cascade.IntervalEnd, 0f, 0f));
        commandBuffer.SetComputeIntParam(compute, FarCascadeOffsetID, farCascade.Offset);
        commandBuffer.SetComputeIntParams(
            compute,
            FarCascadeProbeSizeID,
            farCascade.ProbeWidth,
            farCascade.ProbeHeight);
        commandBuffer.SetComputeIntParam(
            compute,
            FarCascadeProbeSpacingID,
            farCascade.ProbeSpacing);
        commandBuffer.SetComputeIntParam(
            compute,
            FarCascadeDirectionCountID,
            farCascade.DirectionCount);
        commandBuffer.SetComputeVectorParam(
            compute,
            FarCascadeIntervalID,
            new Vector4(
                farCascade.IntervalStart,
                farCascade.IntervalEnd,
                0f,
                0f));
        commandBuffer.SetComputeIntParam(compute, HasFarCascadeID, hasFarCascade ? 1 : 0);
    }

    public static void BindCascadeDispatch(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int originX,
        int originY,
        int width,
        int height,
        int directionCount)
    {
        commandBuffer.SetComputeIntParams(
            compute,
            CascadeDispatchOriginID,
            originX,
            originY);
        commandBuffer.SetComputeIntParams(
            compute,
            CascadeDispatchSizeID,
            width,
            height);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeEntryCountID,
            checked(width * height * directionCount));
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeDispatchRowWidthID,
            checked(width * directionCount));
    }
}
