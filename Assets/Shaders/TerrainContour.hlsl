#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "TerrainLightingData.hlsl"

static const float KERN_TERRAIN_FACE_GRID_SIZE = 32.0;
static const float KERN_TERRAIN_GEOMETRY_EPSILON = 0.0001;

float2 QuantizeTerrainGeometryPoint(float2 samplePosition)
{
    return (floor(samplePosition * KERN_TERRAIN_FACE_GRID_SIZE) + 0.5) /
        KERN_TERRAIN_FACE_GRID_SIZE;
}

float TerrainGeometryEdgeCross(
    float2 edgeStart,
    float2 edgeEnd,
    float2 samplePosition)
{
    float2 edge = edgeEnd - edgeStart;
    float2 toSample = samplePosition - edgeStart;
    return (edge.x * toSample.y) - (edge.y * toSample.x);
}

float2 TerrainGeometryCorner(float4 cornersX, float4 cornersY, int index)
{
    if (index == 0)
    {
        return float2(cornersX.x, cornersY.x);
    }

    if (index == 1)
    {
        return float2(cornersX.y, cornersY.y);
    }

    if (index == 2)
    {
        return float2(cornersX.z, cornersY.z);
    }

    return float2(cornersX.w, cornersY.w);
}

float TerrainGeometryCoverage(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float anchored)
{
    if (anchored < 0.5)
    {
        return 1.0;
    }

    float2 quantizedPoint = QuantizeTerrainGeometryPoint(samplePosition);
    // Quantization belongs to the sample, not to a widened edge. The previous
    // edge-length margin expanded every side by roughly one pixel and made a
    // displaced polygon look like the original smooth rasterized quad. Use a
    // fixed four-edge winding test so concave corner combinations are clipped
    // by the same quantized pixel-center rule as convex ones.
    bool inside = false;
    for (int index = 0; index < 4; index++)
    {
        float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, index);
        float2 edgeEnd = TerrainGeometryCorner(
            cornersX,
            cornersY,
            (index + 1) & 3);
        float2 edge = edgeEnd - edgeStart;
        float edgeCross = TerrainGeometryEdgeCross(
            edgeStart,
            edgeEnd,
            quantizedPoint);
        bool onEdge = abs(edgeCross) <= KERN_TERRAIN_GEOMETRY_EPSILON &&
            quantizedPoint.x >= min(edgeStart.x, edgeEnd.x) - KERN_TERRAIN_GEOMETRY_EPSILON &&
            quantizedPoint.x <= max(edgeStart.x, edgeEnd.x) + KERN_TERRAIN_GEOMETRY_EPSILON &&
            quantizedPoint.y >= min(edgeStart.y, edgeEnd.y) - KERN_TERRAIN_GEOMETRY_EPSILON &&
            quantizedPoint.y <= max(edgeStart.y, edgeEnd.y) + KERN_TERRAIN_GEOMETRY_EPSILON;
        if (onEdge)
        {
            return 1.0;
        }

        bool crossesScanline = (edgeStart.y > quantizedPoint.y) !=
            (edgeEnd.y > quantizedPoint.y);
        if (crossesScanline)
        {
            float xAtScanline = edgeStart.x +
                ((quantizedPoint.y - edgeStart.y) * edge.x / edge.y);
            if (quantizedPoint.x < xAtScanline)
            {
                inside = !inside;
            }
        }
    }

    return inside ? 1.0 : 0.0;
}

float2 QuantizeTerrainFaceUV(float2 uv)
{
    // The input can be the displaced corner coordinate. Keep it outside the
    // canonical range: clamping it would collapse a moved corner onto the
    // edge and turn a one-pixel displacement into a large flat step.
    float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
    return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
}

float EvaluateRoundableBlockAlpha(
    float2 uv,
    float packedContour,
    float packedLightingFlags,
    float antialiasScale)
{
    if (!KernTerrainIsRoundable(packedContour))
    {
        return 1.0;
    }

    uint lightingFlags = KernTerrainLightingFlags(packedLightingFlags);
    int sameMask = KernTerrainSolidBoundary(lightingFlags);
    float4 bits = frac(sameMask * float4(0.5, 0.25, 0.125, 0.0625));
    bool4 hasSame = bits >= 0.5;
    float2 p = QuantizeTerrainFaceUV(uv) - 0.5;
    float rTL = (hasSame.x || hasSame.y) ? 0.0 : 0.5;
    float rTR = (hasSame.x || hasSame.w) ? 0.0 : 0.5;
    float rBL = (hasSame.z || hasSame.y) ? 0.0 : 0.5;
    float rBR = (hasSame.z || hasSame.w) ? 0.0 : 0.5;
    float dist = length(p);
    float alpha = step(dist, 0.51);
    if (rTL < 0.25)
    {
        float fill = step(p.x, 0.0) * step(0.0, p.y);
        alpha = max(alpha, fill);
    }
    if (rTR < 0.25)
    {
        float fill = step(0.0, p.x) * step(0.0, p.y);
        alpha = max(alpha, fill);
    }
    if (rBL < 0.25)
    {
        float fill = step(p.x, 0.0) * step(p.y, 0.0);
        alpha = max(alpha, fill);
    }
    if (rBR < 0.25)
    {
        float fill = step(0.0, p.x) * step(p.y, 0.0);
        alpha = max(alpha, fill);
    }
    float cornerDist = abs(abs(p.x) - abs(p.y));
    float cornerExclude = step(0.4, cornerDist);
    return lerp(alpha, 1.0, cornerExclude);
}

// Выключатель каймы. Настройка игрока, публикуется TerrainRenderer.
float _TerrainReliefRimEnabled;

// Затухание от одной чужой грани. d — расстояние до неё в долях клетки:
// 0 на самой грани, 0.5 в середине клетки.
//
// Огибающая та же, что в оригинале: на грани множитель 0.125, к середине
// выходит в единицу, куб прижимает затемнение к краю. Разница в том, что
// считается расстояние до грани, а не сектор клетки.
float TerrainReliefRimSide(float distanceToEdge)
{
    float s = saturate(1.0 - (distanceToEdge * 2.0));
    float fall = 1.0 - (0.5 * s * s);
    return fall * fall * fall;
}

// Кайма рельефа: затемнение к тем сторонам клетки, за которыми лежит чужая
// рельефная семья. Ради неё маска и считается — без каймы кристалл и скала
// смыкаются тайлами вплотную и читаются одним пятном.
//
// Грани, а не секторы. В оригинале клетка делится диагоналями на секторы, и
// каждый сектор гасится целиком. Тогда полоса вдоль длинной границы массива
// обрывается на каждом стыке клеток: у соседней граничной клетки её сектор
// срезан той же диагональю, и в месте стыка обе полосы сходят на нет —
// кайма не тайлится. Здесь каждая чужая грань даёт своё затухание по
// расстоянию до неё, а затухания перемножаются. Вдоль грани значение
// постоянно, поэтому полоса переходит в соседнюю клетку без разрыва, а на
// углу двух чужих граней множители складываются в более тёмный угол.
//
// Перемножение — тоже из оригинала: там ветки секторов домножают уже
// затемнённый цвет, а не выбирают один из.
// Сырая форма: принимает координаты по отдельности. Вызывать её из прохода
// нельзя — для этого есть TerrainReliefRim(TerrainSurfaceInputs), который
// берёт клеточную координату из общего разбора вершины. Разделение не
// косметическое: пока проход выбирал аргумент сам, он подавал сюда UV тайла,
// а тот отражён и повёрнут вариантом тайла, и кайма садилась на встречную
// грань. Здесь остаётся только математика, и её гоняют тесты растеризации.
float TerrainReliefRimRaw(
    float2 cellSample,
    float4 cornersX,
    float4 cornersY,
    float packedContour)
{
    if (_TerrainReliefRimEnabled < 0.5)
    {
        return 1.0;
    }

    int reliefCode = KernTerrainReliefCode(packedContour);
    if (reliefCode == 0)
    {
        return 1.0;
    }

    // Код хранит маску своих соседей со сдвигом на единицу; кайме нужны
    // чужие, то есть дополнение до четырёх сторон.
    int foreignSides = (~(reliefCode - 1)) & 0x0F;
    if (foreignSides == 0)
    {
        return 1.0;
    }

    // Координата нормируется по границам самого полигона, а не зажимается в
    // единичный квадрат. У смещённой клетки на входе лежит координата
    // несущего прямоугольника, а он шире клетки на величину смещения углов;
    // зажим прибивал весь выступ к дну падения, и выступающая грань темнела
    // сильнее такой же грани ровной клетки.
    float2 boundsMin = float2(
        min(min(cornersX.x, cornersX.y), min(cornersX.z, cornersX.w)),
        min(min(cornersY.x, cornersY.y), min(cornersY.z, cornersY.w)));
    float2 boundsMax = float2(
        max(max(cornersX.x, cornersX.y), max(cornersX.z, cornersX.w)),
        max(max(cornersY.x, cornersY.y), max(cornersY.z, cornersY.w)));
    // Вырожденный полигон — это отсутствие геометрии, а не повод что-то
    // дорисовать: каймы нет. Зажим размаха в эпсилон вместо выхода давал
    // обратное — клеточная координата становилась (1,1) на каждом фрагменте,
    // и клетка гасилась целиком.
    float2 span = boundsMax - boundsMin;
    if (span.x <= KERN_TERRAIN_GEOMETRY_EPSILON ||
        span.y <= KERN_TERRAIN_GEOMETRY_EPSILON)
    {
        return 1.0;
    }

    float2 cellLocal = saturate(
        (QuantizeTerrainFaceUV(cellSample) - boundsMin) / span);

    float rim = 1.0;
    if ((foreignSides & 1) != 0)
    {
        rim *= TerrainReliefRimSide(1.0 - cellLocal.y);
    }

    if ((foreignSides & 2) != 0)
    {
        rim *= TerrainReliefRimSide(cellLocal.x);
    }

    if ((foreignSides & 4) != 0)
    {
        rim *= TerrainReliefRimSide(cellLocal.y);
    }

    if ((foreignSides & 8) != 0)
    {
        rim *= TerrainReliefRimSide(1.0 - cellLocal.x);
    }

    return rim;
}

// Разбор вершины террейна: всё, что оба прохода и отладочный вид раньше
// выводили каждый у себя.
//
// ЗАЧЕМ. Проходов два (экранный Universal2D и поле материалов), путей вершин
// тоже два (CPU-квады и GPU-клетки), и решение «что такое координата клетки»
// принималось в каждом сочетании заново. Каждый дефект каймы и силуэта в этом
// шве и сидел: ветки расходились между собой и с отладочным видом, который
// показывал четвёртое мнение. Здесь разбор один, дальше по коду ходит
// структура, и подать в кайму «не ту» координату больше нечем.
struct TerrainSurfaceInputs
{
    // Клеточная координата угла: 0..1 по клетке, у смещённой — координата
    // несущего прямоугольника. Мировая ориентация, вариантом тайла не тронута.
    float2 cellSample;

    // Координата для контура. У смещённой клетки это та же клеточная, у
    // ровной — UV тайла: скругление блока живёт в тайле и обязано ехать
    // вместе с его отражениями.
    float2 contourUV;

    float4 cornersX;
    float4 cornersY;
    float anchored;
    float packedContour;
    float packedLightingFlags;
    int animationProfile;
};

TerrainSurfaceInputs BuildTerrainSurfaceInputs(
    float4 packedData,
    float2 tileUV,
    float4 cornersX,
    float4 cornersY,
    float4 glowData,
    float packedAnimationProfile)
{
    TerrainSurfaceInputs surface;
    surface.cellSample = packedData.yz;
    surface.contourUV = packedData.x > 0.5 ? packedData.yz : tileUV;
    surface.cornersX = cornersX;
    surface.cornersY = cornersY;
    surface.anchored = packedData.x;
    surface.packedContour = glowData.z;
    surface.packedLightingFlags = glowData.y;
    surface.animationProfile = (int)(packedAnimationProfile + 0.5);
    return surface;
}

float TerrainReliefRim(TerrainSurfaceInputs surface)
{
    return TerrainReliefRimRaw(
        surface.cellSample,
        surface.cornersX,
        surface.cornersY,
        surface.packedContour);
}

// The visible terrain and the material field must use the same cell shape.
// Geometry is evaluated only for the GPU cell path; CPU overlays already carry
// the displaced polygon in POSITION and therefore do not need a second mask.
float EvaluateTerrainCellCoverage(
    TerrainSurfaceInputs surface,
    float antialiasScale,
    float applyGeometry)
{
    float geometryCoverage = applyGeometry > 0.5
        ? TerrainGeometryCoverage(
            surface.cellSample,
            surface.cornersX,
            surface.cornersY,
            surface.anchored)
        : 1.0;
    float contourCoverage = KernTerrainIsRoundable(surface.packedContour)
        ? EvaluateRoundableBlockAlpha(
            surface.contourUV,
            surface.packedContour,
            surface.packedLightingFlags,
            antialiasScale)
        : 1.0;
    return geometryCoverage * contourCoverage;
}

float TerrainCellOccupancy(float coverage)
{
    return step(0.5, coverage);
}

#endif
