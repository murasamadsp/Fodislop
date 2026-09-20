#ifndef KERN_TERRAIN_MOLTEN_HEAT_INCLUDED
#define KERN_TERRAIN_MOLTEN_HEAT_INCLUDED

float3 EvaluateMoltenHeat(float3 baseColor, float3 flowSample, float phase)
{
    float phaseSin;
    float phaseCos;
    sincos(phase, phaseSin, phaseCos);
    float heat = saturate(0.5 + 0.5 * dot(flowSample.rg * 2.0 - 1.0, float2(phaseCos, phaseSin)));
    float hot = heat * heat;
    hot *= hot;
    // Heat opens existing bright veins; cooler regions retain the dark crust.
    // Exact black cannot acquire an orange overlay from the phase field.
    float vein = saturate(baseColor.r * 1.5) * saturate(baseColor.g * 3.0);
    float3 cooling = baseColor * (0.6 + 0.4 * heat);
    float3 incandescence = float3(1.0, 0.72, 0.12) * (hot * vein * 0.65);
    return cooling + incandescence;
}

#endif
