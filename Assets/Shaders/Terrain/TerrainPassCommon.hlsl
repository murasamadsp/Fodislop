#ifndef KERN_TERRAIN_PASS_COMMON_INCLUDED
#define KERN_TERRAIN_PASS_COMMON_INCLUDED

// Геометрический контракт вершин, общий между экранным, material/emission и
// AO-проходами. Выборка анимационных flow-текстур живёт отдельно.

// Атлас у клетки может отсутствовать (клетка за миром, не загружена или слой
// пуст). Такой квад не отбрасывается на CPU, он выталкивается за плоскость
// отсечения: это дешевле, чем менять состав меша каждый кадр.
float4 TerrainVertexClipPosition(float atlasIndex, float3 positionOS)
{
    return atlasIndex >= 0.0
        ? mul(UNITY_MATRIX_P, float4(TransformWorldToView(TransformObjectToWorld(positionOS)), 1.0))
        : TerrainCulledPosition();
}

// Вход вершины у всех проходов один — адрес из меша идентификаторов
// (TerrainCellIdMesh): x, y, слой и номер угла квада. Всё остальное приходит
// из буферов клетки.
struct TerrainVertexInput
{
    float4 positionOS   : POSITION;
};

// Разбор вершины. Объявляет `cell` — проходу она нужна и после макроса.
#define TERRAIN_RESOLVE_CELL_VERTEX(input, output) \
    TerrainCellVertex cell = LoadTerrainCellVertex( \
        input.positionOS.xyz, TerrainCornerBase(input.positionOS.w)); \
    output.positionCS = TerrainVertexClipPosition(cell.atlasIndex, cell.positionOS); \
    output.uv = cell.uv; \
    output.subAtlasRect = cell.subAtlasRect; \
    output.tileSizeUV = cell.tileSizeUV; \
    output.worldPos = cell.worldPos; \
    output.animData = cell.animData; \
    output.packedData = cell.packedData; \
    output.glowData = cell.glowData; \
    output.geometryCornersX = cell.geometryCornersX; \
    output.geometryCornersY = cell.geometryCornersY; \
    output.uvBits = cell.uvBits; \
    output.atlasIndex = cell.atlasIndex; \
    output.isForeground = cell.layer > 0.5 ? 1.0 : 0.0;

#endif
