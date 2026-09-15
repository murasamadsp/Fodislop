#ifndef FODINAE_DYNAMIC_LIGHT_TRACE_HLSL
#define FODINAE_DYNAMIC_LIGHT_TRACE_HLSL

// SolveDynamicLighting и ComposeDynamicLighting: per-lamp tile tracing и композиция.
// При одной лампе SolveDynamicLighting может сразу писать DirectTexture.
//
// READS: _LampPolar, _DynamicLights
// WRITES: _LampTiles, _DirectTexture
// MUST NOT: трогать каскады, bounce

[numthreads(8, 8, 1)]
void SolveDynamicLighting(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(int2(dispatchId.xy) >= _DynamicDispatchSize))
    {
        return;
    }

    int2 pixel = _DynamicDispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _FieldSize))
    {
        return;
    }

    float2 origin = float2(pixel) + 0.5;
    float3 radiance = LampRadianceFromPolar(
        origin,
        _DynamicLights[_DynamicLightIndex],
        8);
    _LampTiles[_LampTileOffset + int2(dispatchId.xy)] = float4(radiance, 1.0);
    if (_WriteDynamicDirect != 0)
    {
        _DirectTexture[pixel] = float4(radiance, 1.0);
    }
}

[numthreads(8, 8, 1)]
void ClearDynamicDirect(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(int2(dispatchId.xy) >= _DynamicDispatchSize))
    {
        return;
    }

    int2 pixel = _DynamicDispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _FieldSize))
    {
        return;
    }

    _DirectTexture[pixel] = 0.0;
}

[numthreads(8, 8, 1)]
void ComposeDynamicLighting(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(int2(dispatchId.xy) >= _ComposeSize))
    {
        return;
    }

    int2 pixel = _ComposeOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _FieldSize))
    {
        return;
    }

    float3 radiance = 0.0;
    [loop]
    for (int lampIndex = 0; lampIndex < _LampTileCount; lampIndex++)
    {
        LampTileInfo tile = _LampTileInfos[lampIndex];
        int2 local = pixel - tile.fieldOrigin;
        if (all(local >= 0) && all(local < tile.size))
        {
            radiance += _LampTilesInput.Load(int3(tile.tileOffset + local, 0)).rgb;
        }
    }

    _DirectTexture[pixel] = float4(radiance, 1.0);
}

#endif // FODINAE_DYNAMIC_LIGHT_TRACE_HLSL
