#ifndef FODINAE_TERRAIN_TILE_ADDRESSING_INCLUDED
#define FODINAE_TERRAIN_TILE_ADDRESSING_INCLUDED

static const float FodinaeTileAddressEpsilon = 0.0001;

float FodinaePositiveModulo(float value, float modulus)
{
    return fmod(fmod(value, modulus) + modulus, modulus);
}

float2 FodinaeResolveTerrainTileIndex(
    float2 gridPosition,
    float2 tileCount,
    float tileGroupColumn,
    float useTileGroupColumn)
{
    float2 integerGridPosition = floor(
        gridPosition + FodinaeTileAddressEpsilon);
    float tileX = useTileGroupColumn > 0.5
        ? floor(tileGroupColumn + FodinaeTileAddressEpsilon)
        : floor(
            FodinaePositiveModulo(integerGridPosition.x, tileCount.x) +
            FodinaeTileAddressEpsilon);
    float tileY = floor(
        tileCount.y -
        FodinaeTileAddressEpsilon -
        FodinaePositiveModulo(integerGridPosition.y, tileCount.y));
    return clamp(float2(tileX, tileY), 0.0, tileCount - 1.0);
}

float2 FodinaeResolveTerrainSheetUv(
    float2 gridPosition,
    float2 localUv,
    float2 tileCount,
    float tileGroupColumn,
    float useTileGroupColumn)
{
    float2 tileIndex = FodinaeResolveTerrainTileIndex(
        gridPosition,
        tileCount,
        tileGroupColumn,
        useTileGroupColumn);
    float2 safeLocalUv = clamp(
        localUv,
        FodinaeTileAddressEpsilon,
        1.0 - FodinaeTileAddressEpsilon);
    return (tileIndex + safeLocalUv) / tileCount;
}

#endif
