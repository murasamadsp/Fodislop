#ifndef KERN_TERRAIN_CELL_DATA_INCLUDED
#define KERN_TERRAIN_CELL_DATA_INCLUDED

#include "TerrainGeometryContract.hlsl"
#include "TerrainCellFormat.hlsl"
#include "TerrainAtlasSampling.hlsl"
#include "TerrainMaterialCBuffer.hlsl"

// Квад террейна из данных клетки — только функции. Данные (раскладка,
// буферы, глобалы) — в TerrainCellFormat.hlsl.
//
// Меш идентификаторов (TerrainCellIdMesh) несёт в POSITION адрес квада
// (x, y, слой: 0 фон, 1 передний план, 2 накладка дверей) и угол. Из
// клетки, её восьми соседей и строк их типов восстанавливаются ровно те
// атрибуты, что TerrainQuadBuilder пишет в вершину.

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
    return (_TerrainCells[index / KERN_TERRAIN_CELLS_PER_WORD] >>
        ((index % KERN_TERRAIN_CELLS_PER_WORD) * KERN_TERRAIN_CELL_BITS)) & KERN_TERRAIN_CELL_TYPE_MASK;
}

// ═══ ФОРМАТ: доступ к полям ═════════════════════════════════════════════
// Только здесь поле клетки или строки типа достаётся по битам; остальной
// код зовёт эти функции. Имя функции — TerrainType + имя поля строки.

// Клетка: её тип.
uint TerrainCellType(uint cell) { return cell & KERN_TERRAIN_CELL_TYPE_MASK; }

// Строка типа читается целиком — и своя, и соседей.
TerrainTypeRow TerrainTypeRowOf(uint type) { return _TerrainTypes[type]; }

uint TerrainField(uint word, uint shift, uint mask) { return (word >> shift) & mask; }

// atlasXY, atlasWH
float4 TerrainTypeAtlasPixels(TerrainTypeRow row)
{
    return float4(
        row.atlasXY & KERN_TERRAIN_TYPE_PIXEL_MASK,
        (row.atlasXY >> KERN_TERRAIN_TYPE_PIXEL_HIGH_SHIFT) & KERN_TERRAIN_TYPE_PIXEL_MASK,
        row.atlasWH & KERN_TERRAIN_TYPE_PIXEL_MASK,
        (row.atlasWH >> KERN_TERRAIN_TYPE_PIXEL_HIGH_SHIFT) & KERN_TERRAIN_TYPE_PIXEL_MASK);
}
float TerrainTypeFrameCount(TerrainTypeRow row) { return (float)(row.atlasXY >> KERN_TERRAIN_TYPE_BYTE_SHIFT); }
uint TerrainTypeRimMass(TerrainTypeRow row) { return row.atlasWH >> KERN_TERRAIN_TYPE_BYTE_SHIFT; }

// speedGlowTile
uint TerrainTypeTileGroupCode(TerrainTypeRow row) { return row.speedGlowTile >> KERN_TERRAIN_TYPE_BYTE_SHIFT; }
bool TerrainTypeHasTileGroup(TerrainTypeRow row) { return TerrainTypeTileGroupCode(row) != 0u; }
uint TerrainTypeTileGroup(TerrainTypeRow row) { return TerrainTypeTileGroupCode(row) - 1u; }

// look
uint TerrainTypeSlot(TerrainTypeRow row) { return row.look & KERN_TERRAIN_TYPE_SLOT_MASK; }
bool TerrainTypeOpaqueOwn(TerrainTypeRow row) { return (row.look & KERN_TERRAIN_TYPE_OPAQUE_OWN) != 0u; }
bool TerrainTypeOpaqueAny(TerrainTypeRow row) { return (row.look & KERN_TERRAIN_TYPE_OPAQUE_ANY) != 0u; }
uint TerrainTypeTextureAnchor(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_TEXTURE_ANCHOR_SHIFT, KERN_TERRAIN_TYPE_TEXTURE_ANCHOR_MASK); }
uint TerrainTypeOutline(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_OUTLINE_SHIFT, KERN_TERRAIN_TYPE_OUTLINE_MASK); }
uint TerrainTypeAnimationType(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_ANIMATION_TYPE_SHIFT, KERN_TERRAIN_TYPE_ANIMATION_TYPE_MASK); }
uint TerrainTypeSurfaceEffect(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_SURFACE_EFFECT_SHIFT, KERN_TERRAIN_TYPE_SURFACE_EFFECT_MASK); }
uint TerrainTypeSurfaceEffectPalette(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_SURFACE_EFFECT_PALETTE_SHIFT, KERN_TERRAIN_TYPE_SURFACE_EFFECT_PALETTE_MASK); }
uint TerrainTypeDecalAtlas(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_DECAL_ATLAS_SHIFT, KERN_TERRAIN_TYPE_DECAL_ATLAS_MASK); }

float TerrainTypeAnimationSpeed(TerrainTypeRow row) { return f16tof32(row.speedGlowTile & KERN_TERRAIN_TYPE_ANIMATION_SPEED_MASK); }
uint TerrainTypeGlowByte(TerrainTypeRow row) { return TerrainField(row.speedGlowTile, KERN_TERRAIN_TYPE_GLOW_SHIFT, KERN_TERRAIN_TYPE_GLOW_MASK); }
float TerrainTypeGlow(TerrainTypeRow row) { return (float)TerrainTypeGlowByte(row) * (1.0 / 255.0); }

// Сравнение одного поля со значением.
// Фон (drawLayer Background или Underlay) лежит сам на себе; под передним
// планом — подложка.
bool TerrainTypeIsBackground(TerrainTypeRow row) { return (row.look & KERN_TERRAIN_TYPE_BACKGROUND) != 0u; }
uint TerrainTypeUnder(uint type, TerrainTypeRow row) { return TerrainTypeIsBackground(row) ? type : (uint)_TerrainUnderlayType; }
bool TerrainTypeGlows(TerrainTypeRow row) { return TerrainTypeGlowByte(row) != 0u; }
bool TerrainTypeWavy(TerrainTypeRow row) { return TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_WAVY; }
bool TerrainTypeHolds(TerrainTypeRow row)
{
    uint outline = TerrainTypeOutline(row);
    return outline != KERN_TERRAIN_OUTLINE_PLIANT && outline != KERN_TERRAIN_OUTLINE_WAVY;
}
bool TerrainTypeRound(TerrainTypeRow row) { return TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_ROUND; }
bool TerrainTypeWall(TerrainTypeRow row) { return TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_WALL; }
bool TerrainTypeCorner(TerrainTypeRow row) { return TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_CORNER; }

// Факты текстуры: прямоугольник, размер тайла, высота кадра.
float4 TerrainTypeAtlasRect(TerrainTypeRow row)
{
    float4 texel = TerrainMaterialAtlasTexelSize((int)TerrainTypeSlot(row));
    return TerrainTypeAtlasPixels(row) * texel.xyxy;
}
bool TerrainTypeHasAtlasRect(TerrainTypeRow row) { return (row.atlasWH & KERN_TERRAIN_TYPE_PIXEL_MASK) != 0u; }
float TerrainTypeTileSize(TerrainTypeRow row)
{
    return KERN_TERRAIN_CELL_TEXELS * TerrainMaterialAtlasTexelSize((int)TerrainTypeSlot(row)).x;
}
float TerrainTypeFrameHeight(TerrainTypeRow row)
{
    float height = TerrainTypeAtlasRect(row).w;
    return height > 0.0 ? height / TerrainTypeTileSize(row) : 1.0;
}

uint TerrainTileDescriptor(uint mask)
{
    return (_TerrainTileDescriptors[mask >> 2] >> ((mask & 3u) * 8u)) & 0xFFu;
}

// Как TerrainCellMaskCalculator.CalculateTilingDescriptor и
// TerrainBackgroundTileResolver: соседи той же тайлгруппы → дескриптор.
// neighbours — признаки соседей того же слоя.
uint TerrainTilingDescriptor(TerrainTypeRow own, TerrainTypeRow neighbours[8])
{
    if (!TerrainTypeHasTileGroup(own))
    {
        return 0u;
    }

    uint group = TerrainTypeTileGroup(own);
    uint mask = 0u;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        TerrainTypeRow other = neighbours[i];
        if (TerrainTypeHasTileGroup(other) && TerrainTypeTileGroup(other) == group)
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
    uint transforms = descriptor & KERN_TERRAIN_TILE_TRANSFORM_MASK;
    if (cornerCount == 1u && (hasRight || hasBottom))
    {
        transforms ^= 1u << KERN_TERRAIN_TILE_FLIP_U_SHIFT;
    }

    if (cornerCount >= 2u && !hasLeft && !hasRight)
    {
        transforms ^= 1u << KERN_TERRAIN_TILE_TURN_SHIFT;
    }

    return transforms | (column & KERN_TERRAIN_TILE_COLUMN_MASK);
}

// Как TerrainCellMaskCalculator.SameRimSurface: соседи одной ненулевой
// массы — одно тело без каймы; масса 0 каймы не имеет.
bool TerrainSameRim(TerrainTypeRow own, TerrainTypeRow other)
{
    uint otherMass = TerrainTypeRimMass(other);
    return otherMass != 0u && TerrainTypeRimMass(own) == otherMass;
}

// Как TerrainForegroundOcclusion.IsOpaqueMassCell (до переноса в шейдер):
// загруженная волнистая клетка, непрозрачная в атласе.
bool TerrainOpaqueMassCell(TerrainTypeRow row)
{
    return TerrainTypeWavy(row) && TerrainTypeOpaqueAny(row);
}

// Углы UV квада после отражений и поворота автотайла — как
// TerrainQuadUvs.Transform: отражения по осям, затем поворот на четверть.
// Биты угла i: (2i) — u, (2i+1) — v.
uint TerrainCornerUvBits(uint descriptor, bool tiling)
{
    uint transforms = tiling ? descriptor : 0u;
    uint flipU = (transforms >> KERN_TERRAIN_TILE_FLIP_U_SHIFT) & 1u;
    uint flipV = (transforms >> KERN_TERRAIN_TILE_FLIP_V_SHIFT) & 1u;
    uint turn = (transforms >> KERN_TERRAIN_TILE_TURN_SHIFT) & 1u;
    uint bits = 0u;
    [unroll]
    for (uint corner = 0u; corner < 4u; corner++)
    {
        uint source = (corner + turn) & 3u;
        uint u = ((source == 1u || source == 2u) ? 1u : 0u) ^ flipU;
        uint v = (source >= 2u ? 1u : 0u) ^ flipV;
        bits |= (u | (v << 1)) << (corner * KERN_TERRAIN_UV_BITS_PER_CORNER);
    }

    return bits;
}

// Декаль — тем же хэшем, что TerrainDecalCatalog.Place.
float TerrainDecalPlacement(uint rule, int worldX, int serverY)
{
    uint percent = rule & KERN_TERRAIN_DECAL_RULE_PERCENT_MASK;
    if (percent == 0u)
    {
        return 0.0;
    }

    uint hash = asuint(worldX) * 374761393u;
    hash += asuint(serverY) * 668265263u;
    hash ^= ((rule >> KERN_TERRAIN_DECAL_RULE_SEED_SHIFT) & KERN_TERRAIN_DECAL_RULE_SEED_MASK) * 2246822519u;
    hash = (hash ^ (hash >> 13)) * 1274126177u;
    hash ^= hash >> 16;
    if (hash % 100u >= percent)
    {
        return 0.0;
    }

    uint packed = 1u + hash % KERN_TERRAIN_DECAL_VARIANTS +
        (((hash >> 8) & 3u) << KERN_TERRAIN_DECAL_ROTATION_SHIFT) +
        (((hash >> 10) & 1u) << KERN_TERRAIN_DECAL_MIRROR_SHIFT) +
        (((hash >> 12) & 3u) << KERN_TERRAIN_DECAL_OFFSET_X_SHIFT) +
        (((hash >> 14) & 3u) << KERN_TERRAIN_DECAL_OFFSET_Y_SHIFT);
    return (float)((rule & KERN_TERRAIN_DECAL_RULE_ROCK) != 0u ? packed | KERN_TERRAIN_DECAL_ROCK_ATLAS : packed);
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
float TerrainAnimationPhase(TerrainTypeRow row, bool hasAtlasRect, int gridX, int serverY)
{
    float palette = (float)TerrainTypeSurfaceEffectPalette(row);
    if (!hasAtlasRect)
    {
        return palette;
    }

    if (TerrainTypeAnimationType(row) == KERN_TERRAIN_ANIMATION_TYPE_BLINKING)
    {
        return TerrainThousandthsAsHalf(TerrainCellHash(gridX, serverY) % 6283u);
    }

    if (TerrainTypeSurfaceEffect(row) == KERN_TERRAIN_SURFACE_EFFECT_FACETED)
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
int2 TerrainNode(int nodeX, int nodeY, TerrainTypeRow ftl, TerrainTypeRow ftr, TerrainTypeRow fbl, TerrainTypeRow fbr)
{
    int worldX = nodeX - 1;
    int worldY = nodeY - 1;
    int worldWidth = (int)round(_TerrainCellOrigin.w);
    int worldHeight = (int)round(_TerrainCellOrigin.z);
    if (_TerrainDistortionStyle == KERN_TERRAIN_DISTORTION_STYLE_OFF ||
        worldX <= 0 || worldX >= worldWidth || worldY <= 0 || worldY >= worldHeight)
    {
        return int2(0, 0);
    }

    bool ctl = TerrainTypeWavy(ftl);
    bool ctr = TerrainTypeWavy(ftr);
    bool cbl = TerrainTypeWavy(fbl);
    bool cbr = TerrainTypeWavy(fbr);
    bool allCause = ctl && ctr && cbl && cbr;
    int rx;
    int ry;
    int center;
    if (_TerrainDistortionStyle == KERN_TERRAIN_DISTORTION_STYLE_ORGANIC)
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

    if (TerrainTypeHolds(ftl) || TerrainTypeHolds(ftr) || TerrainTypeHolds(fbl) || TerrainTypeHolds(fbr) ||
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
int TerrainOrganicNeighbourBend(TerrainTypeRow row, int edgeX, int edgeY, bool vertical, int inwardSign)
{
    bool cause = TerrainTypeWavy(row);
    bool block = TerrainTypeHolds(row);
    bool backgroundEdge = TerrainTypeIsBackground(row) && !block && !cause;
    if (block || (!cause && !backgroundEdge))
    {
        return 0;
    }

    int bend = TerrainOrganicEdgeBend(edgeX, edgeY, vertical);
    return cause ? bend : min(abs(bend), 1) * inwardSign;
}

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
    uint foregroundType = TerrainCellType(cell);
    if (foregroundType == 0u)
    {
        return v;
    }

    // Соседи и признаки их передних типов читаются ровно по разу: всё
    // ниже — узлы, маски, кайма, перекрытие, изгибы — берёт их отсюда.
    uint neighbours[8];
    TerrainTypeRow neighbourRows[8];
    [unroll]
    for (int n = 0; n < 8; n++)
    {
        neighbours[n] = TerrainLoadCell(gridX + KERN_TERRAIN_NEIGHBOUR_X[n], unityY + KERN_TERRAIN_NEIGHBOUR_Y[n]);
        neighbourRows[n] = TerrainTypeRowOf(TerrainCellType(neighbours[n]));
    }

    TerrainTypeRow foregroundRow = TerrainTypeRowOf(foregroundType);
    bool foregroundIsBackground = TerrainTypeIsBackground(foregroundRow);
    bool foregroundCause = TerrainTypeWavy(foregroundRow);
    // Органика — у искажающей клетки при стиле Organic
    // (TerrainQuadBuilder.organicTerrain).
    bool organicTerrain = _TerrainDistortionStyle == KERN_TERRAIN_DISTORTION_STYLE_ORGANIC && foregroundCause;

    // Какой тип рисует слой — как TerrainQuadBuilder. Фон лежит сам на себе
    // и рисуется только фоном; под передним планом фоном рисуется подложка.
    uint layerType = foreground
        ? (foregroundIsBackground ? 0u : foregroundType)
        : TerrainTypeUnder(foregroundType, foregroundRow);

    if (layerType == 0u)
    {
        return v;
    }

    TerrainTypeRow row = TerrainTypeRowOf(layerType);

    // Геометрия — только у искажающего (Cause) переднего плана. Углы — узлы
    // клетки и её соседей справа, сверху и справа-сверху; органические рёбра
    // — от типов четырёх соседей и хэша ребра.
    float4 geometryX = float4(0.0, 1.0, 1.0, 0.0);
    float4 geometryY = float4(0.0, 0.0, 1.0, 1.0);
    if (foregroundCause)
    {
        // Узел — левый нижний угол клетки; клетки вокруг него: tl, tr, bl, br.
        TerrainTypeRow left = neighbourRows[KERN_TERRAIN_LEFT];
        TerrainTypeRow right = neighbourRows[KERN_TERRAIN_RIGHT];
        TerrainTypeRow top = neighbourRows[KERN_TERRAIN_TOP];
        TerrainTypeRow bottom = neighbourRows[KERN_TERRAIN_BOTTOM];
        float2 node00 = TerrainNodeCells(TerrainNode(gridX, unityY,
            left, foregroundRow, neighbourRows[KERN_TERRAIN_BOTTOM_LEFT], bottom));
        float2 node10 = TerrainNodeCells(TerrainNode(gridX + 1, unityY,
            foregroundRow, right, bottom, neighbourRows[KERN_TERRAIN_BOTTOM_RIGHT]));
        float2 node11 = TerrainNodeCells(TerrainNode(gridX + 1, unityY + 1,
            top, neighbourRows[KERN_TERRAIN_TOP_RIGHT], foregroundRow, right));
        float2 node01 = TerrainNodeCells(TerrainNode(gridX, unityY + 1,
            neighbourRows[KERN_TERRAIN_TOP_LEFT], top, left, foregroundRow));
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
    // не скруглён, а смещённая клетка лежит внутри массы — её
    // общие узлы закрывают прямоугольник встык с соседями.
    if (!foreground)
    {
        bool occluded = !foregroundIsBackground &&
            TerrainTypeHasAtlasRect(foregroundRow) &&
            !TerrainTypeRound(foregroundRow) &&
            TerrainTypeOpaqueOwn(foregroundRow);
        if (occluded && foregroundAnchored)
        {
            [unroll]
            for (int m = 0; m < 8; m++)
            {
                occluded = occluded && TerrainOpaqueMassCell(neighbourRows[m]);
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
    TerrainTypeRow layerRows[8];
    [unroll]
    for (int t = 0; t < 8; t++)
    {
        if (foreground)
        {
            layerRows[t] = neighbourRows[t];
        }
        else
        {
            layerRows[t] = TerrainTypeRowOf(TerrainTypeUnder(TerrainCellType(neighbours[t]), neighbourRows[t]));
        }
    }

    uint descriptor = TerrainTilingDescriptor(row, layerRows);
    bool tiling = TerrainTypeHasTileGroup(row);
    if (foreground && TerrainTypeWall(row))
    {
        uint cornerSideMask =
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_LEFT]) ? 1u : 0u) |
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_RIGHT]) ? 2u : 0u) |
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_TOP]) ? 4u : 0u) |
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_BOTTOM]) ? 8u : 0u);
        if (cornerSideMask != 0u)
        {
            descriptor = TerrainBuildingWallVariant(descriptor, cornerSideMask);
            tiling = true;
        }
    }

    uint uvBits = TerrainCornerUvBits(descriptor, tiling);
    v.uvBits = uvBits;
    v.uv = float2(
        (uvBits >> (corner * KERN_TERRAIN_UV_BITS_PER_CORNER)) & 1u,
        (uvBits >> (corner * KERN_TERRAIN_UV_BITS_PER_CORNER + 1)) & 1u);
    v.atlasIndex = (float)TerrainTypeSlot(row);

    // Мировая клетка — от адреса: высота мира переводит строку Unity в
    // серверную, как CoordinateUtils.UnityToServerY.
    int serverY = (int)round(_TerrainCellOrigin.z) - 1 - unityY;
    float tileSize = TerrainTypeTileSize(row);
    v.subAtlasRect = TerrainTypeAtlasRect(row);
    v.tileSizeUV = float4(tileSize, tileSize, TerrainTypeFrameCount(row), TerrainTypeFrameHeight(row));
    v.worldPos = float4(
        gridX,
        serverY,
        (float)((descriptor & KERN_TERRAIN_COLUMN_MASK) | (TerrainTypeTextureAnchor(row) == KERN_TERRAIN_TEXTURE_ANCHOR_WORLD ? KERN_TERRAIN_COLUMN_WORLD_TEXTURE_ANCHOR : 0u)),
        tiling ? 1.0 : 0.0);
    v.animData = float4(
        (float)TerrainTypeAnimationType(row),
        TerrainTypeAnimationSpeed(row),
        TerrainAnimationPhase(row, TerrainTypeHasAtlasRect(row), gridX, serverY),
        (float)TerrainTypeSurfaceEffect(row));

    // Соседи-блоки — общие для слоёв (TerrainCellMaskRules): сверху,
    // слева, снизу, справа.
    uint foregroundSides =
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_TOP]) ? 0u : 1u) |
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_LEFT]) ? 0u : 2u) |
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_BOTTOM]) ? 0u : 4u) |
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_RIGHT]) ? 0u : 8u);

    // Кайма — только у переднего плана с ненулевой группой каймы
    // (TerrainCellMaskCalculator.CalculateRimMasks): стороны, где сосед —
    // та же поверхность, и вогнутые углы.
    uint contour = 0u;
    if (foreground)
    {
        contour = TerrainTypeRound(row) ? KERN_TERRAIN_ROUNDABLE_CONTOUR_FLAG : 0u;
        if (TerrainTypeRimMass(row) != 0u)
        {
            bool top = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_TOP]);
            bool left = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_LEFT]);
            bool bottom = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_BOTTOM]);
            bool right = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_RIGHT]);
            uint rimMask = (top ? 1u : 0u) | (left ? 2u : 0u) | (bottom ? 4u : 0u) | (right ? 8u : 0u);
            uint rimCorners =
                (bottom && left && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_BOTTOM_LEFT]) ? 1u : 0u) |
                (bottom && right && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_BOTTOM_RIGHT]) ? 2u : 0u) |
                (top && right && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_TOP_RIGHT]) ? 4u : 0u) |
                (top && left && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_TOP_LEFT]) ? 8u : 0u);
            contour += ((rimMask + 1u) << KERN_TERRAIN_RIM_SHIFT) +
                (rimCorners << KERN_TERRAIN_RIM_CORNERS_SHIFT);
        }
    }

    // Флаги света — раскладка TerrainLightingData; масса — блок
    // передний план.
    uint lightingFlags = foregroundSides |
        (TerrainTypeGlows(row) ? KERN_TERRAIN_GLOW_FLAG : 0u) |
        (foreground && !TerrainTypeIsBackground(row) ? KERN_TERRAIN_PHYSICAL_MASS_FLAG : 0u);
    uint decalAtlas = TerrainTypeDecalAtlas(row);
    uint decalRule = decalAtlas == KERN_TERRAIN_DECAL_ATLAS_GROUND ? (uint)_TerrainGroundDecalRule
        : decalAtlas == KERN_TERRAIN_DECAL_ATLAS_ROCK ? (uint)_TerrainRockDecalRule
        : 0u;
    v.lightContourDecal = float4(
        0.0,
        (float)lightingFlags + TerrainTypeGlow(row) * KERN_TERRAIN_GLOW_FRACTION_SCALE,
        (float)contour,
        TerrainDecalPlacement(decalRule, gridX, serverY));

    uint organicCode = 0u;
    if (foreground && organicTerrain)
    {
        int bottomBend = TerrainOrganicNeighbourBend(neighbourRows[KERN_TERRAIN_BOTTOM], gridX, unityY, false, 1);
        int rightBend = TerrainOrganicNeighbourBend(neighbourRows[KERN_TERRAIN_RIGHT], gridX + 1, unityY, true, -1);
        int topBend = TerrainOrganicNeighbourBend(neighbourRows[KERN_TERRAIN_TOP], gridX, unityY + 1, false, -1);
        int leftBend = TerrainOrganicNeighbourBend(neighbourRows[KERN_TERRAIN_LEFT], gridX, unityY, true, 1);
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
