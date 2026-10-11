#ifndef KERN_WORLD_RENDER_GRID_INCLUDED
#define KERN_WORLD_RENDER_GRID_INCLUDED

// Density is enabled for world rendering at 32 pixels per world cell and zero
// for passes that should retain their native coordinates.
float _KernWorldGridDensity;

float3 KernWorldGridVertex(float3 worldPosition)
{
    if (_KernWorldGridDensity > 0.0)
    {
        worldPosition.xy = floor(worldPosition.xy * _KernWorldGridDensity + 0.5) / _KernWorldGridDensity;
    }

    return worldPosition;
}

float2 KernWorldGridPixelCenter(float2 worldPosition)
{
    if (_KernWorldGridDensity > 0.0)
    {
        worldPosition = (floor(worldPosition * _KernWorldGridDensity) + 0.5) / _KernWorldGridDensity;
    }

    return worldPosition;
}

// Project through separate view and projection matrices to retain precision at
// large world Y coordinates.
float4 KernWorldGridClipPosition(float3 worldPosition)
{
    return mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, float4(worldPosition, 1.0)));
}

#endif
