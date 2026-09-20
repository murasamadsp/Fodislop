#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "TerrainLightingData.hlsl"

static const float KERN_TERRAIN_FACE_GRID_SIZE = 32.0;

static const float KERN_TERRAIN_GEOMETRY_COORD_BIAS = 2048.0;
static const float KERN_TERRAIN_GEOMETRY_COORD_RANGE = 4096.0;

float2 UnpackTerrainGeometryPair(float packedPair)
{
    float high = floor(packedPair / KERN_TERRAIN_GEOMETRY_COORD_RANGE);
    float low = packedPair - (high * KERN_TERRAIN_GEOMETRY_COORD_RANGE);
    return (float2(high, low) - KERN_TERRAIN_GEOMETRY_COORD_BIAS) /
        KERN_TERRAIN_FACE_GRID_SIZE;
}

void UnpackTerrainGeometryCorners(
    float4 packedCorners,
    out float4 cornersX,
    out float4 cornersY)
{
    float2 corner0 = UnpackTerrainGeometryPair(packedCorners.x);
    float2 corner1 = UnpackTerrainGeometryPair(packedCorners.y);
    float2 corner2 = UnpackTerrainGeometryPair(packedCorners.z);
    float2 corner3 = UnpackTerrainGeometryPair(packedCorners.w);
    cornersX = float4(corner0.x, corner1.x, corner2.x, corner3.x);
    cornersY = float4(corner0.y, corner1.y, corner2.y, corner3.y);
}

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
    float2 corner0 = float2(cornersX.x, cornersY.x);
    float2 corner1 = float2(cornersX.y, cornersY.y);
    float2 corner2 = float2(cornersX.z, cornersY.z);
    float2 corner3 = float2(cornersX.w, cornersY.w);
    float4 edgeCrosses = float4(
        TerrainGeometryEdgeCross(corner0, corner1, quantizedPoint),
        TerrainGeometryEdgeCross(corner1, corner2, quantizedPoint),
        TerrainGeometryEdgeCross(corner2, corner3, quantizedPoint),
        TerrainGeometryEdgeCross(corner3, corner0, quantizedPoint));
    float4 edgeLengths = float4(
        length(corner1 - corner0),
        length(corner2 - corner1),
        length(corner3 - corner2),
        length(corner0 - corner3));
    float4 edgeMargins = edgeLengths / KERN_TERRAIN_FACE_GRID_SIZE;
    bool insideCounterClockwise = all(edgeCrosses >= -edgeMargins);
    bool insideClockwise = all(edgeCrosses <= edgeMargins);
    return (insideCounterClockwise || insideClockwise) ? 1.0 : 0.0;
}

float2 QuantizeTerrainFaceUV(float2 uv)
{
    // The input can be the displaced corner coordinate. Keep it outside the
    // canonical range: clamping it would collapse a moved corner onto the
    // edge and turn a one-pixel displacement into a large flat step.
    float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
    return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
}

float PhysicalContour(
    float2 uv,
    int solidBoundaryMask,
    int solidDiagonalMask)
{
    bool top = (solidBoundaryMask & 1) != 0;
    bool left = (solidBoundaryMask & 2) != 0;
    bool bottom = (solidBoundaryMask & 4) != 0;
    bool right = (solidBoundaryMask & 8) != 0;
    float2 p = QuantizeTerrainFaceUV(uv) - 0.5;
    float contour = step(length(p), 0.5);
    contour = (top || left) && p.x <= 0.0 && p.y >= 0.0 ? 1.0 : contour;
    contour = (top || right) && p.x >= 0.0 && p.y >= 0.0 ? 1.0 : contour;
    contour = (bottom || left) && p.x <= 0.0 && p.y <= 0.0 ? 1.0 : contour;
    contour = (bottom || right) && p.x >= 0.0 && p.y <= 0.0 ? 1.0 : contour;
    bool diagTL = (solidDiagonalMask & 1) != 0;
    bool diagTR = (solidDiagonalMask & 2) != 0;
    bool diagBL = (solidDiagonalMask & 4) != 0;
    bool diagBR = (solidDiagonalMask & 8) != 0;
    contour = diagTL && p.x <= 0.0 && p.y >= 0.0 ? 1.0 : contour;
    contour = diagTR && p.x >= 0.0 && p.y >= 0.0 ? 1.0 : contour;
    contour = diagBL && p.x <= 0.0 && p.y <= 0.0 ? 1.0 : contour;
    contour = diagBR && p.x >= 0.0 && p.y <= 0.0 ? 1.0 : contour;
    return contour;
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

#endif
