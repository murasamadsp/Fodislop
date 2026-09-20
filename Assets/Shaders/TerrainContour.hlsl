#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "TerrainLightingData.hlsl"

static const float KERN_TERRAIN_FACE_GRID_SIZE = 32.0;

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
