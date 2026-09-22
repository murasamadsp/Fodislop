#ifndef KERN_TERRAIN_MOLTEN_HEAT_INCLUDED
#define KERN_TERRAIN_MOLTEN_HEAT_INCLUDED

float3 EvaluateMoltenHeat(float3 baseColor, float2 surfacePosition, float phase)
{
    // One world-anchored 32x32 sampling grid, shared by all neighboring cells.
    // Heat is independent of server sprite-animation flags and texture green.
    float2 pixelPosition = (floor(surfacePosition * 32.0) + 0.5) / 32.0;
    float broadFlow = sin(dot(pixelPosition, float2(1.9, -1.3)) + phase);
    float crossFlow = sin(dot(pixelPosition, float2(-1.1, 2.1)) - phase * 0.7);
    float heat = saturate(0.5 + broadFlow * 0.3 + crossFlow * 0.2);
    float hot = heat * heat;
    float material = max(baseColor.r, max(baseColor.g, baseColor.b));
    return baseColor * (0.35 + 0.8 * heat)
        + float3(0.6, 0.35, 0.035) * (hot * material);
}

#endif
