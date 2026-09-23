#ifndef KERN_TERRAIN_AMBIENT_OCCLUSION_INCLUDED
#define KERN_TERRAIN_AMBIENT_OCCLUSION_INCLUDED

#include "TerrainLightingData.hlsl"

Texture2D<float4> _WorldAmbientOcclusionTexture;
SamplerState sampler_WorldAmbientOcclusionTexture;
int _WorldAmbientOcclusionYFlip;
float _WorldAmbientOcclusionTexelsPerCell;
float _TerrainAmbientOcclusionStrength;
float _TerrainAmbientOcclusionFloor;

float KernSampleTerrainAmbientOcclusion(float2 worldPosition, float4 worldLightRect)
{
    float2 uv = (worldPosition - worldLightRect.xy) /
        max(worldLightRect.zw, float2(0.0001, 0.0001));
    if (_WorldAmbientOcclusionYFlip != 0)
    {
        uv.y = 1.0 - uv.y;
    }

    float texelsPerCell = max(_WorldAmbientOcclusionTexelsPerCell, 1.0);
    // Contact AO must retain the cell silhouette. Averaging over 2.8 cells
    // erased displaced/rounded edges even though mip zero held correct geometry.
    // A half-cell footprint follows the contour at every field resolution;
    // keep the established response curve and strength below unchanged.
    float mip = max(log2(texelsPerCell) - 1.0, 0.0);
    float nearbyOccupancy = _WorldAmbientOcclusionTexture.SampleLevel(
        sampler_WorldAmbientOcclusionTexture,
        saturate(uv),
        mip).a;
    return saturate(sqrt(nearbyOccupancy) * _TerrainAmbientOcclusionStrength);
}

float KernTerrainDiagonalOcclusion(float packedContour, float2 cellPosition)
{
    // Same bit order as CalculateSolidBoundaryMask: top-left, top-right,
    // bottom-left, bottom-right. Cell position is unrotated geometry UV.
    int diagonalMask = KernTerrainSolidDiagonal(packedContour);
    float2 towardLeftBottom = saturate(1.0 - 2.0 * cellPosition);
    float2 towardRightTop = saturate(2.0 * cellPosition - 1.0);
    float topLeft = (diagonalMask & 1) != 0
        ? towardLeftBottom.x * towardRightTop.y : 0.0;
    float topRight = (diagonalMask & 2) != 0
        ? towardRightTop.x * towardRightTop.y : 0.0;
    float bottomLeft = (diagonalMask & 4) != 0
        ? towardLeftBottom.x * towardLeftBottom.y : 0.0;
    float bottomRight = (diagonalMask & 8) != 0
        ? towardRightTop.x * towardLeftBottom.y : 0.0;
    return max(max(topLeft, topRight), max(bottomLeft, bottomRight));
}

float KernTerrainAmbientOcclusionMultiplier(
    float packedLightingFlags,
    float packedContour,
    float2 cellPosition,
    float2 worldPosition,
    float4 worldLightRect)
{
    uint lightingFlags = KernTerrainLightingFlags(packedLightingFlags);
    if (!KernTerrainReceivesAmbientOcclusion(lightingFlags))
    {
        return 1.0;
    }

    // Затенение гасит поверхность не до нуля, а до пола. Полный ноль делал
    // из тени дыру: пол вплотную к массиву становился чёрным, и граница
    // читалась полосой, а не притенением. Пол задаётся в TerrainLook.
    float occlusion = max(
        KernSampleTerrainAmbientOcclusion(worldPosition, worldLightRect),
        KernTerrainDiagonalOcclusion(packedContour, cellPosition) *
            _TerrainAmbientOcclusionStrength);
    return 1.0 - (occlusion * (1.0 - _TerrainAmbientOcclusionFloor));
}

#endif
