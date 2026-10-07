#ifndef KERN_TERRAIN_ANIMATION_SAMPLING_INCLUDED
#define KERN_TERRAIN_ANIMATION_SAMPLING_INCLUDED

// Requires TerrainAnimationProfile.hlsl, TerrainPrismaticCrystal.hlsl, and
// caller-owned flow texture/sampler declarations.
float3 TerrainResolveFlowSample(
    int cellSurfaceEffect,
    int cellAnimationType,
    float4 worldPos,
    float4 packedData,
    float4 flowScale)
{
    float3 result = 0.0;

    if (cellSurfaceEffect == (int)KERN_TERRAIN_SURFACE_EFFECT_PRISMATIC)
    {
        result = SAMPLE_TEXTURE2D(
            _PrismaticFlowMap,
            sampler_PrismaticFlowMap,
            PrismaticCrystalFlowUV(worldPos.xy, packedData.yz)).rgb;
    }
    else if (TerrainAnimationUsesFlowMap(cellAnimationType, cellSurfaceEffect))
    {
        float2 flowPosition = worldPos.xy + packedData.yz * float2(1.0, -1.0);
        result = SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, flowPosition / flowScale.xy).rgb;
    }

    return result;
}

// World-stable surface coordinates shared by visual material animations.
float2 TerrainAnimationWorldPosition(float4 worldPos, float4 packedData)
{
    return worldPos.xy + packedData.yz * float2(1.0, -1.0);
}

#endif
