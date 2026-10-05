#ifndef KERN_TERRAIN_LIGHTING_FIELD_COMMON_INCLUDED
#define KERN_TERRAIN_LIGHTING_FIELD_COMMON_INCLUDED

#include "Assets/Shaders/World/LightingFieldRaster.hlsl"

struct TerrainLightingFieldVaryings
{
    float4 positionCS   : SV_POSITION;
    float2 uv           : TEXCOORD0;
    float4 worldPos     : TEXCOORD1;
    float4 animData     : TEXCOORD2;
    float4 packedData   : TEXCOORD3;
    float4 glowData     : TEXCOORD4;
    nointerpolation float isForeground : TEXCOORD5;
    float4 subAtlasRect : TEXCOORD6;
    float4 tileSizeUV   : TEXCOORD7;
    nointerpolation float atlasIndex : TEXCOORD8;
    nointerpolation float4 geometryCornersX : TEXCOORD9;
    nointerpolation float4 geometryCornersY : TEXCOORD10;
    nointerpolation float uvBits : TEXCOORD11;
};

TerrainLightingFieldVaryings TerrainLightingFieldVert(TerrainVertexInput input)
{
    TerrainLightingFieldVaryings output = (TerrainLightingFieldVaryings)0;
    // Background contributes neither material, emission nor occupancy.
    if (input.positionOS.z < 0.5)
    {
        output.positionCS = TerrainCulledPosition();
        return output;
    }

    TERRAIN_RESOLVE_CELL_VERTEX(input, output)
    output.positionCS = cell.atlasIndex >= 0.0
        ? KernLightingFieldClipPosition(cell.positionOS)
        : TerrainCulledPosition();
#if defined(KERN_TERRAIN_AO_FIELD)
    // A non-physical foreground cell cannot contribute contact occlusion.
    // Cull its quad before rasterization, including its expanded AO carrier.
    if (!KernTerrainIsPhysicalMass(KernTerrainLightingFlags(output.glowData.y)))
    {
        output.positionCS = TerrainCulledPosition();
    }
#endif
    return output;
}

half4 SampleTerrainLightingFieldAlbedoTexel(
    float2 geometryUv,
    float2 geometryCellPosition,
    float4 subAtlasRect,
    float4 tileSize,
    float4 worldPos,
    float4 animData,
    float4 packedData,
    int atlasSlot,
    float4 atlasTexelSize,
    float2 carrierPixelWidth)
{
    TerrainTileUvResult tileUV = ResolveTerrainTileUV(
        geometryUv,
        geometryCellPosition,
        subAtlasRect,
        tileSize,
        worldPos,
        animData,
        packedData,
        0.0,
        atlasTexelSize.xy);

    if (!tileUV.isValid)
    {
        return half4(0.0, 0.0, 0.0, 0.0);
    }

    float2 finalUV = PixelArtSampleUV(
        tileUV.finalUV,
        atlasTexelSize.zw,
        TerrainPixelArtWidthTexels(carrierPixelWidth, tileSize, atlasTexelSize));
    finalUV = ClampTerrainTileUV(finalUV, tileUV);
    [branch]
    if (_PixelArtFiltering < 0.5)
    {
        return TerrainSampleAtlas(atlasSlot, sampler_PointClamp, finalUV);
    }

    return TerrainSampleAtlas(atlasSlot, sampler_LinearClamp, finalUV);
}

#endif
