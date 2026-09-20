#ifndef KERN_TERRAIN_PRISMATIC_CRYSTAL_INCLUDED
#define KERN_TERRAIN_PRISMATIC_CRYSTAL_INCLUDED

// Palette index lives in animData.z; independent of minimap/emission colors.
float3 PrismaticCrystalTint(float paletteIndex)
{
    if (paletteIndex == 1.0) { return float3(0.25, 1.0, 0.45); }
    if (paletteIndex == 2.0) { return float3(0.25, 0.5, 1.0); }
    if (paletteIndex == 3.0) { return float3(1.0, 0.25, 0.12); }
    if (paletteIndex == 4.0) { return float3(0.7, 0.3, 1.0); }
    if (paletteIndex == 5.0) { return float3(0.15, 0.9, 1.0); }
    return float3(0.0, 0.0, 0.0);
}

float2 PrismaticCrystalFlowUV(float2 serverCell, float2 localPosition)
{
    // One continuous field over the terrain, independent of atlas rotation,
    // cell/chunk boundaries and camera. Local Y is up; server Y is down.
    return float2(serverCell.x + localPosition.x, serverCell.y - localPosition.y)
        / float2(10.0, 8.0);
}

float3 EvaluatePrismaticCrystal(float3 baseColor, float3 flowSample, float paletteIndex, float phase)
{
    float phaseSin;
    float phaseCos;
    sincos(phase, phaseSin, phaseCos);
    float2 phaseVector = flowSample.rg * 2.0 - 1.0;
    float wave = saturate(0.5 + 0.5 * dot(phaseVector, float2(phaseCos, phaseSin)));
    float band = wave * wave * wave;
    float crest = band * band;
    crest *= crest;
    crest *= crest;

    // Texture brightness attaches highlights to authored facets, regardless
    // of tile rotation. Dark crevices stay dark; no white overlay is added.
    float luminance = dot(baseColor, float3(0.299, 0.587, 0.114));
    float facet = saturate((luminance - 0.08) * 2.5);
    float headroom = saturate(1.0 - luminance);
    float highlight = facet * headroom * (0.28 * band + 0.75 * crest * flowSample.b);
    return baseColor * (1.0 - 0.14 * band)
        + PrismaticCrystalTint(paletteIndex) * highlight;
}

#endif
