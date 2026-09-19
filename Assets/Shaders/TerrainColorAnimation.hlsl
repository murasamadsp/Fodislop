#ifndef KERN_TERRAIN_COLOR_ANIMATION_INCLUDED
#define KERN_TERRAIN_COLOR_ANIMATION_INCLUDED

// Анимация цвета клетки террейна — одна на оба пасса.
//
// ЗАЧЕМ ОТДЕЛЬНЫМ ФАЙЛОМ. Раньше анимация жила только в видимом пассе, а поле
// материалов писало упакованный цвет вершины как есть. Из-за этого мигающая
// лава светила ровно, радужный блок отдавал в отскок постоянный цвет, и
// освещение вообще не знало, что текстура анимирована: альбедо получалось
// статическим при динамической картинке. Копировать код во второй пасс нельзя —
// две копии разойдутся на первой же правке вида, и разойдутся молча.
//
// Текстур здесь нет намеренно: выборка потока делается снаружи и приезжает
// готовым цветом. Пассы объявляют разные наборы текстур, и включаемый файл не
// должен зависеть ни от одного из них.

float3 TerrainRGBToHSV(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 TerrainHSVToRGB(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

static const int KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL = 1;
static const int KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE = 2;

struct TerrainShimmerSignal
{
    float wave;
    float body;
    float surfaceMask;
};

TerrainShimmerSignal EvaluateTerrainShimmer(
    float3 luminanceSource,
    float3 flowSample,
    float animationSpeed,
    float shimmerSpeedScale)
{
    float3 flowHSV = TerrainRGBToHSV(flowSample);
    float hueAngle = flowHSV.x * 6.28318548;
    float chroma =
        max(flowSample.r, max(flowSample.g, flowSample.b)) -
        min(flowSample.r, min(flowSample.g, flowSample.b));

    float wave = sin(-(hueAngle + _Time.y * animationSpeed * shimmerSpeedScale));
    wave = wave * 0.5 + 0.5;

    float luminance = dot(luminanceSource, float3(0.299, 0.587, 0.114));
    float inverseLuminance = 1.0 - luminance;
    float luminanceMask =
        1.0 - inverseLuminance * inverseLuminance * inverseLuminance;

    TerrainShimmerSignal signal;
    signal.wave = wave;
    signal.body = wave * wave * wave;
    signal.surfaceMask = luminanceMask * chroma;
    return signal;
}

bool TerrainAnimationUsesFlowMap(int animationType, int animationProfile)
{
    return animationType == 2 ||
        animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL ||
        animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE;
}

float2 TerrainFlowSamplePosition(
    float2 worldPosition,
    int animationProfile,
    float animationSpeed)
{
    if (animationProfile != KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE)
    {
        return worldPosition;
    }

    // Move the vector field more slowly than the lava sheet. The two axes use
    // different rates so the deformation does not repeat as a diagonal pan.
    return worldPosition + _Time.y * animationSpeed * float2(0.018, -0.011);
}

float2 AnimateTerrainSampleUV(
    float2 atlasUV,
    float4 subAtlasRect,
    float2 tileSizeUV,
    int animationProfile,
    float3 flowSample)
{
    if (animationProfile != KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE)
    {
        return atlasUV;
    }

    float2 flowDirection = flowSample.rg * 2.0 - 1.0;
    float2 distortion = flowDirection * tileSizeUV * 0.14;
    float2 localUV = atlasUV - subAtlasRect.xy + distortion;
    return subAtlasRect.xy + frac(localUV / subAtlasRect.zw) * subAtlasRect.zw;
}

float TerrainContourAntialiasScale(int animationProfile)
{
    return animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE
        ? 2.5
        : 1.0;
}

float3 TerrainUnpackRgb24(float packedColor)
{
    uint packed = (uint)round(packedColor);
    return float3(
        packed & 0xFFu,
        (packed >> 8u) & 0xFFu,
        (packed >> 16u) & 0xFFu) / 255.0;
}

// baseColor      — цвет, который анимируется.
// luminanceSource — по чему считается маска яркости для мерцания. В видимом
//                   пассе и в поле материалов это цвет текселя атласа.
// flowSample     — выборка карты потока в мировой точке, снаружи.
float3 AnimateTerrainColor(
    float3 baseColor,
    float3 luminanceSource,
    int animationType,
    int animationProfile,
    float animationSpeed,
    float animationOffset,
    float3 flowSample,
    float packedCellColor,
    float3 shimmerColor,
    float shimmerSpeedScale,
    float pulseSpeedScale)
{
    if (animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL)
    {
        float3 cellColor = TerrainUnpackRgb24(packedCellColor);
        TerrainShimmerSignal signal = EvaluateTerrainShimmer(
            luminanceSource,
            flowSample,
            animationSpeed,
            shimmerSpeedScale);

        // The broad band keeps the old X-crystal color travel. A narrower,
        // brighter crest turns it into a local facet glint without washing
        // out the atlas texture between highlights.
        float bodyStrength = signal.body * signal.surfaceMask * 0.68;
        float glintRamp = saturate((signal.wave - 0.76) * 4.16666667);
        float glintStrength =
            glintRamp * glintRamp * signal.surfaceMask * 0.34;
        float3 coloredFacet = lerp(baseColor, cellColor, bodyStrength);
        float3 glintColor = lerp(cellColor, 1.0.xxx, 0.68);
        return coloredFacet + glintColor * glintStrength;
    }

    if (animationType == 1) // Blinking
    {
        float pulse = 0.5 + 0.5 * sin(
            _Time.y * animationSpeed * pulseSpeedScale + animationOffset);
        return baseColor * pulse;
    }

    if (animationType == 2) // Shimmer
    {
        TerrainShimmerSignal signal = EvaluateTerrainShimmer(
            luminanceSource,
            flowSample,
            animationSpeed,
            shimmerSpeedScale);
        return lerp(
            baseColor,
            shimmerColor,
            signal.body * signal.surfaceMask);
    }

    if (animationType == 3) // Rainbow
    {
        float3 rainbowHSV = TerrainRGBToHSV(baseColor);
        rainbowHSV.x = frac(rainbowHSV.x + _Time.y * (animationSpeed / 255.0));
        return TerrainHSVToRGB(rainbowHSV);
    }

    return baseColor;
}

#endif
