#ifndef KERN_TERRAIN_AMBIENT_OCCLUSION_INCLUDED
#define KERN_TERRAIN_AMBIENT_OCCLUSION_INCLUDED

#include "TerrainLightingData.hlsl"

Texture2D<float4> _WorldAmbientOcclusionTexture;
SamplerState sampler_WorldAmbientOcclusionTexture;
int _WorldAmbientOcclusionYFlip;
float _WorldAmbientOcclusionTexelsPerCell;
float _TerrainAmbientOcclusionStrength;

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

float KernTerrainAmbientOcclusionMultiplier(
    float packedLightingFlags,
    float2 worldPosition,
    float4 worldLightRect)
{
    uint lightingFlags = KernTerrainLightingFlags(packedLightingFlags);
    if (!KernTerrainReceivesAmbientOcclusion(lightingFlags))
    {
        return 1.0;
    }

    return 1.0 - KernSampleTerrainAmbientOcclusion(worldPosition, worldLightRect);
}

#endif
