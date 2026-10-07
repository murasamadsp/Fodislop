#ifndef KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED
#define KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED

#include "Assets/Shaders/Terrain/TerrainCellFormat.hlsl"

bool TerrainAnimationUsesFlowMap(int cellAnimationType, int cellSurfaceEffect)
{
    if (cellSurfaceEffect == (int)KERN_TERRAIN_SURFACE_EFFECT_PRISMATIC)
    {
        return true;
    }

    if (cellSurfaceEffect == (int)KERN_TERRAIN_SURFACE_EFFECT_FACETED)
    {
        return false;
    }

    return cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_SHIMMER;
}

#endif
