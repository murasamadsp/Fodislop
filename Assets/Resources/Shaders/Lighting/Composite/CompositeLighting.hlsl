#ifndef FODINAE_COMPOSITE_LIGHTING_HLSL
#define FODINAE_COMPOSITE_LIGHTING_HLSL

// CompositeLighting: финальная сборка изображения.
//
// READS: _DirectInput, _StaticDirectInput, _BounceInput, _MaterialField, _EmissionField, _BounceFilterWeights
// WRITES: _Result
// MUST NOT: вызывать DDA, трогать каскады, лампы

float3 SurfaceReflection(float2 position, float3 albedo)
{
    float2 uv = OutputUv(position);
    float3 incident = _DirectInput.SampleLevel(sampler_LinearClamp, uv, 0).rgb +
        _StaticDirectInput.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    float2 pixelsPerCell = float2(_FieldSize) * _CellSize / _WorldRect.zw;
    int2 pixel = int2(floor(position));
    static const int2 offsets[4] =
    {
        int2(-1, 0),
        int2(1, 0),
        int2(0, -1),
        int2(0, 1),
    };
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        // Reach stays one cell, but the light is read at the first air texel
        // on this row or column - the face itself. Sampling a fixed cell away
        // gave every texel of a one-cell block the same point in front of the
        // face, so whole blocks lit as flat squares.
        int reach = max(1, (int)ceil(abs(dot(float2(offsets[i]), pixelsPerCell))));
        [loop]
        for (int stepIndex = 1; stepIndex <= reach; stepIndex++)
        {
            int2 neighbor = pixel + offsets[i] * stepIndex;
            if (any(neighbor < 0) || any(neighbor >= _FieldSize))
            {
                break;
            }

            int2 materialNeighbor = neighbor;
            if (_MaterialYFlip != 0)
            {
                materialNeighbor.y = _FieldSize.y - 1 - materialNeighbor.y;
            }

            if (_MaterialField.Load(int3(materialNeighbor, 0)).a >= 0.5)
            {
                continue;
            }

            float3 light = _DirectInput.Load(int3(neighbor, 0)).rgb +
                _StaticDirectInput.Load(int3(neighbor, 0)).rgb;
            // Incident light reaches the exposed face through half an air cell.
            // Surface reflection is presentation only; it is never transmitted
            // through the wall or used as light on its opposite face.
            incident = max(incident, light * SegmentTransmission(0.0, 0.5));
            break;
        }
    }

    return incident * saturate(albedo) * _BounceStrength;
}

[numthreads(8, 8, 1)]
void CompositeLighting(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_FieldSize))
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);
    float2 uv = (float2(pixel) + 0.5) / float2(_FieldSize);
    int2 materialPixel = pixel;
    if (_MaterialYFlip != 0)
    {
        materialPixel.y = _FieldSize.y - 1 - materialPixel.y;
    }

    float4 material = _MaterialField.Load(int3(materialPixel.x, materialPixel.y, 0)).rgba;
    float4 emission = _EmissionField.Load(int3(materialPixel.x, materialPixel.y, 0)).rgba;
    if (_DebugView == 1) // Occupancy
    {
        _Result[pixel] = float4(material.aaa, 1.0);
        return;
    }

    // Тот же мип занятости, из которого террейн строит AO вокруг блоков.
    // Радиус держится здесь и в Terrain.shader порознь: там он в пикселях
    // экрана, тут в текселях поля, и сводить их в одну константу нечем.
    // Вид показывает источник, а не готовую тень.
    if (_DebugView == 10) // AmbientOcclusion
    {
        float nearby = SampleOccupancy(float2(pixel) + 0.5, _TerrainAmbientOcclusionMip);
        float occlusion = saturate(sqrt(nearby) * _TerrainAmbientOcclusionStrength);
        _Result[pixel] = float4(occlusion, occlusion, occlusion, 1.0);
        return;
    }

    if (_DebugView == 2) // Albedo
    {
        _Result[pixel] = float4(material.rgb, 1.0);
        return;
    }

    if (_DebugView == 3) // Emission
    {
        _Result[pixel] = float4(emission.rgb, 1.0);
        return;
    }

    float4 dynamicDirect = _DirectInput.Load(int3(pixel.x, pixel.y, 0)).rgba;
    float4 staticDirect = _StaticDirectInput.Load(int3(pixel.x, pixel.y, 0)).rgba;

    // Прозрачность среды берётся из статической половины, а не из
    // динамической.
    //
    // ЗАЧЕМ. В этом отладочном виде ResolveDirect выходит раньше и пишет одну
    // прозрачность, без всякой эмиссии, — обе половины содержат одно и то же,
    // и опасаться загрязнения статикой здесь не от чего. Зато динамическая
    // половина решается только когда в кадре есть хоть один динамический
    // источник, а иначе её текстуру просто обнуляют (ClearDynamicDirect). Вид
    // выходил чёрным ровно там, где рядом нет ни одной лампы, — то есть почти
    // всегда. Статическая половина пересчитывается при каждой смене
    // отладочного вида и потому заполнена всегда.
    if (_DebugView == 4) // Transmission
    {
        // Берётся та половина, которая в этом кадре решалась.
        //
        // В этом отладочном виде ResolveDirect выходит раньше и пишет одну
        // прозрачность, без эмиссии, — обе половины содержат одно и то же,
        // и выбирать между ними по смыслу не из чего. Зато пропущена может
        // быть любая: динамическая не решается, когда в кадре нет ни одного
        // динамического источника (её текстуру тогда обнуляют), а статическая
        // — когда геометрия не менялась. Максимум переживает пропуск любой из
        // них, тогда как жёсткая привязка к одной давала чёрный экран.
        _Result[pixel] = float4(max(staticDirect.rgb, dynamicDirect.rgb), 1.0);
        return;
    }

    if (_DebugView == 5) // StaticDirect
    {
        _Result[pixel] = float4(staticDirect.rgb, 1.0);
        return;
    }

    if (_DebugView == 6) // DynamicDirect
    {
        _Result[pixel] = float4(dynamicDirect.rgb, 1.0);
        return;
    }

    float4 combinedDirect = dynamicDirect + staticDirect;

    if (_BlockAveraged != 0 && _DebugView == 0)
    {
        _Result[pixel] = float4(max(combinedDirect.rgb, 0.0), 1.0);
        return;
    }

    if (_DebugView == 7) // DirectRadiance (combined)
    {
        _Result[pixel] = float4(combinedDirect.rgb, 1.0);
        return;
    }

    float solid = saturate(material.a);
    // The bounce term reaches the image only when bounce is on, or in its own
    // debug view. Otherwise it was evaluated per pixel and then discarded.
    float3 bounce = 0.0;
    if (_EnableDiffuseBounce != 0 || _DebugView == 8)
    {
        bounce = (1.0 - solid) * SampleBounceFiltered(pixel, uv);
        if (solid > 0.0)
        {
            bounce += solid * SurfaceReflection(float2(pixel) + 0.5, material.rgb);
        }
    }

    if (_DebugView == 8) // DiffuseBounce
    {
        _Result[pixel] = float4(bounce, 1.0);
        return;
    }

    float3 bounceTerm = _EnableDiffuseBounce != 0 ? bounce : 0.0;

    float3 directAndBounce = bounceTerm + combinedDirect.rgb;
    float3 ambient = _AmbientColor.rgb;

    if (_DebugView == 9) // Exposure (false-color zebras)
    {
        float maximumLight = max(_MaximumLightMultiplier, 0.0001);
        float3 result = ambient + directAndBounce;
        float peak = Max3(result);
        float3 falseColor = float3(0.0, 0.0, 0.0);
        if (peak > maximumLight)
        {
            // Overexposed / clipped -> bright red
            falseColor = float3(1.0, 0.1, 0.1);
        }
        else if (peak > 1.0)
        {
            // High dynamic range (1.0 to maximumLight) -> yellow-orange
            float t = (peak - 1.0) / max(maximumLight - 1.0, 0.0001);
            falseColor = lerp(float3(0.3, 0.9, 0.2), float3(1.0, 0.7, 0.0), saturate(t));
        }
        else if (peak > 0.05)
        {
            // Well-exposed (< 1.0) -> green gradient
            float t = (peak - 0.05) / 0.95;
            falseColor = lerp(float3(0.05, 0.3, 0.1), float3(0.3, 0.9, 0.2), saturate(t));
        }
        else
        {
            // Deep shadow (< 0.05) -> dark blue
            float t = peak / 0.05;
            falseColor = lerp(float3(0.02, 0.04, 0.15), float3(0.05, 0.3, 0.1), saturate(t));
        }

        _Result[pixel] = float4(falseColor, 1.0);
        return;
    }

    float3 output = max(directAndBounce, 0.0);
    _Result[pixel] = float4(ambient + output, 1.0);
}

#endif // FODINAE_COMPOSITE_LIGHTING_HLSL
