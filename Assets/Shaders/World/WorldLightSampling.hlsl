#ifndef KERN_WORLD_LIGHT_SAMPLING_INCLUDED
#define KERN_WORLD_LIGHT_SAMPLING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/WorldRenderGrid.hlsl"

Texture2D<float4> _WorldLightTexture;
SamplerState sampler_WorldLightTexture;
float4 _WorldLightRect;
float4 _WorldLightTextureSize;
int _WorldLightDebugView;

float2 GetWorldLightUvUnclamped(float2 worldPos)
{
    float2 rectSize = max(_WorldLightRect.zw, float2(0.0001, 0.0001));
    return (KernWorldGridPixelCenter(worldPos) - _WorldLightRect.xy) / rectSize;
}

float2 GetWorldLightUv(float2 worldPos)
{
    return saturate(GetWorldLightUvUnclamped(worldPos));
}

float4 SampleWorldLightColorAtUv(float2 lightUV)
{
    float4 lightColor;
    if (_WorldLightDebugView >= 1 && _WorldLightDebugView <= 3)
    {
        int2 debugPixel = clamp(
            int2(lightUV * _WorldLightTextureSize.xy),
            int2(0, 0),
            int2(_WorldLightTextureSize.xy) - 1);
        lightColor = _WorldLightTexture.Load(int3(debugPixel.x, debugPixel.y, 0));
    }
    else
    {
        // Явный уровень: у текстуры света нет мипов, а выборка идёт после
        // ранних return у прозрачных текселей. В квадах 2×2 на кромке
        // обрезанной геометрии (валуны) производные там не определены, и
        // неявный Sample отдавал свет не из этой точки — какие пиксели
        // кромки попадали под это, решало положение экранной сетки, то есть
        // зум.
        lightColor = _WorldLightTexture.SampleLevel(
            sampler_WorldLightTexture,
            lightUV,
            0.0);
    }

    return lightColor;
}

float4 SampleWorldLightColor(float2 worldPos)
{
    return SampleWorldLightColorAtUv(GetWorldLightUv(worldPos));
}

float4 SampleWorldLightColorUnclamped(float2 worldPos)
{
    return SampleWorldLightColorAtUv(GetWorldLightUvUnclamped(worldPos));
}

float4 GetWorldLightColor(float2 worldPos)
{
#if !defined(KERN_WORLD_LIGHTING)
    return 1.0;
#else
    return SampleWorldLightColor(worldPos);
#endif
}

#endif
