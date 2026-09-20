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
    uint variant = code & 7u;
    uint rotation = (code >> 3u) & 3u;
    bool mirror = ((code >> 5u) & 1u) != 0u;
    uint offsetX = (code >> 6u) & 3u;
    uint offsetY = (code >> 8u) & 3u;
    uint applicationMode = (code >> 10u) & 3u;
    float2 transformedUV = TerrainTransformDecalUV(localUV, rotation, mirror);
    float2 placementOffset = float2(offsetX, offsetY) / 3.0 - 0.5;
    float2 modeScale = float2(0.78, 0.78);
    float2 modeAnchor = float2(0.11, 0.11);
    if (applicationMode == 1u)
    {
        modeScale = float2(0.62, 0.86);
        modeAnchor = float2(0.19, 0.07);
    }
    else if (applicationMode == 2u)
    {
        modeScale = float2(0.70, 0.58);
        modeAnchor = float2(0.08, 0.25);
    }
    else if (applicationMode == 3u)
    {
        modeScale = float2(0.92, 0.46);
        modeAnchor = float2(0.04, 0.29);
    }

    transformedUV = transformedUV * modeScale + modeAnchor + placementOffset * 0.18;

    // Eight horizontal 32x32 slots in a 256x32 atlas. Sampling texel centres
    // prevents a transformed edge from crossing into the neighbouring slot.
    float2 pixel = float2(variant * 32.0, 0.0) +
        clamp(saturate(transformedUV) * 32.0, 0.5, 31.5);
    float2 atlasUV = pixel / float2(256.0, 32.0);
    half4 decal = SAMPLE_TEXTURE2D_LOD(
        _TerrainDecalAtlas,
        sampler_TerrainDecalAtlas,
        atlasUV,
        0);
    return lerp(baseColor, decal.rgb, decal.a);
}

#endif
