#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "TerrainLightingData.hlsl"

static const float KERN_TERRAIN_FACE_GRID_SIZE = 32.0;
static const float KERN_TERRAIN_GEOMETRY_EPSILON = 0.0001;
// sqrt(2): углы клетки уезжают на ±1/√2, как в оригинале.
static const float KERN_TERRAIN_RELIEF_RIM_SCALE = 1.41421356;

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

// Кайма рельефа: затемнение к той стороне клетки, за которой лежит чужая
// рельефная группа. Ради неё вся маска и считается — без каймы кристалл и
// порода образуют одно сплошное пятно, потому что тайлы у них смыкаются
// вплотную и границы семьи в картинке нет.
//
// Падение (1 - max(x², y²))³ повторяет оригинал: в центре клетки множитель
// равен единице и текстура не трогается вовсе. Куб держит затемнение
// прижатым к краю — линейное расплывалось бы на половину клетки и читалось
// как тень, а не как грань.
//
// Половина диагонали, а не половина стороны. Углы клетки в оригинале лежат
// на ±1/√2, поэтому max(x², y²) у границы равен ровно 1/2, и самое тёмное,
// что кайма даёт, — 0.125, а не ноль. С масштабом по стороне кайма садилась
// в чистый чёрный по всему периметру: кристаллы получали жирную обводку и
// распадались на отдельные плитки вместо одного тела.
//
// Клетка делится диагоналями на четыре сектора, по одному на сторону, и
// сектор темнеет только если его сторона чужая. Сектор ровно один на
// фрагмент: диагонали делят клетку без перекрытий.
float TerrainReliefRim(float2 contourSample, float packedContour)
{
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

    // Зажим в пределы клетки обязателен. У якорной клетки на входе лежит
    // координата несущего прямоугольника, а он шире клетки на величину
    // смещения углов — до -0.19..1.19. Без зажима max(x², y²) упирается в
    // единицу, множитель садится в ноль, и по краям искажённых клеток идёт
    // чёрная полоса, которой на ровных клетках нет.
    float2 cellLocal = saturate(QuantizeTerrainFaceUV(contourSample));
    float2 p = (cellLocal - 0.5) * KERN_TERRAIN_RELIEF_RIM_SCALE;
    float edge = max(p.x * p.x, p.y * p.y);
    float fall = 1.0 - edge;
    float darken = fall * fall * fall;

    bool aboveMinorDiagonal = (p.y - p.x) > 0.0;
    bool aboveMainDiagonal = (p.y + p.x) > 0.0;
    int side;
    if (aboveMinorDiagonal)
    {
        side = aboveMainDiagonal ? 1 : 2;
    }
    else
    {
        side = aboveMainDiagonal ? 8 : 4;
    }

    return (foreignSides & side) != 0 ? darken : 1.0;
}

// The visible terrain and the material field must use the same cell shape.
// Geometry is evaluated only for the GPU cell path; CPU overlays already carry
// the displaced polygon in POSITION and therefore do not need a second mask.
float EvaluateTerrainCellCoverage(
    float2 geometrySample,
    float2 contourSample,
    float4 geometryCornersX,
    float4 geometryCornersY,
    float anchored,
    float packedContour,
    float packedLightingFlags,
    float antialiasScale,
    float applyGeometry)
{
    float geometryCoverage = applyGeometry > 0.5
        ? TerrainGeometryCoverage(
            geometrySample,
            geometryCornersX,
            geometryCornersY,
            anchored)
        : 1.0;
    float contourCoverage = KernTerrainIsRoundable(packedContour)
        ? EvaluateRoundableBlockAlpha(
            contourSample,
            packedContour,
            packedLightingFlags,
            antialiasScale)
        : 1.0;
    return geometryCoverage * contourCoverage;
}

float TerrainCellOccupancy(float coverage)
{
    return step(0.5, coverage);
}

#endif
