#ifndef KERN_TERRAIN_CRYSTAL_REFLECTION_INCLUDED
#define KERN_TERRAIN_CRYSTAL_REFLECTION_INCLUDED

// Presentation-only reflection. This reads solved irradiance, not transport;
// its gradient is a direction cue, not a reconstructed physical light ray.
float3 EvaluateXGreenReflection(
    float3 facets, float2 worldPosition, float3 lightColor,
    float2 uvDx, float2 uvDy, float2 worldDx, float2 worldDy)
{
#if !defined(KERN_WORLD_LIGHTING)
    return 0.0;
#else
    if (facets.b <= 0.0 || _WorldLightDebugView != 0)
    {
        return 0.0;
    }

    const float radius = 0.35;
    const float3 luma = float3(0.299, 0.587, 0.114);
    float2 gradient = float2(
        dot(GetWorldLightColor(worldPosition + float2(radius, 0.0)).rgb
            - GetWorldLightColor(worldPosition - float2(radius, 0.0)).rgb, luma),
        dot(GetWorldLightColor(worldPosition + float2(0.0, radius)).rgb
            - GetWorldLightColor(worldPosition - float2(0.0, radius)).rgb, luma));
    float gradientLength = length(gradient);
    float determinant = uvDx.x * uvDy.y - uvDx.y * uvDy.x;
    if (gradientLength < 0.0001 || abs(determinant) < 0.00000001)
    {
        return 0.0;
    }

    float2 tangent = normalize((worldDx * uvDy.y - worldDy * uvDx.y) / determinant);
    float2 bitangent = normalize((worldDy * uvDx.x - worldDx * uvDy.x) / determinant);
    float2 normalXY = facets.rg * 2.0 - 1.0;
    float3 normal = float3(tangent * normalXY.x + bitangent * normalXY.y,
        sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    float3 halfVector = normalize(float3(gradient / gradientLength, 1.0));
    float highlight = saturate(dot(normal, halfVector));
    highlight *= highlight;
    highlight *= highlight;
    highlight *= highlight;
    highlight *= highlight;
    highlight *= highlight;
    return lightColor * float3(0.5, 1.0, 0.65)
        * (highlight * facets.b * saturate(gradientLength * 6.0) * 1.6);
#endif
}

#endif
