#nullable enable

using Fodinae.World.Terrain;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.World.Lighting.Pipeline;
public readonly record struct LightingFrameContext(
    ComputeShader Compute,
    int FieldWidth,
    int FieldHeight,
    int BounceWidth,
    int BounceHeight,
    RenderTexture DirectTexture,
    RenderTexture StaticDirectTexture,
    RenderTexture BounceTexture,
    RenderTexture ResultTexture,
    RenderTexture MaterialField,
    RenderTexture StaticEmissionField,
    RenderTexture DynamicEmissionField,
    Material DynamicEmissionMaterial,
    ComputeBuffer? DynamicLightBuffer,
    TerrainRenderer TerrainRenderer,
    LightingGeometryRegistry GeometryRegistry,
    Vector4 WorldRect = default,
    float CellSize = 0f,
    int DynamicLightCount = 0);
