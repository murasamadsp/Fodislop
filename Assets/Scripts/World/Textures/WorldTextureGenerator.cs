#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Textures;

public static class WorldTextureGenerator
{
    public static Texture2D CreatePrismaticFlowMap()
    {
        return LoadNumericalTexture(
            Kern.Core.ProjectRuntimeContracts.ResourcePaths.PrismaticFlowMap, 160, 128, FilterMode.Bilinear);
    }

    private static Texture2D LoadNumericalTexture(string resource, int width, int height, FilterMode filter)
    {
        TextAsset data = Resources.Load<TextAsset>(resource);
        if (data == null)
        {
            throw new System.InvalidOperationException($"Missing numerical terrain texture: {resource}.");
        }

        byte[] pixels = data.bytes;
        Resources.UnloadAsset(data);
        if (pixels.Length != width * height * 4)
        {
            throw new System.InvalidOperationException($"Invalid numerical terrain texture dimensions: {resource}.");
        }

        var texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            width,
            height,
            resource,
            RuntimeTextureColorSpace.Linear,
            filter,
            TextureWrapMode.Repeat);
        texture.LoadRawTextureData(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    public static Texture2D CreateFlowMap()
    {
        var texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            12,
            10,
            "ShimmerFlowMap",
            RuntimeTextureColorSpace.Linear,
            FilterMode.Bilinear,
            TextureWrapMode.Repeat);

        var random = new System.Random(42);
        var pixels = new Color[12 * 10];
        for (int i = 0; i < pixels.Length; i++)
        {
            float h = (float)random.NextDouble();
            pixels[i] = Color.HSVToRGB(h, 1f, 1f);
        }

        texture.SetPixels(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    public static Texture2D CreateMapColorCellTexture(CellType cellType, int cellSize, Color mapColor)
    {
        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            cellSize,
            cellSize,
            $"MissingCell_{(int)cellType}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        Color[] pixels = new Color[cellSize * cellSize];
        Array.Fill(pixels, mapColor);

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
