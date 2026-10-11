#ifndef KERN_WORLD_SURFACE_COMMON_INCLUDED
#define KERN_WORLD_SURFACE_COMMON_INCLUDED

#include "../Terrain/TerrainTileAddressing.hlsl"

float2 KernResolveRedRockUv(
    float2 worldPosition,
    float2 tileCount,
    float worldHeight)
{
    float2 cell = floor(worldPosition);
    float2 localUv = frac(worldPosition);
    float serverCellY = (worldHeight - 1.0) - cell.y;
    return KernResolveTerrainSheetUv(
        float2(cell.x, serverCellY),
        localUv,
        tileCount,
        0.0,
        0.0);
}

float KernWrapSurfaceU(float u)
{
    return frac(u);
}

float2 KernResolveSurfaceUv(
    float2 uv,
    float2 worldPosition,
    float2 baseMapTileCount,
    float worldHeight)
{
#if defined(KERN_SURFACE_REDROCK)
    return KernResolveRedRockUv(
        worldPosition,
        baseMapTileCount,
        worldHeight);
#elif defined(KERN_SURFACE_TRANSIT)
    return float2(KernWrapSurfaceU(uv.x), saturate(uv.y));
#else
    return uv;
#endif
}

// World cells contain exactly 32 presentation samples on each axis.
// Original band heights and alpha UVs are independent of this color raster.
float2 KernSurfaceColorRasterUv(float2 worldPosition, float worldHeight, float2 textureSize)
{
    float2 samplePosition = (floor(worldPosition * 32.0) + 0.5) / 32.0;
#if defined(KERN_SURFACE_PERSPECTIVE)
    const float bandBottom = 2.0;
#else
    const float bandBottom = 0.0;
#endif
    return float2(-samplePosition.x * 32.0 / textureSize.x,
        saturate((samplePosition.y - worldHeight - bandBottom) / 2.0));
}

// Authored strip detail and alpha stay intact. This is linear albedo tint,
// shared by visible rendering and the material field; it does not alter emission.
float3 KernOriginalSurfaceAlbedo(float3 authoredAlbedo)
{
#if defined(KERN_SURFACE_TRANSIT) || defined(KERN_SURFACE_PERSPECTIVE)
    return authoredAlbedo * float3(1.0, 0.38, 0.30);
#else
    return authoredAlbedo;
#endif
}

#endif
