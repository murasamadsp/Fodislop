#ifndef KERN_TERRAIN_GEOMETRY_INCLUDED
#define KERN_TERRAIN_GEOMETRY_INCLUDED

#include "TerrainGeometryContract.hlsl"

static const float KERN_TERRAIN_GEOMETRY_EPSILON = 0.0001;

float TerrainGeometryEdgeCross(
    float2 edgeStart,
    float2 edgeEnd,
    float2 samplePosition)
{
    float2 edge = edgeEnd - edgeStart;
    float2 toSample = samplePosition - edgeStart;
    return (edge.x * toSample.y) - (edge.y * toSample.x);
}

float2 TerrainGeometrySegmentClosestPoint(
    float2 samplePosition,
    float2 edgeStart,
    float2 edgeEnd)
{
    float2 edge = edgeEnd - edgeStart;
    float edgeLengthSquared = max(dot(edge, edge), KERN_TERRAIN_GEOMETRY_EPSILON);
    float projection = saturate(dot(samplePosition - edgeStart, edge) / edgeLengthSquared);
    return edgeStart + edge * projection;
}

float TerrainGeometrySegmentDistanceSquared(
    float2 samplePosition,
    float2 edgeStart,
    float2 edgeEnd)
{
    float2 delta = samplePosition - TerrainGeometrySegmentClosestPoint(
        samplePosition, edgeStart, edgeEnd);
    return dot(delta, delta);
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

    // Classic distortion moves each corner by less than a quarter cell, so its
    // four-corner contour remains convex. The signed edge tests avoid the
    // divisions and scanline branches of the general polygon test.
    float insidePositive = 1.0;
    float insideNegative = 1.0;
    [unroll]
    for (int side = 0; side < KERN_TERRAIN_SIDE_COUNT; side++)
    {
        float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, side);
        float2 edgeEnd = TerrainGeometryCorner(
            cornersX, cornersY, (side + 1) % KERN_TERRAIN_SIDE_COUNT);
        float edgeCross = TerrainGeometryEdgeCross(edgeStart, edgeEnd, samplePosition);
        insidePositive *= edgeCross >= -KERN_TERRAIN_GEOMETRY_EPSILON ? 1.0 : 0.0;
        insideNegative *= edgeCross <= KERN_TERRAIN_GEOMETRY_EPSILON ? 1.0 : 0.0;
    }

    return insidePositive > 0.5 || insideNegative > 0.5 ? 1.0 : 0.0;
}

// Distortion changes each side's direction and distance from the carrier
// coordinate. Measure contact from the actual straight side segment.
float TerrainGeometrySideDistance(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    int side)
{
    float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, side);
    float2 edgeEnd = TerrainGeometryCorner(
        cornersX, cornersY, (side + 1) % KERN_TERRAIN_SIDE_COUNT);
    return sqrt(TerrainGeometrySegmentDistanceSquared(
        samplePosition, edgeStart, edgeEnd));
}

// AO needs the sign for pixels safely inside the silhouette, not their exact
// distance to the nearest edge. Return true only for a conservative interior
// core; boundary and exterior pixels continue through the exact distance path.
bool TerrainGeometryIsDeepInterior(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY)
{
    float insidePositive = 1.0;
    float insideNegative = 1.0;
    [unroll]
    for (int side = 0; side < KERN_TERRAIN_SIDE_COUNT; side++)
    {
        float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, side);
        float2 edgeEnd = TerrainGeometryCorner(
            cornersX, cornersY, (side + 1) % KERN_TERRAIN_SIDE_COUNT);
        float edgeCross = TerrainGeometryEdgeCross(edgeStart, edgeEnd, samplePosition);
        insidePositive *= (edgeCross > 0.0 ? 1.0 : 0.0);
        insideNegative *= (edgeCross < 0.0 ? 1.0 : 0.0);
    }

    return insidePositive > 0.5 || insideNegative > 0.5;
}

float TerrainGeometrySignedDistance(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    out float2 nearestPosition)
{
    float nearestDistanceSquared = 1.0e20;
    nearestPosition = samplePosition;
    int edgeCount = KERN_TERRAIN_SIDE_COUNT;
    bool inside = false;
    bool onBoundary = false;

    for (int index = 0; index < edgeCount; index++)
    {
        float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, index);
        float2 edgeEnd = TerrainGeometryCorner(cornersX, cornersY, (index + 1) % edgeCount);
        float2 edge = edgeEnd - edgeStart;
        float edgeCross = TerrainGeometryEdgeCross(
            edgeStart, edgeEnd, samplePosition);
        onBoundary = onBoundary ||
            (abs(edgeCross) <= KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.x >= min(edgeStart.x, edgeEnd.x) - KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.x <= max(edgeStart.x, edgeEnd.x) + KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.y >= min(edgeStart.y, edgeEnd.y) - KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.y <= max(edgeStart.y, edgeEnd.y) + KERN_TERRAIN_GEOMETRY_EPSILON);
        if ((edgeStart.y > samplePosition.y) != (edgeEnd.y > samplePosition.y))
        {
            float xAtScanline = edgeStart.x +
                ((samplePosition.y - edgeStart.y) * edge.x / edge.y);
            if (samplePosition.x < xAtScanline)
            {
                inside = !inside;
            }
        }
        float2 closestPosition = TerrainGeometrySegmentClosestPoint(
            samplePosition, edgeStart, edgeEnd);
        float2 delta = samplePosition - closestPosition;
        float distanceSquared = dot(delta, delta);
        if (distanceSquared < nearestDistanceSquared)
        {
            nearestDistanceSquared = distanceSquared;
            nearestPosition = closestPosition;
        }
    }

    float distanceToEdge = sqrt(nearestDistanceSquared);
    return inside || onBoundary ? distanceToEdge : -distanceToEdge;
}

float TerrainGeometrySignedDistance(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY)
{
    float2 nearestPosition;
    return TerrainGeometrySignedDistance(
        samplePosition,
        cornersX,
        cornersY,
        nearestPosition);
}

float TerrainSignedDistanceToBox(float2 samplePosition, float2 boxCenter, float2 halfSize)
{
    float2 delta = abs(samplePosition - boxCenter) - halfSize;
    return length(max(delta, float2(0.0, 0.0))) + min(max(delta.x, delta.y), 0.0);
}


#endif
