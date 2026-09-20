#nullable enable

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

    public static Texture2D CreateXGreenFacets()
    {
        return LoadNumericalTexture(
            Kern.Core.ProjectRuntimeContracts.ResourcePaths.XGreenFacets, 320, 320, FilterMode.Point);
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

    public static Texture2D CreateMissingCellTexture(CellType cellType, int cellSize)
    {
        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            cellSize,
            cellSize,
            $"MissingCell_{(int)cellType}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        int seed = unchecked((int)cellType * 397) ^ 0x5F3759DF;
        float baseHue = (float)((seed & 0xFFFF) / 65536.0);
        Color primaryColor = Color.HSVToRGB(baseHue, 0.85f, 0.90f);
        Color secondaryColor = Color.HSVToRGB((baseHue + 0.5f) % 1.0f, 0.70f, 0.35f);
        Color borderColor = Color.HSVToRGB(baseHue, 0.95f, 0.20f);

        Color[] pixels = new Color[cellSize * cellSize];
        for (int y = 0; y < cellSize; y++)
        {
            for (int x = 0; x < cellSize; x++)
            {
                bool isBorder = x == 0 || y == 0 || x == cellSize - 1 || y == cellSize - 1;
                bool isCross = x == y || x == (cellSize - 1 - y);
                bool isChecker = (((x / 4) + (y / 4)) & 1) == 0;

                Color pixelColor = isBorder
                    ? borderColor
                    : isCross || isChecker ? primaryColor : secondaryColor;

                pixels[(y * cellSize) + x] = pixelColor;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
