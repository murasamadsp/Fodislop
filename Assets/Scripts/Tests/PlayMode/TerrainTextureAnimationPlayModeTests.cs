#nullable enable

using System.Collections;
using System.Collections.Generic;
using Kern.World;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class TerrainTextureAnimationPlayModeTests
{
    private const string TerrainShaderName = "Universal Render Pipeline/Custom/Terrain";
    private const float AnimationCycleSeconds = 2.5f;
    private readonly List<Object> _ownedObjects = [];
    private readonly int[] _vectorGlobalIds =
    [
        TerrainCellBuffers.GridSizeId,
        TerrainCellBuffers.OriginId,
        TerrainCellBuffers.ViewOffsetId,
    ];
    private static readonly int s_terrainDebugViewId = Shader.PropertyToID("_TerrainDebugView");
    private static readonly int s_worldLightDebugViewId = Shader.PropertyToID("_WorldLightDebugView");
    private static readonly int s_pixelArtFilteringId = Shader.PropertyToID("_PixelArtFiltering");
    private Vector4[] _previousGlobalVectors = null!;
    private int _previousTerrainDebugView;
    private int _previousWorldLightDebugView;
    private float _previousPixelArtFiltering;
    private TerrainCellBuffers _cellData = null!;
    private float _previousTimeScale;
    private bool _worldLightingKeywordWasEnabled;
    private Texture2D _readback = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _previousGlobalVectors = new Vector4[_vectorGlobalIds.Length];
        for (int index = 0; index < _vectorGlobalIds.Length; index++)
        {
            _previousGlobalVectors[index] = Shader.GetGlobalVector(_vectorGlobalIds[index]);
        }

        _previousTerrainDebugView = Shader.GetGlobalInteger(s_terrainDebugViewId);
        _previousWorldLightDebugView = Shader.GetGlobalInteger(s_worldLightDebugViewId);
        _previousPixelArtFiltering = Shader.GetGlobalFloat(s_pixelArtFilteringId);
        Shader.SetGlobalInteger(s_terrainDebugViewId, 0);
        Shader.SetGlobalInteger(s_worldLightDebugViewId, 0);
        Shader.SetGlobalFloat(s_pixelArtFilteringId, 0f);
        _previousTimeScale = Time.timeScale;
        _worldLightingKeywordWasEnabled = Shader.IsKeywordEnabled("KERN_WORLD_LIGHTING");
        Shader.DisableKeyword("KERN_WORLD_LIGHTING");
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = _previousTimeScale;
        if (_worldLightingKeywordWasEnabled)
        {
            Shader.EnableKeyword("KERN_WORLD_LIGHTING");
        }
        else
        {
            Shader.DisableKeyword("KERN_WORLD_LIGHTING");
        }

        for (int index = 0; index < _vectorGlobalIds.Length; index++)
        {
            Shader.SetGlobalVector(_vectorGlobalIds[index], _previousGlobalVectors[index]);
        }

        Shader.SetGlobalInteger(s_terrainDebugViewId, _previousTerrainDebugView);
        Shader.SetGlobalInteger(s_worldLightDebugViewId, _previousWorldLightDebugView);
        Shader.SetGlobalFloat(s_pixelArtFilteringId, _previousPixelArtFiltering);
        _cellData?.Dispose();
        foreach (Object ownedObject in _ownedObjects)
        {
            if (ownedObject != null)
            {
                Object.Destroy(ownedObject);
            }
        }

        _ownedObjects.Clear();
        yield return null;
    }

    [UnityTest]
    [Timeout(30_000)]
    public IEnumerator ProductionTerrainPass_AdvancesTextureAtlasFrames()
    {
        Shader shader = Shader.Find(TerrainShaderName);
        Assert.That(shader, Is.Not.Null, $"Production terrain shader '{TerrainShaderName}' was not found.");
        Assert.That(shader!.isSupported, Is.True, "Production terrain shader is unsupported on this graphics device.");

        Material material = Own(new Material(shader));
        Texture2D atlas = CreateTwoFrameAtlas();
        material.SetTexture("_TerrainAtlas0", atlas);
        material.SetTexture("_TerrainDecalAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_TerrainDecalRockAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_FlowMap", CreateSolidTexture(Color.gray));
        material.SetTexture("_PrismaticFlowMap", CreateSolidTexture(Color.gray));
        material.SetFloat("_AlphaCutoff", 0.01f);
        material.SetFloat("_GroundDecalStrength", 0f);
        material.SetFloat("_RockDecalStrength", 0f);

        Mesh mesh = Own(CreateTerrainAnimationQuad());
        // Атлас 64×64: клетка 32×32 (тайл — полатласа, как выводит шейдер из
        // 32 текселей на клетку), два кадра по 32 строки.
        UploadSingleCell(
            new TerrainTypeFields(
                Type: (MinesServer.Data.CellType)1,
                Block: Block(CellSurfaceEffect.Plain, 1f),
                Slot: 0,
                AtlasRect: new Vector4(0f, 0f, 0.5f, 0.5f),
                TileSize: 0.5f,
                FrameCount: 2,
                FrameHeightTiles: 1f,
                OpaqueOwn: false,
                OpaqueAny: false,
                HasTileGroup: false,
                TileGroupId: 0),
            originX: 0,
            worldHeight: 1);
        RenderTexture target = Own(new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32)
        {
            name = "TerrainAnimationRegressionTarget",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        });
        Assert.That(target.Create(), Is.True, "Could not allocate the terrain animation render target.");
        _readback = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            1, 1, "TerrainAnimationReadback", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));

        int pass = material.FindPass("Universal2D");
        Assert.That(pass, Is.GreaterThanOrEqualTo(0), "Production terrain shader has no Universal2D pass.");

        var observedRedFrame = false;
        var observedBlueFrame = false;
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() +
            (long)(AnimationCycleSeconds * System.Diagnostics.Stopwatch.Frequency);

        while (System.Diagnostics.Stopwatch.GetTimestamp() < deadline &&
            !(observedRedFrame && observedBlueFrame))
        {
            Color sample = RenderAndReadCenter(mesh, material, pass, target);
            observedRedFrame |= sample.r > sample.b * 2f && sample.r > 0.2f;
            observedBlueFrame |= sample.b > sample.r * 2f && sample.b > 0.2f;
            yield return null;
        }

        Assert.That(observedRedFrame, Is.True,
            "The production terrain pass never rendered atlas frame 0 (red). " +
            "Check the mesh animation attributes, atlas binding, UV resolution, and shader pass.");
        Assert.That(observedBlueFrame, Is.True,
            "The production terrain pass never advanced to atlas frame 1 (blue). " +
            "Check animation frame count/speed propagation and the _Time-based UV offset.");
    }

    [UnityTest]
    [Timeout(30_000)]
    public IEnumerator ProductionTerrainPass_AnimatesMoltenLavaSurface()
    {
        Shader shader = Shader.Find(TerrainShaderName);
        Assert.That(shader, Is.Not.Null, $"Production terrain shader '{TerrainShaderName}' was not found.");
        Assert.That(shader!.isSupported, Is.True, "Production terrain shader is unsupported on this graphics device.");

        Material material = Own(new Material(shader));
        Texture2D lavaAtlas = CreateSolidTexture(new Color(0.9f, 0.12f, 0.015f, 1f), 32);
        material.SetTexture("_TerrainAtlas0", lavaAtlas);
        material.SetTexture("_TerrainDecalAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_TerrainDecalRockAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_FlowMap", CreateSolidTexture(Color.gray));
        material.SetTexture("_PrismaticFlowMap", CreateSolidTexture(Color.gray));
        material.SetFloat("_AlphaCutoff", 0.01f);
        material.SetFloat("_GroundDecalStrength", 0f);
        material.SetFloat("_RockDecalStrength", 0f);

        Mesh mesh = Own(CreateTerrainAnimationQuad());
        // Лава: поверхность расплава, мировая клетка (4, 4) — фаза потока от неё.
        UploadSingleCell(
            new TerrainTypeFields(
                Type: (MinesServer.Data.CellType)1,
                Block: Block(CellSurfaceEffect.Molten, 10f),
                Slot: 0,
                AtlasRect: new Vector4(0f, 0f, 1f, 1f),
                TileSize: 1f,
                FrameCount: 1,
                FrameHeightTiles: 1f,
                OpaqueOwn: false,
                OpaqueAny: false,
                HasTileGroup: false,
                TileGroupId: 0),
            originX: 4,
            worldHeight: 5);

        RenderTexture target = Own(new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32)
        {
            name = "TerrainMoltenAnimationRegressionTarget",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        });
        Assert.That(target.Create(), Is.True, "Could not allocate the molten terrain render target.");
        _readback = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            1, 1, "TerrainMoltenReadback", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));

        int pass = material.FindPass("Universal2D");
        Assert.That(pass, Is.GreaterThanOrEqualTo(0), "Production terrain shader has no Universal2D pass.");

        Color initial = RenderAndReadCenter(mesh, material, pass, target);
        bool changed = false;
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() +
            (long)(AnimationCycleSeconds * System.Diagnostics.Stopwatch.Frequency);
        while (System.Diagnostics.Stopwatch.GetTimestamp() < deadline && !changed)
        {
            yield return null;
            Color current = RenderAndReadCenter(mesh, material, pass, target);
            changed = Mathf.Abs(current.r - initial.r) +
                Mathf.Abs(current.g - initial.g) +
                Mathf.Abs(current.b - initial.b) > 0.04f;
        }

        Assert.That(changed, Is.True,
            "Lava's production terrain pass stayed static. Check the molten profile in mesh animation data and its time-based shader branch.");
    }

    // Одна клетка переднего плана типа 1 через рабочий формат и рабочую
    // выгрузку: строка типа, клетка (кайма вокруг пуста), глобальные адреса окна.
    // Тип 1 — квадрат на земле: передний план, без каймы и свечения.
    private static BlockDefinition Block(CellSurfaceEffect surfaceEffect, float animationSpeed) => new(
        DrawLayer: CellDrawLayer.Background,
        Glow: 0f,
        Outline: CellOutline.Pliant,
        TextureAnchor: CellTextureAnchor.Cell,
        AnimationType: MinesServer.Data.CellAnimationType.None,
        AnimationSpeed: animationSpeed,
        SurfaceEffect: surfaceEffect,
        SurfaceEffectPalette: 0,
        DecalAtlas: CellDecalAtlas.None,
        RimMass: 0,
        MapColor: default);

    private void UploadSingleCell(TerrainTypeFields surface, int originX, int worldHeight)
    {
        var type = surface.Type;
        _cellData = new TerrainCellBuffers();
        _cellData.EnsureCapacity(1, 1);
        _cellData.SetType(type, TerrainCellData.PackType(surface));
        _cellData.SetCell(
            originX,
            0,
            TerrainCellData.PackCell(type));
        _cellData.MarkAllDirty();
        _cellData.Apply();
        _cellData.BindGlobals(cellSize: 2f, originX, originY: 0, worldWidth: originX + 1, worldHeight, distortionStyle: TerrainCellFormat.DistortionStyleOff);
        Shader.SetGlobalVector(TerrainCellBuffers.ViewOffsetId, Vector4.zero);
    }

    private Texture2D CreateTwoFrameAtlas()
    {
        var atlas = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            64, 64, "TerrainAnimationRegressionAtlas", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp));
        var pixels = new Color32[64 * 64];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = index < 32 * 64
                ? new Color32(255, 0, 0, 255)
                : new Color32(0, 0, 255, 255);
        }

        atlas.SetPixels32(pixels);
        atlas.Apply(false, false);
        return atlas;
    }

    private Texture2D CreateSolidTexture(Color color, int size = 1)
    {
        var texture = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            size, size, "TerrainAnimationSolid", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp));
        var pixels = new Color[size * size];
        System.Array.Fill(pixels, color);
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private Mesh CreateTerrainAnimationQuad()
    {
        var mesh = new Mesh { name = "TerrainAnimationRegressionMesh" };
        mesh.vertices =
        [
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
        ];
        mesh.uv = [Vector2.zero, Vector2.right, Vector2.one, Vector2.up];
        mesh.triangles = [0, 1, 2, 0, 2, 3];
        mesh.RecalculateBounds();
        return mesh;
    }

    private Color RenderAndReadCenter(Mesh mesh, Material material, int pass, RenderTexture target)
    {
        var commandBuffer = new CommandBuffer { name = "Terrain animation regression capture" };
        commandBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        commandBuffer.SetRenderTarget(target);
        commandBuffer.ClearRenderTarget(true, true, Color.black);
        commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
        Graphics.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Release();

        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            _readback.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0, false);
            _readback.Apply(false, false);
            return _readback.GetPixel(0, 0);
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private T Own<T>(T value)
        where T : Object
    {
        _ownedObjects.Add(value);
        return value;
    }
}
