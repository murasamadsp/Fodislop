#nullable enable

using System;
using System.Collections;
using System.Text;
using Kern.Tests.World;
using Kern.World.Terrain;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

// This is deliberately a production-path test. It uses the real cell-data
// textures, the real address mesh and the real Terrain shader. A standalone
// probe shader cannot catch binding, keyword, ring-address or rasterizer bugs.
[TestFixture]
[Category("GPU")]
public sealed class TerrainQuantizationGpuPlayModeTests
{
    private const int GridSize = TerrainQuantizationOracle.GridSize;
    private const int Resolution = GridSize;
    private static readonly int _WorldLightDebugViewID = Shader.PropertyToID("_WorldLightDebugView");
    private static readonly int _WorldLightPerBlockID = Shader.PropertyToID("_WorldLightPerBlock");
    private static readonly int _PixelArtFilteringID = Shader.PropertyToID("_PixelArtFiltering");
    private static readonly int _TerrainAtlas0ID = Shader.PropertyToID("_TerrainAtlas0");
    private static readonly int _TerrainAtlas1ID = Shader.PropertyToID("_TerrainAtlas1");
    private static readonly int _BaseMapID = Shader.PropertyToID("_BaseMap");
    private static readonly int _FlowMapID = Shader.PropertyToID("_FlowMap");
    private static readonly int _TerrainDecalAtlasID = Shader.PropertyToID("_TerrainDecalAtlas");

    [UnityTest]
    public IEnumerator ProductionTerrainShader_RasterizesCellGeometryAndKeepsBackground()
    {
        Assume.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));

        Shader shader = Shader.Find("Universal Render Pipeline/Custom/Terrain");
        Assert.That(shader, Is.Not.Null, "The production Terrain shader is missing.");

        TerrainCellDataTextures cellData = new();
        TerrainCellIDMesh idMesh = new();
        Material material = new(shader!);
        Texture2D backgroundAtlas = CreateSolidTexture(new Color32(220, 32, 32, 255));
        Texture2D foregroundAtlas = CreateSolidTexture(new Color32(32, 220, 64, 255));
        Texture2D blackTransparent = CreateSolidTexture(new Color32(0, 0, 0, 0));
        RenderTexture target = CreateTarget();

        try
        {
            material.EnableKeyword("KERN_TERRAIN_CELLS");
            material.SetTexture(_BaseMapID, backgroundAtlas);
            material.SetTexture(_TerrainAtlas0ID, backgroundAtlas);
            material.SetTexture(_TerrainAtlas1ID, foregroundAtlas);
            material.SetTexture(_FlowMapID, blackTransparent);
            material.SetTexture(_TerrainDecalAtlasID, blackTransparent);
            material.SetFloat(_PixelArtFilteringID, 0f);
            Shader.SetGlobalInt(_WorldLightDebugViewID, 0);
            Shader.SetGlobalInt(_WorldLightPerBlockID, 0);

            idMesh.EnsureSize(1, 1, 1f, 1, 1);
            cellData.EnsureCapacity(1, 1);
            cellData.SetCell(
                0,
                0,
                TerrainCellDataPacker.BackgroundLayer,
                CreateCell(
                    atlasIndex: 0,
                    anchored: false,
                    TerrainCellGeometry.FromOffsets(
                        Vector3.zero,
                        Vector3.zero,
                        Vector3.zero,
                        Vector3.zero)));

            TerrainCellGeometry displaced = TerrainCellGeometry.FromOffsets(
                new Vector3(4f / 32f, 4f / 32f, 0f),
                Vector3.zero,
                Vector3.zero,
                Vector3.zero);
            cellData.SetCell(
                0,
                0,
                TerrainCellDataPacker.ForegroundLayer,
                CreateCell(atlasIndex: 1, anchored: true, displaced));
            cellData.Apply();
            cellData.BindGlobals(1f, 0, 0);
            Shader.SetGlobalVector(TerrainCellDataTextures.ViewOffsetID, Vector4.zero);

            TerrainQuantizedPolygon expectedPolygon = new(
                displaced.Corner00,
                displaced.Corner10,
                displaced.Corner11,
                displaced.Corner01);
            bool[,] expected = TerrainQuantizationOracle.Rasterize(expectedPolygon);

            yield return RenderProductionTerrain(material, idMesh.Mesh!, target, pass: 0);
            yield return AssertProductionPixels(target, expected, "anchored foreground");

            // Stale geometry in the ring slot must not move a non-anchored
            // cell. This is a regression the old helper probe could never see
            // because it had no production cell textures.
            cellData.SetCell(
                0,
                0,
                TerrainCellDataPacker.ForegroundLayer,
                CreateCell(atlasIndex: 1, anchored: false, displaced));
            cellData.MarkCells(0, 0, 1, 1);
            cellData.Apply();
            cellData.BindGlobals(1f, 0, 0);
            yield return RenderProductionTerrain(material, idMesh.Mesh!, target, pass: 0);
            yield return AssertProductionPixels(target, CreateFullMask(), "unanchored foreground");
        }
        finally
        {
            cellData.Dispose();
            idMesh.Dispose();
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(backgroundAtlas);
            UnityEngine.Object.DestroyImmediate(foregroundAtlas);
            UnityEngine.Object.DestroyImmediate(blackTransparent);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static TerrainCellTexels CreateCell(
        int atlasIndex,
        bool anchored,
        TerrainCellGeometry geometry)
    {
        byte drawn = (byte)(atlasIndex + 1);
        byte uvBits = TerrainCellDataPacker.PackCornerUvs(CreateUvQuad());
        Color32 meta = new(drawn, uvBits, 0, anchored ? byte.MaxValue : (byte)0);
        return new TerrainCellTexels(
            Color.white,
            meta,
            Half4(0f, 0f, 1f, 1f),
            Half4(1f, 1f, 1f, 1f),
            Half4(0f, 0f, 1f, 1f),
            Vector4.zero,
            Vector4.zero,
            Half4(
                geometry.Corner00.x,
                geometry.Corner10.x,
                geometry.Corner11.x,
                geometry.Corner01.x),
            Half4(
                geometry.Corner00.y,
                geometry.Corner10.y,
                geometry.Corner11.y,
                geometry.Corner01.y));
    }

    private static TerrainVertex[] CreateUvQuad()
    {
        TerrainVertex[] quad = new TerrainVertex[4];
        quad[0].UV0 = new Vector2(0f, 0f);
        quad[1].UV0 = new Vector2(1f, 0f);
        quad[2].UV0 = new Vector2(1f, 1f);
        quad[3].UV0 = new Vector2(0f, 1f);
        return quad;
    }

    private static TerrainHalfTexel Half4(float x, float y, float z, float w) =>
        new(TerrainVertex.H(x), TerrainVertex.H(y), TerrainVertex.H(z), TerrainVertex.H(w));

    private static RenderTexture CreateTarget()
    {
        RenderTexture target = new(Resolution, Resolution, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1,
        };
        target.Create();
        return target;
    }

    private static Texture2D CreateSolidTexture(Color32 color)
    {
        Texture2D texture = new(1, 1, TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixel(0, 0, color);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    private static IEnumerator RenderProductionTerrain(
        Material material,
        Mesh idMesh,
        RenderTexture target,
        int pass)
    {
        CommandBuffer command = new()
        {
            name = "TerrainQuantizationProductionPath",
        };
        command.SetRenderTarget(target);
        command.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);
        command.SetViewProjectionMatrices(
            Matrix4x4.identity,
            GL.GetGPUProjectionMatrix(
                Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f),
                renderIntoTexture: true));
        command.DrawMesh(idMesh, Matrix4x4.identity, material, 0, pass);
        Graphics.ExecuteCommandBuffer(command);
        command.Release();
        yield return null;
    }

    private static IEnumerator AssertProductionPixels(
        RenderTexture target,
        bool[,] expectedForeground,
        string caseName)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
            target,
            0,
            TextureFormat.RGBA32);
        while (!request.done)
        {
            yield return null;
        }

        Assert.That(request.hasError, Is.False, $"GPU readback failed for {caseName}.");
        NativeArray<Color32> pixels = request.GetData<Color32>();
        var failures = new StringBuilder();
        int blackPixels = 0;
        int lowAlphaPixels = 0;
        int normalMismatches = 0;
        int flippedMismatches = 0;
        var mismatchCoordinates = new StringBuilder();
        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                Color32 pixel = pixels[(y * Resolution) + x];
                if (pixel.a < 240)
                {
                    lowAlphaPixels++;
                }

                if (pixel.r < 8 && pixel.g < 8 && pixel.b < 8)
                {
                    blackPixels++;
                }
            }
        }

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                Color32 pixel = pixels[(y * Resolution) + x];
                bool actualForeground = pixel.g > pixel.r + 40;
                normalMismatches += actualForeground != expectedForeground[x, y] ? 1 : 0;
                flippedMismatches += actualForeground != expectedForeground[x, GridSize - 1 - y] ? 1 : 0;
                if (actualForeground != expectedForeground[x, y] && mismatchCoordinates.Length < 512)
                {
                    mismatchCoordinates.Append($" ({x},{y}) actual={actualForeground} expected={expectedForeground[x, y]}");
                }
            }
        }

        if (blackPixels != 0 || lowAlphaPixels != 0 || Math.Min(normalMismatches, flippedMismatches) != 0)
        {
            failures.Append($"black={blackPixels}, lowAlpha={lowAlphaPixels}, ");
            failures.Append($"maskMismatch={Math.Min(normalMismatches, flippedMismatches)} normal={normalMismatches} flipped={flippedMismatches}");
            failures.Append(mismatchCoordinates);
        }

        Assert.That(failures.ToString(), Is.Empty, $"Production terrain render mismatch ({caseName}).");
    }

    private static bool[,] CreateFullMask()
    {
        bool[,] mask = new bool[GridSize, GridSize];
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                mask[x, y] = true;
            }
        }

        return mask;
    }
}
