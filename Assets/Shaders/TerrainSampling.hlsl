#ifndef KERN_TERRAIN_SAMPLING_INCLUDED
#define KERN_TERRAIN_SAMPLING_INCLUDED

#include "TerrainTileAddressing.hlsl"

struct TerrainTileUvResult
{
    float2 finalUV;
    float2 minTileUV;
    float2 maxTileUV;
    float2 availableTileSize;
    float2 baseUV;
    float2 tileOffsetUV;
    bool isScrollAnimated;
    float animOffsetUV;
    bool isValid;
};

TerrainTileUvResult ResolveTerrainTileUV(
    float2 cornerUV,
    float4 subAtlasRect,
    float4 tileSize,
    float4 worldPos,
    float4 animData,
    float4 packedData,
    float timeY,
    float2 atlasTexelSize)
{
    TerrainTileUvResult res;
    res.baseUV = subAtlasRect.xy;
    res.tileOffsetUV = 0.0;
    res.availableTileSize = 0.0;
    res.finalUV = 0.0;
    res.minTileUV = 0.0;
    res.maxTileUV = 0.0;
    res.isScrollAnimated = false;
    res.animOffsetUV = 0.0;
    res.isValid = false;

    float2 baseUV = subAtlasRect.xy;
    float2 subAtlasSizeUV = subAtlasRect.zw;
    float2 tileSizeUV = tileSize.xy;
    if (subAtlasSizeUV.x <= 0.0 || tileSizeUV.x <= 0.0)
    {
        return res;
    }

    res.isValid = true;
    float frameCount = tileSize.z;
    float frameHeightTiles = tileSize.w;
    float animOffsetUV = 0.0;
    if (frameCount > 1.5)
    {
        float speed = animData.y;
        float frameIndex = floor(fmod(timeY * speed, frameCount));
        animOffsetUV = frameIndex * frameHeightTiles * tileSizeUV.y;
    }
    res.animOffsetUV = animOffsetUV;

    float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
    tilesCount = max(tilesCount, 1.0);

    // Molten surfaces scroll one complete authored sheet, never separate cell
    // tiles. Both raster-carrier and regular quads use the same world position.
    if ((int)(animData.w + 0.5) == 2)
    {
        float2 sheetPosition = float2(worldPos.x, -worldPos.y - 1.0) + packedData.yz;
        sheetPosition.y += timeY * animData.y * 0.05;
        float2 sheetUV = frac(sheetPosition / tilesCount);
        res.finalUV = baseUV + sheetUV * subAtlasSizeUV;
        res.finalUV.y += animOffsetUV;
        res.availableTileSize = subAtlasSizeUV;
        res.minTileUV = baseUV + atlasTexelSize * 0.5;
        res.maxTileUV = baseUV + subAtlasSizeUV - atlasTexelSize * 0.5;
        return res;
    }

    // Раскладка упакованных каналов клетки:
    //   w — бит 0: автотайлинг по соседям. Биты выше свободны.
    //   z — биты 0-4: колонка тайлгруппы (descriptor & 0x1F);
    //       бит 5: сплошной лист.
    //
    // В w когда-то читали значение больше 1.5 как «выбросить квад», но
    // писателя у него не было ни в одном коммите, и канал только выглядел
    // занятым. Читателей сняли; выбрасывание квада выражено там, где оно
    // и принимается, — atlasIndex < 0 в LoadTerrainCellVertex и
    // TerrainCellLayers.TryGetType.
    bool isTiling = fmod(worldPos.w, 2.0) > 0.5;
    int packedColumn = (int)(worldPos.z + 0.5);
    bool isContinuousSheet = (packedColumn & 32) != 0;
    float tileGroupColumn = (float)(packedColumn & 31);

    // Кристаллы и камень адресуют лист целиком по мировой координате, а не
    // тайлом на клетку. Клеточная координата фрагмента уже несёт смещение
    // узлов (packedData.yz у якорной клетки — положение внутри несущего
    // прямоугольника), поэтому выборка перетекает за край своего тайла в
    // соседний ровно так же, как в оригинале: там к UV прибавляют тот же
    // _dists, что и к позиции вершины.
    //
    // Тайл на клетку этого не умеет. Его UV зажат в свою клетку, геометрия
    // уезжает без него, и на каждой границе остаётся шов: массив читается
    // кладкой из штампов, а не одним камнем.
    if (isContinuousSheet)
    {
        float2 sheetPosition = float2(worldPos.x, -worldPos.y - 1.0) + packedData.yz;
        float2 sheetUV = frac(sheetPosition / tilesCount);
        res.finalUV = baseUV + sheetUV * subAtlasSizeUV;
        res.finalUV.y += animOffsetUV;
        res.availableTileSize = subAtlasSizeUV;
        res.minTileUV = baseUV + atlasTexelSize * 0.5;
        res.maxTileUV = baseUV + subAtlasSizeUV - atlasTexelSize * 0.5;
        return res;
    }

    float2 wrapped = KernResolveTerrainTileIndex(
        worldPos.xy,
        tilesCount,
        tileGroupColumn,
        isTiling ? 1.0 : 0.0);
    float2 tileOffsetUV = wrapped * tileSizeUV;
    float2 availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
    float2 quadUV = cornerUV;
    int animTypeEarly = (int)(animData.x + 0.5);
    bool isScrollAnimated = animTypeEarly == 4;
    res.isScrollAnimated = isScrollAnimated;

    if (packedData.x > 0.5)
    {
        float2 anchoredUV = packedData.yz;
        float2 stepUV = float2(0.0, 0.0);
        stepUV.x = anchoredUV.x > 1.0 ? 1.0 : (anchoredUV.x < 0.0 ? -1.0 : 0.0);
        stepUV.y = anchoredUV.y > 1.0 ? -1.0 : (anchoredUV.y < 0.0 ? 1.0 : 0.0);
        bool outsideX = stepUV.x != 0.0;
        bool outsideY = stepUV.y != 0.0;
        if (isScrollAnimated)
        {
            quadUV.x = outsideX ? frac(anchoredUV.x) : anchoredUV.x;
            quadUV.y = anchoredUV.y;
        }
        else
        {
            quadUV = (outsideX || outsideY) ? frac(anchoredUV) : anchoredUV;
        }

        if (outsideX || outsideY)
        {
            float2 stepPos = worldPos.xy + stepUV;
            float2 wrappedStep = KernResolveTerrainTileIndex(
                stepPos,
                tilesCount,
                tileGroupColumn,
                isTiling ? 1.0 : 0.0);
            if (isScrollAnimated)
            {
                wrappedStep.y = wrapped.y;
            }

            tileOffsetUV = wrappedStep * tileSizeUV;
            availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
        }
    }

    quadUV.x = clamp(quadUV.x, KernTileAddressEpsilon, 1.0 - KernTileAddressEpsilon);
    if (!isScrollAnimated || packedData.x <= 0.5)
    {
        quadUV.y = clamp(quadUV.y, KernTileAddressEpsilon, 1.0 - KernTileAddressEpsilon);
    }

    float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;
    finalUV.y += animOffsetUV;

    if (isScrollAnimated)
    {
        float speed = animData.y;
        float scrollUV = fmod(timeY * speed * tileSizeUV.y * 0.05, subAtlasSizeUV.y);
        finalUV.y = baseUV.y + fmod(finalUV.y - baseUV.y + scrollUV + subAtlasSizeUV.y, subAtlasSizeUV.y);
    }

    res.baseUV = baseUV;
    res.tileOffsetUV = tileOffsetUV;
    res.availableTileSize = availableTileSize;
    res.finalUV = finalUV;
    res.minTileUV = baseUV + tileOffsetUV + atlasTexelSize * 0.5;
    res.maxTileUV = baseUV + tileOffsetUV + availableTileSize - atlasTexelSize * 0.5;
    return res;
}

float2 ClampTerrainTileUV(float2 uv, TerrainTileUvResult tile)
{
    if (!tile.isScrollAnimated)
    {
        float2 minTile = tile.minTileUV;
        float2 maxTile = tile.maxTileUV;
        minTile.y += tile.animOffsetUV;
        maxTile.y += tile.animOffsetUV;
        return clamp(uv, minTile, maxTile);
    }
    else
    {
        uv.x = clamp(uv.x, tile.minTileUV.x, tile.maxTileUV.x);
        return uv;
    }
}

#endif
