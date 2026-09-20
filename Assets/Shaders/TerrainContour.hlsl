#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "TerrainLightingData.hlsl"

static const float KERN_TERRAIN_FACE_GRID_SIZE = 32.0;

float2 QuantizeTerrainFaceUV(float2 uv)
{
    // Contours are evaluated in the canonical 0..1 face space. Vertex
    // distortion is already quantized in TerrainVertexDistortionCalculator;
    // feeding displaced interpolants here would quantize the same movement a
    // second time and produce oversized steps at the lava boundary.
    float2 pixel = floor(saturate(uv) * KERN_TERRAIN_FACE_GRID_SIZE);
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
