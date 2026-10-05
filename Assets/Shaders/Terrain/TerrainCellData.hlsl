#ifndef KERN_TERRAIN_CELL_DATA_INCLUDED
#define KERN_TERRAIN_CELL_DATA_INCLUDED

#include "TerrainGeometryContract.hlsl"

// Квад террейна из данных клетки. Раскладка битов — та же таблица, что в
// Assets/Scripts/World/Terrain/GPU/TerrainCellData.cs; меняются только вместе.
//
// Меш идентификаторов (TerrainCellIDMesh) несёт в POSITION адрес квада
// (x, y, слой: 0 фон, 1 передний план, 2 накладка дверей), а в TEXCOORD0
// угол. Здесь из клетки, её восьми соседей и
// строк их типов восстанавливаются ровно те атрибуты, что TerrainQuadBuilder
// пишет в вершину.
//
// КЛЕТКА, ushort; две клетки в uint (младшая — с чётным индексом кольца):
//    0- 7  тип переднего плана (0 — не загружена)
//    8-15  тип фона до решения слоя (0 — нет)
// Узлы сетки (углы клетки) шейдер считает сам по типам клеток вокруг узла.
//
// ТИП, два uint4:
//   a.x/a.y  прямоугольник атласа (4 half)
//   a.z      размер тайла, число кадров (half, half)
//   a.w      высота кадра, скорость анимации (half, half)
//   b.x      цвет света RGB 0-23, слот атласа 24-31
//   b.y      доля свечения (float)
//   b.z      тип анимации 0-7, профиль 8-15, светится 16, твёрдый 17,
//            масса 18, скругление 19, Cause 20, Block 21, Empty 22,
//            есть прямоугольник атласа 23; палитра 24-31
//   b.w      семья декали 0-1, тайлгруппа есть 2, стена пака 3, угол пака 4,
//            непрозрачен в своём атласе 5, хоть в одном 6; текстура полотном 8;
//            рельефная группа 16-23; тайлгруппа 24-31

struct TerrainTypeRow
{
    uint4 a;
    uint4 b;
};

StructuredBuffer<uint> _TerrainCells;
StructuredBuffer<TerrainTypeRow> _TerrainTypes;
// TileBitmaskConverter: маска восьми соседей → дескриптор автотайла,
// четыре байта на uint.
StructuredBuffer<uint> _TerrainTileDescriptors;

// b.z
static const uint KERN_TERRAIN_TYPE_GLOWING = 1u << 16;
static const uint KERN_TERRAIN_TYPE_SOLID = 1u << 17;
static const uint KERN_TERRAIN_TYPE_ROUNDABLE = 1u << 19;
static const uint KERN_TERRAIN_TYPE_CAUSE = 1u << 20;
static const uint KERN_TERRAIN_TYPE_BLOCK = 1u << 21;
static const uint KERN_TERRAIN_TYPE_EMPTY = 1u << 22;
static const uint KERN_TERRAIN_TYPE_ATLAS_RECT = 1u << 23;

// b.w
static const uint KERN_TERRAIN_TYPE_TILE_GROUP = 1u << 2;
static const uint KERN_TERRAIN_TYPE_WALL = 1u << 3;
static const uint KERN_TERRAIN_TYPE_CORNER = 1u << 4;
static const uint KERN_TERRAIN_TYPE_OPAQUE_OWN = 1u << 5;
static const uint KERN_TERRAIN_TYPE_OPAQUE_ANY = 1u << 6;
static const uint KERN_TERRAIN_TYPE_SHEET = 1u << 8;
static const uint KERN_TERRAIN_WALL_BASE_COLUMN = 8u;

// Соседи в порядке битов маски автотайла (TileBitmaskConverter):
// L, BL, B, BR, R, TR, T, TL.
static const int KERN_TERRAIN_LEFT = 0;
static const int KERN_TERRAIN_BOTTOM_LEFT = 1;
static const int KERN_TERRAIN_BOTTOM = 2;
static const int KERN_TERRAIN_BOTTOM_RIGHT = 3;
static const int KERN_TERRAIN_RIGHT = 4;
static const int KERN_TERRAIN_TOP_RIGHT = 5;
static const int KERN_TERRAIN_TOP = 6;
static const int KERN_TERRAIN_TOP_LEFT = 7;
static const int KERN_TERRAIN_NEIGHBOUR_X[8] = { -1, -1, 0, 1, 1, 1, 0, -1 };
static const int KERN_TERRAIN_NEIGHBOUR_Y[8] = { 0, -1, -1, -1, 0, 1, 1, 1 };

// x, y — размер кольца клеток (окно и кайма по клетке с каждой стороны);
// z — размер клетки в мире.
float4 _TerrainCellGridSize;

// x, y — мировая клетка локального (0, 0) окна; z, w — высота и ширина мира
// в клетках.
// Клетки лежат по кольцевому адресу: мировая координата по модулю размера
// кольца.
float4 _TerrainCellOrigin;

// Искажение: 0 — выключено, 1 — классика, 2 — органика; константы —
// TerrainCellData.PackDistortion (раскладка там же). Зёрна органических
// рёбер — TerrainConfigHolder.OrganicEdge*Seed.
int _TerrainDistortionMode;
float4 _TerrainDistortion[8];
int _TerrainOrganicHorizontalSeed;
int _TerrainOrganicVerticalSeed;

// Правила декалей земли и камня (TerrainCellData.PackDecal): процент 0-6,
// зерно 7-14, атлас камня 15.
int _TerrainGroundDecalRule;
int _TerrainRockDecalRule;

// Начало рисуемого окна внутри сетки. Экран рисует меш размером с видимое
// окно, поле материалов — меш всей сетки со смещением ноль.
float4 _TerrainCellViewOffset;
float2 _TerrainGeometryCarrierPaddingWorld;
int _TerrainDebugBackgroundTileIdentity;

int TerrainRing(int value, int size)
{
    int quotient = value / size;
    int remainder = value - quotient * size;
    return remainder < 0 ? remainder + size : remainder;
}

uint TerrainLoadCell(int gridX, int unityY)
{
    int width = (int)round(_TerrainCellGridSize.x);
    int height = (int)round(_TerrainCellGridSize.y);
    uint index = (uint)(TerrainRing(unityY, height) * width + TerrainRing(gridX, width));
    return (_TerrainCells[index >> 1] >> ((index & 1u) * 16u)) & 0xFFFFu;
}

float TerrainLowHalf(uint packed)
{
    return f16tof32(packed & 0xFFFFu);
}

float TerrainHighHalf(uint packed)
{
    return f16tof32(packed >> 16);
}

uint TerrainForegroundType(uint cell)
{
    return cell & 0xFFu;
}

uint TerrainBackgroundType(uint cell)
{
    return (cell >> 8) & 0xFFu;
}

// Признаки типа, нужные соседям: x — флаги (b.z), y — соседство (b.w).
// Одно чтение на клетку; у строки 0 оба нуля, поэтому незагруженная клетка
// не проходит ни одной проверки ниже.
uint2 TerrainTypeTraits(uint type)
{
    return _TerrainTypes[type].b.zw;
}

uint TerrainTileDescriptor(uint mask)
{
    return (_TerrainTileDescriptors[mask >> 2] >> ((mask & 3u) * 8u)) & 0xFFu;
}

// Как TerrainCellMaskCalculator.CalculateTilingDescriptor и
// TerrainBackgroundTileResolver: соседи той же тайлгруппы → дескриптор.
// neighbourhoods — соседство (b.w) соседей того же слоя.
uint TerrainTilingDescriptor(uint ownNeighbourhood, uint neighbourhoods[8])
{
    if ((ownNeighbourhood & KERN_TERRAIN_TYPE_TILE_GROUP) == 0u)
    {
        return 0u;
    }

    uint group = ownNeighbourhood >> 24;
    uint mask = 0u;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        uint other = neighbourhoods[i];
        if ((other & KERN_TERRAIN_TYPE_TILE_GROUP) != 0u && (other >> 24) == group)
        {
            mask |= 1u << i;
        }
    }

    return TerrainTileDescriptor(mask);
}

// Как TerrainQuadBuilder.ResolveBuildingWallVariant: колонка по числу
// примыкающих углов пака, отражения — из собственного дескриптора.
uint TerrainBuildingWallVariant(uint descriptor, uint cornerSideMask)
{
    bool hasLeft = (cornerSideMask & 1u) != 0u;
    bool hasRight = (cornerSideMask & 2u) != 0u;
    bool hasTop = (cornerSideMask & 4u) != 0u;
    bool hasBottom = (cornerSideMask & 8u) != 0u;
    uint cornerCount = (hasLeft ? 1u : 0u) + (hasRight ? 1u : 0u) + (hasTop ? 1u : 0u) + (hasBottom ? 1u : 0u);
    uint column = KERN_TERRAIN_WALL_BASE_COLUMN + (cornerCount < 2u ? cornerCount : 2u);
    uint transforms = descriptor & 0xE0u;
    if (cornerCount == 1u && (hasRight || hasBottom))
    {
        transforms ^= 0x40u;
    }

    if (cornerCount >= 2u && !hasLeft && !hasRight)
    {
        transforms ^= 0x80u;
    }

    return transforms | (column & 0x1Fu);
}

// Как TerrainCellMaskCalculator.SameReliefSurface: одна масса — одна группа
// каймы; группа 0 каймы не имеет.
bool TerrainSameRelief(uint own, uint other)
{
    uint otherGroup = (other >> 16) & 0xFFu;
    return otherGroup != 0u && ((own >> 16) & 0xFFu) == otherGroup;
}

// Как TerrainQuadBuilder.IsOrganicEmptyEdge.
bool TerrainOrganicEmptyEdge(uint flags)
{
    return (flags & KERN_TERRAIN_TYPE_EMPTY) != 0u &&
        (flags & (KERN_TERRAIN_TYPE_BLOCK | KERN_TERRAIN_TYPE_CAUSE)) == 0u;
}

// Как TerrainForegroundOcclusion.IsSolidMassCell (до переноса в шейдер):
// загруженная искажающая нескруглённая клетка, непрозрачная в атласе.
bool TerrainSolidMassCell(uint2 traits)
{
    return (traits.x & KERN_TERRAIN_TYPE_CAUSE) != 0u &&
        (traits.x & KERN_TERRAIN_TYPE_ROUNDABLE) == 0u &&
        (traits.y & KERN_TERRAIN_TYPE_OPAQUE_ANY) != 0u;
}

// Углы UV квада после отражений и поворота автотайла — как
// TerrainQuadUvs.Transform: отражения по осям, затем поворот на четверть.
// Биты угла i: (2i) — u, (2i+1) — v.
uint TerrainCornerUvBits(uint descriptor, bool tiling)
{
    uint transforms = tiling ? descriptor : 0u;
    uint flipU = (transforms >> 6) & 1u;
    uint flipV = (transforms >> 5) & 1u;
    uint turn = (transforms >> 7) & 1u;
    uint bits = 0u;
    [unroll]
    for (uint corner = 0u; corner < 4u; corner++)
    {
        uint source = (corner + turn) & 3u;
        uint u = ((source == 1u || source == 2u) ? 1u : 0u) ^ flipU;
        uint v = (source >= 2u ? 1u : 0u) ^ flipV;
        bits |= (u | (v << 1)) << (corner * 2u);
    }

    return bits;
}

// Декаль — тем же хэшем, что TerrainDecalCatalog.Place.
float TerrainDecalPlacement(uint rule, int worldX, int serverY)
{
    uint percent = rule & 0x7Fu;
    if (percent == 0u)
    {
        return 0.0;
    }

    uint hash = asuint(worldX) * 374761393u;
    hash += asuint(serverY) * 668265263u;
    hash ^= ((rule >> 7) & 0xFFu) * 2246822519u;
    hash = (hash ^ (hash >> 13)) * 1274126177u;
    hash ^= hash >> 16;
    if (hash % 100u >= percent)
    {
        return 0.0;
    }

    uint packed = 1u + hash % 16u +
        (((hash >> 8) & 3u) << 4) +
        (((hash >> 10) & 1u) << 6) +
        (((hash >> 12) & 3u) << 7) +
        (((hash >> 14) & 3u) << 9);
    return (float)((rule & 0x8000u) != 0u ? packed | 4096u : packed);
}

// Значение после записи в half вершины (TerrainVertex.H): мантисса
// обрезается до 10 бит, слишком малое становится нулём.
float TerrainTruncateToHalf(float value)
{
    uint bits = asuint(value);
    int exponent = (int)((bits >> 23) & 0xFFu) - 127 + 15;
    if (exponent <= 0)
    {
        return asfloat(bits & 0x80000000u);
    }

    return asfloat(bits & 0xFFFFE000u);
}

// H(k / 1000f) без деления float: деление на GPU округляется не так, как на
// CPU, а целое — одинаково везде. s — наименьший сдвиг, при котором
// k·2^s ≥ 1024·1000; мантисса m = ⌊k·2^s / 1000⌋ ∈ [1024, 2047]; значение
// m·2^-s. Округление float до 24 бит перед обрезкой не переходит границу
// 10-битной мантиссы (остаток кратен 1/1000 её шага), поэтому равенство
// с H(k / 1000f) точное.
float TerrainThousandthsAsHalf(uint k)
{
    if (k == 0u)
    {
        return 0.0;
    }

    uint shift = 8u;
    [loop]
    while (shift < 20u && (k << shift) < 1024000u)
    {
        shift++;
    }

    uint mantissa = (k << shift) / 1000u;
    return (float)mantissa * asfloat((127u - shift) << 23);
}

// Как TerrainQuadBuilder.HashCell.
uint TerrainCellHash(int gridX, int serverY)
{
    uint seed = asuint(gridX) * 374761397u + asuint(serverY) * 668265263u;
    seed = (seed ^ (seed >> 13)) * 1274126177u;
    return seed ^ (seed >> 16);
}

// Фаза анимации — как TerrainQuadBuilder.ResolveAnimationOffset.
float TerrainAnimationPhase(uint flags, int gridX, int serverY)
{
    float palette = (float)(flags >> 24);
    if ((flags & KERN_TERRAIN_TYPE_ATLAS_RECT) == 0u)
    {
        return palette;
    }

    uint animation = flags & 0xFFu;
    uint profile = (flags >> 8) & 0xFFu;
    if (profile == 0u && animation == 1u)
    {
        return TerrainThousandthsAsHalf(TerrainCellHash(gridX, serverY) % 6283u);
    }

    if (profile == 3u)
    {
        return TerrainTruncateToHalf(
            (float)(TerrainCellHash(gridX, serverY) & 0xFFFFu) * (1.0 / 65536.0));
    }

    return palette;
}

// 24-битный хэш решётки — как TerrainVertexDistortionCalculator.Hash.
uint TerrainHash24(int x, int y, uint seed)
{
    uint hash = (asuint(x) * 0x9E3779B9u) ^ (asuint(y) * 0x85EBCA6Bu) ^ seed;
    hash ^= hash >> 16;
    hash *= 0x7FEB352Du;
    hash ^= hash >> 15;
    hash *= 0x846CA68Bu;
    hash ^= hash >> 16;
    return hash & 0x00FFFFFFu;
}

// Как TerrainVertexDistortionCalculator.ComputeOrganicEdgeBend: −2..2.
int TerrainOrganicEdgeBend(int edgeX, int edgeY, bool vertical)
{
    uint seed = asuint(vertical ? _TerrainOrganicVerticalSeed : _TerrainOrganicHorizontalSeed);
    return (int)min((TerrainHash24(edgeX, edgeY, seed) * 5u) >> 24, 4u) - 2;
}

// Классический джиттер — как RandXd (row 1) и RandYd (row 2). Произведение
// переполняется так же, как int в C#: умножение в uint.
int TerrainClassicJitter(int x, int y, int row)
{
    float4 k = _TerrainDistortion[row];
    int a = (int)k.x * x + (int)k.y * y;
    int b = (int)k.z * x + (int)k.w * y;
    int product = asint(asuint(a) * asuint(b));
    int number = product % (int)_TerrainDistortion[3][row - 1];
    return (number * number) % (int)_TerrainDistortion[0].y;
}

// Билинейный шум периода p со сглаживанием t²(3 − 2t) в целых — как
// TerrainVertexDistortionCalculator.ValueNoise. Переполнение uint в
// промежуточных разностях взаимно гасится: итог укладывается в 32 бита.
uint TerrainValueNoise(int worldX, int worldY, uint period, uint seed, uint shift)
{
    int x0 = worldX / (int)period;
    int y0 = worldY / (int)period;
    uint rx = (uint)(worldX % (int)period);
    uint ry = (uint)(worldY % (int)period);
    uint d = period * period * period;
    uint sx = rx * rx * (3u * period - 2u * rx);
    uint sy = ry * ry * (3u * period - 2u * ry);
    uint h00 = TerrainHash24(x0, y0, seed) >> shift;
    uint h10 = TerrainHash24(x0 + 1, y0, seed) >> shift;
    uint h01 = TerrainHash24(x0, y0 + 1, seed) >> shift;
    uint h11 = TerrainHash24(x0 + 1, y0 + 1, seed) >> shift;
    uint bottom = h00 * d + (h10 - h00) * sx;
    uint top = h01 * d + (h11 - h01) * sx;
    uint value = bottom * d + (top - bottom) * sy;
    return ((value << 1) + (d * d) / 2u) / (d * d);
}

// Смещение органики в единицах узла — как OrganicOffsetUnits.
int TerrainOrganicUnits(int worldX, int worldY, float4 seeds)
{
    float4 periods = _TerrainDistortion[4];
    float4 weights = _TerrainDistortion[5];
    uint hashBits = (uint)_TerrainDistortion[6].w;
    uint shift = 24u - hashBits;
    int scale = (int)(((1u << hashBits) - 1u) << 1);
    int noise =
        (int)weights.x * (int)TerrainValueNoise(worldX, worldY, (uint)periods.x, (uint)seeds.x, shift) +
        (int)weights.y * (int)TerrainValueNoise(worldX, worldY, (uint)periods.y, (uint)seeds.y, shift) +
        (int)weights.z * (int)TerrainValueNoise(worldX, worldY, (uint)periods.z, (uint)seeds.z, shift);
    int full = 100 * scale;
    int contrasted = (int)periods.w * noise - (int)weights.w * scale;
    int clamped = clamp(contrasted, 0, full);
    int maximum = (int)_TerrainDistortion[3].z;
    return (clamped * maximum + full / 2) / full;
}

// Узел в долях клетки.
float2 TerrainNodeCells(int2 units)
{
    return float2(units.x, units.y) * (1.0 / 256.0);
}

// Узел сетки — левый нижний угол клетки (nodeX, nodeY) — в единицах 1/256
// клетки по флагам передних типов клеток вокруг узла: как
// TerrainVertexDistortionCalculator.ComputeNode.
int2 TerrainNode(int nodeX, int nodeY, uint ftl, uint ftr, uint fbl, uint fbr)
{
    int worldX = nodeX - 1;
    int worldY = nodeY - 1;
    int worldWidth = (int)round(_TerrainCellOrigin.w);
    int worldHeight = (int)round(_TerrainCellOrigin.z);
    if (_TerrainDistortionMode == 0 ||
        worldX <= 0 || worldX >= worldWidth || worldY <= 0 || worldY >= worldHeight)
    {
        return int2(0, 0);
    }

    bool ctl = (ftl & KERN_TERRAIN_TYPE_CAUSE) != 0u;
    bool ctr = (ftr & KERN_TERRAIN_TYPE_CAUSE) != 0u;
    bool cbl = (fbl & KERN_TERRAIN_TYPE_CAUSE) != 0u;
    bool cbr = (fbr & KERN_TERRAIN_TYPE_CAUSE) != 0u;
    bool allCause = ctl && ctr && cbl && cbr;
    int rx;
    int ry;
    int center;
    if (_TerrainDistortionMode == 2)
    {
        if (!allCause)
        {
            return int2(0, 0);
        }

        rx = TerrainOrganicUnits(worldX, worldY, _TerrainDistortion[6]);
        ry = TerrainOrganicUnits(worldX, worldY, _TerrainDistortion[7]);
        center = (int)_TerrainDistortion[3].w;
    }
    else
    {
        int step = (int)_TerrainDistortion[0].x;
        rx = TerrainClassicJitter(worldX, worldY, 1) * step;
        ry = TerrainClassicJitter(worldX, worldY, 2) * step;
        center = (int)_TerrainDistortion[0].z;
    }

    // Как ComputeOffsetFromJitter.
    if (allCause)
    {
        return int2(rx - center, -(ry - center));
    }

    if (((ftl | ftr | fbl | fbr) & KERN_TERRAIN_TYPE_BLOCK) != 0u ||
        (ctl && cbr) || (ctr && cbl))
    {
        return int2(0, 0);
    }

    if (ctl && ctr) return int2(0, -ry);
    if (ctl && cbl) return int2(-rx, 0);
    if (ctr && cbr) return int2(rx, 0);
    if (cbl && cbr) return int2(0, ry);
    if (ctl) return int2(-rx, -ry);
    if (ctr) return int2(rx, -ry);
    if (cbl) return int2(-rx, ry);
    if (cbr) return int2(rx, ry);
    return int2(0, 0);
}

// Как TerrainQuadBuilder.OrganicEdgeBend: сосед по ребру — по флагам его
// типа (у незагруженного они нулевые, и ребро не гнётся).
int TerrainOrganicNeighbourBend(uint flags, int edgeX, int edgeY, bool vertical, int inwardSign)
{
    bool cause = (flags & KERN_TERRAIN_TYPE_CAUSE) != 0u;
    bool block = (flags & KERN_TERRAIN_TYPE_BLOCK) != 0u;
    bool emptyEdge = (flags & KERN_TERRAIN_TYPE_EMPTY) != 0u && !block && !cause;
    if (block || (!cause && !emptyEdge))
    {
        return 0;
    }

    int bend = TerrainOrganicEdgeBend(edgeX, edgeY, vertical);
    return cause ? bend : min(abs(bend), 1) * inwardSign;
}

struct TerrainCellVertex
{
    float3 positionOS;
    float2 uv;
    float4 subAtlasRect;
    float4 tileSizeUV;
    float4 worldPos;
    float4 animData;
    float4 packedData;
    float4 glowData;
    float4 geometryCornersX;
    float4 geometryCornersY;
    float uvBits;
    float atlasIndex;
    float layer;
};

// Номер угла квада из меша идентификаторов (0..3, против часовой стрелки
// от левого нижнего) — в угол клетки.
float2 TerrainCornerBase(float corner)
{
    int index = (int)round(corner);
    return float2(index == 1 || index == 2 ? 1.0 : 0.0, index >= 2 ? 1.0 : 0.0);
}

TerrainCellVertex LoadTerrainCellVertex(
    float3 address,
    float2 cornerBase)
{
    TerrainCellVertex v = (TerrainCellVertex)0;
    // Слой 2 — накладка дверей: передний план, адрес уже в сетке, смещение
    // видимого окна к нему не прибавляется (меш накладки собран по сетке).
    int layer = (int)round(address.z);
    bool overlay = layer == 2;
    int x = (int)round(address.x) + (overlay ? 0 : (int)round(_TerrainCellViewOffset.x));
    int y = (int)round(address.y) + (overlay ? 0 : (int)round(_TerrainCellViewOffset.y));
    layer = overlay ? 1 : layer;
    int2 cornerStep = (int2)round(cornerBase);
    int corner = cornerStep.y == 0
        ? (cornerStep.x == 0 ? 0 : 1)
        : (cornerStep.x == 1 ? 2 : 3);
    int2 origin = (int2)round(_TerrainCellOrigin.xy);
    int gridX = origin.x + x;
    int unityY = origin.y + y;
    bool foreground = layer > 0;
    v.layer = layer;
    v.atlasIndex = -1.0;

    // Незагруженная клетка не рисует ни одного слоя.
    uint cell = TerrainLoadCell(gridX, unityY);
    uint foregroundType = TerrainForegroundType(cell);
    if (foregroundType == 0u)
    {
        return v;
    }

    // Соседи и признаки их передних типов читаются ровно по разу: всё
    // ниже — узлы, маски, рельеф, перекрытие, изгибы — берёт их отсюда.
    uint neighbours[8];
    uint2 neighbourTraits[8];
    [unroll]
    for (int n = 0; n < 8; n++)
    {
        neighbours[n] = TerrainLoadCell(gridX + KERN_TERRAIN_NEIGHBOUR_X[n], unityY + KERN_TERRAIN_NEIGHBOUR_Y[n]);
        neighbourTraits[n] = TerrainTypeTraits(TerrainForegroundType(neighbours[n]));
    }

    uint2 foregroundTraits = TerrainTypeTraits(foregroundType);
    uint foregroundFlags = foregroundTraits.x;
    bool foregroundEmpty = (foregroundFlags & KERN_TERRAIN_TYPE_EMPTY) != 0u;
    bool foregroundCause = (foregroundFlags & KERN_TERRAIN_TYPE_CAUSE) != 0u;
    bool foregroundRoundable = (foregroundFlags & KERN_TERRAIN_TYPE_ROUNDABLE) != 0u;
    // Органика — у искажающей клетки при стиле Organic
    // (TerrainQuadBuilder.organicTerrain).
    bool organicTerrain = _TerrainDistortionMode == 2 && foregroundCause;

    // Какой тип рисует слой — как TerrainCellLayers.TryGetType. Пустота
    // принадлежит фону; фон того же типа под клеткой, закрывающей её
    // целиком, не виден ни в одном пикселе и не рисуется. Скруглённая клетка
    // и органический край у пустоты клетку целиком не закрывают.
    uint typeIndex;
    bool backgroundDrawn = false;
    if (foreground)
    {
        typeIndex = foregroundEmpty ? 0u : foregroundType;
    }
    else
    {
        typeIndex = TerrainBackgroundType(cell);
        bool needsOrganicUnderlay = organicTerrain && (
            TerrainOrganicEmptyEdge(neighbourTraits[KERN_TERRAIN_BOTTOM].x) ||
            TerrainOrganicEmptyEdge(neighbourTraits[KERN_TERRAIN_RIGHT].x) ||
            TerrainOrganicEmptyEdge(neighbourTraits[KERN_TERRAIN_TOP].x) ||
            TerrainOrganicEmptyEdge(neighbourTraits[KERN_TERRAIN_LEFT].x));
        bool foregroundFillsCell = !foregroundRoundable && !needsOrganicUnderlay;
        backgroundDrawn = foregroundEmpty ||
            (typeIndex != 0u && (typeIndex != foregroundType || !foregroundFillsCell));
        if (!backgroundDrawn)
        {
            typeIndex = 0u;
        }
    }

    if (typeIndex == 0u)
    {
        return v;
    }

    TerrainTypeRow type = _TerrainTypes[typeIndex];
    uint flags = type.b.z;
    uint neighbourhood = type.b.w;

    // Геометрия — только у искажающего (Cause) переднего плана. Углы — узлы
    // клетки и её соседей справа, сверху и справа-сверху; органические рёбра
    // — от типов четырёх соседей и хэша ребра.
    float4 geometryX = float4(0.0, 1.0, 1.0, 0.0);
    float4 geometryY = float4(0.0, 0.0, 1.0, 1.0);
    if (foregroundCause)
    {
        // Узел — левый нижний угол клетки; клетки вокруг него: tl, tr, bl, br.
        uint left = neighbourTraits[KERN_TERRAIN_LEFT].x;
        uint right = neighbourTraits[KERN_TERRAIN_RIGHT].x;
        uint top = neighbourTraits[KERN_TERRAIN_TOP].x;
        uint bottom = neighbourTraits[KERN_TERRAIN_BOTTOM].x;
        float2 node00 = TerrainNodeCells(TerrainNode(gridX, unityY,
            left, foregroundFlags, neighbourTraits[KERN_TERRAIN_BOTTOM_LEFT].x, bottom));
        float2 node10 = TerrainNodeCells(TerrainNode(gridX + 1, unityY,
            foregroundFlags, right, bottom, neighbourTraits[KERN_TERRAIN_BOTTOM_RIGHT].x));
        float2 node11 = TerrainNodeCells(TerrainNode(gridX + 1, unityY + 1,
            top, neighbourTraits[KERN_TERRAIN_TOP_RIGHT].x, foregroundFlags, right));
        float2 node01 = TerrainNodeCells(TerrainNode(gridX, unityY + 1,
            neighbourTraits[KERN_TERRAIN_TOP_LEFT].x, top, left, foregroundFlags));
        geometryX += float4(node00.x, node10.x, node11.x, node01.x);
        geometryY += float4(node00.y, node10.y, node11.y, node01.y);
    }

    bool foregroundAnchored = organicTerrain ||
        any(geometryX != float4(0.0, 1.0, 1.0, 0.0)) ||
        any(geometryY != float4(0.0, 0.0, 1.0, 1.0));

    // Фон под передним планом, закрывающим каждый пиксель клетки,
    // отбрасывается так же, как незаполненный квад (как
    // TerrainForegroundOcclusion.CoversCell до переноса в шейдер): у
    // переднего плана есть текстура, она непрозрачна в своём атласе, контур
    // не скруглён, а смещённая клетка лежит внутри сплошного массива — её
    // общие узлы закрывают прямоугольник встык с соседями.
    if (!foreground)
    {
        bool occluded = !foregroundEmpty &&
            (foregroundFlags & (KERN_TERRAIN_TYPE_ATLAS_RECT | KERN_TERRAIN_TYPE_ROUNDABLE)) == KERN_TERRAIN_TYPE_ATLAS_RECT &&
            (foregroundTraits.y & KERN_TERRAIN_TYPE_OPAQUE_OWN) != 0u;
        if (occluded && foregroundAnchored)
        {
            [unroll]
            for (int m = 0; m < 8; m++)
            {
                occluded = occluded && TerrainSolidMassCell(neighbourTraits[m]);
            }
        }

#if defined(KERN_TERRAIN_DEBUG_BACKGROUND_TILE_VIEW)
        occluded = occluded && _TerrainDebugBackgroundTileIdentity == 0;
#endif
        if (occluded)
        {
            return v;
        }
    }

    // Дескриптор автотайла: передний план — по типам соседей и стене пака,
    // фон — по фоновым типам соседей (в кайме фона нет, как и в
    // TerrainBackgroundTileResolver за окном).
    uint layerNeighbourhoods[8];
    [unroll]
    for (int t = 0; t < 8; t++)
    {
        layerNeighbourhoods[t] = foreground
            ? neighbourTraits[t].y
            : TerrainTypeTraits(TerrainBackgroundType(neighbours[t])).y;
    }

    uint descriptor = TerrainTilingDescriptor(neighbourhood, layerNeighbourhoods);
    bool tiling = (neighbourhood & KERN_TERRAIN_TYPE_TILE_GROUP) != 0u;
    if (foreground && (neighbourhood & KERN_TERRAIN_TYPE_WALL) != 0u)
    {
        uint cornerSideMask =
            ((neighbourTraits[KERN_TERRAIN_LEFT].y & KERN_TERRAIN_TYPE_CORNER) != 0u ? 1u : 0u) |
            ((neighbourTraits[KERN_TERRAIN_RIGHT].y & KERN_TERRAIN_TYPE_CORNER) != 0u ? 2u : 0u) |
            ((neighbourTraits[KERN_TERRAIN_TOP].y & KERN_TERRAIN_TYPE_CORNER) != 0u ? 4u : 0u) |
            ((neighbourTraits[KERN_TERRAIN_BOTTOM].y & KERN_TERRAIN_TYPE_CORNER) != 0u ? 8u : 0u);
        if (cornerSideMask != 0u)
        {
            descriptor = TerrainBuildingWallVariant(descriptor, cornerSideMask);
            tiling = true;
        }
    }

    uint uvBits = TerrainCornerUvBits(descriptor, tiling);
    v.uvBits = uvBits;
    v.uv = float2((uvBits >> (corner * 2)) & 1u, (uvBits >> (corner * 2 + 1)) & 1u);
    v.atlasIndex = (float)(type.b.x >> 24);

    // Мировая клетка — от адреса: высота мира переводит строку Unity в
    // серверную, как CoordinateUtils.UnityToServerY.
    int serverY = (int)round(_TerrainCellOrigin.z) - 1 - unityY;
    float tileSize = TerrainLowHalf(type.a.z);
    v.subAtlasRect = float4(
        TerrainLowHalf(type.a.x),
        TerrainHighHalf(type.a.x),
        TerrainLowHalf(type.a.y),
        TerrainHighHalf(type.a.y));
    v.tileSizeUV = float4(tileSize, tileSize, TerrainHighHalf(type.a.z), TerrainLowHalf(type.a.w));
    v.worldPos = float4(
        gridX,
        serverY,
        (float)((descriptor & 31u) | ((neighbourhood & KERN_TERRAIN_TYPE_SHEET) != 0u ? 32u : 0u)),
        tiling ? 1.0 : 0.0);
    v.animData = float4(
        (float)(flags & 0xFFu),
        TerrainHighHalf(type.a.w),
        TerrainAnimationPhase(flags, gridX, serverY),
        (float)((flags >> 8) & 0xFFu));

    // Твёрдые соседи — общие для слоёв (TerrainCellMaskRules): сверху,
    // слева, снизу, справа.
    uint solidMask =
        ((neighbourTraits[KERN_TERRAIN_TOP].x & KERN_TERRAIN_TYPE_SOLID) != 0u ? 1u : 0u) |
        ((neighbourTraits[KERN_TERRAIN_LEFT].x & KERN_TERRAIN_TYPE_SOLID) != 0u ? 2u : 0u) |
        ((neighbourTraits[KERN_TERRAIN_BOTTOM].x & KERN_TERRAIN_TYPE_SOLID) != 0u ? 4u : 0u) |
        ((neighbourTraits[KERN_TERRAIN_RIGHT].x & KERN_TERRAIN_TYPE_SOLID) != 0u ? 8u : 0u);

    // Рельеф — только у переднего плана с ненулевой рельефной группой
    // (TerrainCellMaskCalculator.CalculateReliefMasks): стороны, где сосед —
    // та же поверхность, и вогнутые углы.
    uint contour = 0u;
    if (foreground)
    {
        contour = (flags & KERN_TERRAIN_TYPE_ROUNDABLE) != 0u ? 1u : 0u;
        if (((neighbourhood >> 16) & 0xFFu) != 0u)
        {
            bool top = TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_TOP].y);
            bool left = TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_LEFT].y);
            bool bottom = TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_BOTTOM].y);
            bool right = TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_RIGHT].y);
            uint reliefMask = (top ? 1u : 0u) | (left ? 2u : 0u) | (bottom ? 4u : 0u) | (right ? 8u : 0u);
            uint reliefCorners =
                (bottom && left && !TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_BOTTOM_LEFT].y) ? 1u : 0u) |
                (bottom && right && !TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_BOTTOM_RIGHT].y) ? 2u : 0u) |
                (top && right && !TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_TOP_RIGHT].y) ? 4u : 0u) |
                (top && left && !TerrainSameRelief(neighbourhood, neighbourTraits[KERN_TERRAIN_TOP_LEFT].y) ? 8u : 0u);
            contour += (reliefMask + 1u) * 32u + (reliefCorners << 10);
        }
    }

    // Флаги света — раскладка TerrainLightingData; масса — твёрдый
    // передний план.
    uint lightingFlags = solidMask |
        ((flags & KERN_TERRAIN_TYPE_GLOWING) != 0u ? 16u : 0u) |
        (foreground && (flags & KERN_TERRAIN_TYPE_SOLID) != 0u ? 32u : 0u);
    uint decalFamily = neighbourhood & 3u;
    uint decalRule = !foreground ? (uint)_TerrainGroundDecalRule
        : decalFamily == 1u ? (uint)_TerrainGroundDecalRule
        : decalFamily == 2u ? (uint)_TerrainRockDecalRule
        : 0u;
    v.glowData = float4(
        (float)(type.b.x & 0xFFFFFFu),
        (float)lightingFlags + asfloat(type.b.y),
        (float)contour,
        TerrainDecalPlacement(decalRule, gridX, serverY));

    uint organicCode = 0u;
    if (foreground && organicTerrain)
    {
        int bottomBend = TerrainOrganicNeighbourBend(neighbourTraits[KERN_TERRAIN_BOTTOM].x, gridX, unityY, false, 1);
        int rightBend = TerrainOrganicNeighbourBend(neighbourTraits[KERN_TERRAIN_RIGHT].x, gridX + 1, unityY, true, -1);
        int topBend = TerrainOrganicNeighbourBend(neighbourTraits[KERN_TERRAIN_TOP].x, gridX, unityY + 1, false, -1);
        int leftBend = TerrainOrganicNeighbourBend(neighbourTraits[KERN_TERRAIN_LEFT].x, gridX, unityY, true, 1);
        organicCode = (uint)(1 + (bottomBend + 2) + (rightBend + 2) * 5 +
            (topBend + 2) * 25 + (leftBend + 2) * 125);
    }

    if (!foreground)
    {
        geometryX = float4(0.0, 1.0, 1.0, 0.0);
        geometryY = float4(0.0, 0.0, 1.0, 1.0);
    }

    bool anchored = foreground && foregroundAnchored;
    float organicEdges = (float)organicCode;
    bool organic = organicCode != 0u;
    // Rasterize a carrier enclosing the ENTIRE pixel silhouette. Rasterizing
    // the displaced polygon first loses fragments on the outward half of every
    // staircase; fragment clipping cannot bring those fragments back.
    // Corners are cell-local, so the interpolant and POSITION use one scale.
    float2 carrierMin = float2(0.0, 0.0);
    float2 carrierMax = float2(1.0, 1.0);
    if (anchored)
    {
        float2 firstCorner = TerrainGeometryCorner(geometryX, geometryY, 0);
        float2 boundsMin = firstCorner;
        float2 boundsMax = firstCorner;
        [unroll]
        for (int corner = 1; corner < KERN_TERRAIN_ORGANIC_EDGE_COUNT; corner++)
        {
            float2 finalCorner = TerrainGeometryCorner(geometryX, geometryY, corner);
            boundsMin = min(boundsMin, finalCorner);
            boundsMax = max(boundsMax, finalCorner);
        }

        // The carrier bounds use the exact same corner and bend points as the
        // fragment geometry predicate. It encloses the silhouette without a
        // second bend/pivot/rounding implementation.
        if (organic)
        {
            float4 bends = TerrainOrganicGeometryBends(organicEdges);
            [unroll]
            for (int side = 0; side < 4; side++)
            {
                float2 bend = TerrainOrganicGeometryPoint(
                    geometryX,
                    geometryY,
                    bends,
                    side * 2 + 1);
                boundsMin = min(boundsMin, bend);
                boundsMax = max(boundsMax, bend);
            }
        }
        carrierMin = boundsMin;
        carrierMax = boundsMax;
    }
    // Only the AO field extends its raster carrier for signed-distance filter
    // support. Screen and material passes cannot inherit this global state.
    float2 carrierPadding = float2(0.0, 0.0);
#if defined(KERN_TERRAIN_AO_FIELD)
    carrierPadding = max(_TerrainGeometryCarrierPaddingWorld, float2(0.0, 0.0)) /
        max(_TerrainCellGridSize.z, 0.0001);
#endif
    carrierMin -= carrierPadding;
    carrierMax += carrierPadding;
    float2 carrierCorner = lerp(carrierMin, carrierMax, cornerBase);
    v.packedData = float4(anchored ? 1.0 : 0.0, carrierCorner, organicEdges);
    v.geometryCornersX = anchored
        ? geometryX
        : float4(0.0, 1.0, 1.0, 0.0);
    v.geometryCornersY = anchored
        ? geometryY
        : float4(0.0, 0.0, 1.0, 1.0);
    float cellSize = _TerrainCellGridSize.z;
    v.positionOS = float3(
        (x + carrierCorner.x) * cellSize,
        (y + carrierCorner.y) * cellSize,
        layer == 0 ? 0.1 : 0.0);
    return v;
}

// Клип-позиция, которую растеризатор отбрасывает целиком.
float4 TerrainCulledPosition()
{
    return float4(2.0, 2.0, 2.0, 1.0);
}

#endif
