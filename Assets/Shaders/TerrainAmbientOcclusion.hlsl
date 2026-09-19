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
    // Mip 1.5 is the established AO radius at one texel per cell. The
    // density offset keeps that radius in world space while mip zero retains
    // the authored rounded and alpha-cutout silhouette.
    float mip = 1.5 + log2(texelsPerCell);
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
