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
    public static readonly int MaterialFieldID = Shader.PropertyToID("_MaterialField");
    public static readonly int EmissionFieldID = Shader.PropertyToID("_EmissionField");
    public static readonly int RadianceAtlasID = Shader.PropertyToID("_RadianceAtlas");
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
    public static readonly int BlockAveragedID = Shader.PropertyToID("_BlockAveraged");
    public static readonly int DynamicLightsID = Shader.PropertyToID("_DynamicLights");
    public static readonly int DynamicLightCountID = Shader.PropertyToID("_DynamicLightCount");
    public static readonly int BlockLightInputID = Shader.PropertyToID("_BlockLightInput");
    public static readonly int BlockLightOutputID = Shader.PropertyToID("_BlockLightOutput");
    public static readonly int CellGridSizeID = Shader.PropertyToID("_CellGridSize");
    public static readonly int CellSolidMaskID = Shader.PropertyToID("_CellSolidMask");
    public static readonly int CellSolidMaskOutputID = Shader.PropertyToID("_CellSolidMaskOutput");
    public static readonly int BounceTapsID = Shader.PropertyToID("_BounceTaps");
    public static readonly int BounceFilterWeightsID = Shader.PropertyToID("_BounceFilterWeights");

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

    // Relative tail tolerance, not an artistic light radius. The weakest RGB
    // channel and least absorbing material determine the required path length.
    public static int ResolveBlockPropagationIterations(int width, int height)
    {
        Color empty = LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier;
        Color solid = LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier;
        float weakest = Mathf.Max(0f, Mathf.Min(
            Mathf.Min(empty.r, Mathf.Min(empty.g, empty.b)),
            Mathf.Min(solid.r, Mathf.Min(solid.g, solid.b))));
        int maximumPath = checked(width * height - 1);
        if (weakest <= 0f)
        {
            // A lossless maze can require visiting every cell; a fixed radius
            // would introduce a visible cutoff unrelated to absorption.
            return maximumPath;
        }

        return Mathf.CeilToInt(Mathf.Min(maximumPath, -Mathf.Log(1e-6f) / weakest + 1f));
    }

    public static void BindFieldTextures(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel,
        RenderTexture materialField,
        RenderTexture emissionField)
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
        commandBuffer.SetComputeIntParam(compute, CascadeEntryCountID, cascade.EntryCount);
    }
}
