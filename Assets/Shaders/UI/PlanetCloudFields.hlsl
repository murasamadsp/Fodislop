#ifndef FODINAE_PLANET_CLOUD_FIELDS_INCLUDED
#define FODINAE_PLANET_CLOUD_FIELDS_INCLUDED

// Поля облачной палубы, зависящие ТОЛЬКО от направления.
//
// Палуба не маршируется объёмно: она вычисляется на одной сфере на высоте
// облачного верха и затеняется как рельеф. Значит, и покрытие, и наклон —
// чистые функции направления, и их можно запечь целиком. См. пояснение в
// PlanetSurfaceFields.hlsl о том, почему код общий, а не продублирован.

#include "PlanetNoise.hlsl"

#ifndef PI
#define PI 3.14159265359
#endif

// Покрытие: доменный варп даёт вихревую структуру, зональные полосы —
// кориолисовы пояса, высокочастотные волокна — перистые полосы.
float FodinaeCloudField(
    float3 d,
    float cloudScale,
    float cloudWarp,
    float cloudBands,
    float cloudBandStrength)
{
    float3 p = d * cloudScale;

    float3 warp = float3(
        GradientNoise(p + float3(11.3, 5.1, 27.7)),
        GradientNoise(p + float3(47.9, 63.2, 8.4)),
        GradientNoise(p + float3(83.1, 19.6, 51.3)));

    float cov = Fbm(p + (warp * cloudWarp), 3);

    float wobble = GradientNoise(p * 0.5) * 0.25;
    float bands = sin(((d.y + wobble) * cloudBands * PI) + 1.1);
    cov += bands * cloudBandStrength;

    float wisps = GradientNoise((p * 3.2) + (warp * 0.3)) * 0.12;
    cov += wisps;

    return cov;
}

/// Дешёвая версия поля — только для конечных разностей нормали палубы.
float FodinaeFastCloudField(float3 d, float cloudScale)
{
    return Fbm(d * cloudScale, 2);
}

// Наклон облачной палубы. Базис строится тем же способом, что и на
// поверхности, но шаг конечной разности свой: палуба крупнее и мягче.
float2 FodinaeCloudSlope(float3 d, float cloudScale)
{
    float3 up = abs(d.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 cTangent = normalize(cross(up, d));
    float3 cBitangent = cross(d, cTangent);

    const float cEps = 0.012;
    float c0 = FodinaeFastCloudField(d, cloudScale);
    float cT = FodinaeFastCloudField(normalize(d + (cTangent * cEps)), cloudScale);
    float cB = FodinaeFastCloudField(normalize(d + (cBitangent * cEps)), cloudScale);

    return float2(cT - c0, cB - c0) / cEps;
}

// Запекаемый набор: x — покрытие, yz — наклон палубы.
float4 FodinaePackCloudFields(
    float3 d,
    float cloudScale,
    float cloudWarp,
    float cloudBands,
    float cloudBandStrength)
{
    float coverage = FodinaeCloudField(d, cloudScale, cloudWarp, cloudBands, cloudBandStrength);
    float2 slope = FodinaeCloudSlope(d, cloudScale);
    return float4(coverage, slope, 0.0);
}

#endif // FODINAE_PLANET_CLOUD_FIELDS_INCLUDED
