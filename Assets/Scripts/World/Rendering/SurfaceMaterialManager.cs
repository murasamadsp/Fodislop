#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;

namespace Fodinae.World;

/// <summary>
/// Manages materials, shader keywords, and property updates for surface rendering bands.
/// </summary>
public sealed class SurfaceMaterialManager
{
    private const string SurfaceShaderName = ProjectRuntimeContracts.ShaderNames.WorldSurface;
    private const string RedRockKeyword = "FODINAE_SURFACE_REDROCK";
    private const string TransitKeyword = "FODINAE_SURFACE_TRANSIT";
    private const string PerspectiveKeyword = "FODINAE_SURFACE_PERSPECTIVE";

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
    private static readonly int OccupancyId = Shader.PropertyToID("_Occupancy");
    private static readonly int BaseMapTileCountId = Shader.PropertyToID("_BaseMapTileCount");
    private static readonly int WorldSizeId = Shader.PropertyToID("_WorldSize");

    public enum SurfaceKind
    {
        RedRock,
        Transit,
        Perspective,
    }

    public Material CreateSurfaceMaterial(
        Texture2D texture,
        Color emissionColor,
        float emissionStrength,
        float occupancy,
        Vector2 baseMapTileCount,
        Vector2 worldSize,
        SurfaceKind kind,
        string materialName)
    {
        Shader surfaceShader = Shader.Find(SurfaceShaderName);
        if (surfaceShader == null || !surfaceShader.isSupported)
        {
            throw new InvalidOperationException(
                $"Required surface shader '{SurfaceShaderName}' is missing or unsupported.");
        }

        var material = new Material(surfaceShader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave,
        };
        RequireShaderProperties(material);
        material.SetTexture(BaseMapId, texture);
        material.SetColor(EmissionColorId, emissionColor);
        material.SetFloat(EmissionStrengthId, emissionStrength);
        material.SetFloat(OccupancyId, occupancy);
        material.SetVector(
            BaseMapTileCountId,
            new Vector4(baseMapTileCount.x, baseMapTileCount.y, 0f, 0f));
        material.SetVector(
            WorldSizeId,
            new Vector4(worldSize.x, worldSize.y, 0f, 0f));
        material.EnableKeyword(kind switch
        {
            SurfaceKind.RedRock => RedRockKeyword,
            SurfaceKind.Transit => TransitKeyword,
            SurfaceKind.Perspective => PerspectiveKeyword,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown surface kind."),
        });
        return material;
    }

    public void ApplyMaterialConfig(
        Material material,
        Color emissionColor,
        float emissionStrength,
        float occupancy)
    {
        material.SetColor(EmissionColorId, emissionColor);
        material.SetFloat(EmissionStrengthId, emissionStrength);
        material.SetFloat(OccupancyId, occupancy);
    }

    public void SetMaterialWorldSize(
        Material? transitMaterial,
        Material? perspectiveMaterial,
        Material? redRockMaterial,
        int worldWidth,
        int worldHeight)
    {
        if (transitMaterial == null || perspectiveMaterial == null || redRockMaterial == null)
        {
            throw new InvalidOperationException("SurfaceRenderer materials must be initialized.");
        }

        Vector4 worldSize = new(worldWidth, worldHeight, 0f, 0f);
        transitMaterial.SetVector(WorldSizeId, worldSize);
        perspectiveMaterial.SetVector(WorldSizeId, worldSize);
        redRockMaterial.SetVector(WorldSizeId, worldSize);
    }

    public Vector2 GetTerrainSheetTileCount(Texture2D texture)
    {
        const int tileSize = RenderingConstants.CELL_SIZE;
        if (texture.width <= 0 || texture.height <= 0 ||
            texture.width % tileSize != 0 || texture.height % tileSize != 0)
        {
            throw new InvalidOperationException(
                $"Surface terrain sheet '{texture.name}' dimensions " +
                $"{texture.width}x{texture.height} must be positive multiples " +
                $"of the terrain tile size {tileSize}.");
        }

        return new Vector2(texture.width / tileSize, texture.height / tileSize);
    }

    private static void RequireShaderProperties(Material material)
    {
        string[] requiredProperties =
        [
            "_BaseMap",
            "_EmissionColor",
            "_EmissionStrength",
            "_Occupancy",
            "_BaseMapTileCount",
            "_WorldSize",
        ];
        foreach (string propertyName in requiredProperties)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"World surface shader '{material.shader.name}' is missing required property " +
                    $"'{propertyName}'. Client graphics settings cannot be applied.");
            }
        }
    }
}
