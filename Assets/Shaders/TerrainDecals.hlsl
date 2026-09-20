#ifndef KERN_TERRAIN_DECALS_INCLUDED
#define KERN_TERRAIN_DECALS_INCLUDED

TEXTURE2D(_TerrainDecalAtlas);
SAMPLER(sampler_TerrainDecalAtlas);

float2 TerrainTransformDecalUV(float2 uv, uint rotation, bool mirror)
{
    if (mirror)
    {
        uv.x = 1.0 - uv.x;
    }

    if (rotation == 1u)
    {
        return float2(uv.y, 1.0 - uv.x);
    }

    if (rotation == 2u)
    {
        return 1.0 - uv;
    }

    if (rotation == 3u)
    {
        return float2(1.0 - uv.y, uv.x);
    }

    return uv;
}

float3 ApplyTerrainDecal(float3 baseColor, float2 localUV, float packedPlacement)
{
    if (packedPlacement < 0.5)
    {
        return baseColor;
    }

    uint code = (uint)round(packedPlacement) - 1u;
    uint variant = code & 15u;
    uint rotation = (code >> 4u) & 3u;
    bool mirror = ((code >> 6u) & 1u) != 0u;
    uint offsetX = (code >> 7u) & 3u;
    uint offsetY = (code >> 9u) & 3u;
    float2 transformedUV = TerrainTransformDecalUV(localUV, rotation, mirror);
    float2 placementOffset = float2(offsetX, offsetY) / 3.0 - 0.5;
    transformedUV += placementOffset * 0.50;

    // Sixteen horizontal 32x32 slots in a 512x32 atlas. Sampling texel centres
    // prevents a transformed edge from crossing into the neighbouring slot.
    float2 pixel = float2(variant * 32.0, 0.0) +
        clamp(saturate(transformedUV) * 32.0, 0.5, 31.5);
    float2 atlasUV = pixel / float2(512.0, 32.0);
    half4 decal = SAMPLE_TEXTURE2D_LOD(
        _TerrainDecalAtlas,
        sampler_TerrainDecalAtlas,
        atlasUV,
        0);
    float3 screen = 1.0 - (1.0 - baseColor) * (1.0 - decal.rgb);
    return lerp(baseColor, screen, decal.a * 0.35);
}

#endif
