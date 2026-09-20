#nullable enable

using System.Collections;
using System.Text;
using Kern.Tests.World;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class TerrainQuantizationGpuPlayModeTests
{
    private const int Resolution = 32;

    [UnityTest]
    public IEnumerator TerrainCoverageMatchesIndependentOracleInBothPasses()
    {
        Shader shader = Shader.Find("Hidden/Kern/TerrainQuantizationProbe");
        Assert.That(shader, Is.Not.Null, "The quantization probe shader is missing.");

        Material material = new(shader!);
        RenderTexture target = new(Resolution, Resolution, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        target.Create();

        TerrainQuantizedPolygon[] cases =
        [
            TerrainQuantizationOracle.FromSteps(0, 0, 0, 0, 0, 0, 0, 0),
            TerrainQuantizationOracle.FromSteps(1, 1, 0, 0, 0, 0, 0, 0),
            TerrainQuantizationOracle.FromSteps(-5, 3, 4, -2, 6, 5, -3, 7),
            TerrainQuantizationOracle.FromSteps(7, -9, -4, 5, 11, 2, -8, -1),
        ];

        try
        {
            foreach (TerrainQuantizedPolygon polygon in cases)
            {
                bool[,] expected = TerrainQuantizationOracle.Rasterize(polygon);
                SetGeometry(material, polygon, anchored: true, applyGeometry: true);

                for (int pass = 0; pass < 2; pass++)
                {
                    yield return RenderAndAssert(material, target, expected, pass, Describe(polygon));
                }
            }

            // An unanchored cell and a material-field cell with geometry
            // disabled must remain full quads. This catches accidentally
            // applying the shape mask to background/overlay data.
            TerrainQuantizedPolygon displaced = cases[2];
            bool[,] full = TerrainQuantizationOracle.Rasterize(
                TerrainQuantizationOracle.FromSteps(0, 0, 0, 0, 0, 0, 0, 0));
            SetGeometry(material, displaced, anchored: false, applyGeometry: true);
            yield return RenderAndAssert(material, target, full, 0, "unanchored");
            SetGeometry(material, displaced, anchored: true, applyGeometry: false);
            yield return RenderAndAssert(material, target, full, 1, "geometry-disabled");
        }
        finally
        {
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(material);
        }
    }

    private static void SetGeometry(
        Material material,
        TerrainQuantizedPolygon polygon,
        bool anchored,
        bool applyGeometry)
    {
        material.SetVector("_CornersX", new Vector4(
            polygon.Corner00.x,
            polygon.Corner10.x,
            polygon.Corner11.x,
            polygon.Corner01.x));
        material.SetVector("_CornersY", new Vector4(
            polygon.Corner00.y,
            polygon.Corner10.y,
            polygon.Corner11.y,
            polygon.Corner01.y));
        material.SetFloat("_Anchored", anchored ? 1f : 0f);
        material.SetFloat("_ApplyGeometry", applyGeometry ? 1f : 0f);
    }

    private static IEnumerator RenderAndAssert(
        Material material,
        RenderTexture target,
        bool[,] expected,
        int pass,
        string description)
    {
        Graphics.Blit(Texture2D.blackTexture, target, material, pass);
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
        while (!request.done)
        {
            yield return null;
        }

        Assert.That(request.hasError, Is.False, $"GPU readback failed for {description}, pass {pass}.");
        NativeArray<Color32> pixels = request.GetData<Color32>();
        Assert.That(
            Compare(expected, pixels),
            Is.Empty,
            $"Quantization mismatch for {description}, pass {pass}.");
    }

    private static string Compare(bool[,] expected, NativeArray<Color32> actual)
    {
        var differences = new StringBuilder();
        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                bool actualOccupied = actual[(y * Resolution) + x].a >= 128;
                if (actualOccupied != expected[x, y])
                {
                    differences.Append($"({x},{y}) expected={expected[x, y]} actual={actualOccupied} ");
                }
            }
        }

        return differences.ToString();
    }

    private static string Describe(TerrainQuantizedPolygon polygon) =>
        $"{polygon.Corner00}, {polygon.Corner10}, {polygon.Corner11}, {polygon.Corner01}";
}
