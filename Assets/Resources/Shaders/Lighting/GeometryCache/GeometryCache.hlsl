#ifndef FODINAE_GEOMETRY_CACHE_HLSL
#define FODINAE_GEOMETRY_CACHE_HLSL

// BuildCellSolidMask: кэш геометрии для closed-diagonal rule.
//
// READS: _MaterialField
// WRITES: _CellSolidMaskOutput
// MUST NOT: знать о каскадах, лампах, bounce


[numthreads(8, 8, 1)]
void BuildCellSolidMask(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_CellGridSize))
    {
        return;
    }

    int2 cell = int2(dispatchId.xy);
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    bool isSolid = false;
    SampleCellSolid(cell, cellsPerPixel, isSolid);
    _CellSolidMaskOutput[cell] = float4(isSolid ? 1.0 : 0.0, 0.0, 0.0, 0.0);
}

#endif // FODINAE_GEOMETRY_CACHE_HLSL
