#nullable enable

namespace Fodinae.World.Lighting;

using System;
using Fodinae.Core;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Centralizes Shader property IDs and provides helpers for binding compute shader parameters
/// across Radiance Cascades kernels.
/// </summary>
internal static class LightingComputeBinder
{
    public static readonly int MaterialFieldId = Shader.PropertyToID("_MaterialField");
    public static readonly int EmissionFieldId = Shader.PropertyToID("_EmissionField");
    public static readonly int AutomaticNormalInputId = Shader.PropertyToID("_AutomaticNormalInput");
    public static readonly int RadianceAtlasId = Shader.PropertyToID("_RadianceAtlas");
    public static readonly int DirectTextureId = Shader.PropertyToID("_DirectTexture");
    public static readonly int DirectInputId = Shader.PropertyToID("_DirectInput");
    public static readonly int StaticDirectInputId = Shader.PropertyToID("_StaticDirectInput");
    public static readonly int BounceTextureId = Shader.PropertyToID("_BounceTexture");
    public static readonly int BounceInputId = Shader.PropertyToID("_BounceInput");
    public static readonly int ResultId = Shader.PropertyToID("_Result");
    public static readonly int FieldSizeId = Shader.PropertyToID("_FieldSize");
    public static readonly int BounceSizeId = Shader.PropertyToID("_BounceSize");
    public static readonly int WorldRectId = Shader.PropertyToID("_WorldRect");
    public static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");
    public static readonly int EmptyExtinctionRgbId = Shader.PropertyToID("_EmptyExtinctionRgb");
    public static readonly int SolidExtinctionRgbId = Shader.PropertyToID("_SolidExtinctionRgb");
    public static readonly int MinimumTransmissionId = Shader.PropertyToID("_MinimumTransmission");
    public static readonly int BounceStrengthId = Shader.PropertyToID("_BounceStrength");
    public static readonly int EmissionScaleId = Shader.PropertyToID("_EmissionScale");
    public static readonly int MaximumLightMultiplierId = Shader.PropertyToID("_MaximumLightMultiplier");
    public static readonly int EnableFinalLightingClampId = Shader.PropertyToID("_EnableFinalLightingClamp");
    public static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    public static readonly int TransmittanceDebugDistanceCellsId = Shader.PropertyToID("_TransmittanceDebugDistanceCells");
    public static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
    public static readonly int MaterialYFlipId = Shader.PropertyToID("_MaterialYFlip");
    public static readonly int MaximumIntervalStepsId = Shader.PropertyToID("_MaximumIntervalSteps");
    public static readonly int EnableDiffuseBounceId = Shader.PropertyToID("_EnableDiffuseBounce");
    public static readonly int CascadeOffsetId = Shader.PropertyToID("_CascadeOffset");
    public static readonly int CascadeProbeSizeId = Shader.PropertyToID("_CascadeProbeSize");
    public static readonly int CascadeProbeSpacingId = Shader.PropertyToID("_CascadeProbeSpacing");
    public static readonly int CascadeDirectionCountId = Shader.PropertyToID("_CascadeDirectionCount");
    public static readonly int CascadeIntervalId = Shader.PropertyToID("_CascadeInterval");
    public static readonly int FarCascadeOffsetId = Shader.PropertyToID("_FarCascadeOffset");
    public static readonly int FarCascadeProbeSizeId = Shader.PropertyToID("_FarCascadeProbeSize");
    public static readonly int FarCascadeProbeSpacingId = Shader.PropertyToID("_FarCascadeProbeSpacing");
    public static readonly int FarCascadeDirectionCountId = Shader.PropertyToID("_FarCascadeDirectionCount");
    public static readonly int FarCascadeIntervalId = Shader.PropertyToID("_FarCascadeInterval");
    public static readonly int HasFarCascadeId = Shader.PropertyToID("_HasFarCascade");
    public static readonly int EnableBilinearFixId = Shader.PropertyToID("_EnableBilinearFix");
    public static readonly int CascadeEntryCountId = Shader.PropertyToID("_CascadeEntryCount");
    public static readonly int CascadeDispatchRowWidthId = Shader.PropertyToID("_CascadeDispatchRowWidth");
    public static readonly int BlockAveragedId = Shader.PropertyToID("_BlockAveraged");

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
            MaterialFieldId,
            materialField);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            EmissionFieldId,
            emissionField);
    }

    public static void BindAutomaticNormalInput(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel,
        RenderTexture automaticNormalField)
    {
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            AutomaticNormalInputId,
            automaticNormalField);
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
        LightingConfigHolder configHolder,
        in GraphicsQualitySettings qualitySettings,
        LightingQualityMode qualityMode,
        LightingEngine.DebugView debugView,
        RenderTexture materialField,
        RenderTexture emissionField,
        RenderTexture automaticNormalField,
        int solveCascadeKernel,
        int solveAutomaticNormalsKernel,
        int resolveDirectKernel,
        int solveDiffuseBounceKernel,
        int compositeLightingKernel)
    {
        commandBuffer.SetComputeIntParams(compute, FieldSizeId, fieldWidth, fieldHeight);
        commandBuffer.SetComputeIntParams(compute, BounceSizeId, bounceWidth, bounceHeight);
        commandBuffer.SetComputeVectorParam(compute, WorldRectId, worldRect);
        commandBuffer.SetComputeVectorParam(
            compute,
            AmbientColorId,
            configHolder.AmbientColor * configHolder.AmbientIntensity);
        commandBuffer.SetComputeVectorParam(
            compute,
            EmptyExtinctionRgbId,
            configHolder.EmptyExtinctionRgb * configHolder.EmptyExtinctionMultiplier);
        commandBuffer.SetComputeVectorParam(
            compute,
            SolidExtinctionRgbId,
            configHolder.SolidExtinctionRgb * configHolder.SolidExtinctionMultiplier);
        commandBuffer.SetComputeFloatParam(compute, MinimumTransmissionId, configHolder.MinimumTransmission);
        commandBuffer.SetComputeFloatParam(compute, BounceStrengthId, configHolder.BounceStrength);
        commandBuffer.SetComputeFloatParam(compute, EmissionScaleId, configHolder.EmissionScale);
        commandBuffer.SetComputeFloatParam(compute, MaximumLightMultiplierId, configHolder.MaximumLightMultiplier);
        commandBuffer.SetComputeIntParam(
            compute,
            EnableFinalLightingClampId,
            configHolder.EnableFinalLightingClamp ? 1 : 0);
        commandBuffer.SetComputeFloatParam(compute, CellSizeId, cellSize);
        commandBuffer.SetComputeFloatParam(
            compute,
            TransmittanceDebugDistanceCellsId,
            configHolder.TransmittanceDebugDistanceCells);
        commandBuffer.SetComputeIntParam(compute, DebugViewId, (int)debugView);
        commandBuffer.SetComputeIntParam(
            compute,
            MaterialYFlipId,
            SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        commandBuffer.SetComputeIntParam(
            compute,
            MaximumIntervalStepsId,
            Mathf.Clamp(qualitySettings.LightingMaximumRaySteps, 1, 64));
        commandBuffer.SetComputeIntParam(
            compute,
            EnableDiffuseBounceId,
            configHolder.DiffuseBounceEnabled ? 1 : 0);
        commandBuffer.SetComputeIntParam(
            compute,
            BlockAveragedId,
            qualityMode == LightingQualityMode.PerBlock ? 1 : 0);

        BindFieldTextures(commandBuffer, compute, solveCascadeKernel, materialField, emissionField);
        commandBuffer.SetComputeTextureParam(
            compute,
            solveAutomaticNormalsKernel,
            MaterialFieldId,
            materialField);
        BindFieldTextures(commandBuffer, compute, resolveDirectKernel, materialField, emissionField);
        BindFieldTextures(commandBuffer, compute, solveDiffuseBounceKernel, materialField, emissionField);
        BindFieldTextures(commandBuffer, compute, compositeLightingKernel, materialField, emissionField);
        BindAutomaticNormalInput(commandBuffer, compute, resolveDirectKernel, automaticNormalField);
        BindAutomaticNormalInput(commandBuffer, compute, solveDiffuseBounceKernel, automaticNormalField);
        BindAutomaticNormalInput(commandBuffer, compute, compositeLightingKernel, automaticNormalField);
    }

    public static void BindCascadeParameters(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        CascadeLayout cascade,
        CascadeLayout farCascade,
        bool hasFarCascade,
        bool bilinearFix)
    {
        commandBuffer.SetComputeIntParam(compute, CascadeOffsetId, cascade.Offset);
        commandBuffer.SetComputeIntParams(
            compute,
            CascadeProbeSizeId,
            cascade.ProbeWidth,
            cascade.ProbeHeight);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeProbeSpacingId,
            cascade.ProbeSpacing);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeDirectionCountId,
            cascade.DirectionCount);
        commandBuffer.SetComputeVectorParam(
            compute,
            CascadeIntervalId,
            new Vector4(cascade.IntervalStart, cascade.IntervalEnd, 0f, 0f));
        commandBuffer.SetComputeIntParam(compute, FarCascadeOffsetId, farCascade.Offset);
        commandBuffer.SetComputeIntParams(
            compute,
            FarCascadeProbeSizeId,
            farCascade.ProbeWidth,
            farCascade.ProbeHeight);
        commandBuffer.SetComputeIntParam(
            compute,
            FarCascadeProbeSpacingId,
            farCascade.ProbeSpacing);
        commandBuffer.SetComputeIntParam(
            compute,
            FarCascadeDirectionCountId,
            farCascade.DirectionCount);
        commandBuffer.SetComputeVectorParam(
            compute,
            FarCascadeIntervalId,
            new Vector4(
                farCascade.IntervalStart,
                farCascade.IntervalEnd,
                0f,
                0f));
        commandBuffer.SetComputeIntParam(compute, HasFarCascadeId, hasFarCascade ? 1 : 0);
        commandBuffer.SetComputeIntParam(compute, EnableBilinearFixId, bilinearFix ? 1 : 0);
        commandBuffer.SetComputeIntParam(compute, CascadeEntryCountId, cascade.EntryCount);
    }
}
