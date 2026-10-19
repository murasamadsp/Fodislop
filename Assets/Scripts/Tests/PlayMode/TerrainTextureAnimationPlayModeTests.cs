#nullable enable

using System.Collections;
using System.Collections.Generic;
using Kern.World;
using Kern.World.Common.Rendering;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class TerrainTextureAnimationPlayModeTests
{
    private const string TerrainShaderName = "Universal Render Pipeline/Custom/Terrain";
    private const float AnimationCycleSeconds = 2.5f;
    private readonly List<Object> _ownedObjects = new();
    private readonly int[] _vectorGlobalIds =
    new[] {

        TerrainCellBuffers.GridSizeId,
        TerrainCellBuffers.OriginId,
        TerrainCellBuffers.ViewOffsetId,

    };
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
    private Camera? _renderCamera;
    private MeshFilter? _renderMesh;
    private MeshRenderer? _renderRenderer;

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

    [Test]
    public void LightingSurfacePublicationPreservesTerrainRimSetting()
    {
        int rimId = Shader.PropertyToID("_TerrainRimEnabled");
        float previousRim = Shader.GetGlobalFloat(rimId);
        try
        {
            foreach (float enabled in new[] { 0f, 1f })
            {
                Shader.SetGlobalFloat(rimId, enabled);
                for (int frame = 0; frame < 3; frame++)
                {
                    TerrainSurfaceShaderGlobals.ApplyShaderGlobals();
                    Assert.That(Shader.GetGlobalFloat(rimId), Is.EqualTo(enabled),
                        "Publishing lighting surface constants must preserve the terrain rim setting.");
                }
            }
        }
        finally
        {
            Shader.SetGlobalFloat(rimId, previousRim);
        }
    }

    [UnityTest]
    public IEnumerator ProductionTerrainRim_FollowsWorldNeighborsAndHonorsFalloff()
    {
        Material material = Own(new Material(Shader.Find(TerrainShaderName)));
        material.SetTexture("_TerrainAtlas0", CreateSolidTexture(Color.white, 32));
        material.SetTexture("_TerrainDecalAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_TerrainDecalRockAtlas", CreateSolidTexture(Color.clear));
        material.SetFloat("_AlphaCutoff", 0.01f);
        material.SetFloat("_GroundDecalStrength", 0f);
        material.SetFloat("_RockDecalStrength", 0f);
        material.SetFloat("_RoundableCornerRadius", 0.51f);
        material.SetFloat("_RimDistanceScale", 4f);
        material.SetFloat("_RimFalloff", 0.5f);
        material.SetFloat("_RimQuantizationEnabled", 1f);
        int stageId = Shader.PropertyToID("_KernTerrainBenchmarkStage");
        int rimId = Shader.PropertyToID("_TerrainRimEnabled");
        int previousStage = Shader.GetGlobalInteger(stageId);
        float previousRim = Shader.GetGlobalFloat(rimId);
        material.SetFloat(rimId, 1f);
        Mesh mesh = Own(CreateTerrainAnimationQuad());
        mesh.SetVertexBufferParams(4, TerrainCellIdMesh.VertexLayout);
        mesh.SetVertexBufferData(new[]
        {
            new TerrainCellIdVertex(1, 1, 1, 0),
            new TerrainCellIdVertex(1, 1, 1, 1),
            new TerrainCellIdVertex(1, 1, 1, 2),
            new TerrainCellIdVertex(1, 1, 1, 3),
        }, 0, 0, 4);
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _cellData = new TerrainCellBuffers();
        _cellData.EnsureCapacity(3, 3);
        _cellData.SetType(0, TerrainCellData.PackType(new TerrainTypeFields(
            Type: 0, Block: Block(CellSurfaceEffect.Plain, 0f),
            Slot: 0, AtlasRect: Vector4.zero, TileSize: 1f,
            FrameCount: 1, FrameHeightTiles: 1f, OpaqueOwn: false, OpaqueAny: false,
            HasTileGroup: false, TileGroupId: 0)));
        var type = (MinesServer.Data.CellType)1;
        _cellData.SetType(type, TerrainCellData.PackType(new TerrainTypeFields(
            Type: type,
            Block: new BlockDefinition(
                DrawLayer: CellDrawLayer.Foreground, Glow: 0f, Outline: CellOutline.Round,
                TextureAnchor: CellTextureAnchor.Cell, AnimationType: MinesServer.Data.CellAnimationType.None,
                AnimationSpeed: 0f, SurfaceEffect: CellSurfaceEffect.Plain, SurfaceEffectPalette: 0,
                DecalAtlas: CellDecalAtlas.None, RimMass: 1, MapColor: default),
            Slot: 0, AtlasRect: new Vector4(0, 0, 1, 1), TileSize: 1f,
            FrameCount: 1, FrameHeightTiles: 1f, OpaqueOwn: false, OpaqueAny: false,
            HasTileGroup: false, TileGroupId: 0)));
        RenderTexture target = Own(new RenderTexture(32, 32, 0, RenderTextureFormat.ARGBHalf));
        Assert.That(target.Create(), Is.True);
        Texture2D pixels = Own(RuntimeTextureFactory.CreateRGBAHalfNoMip(32, 32,
            "RimProductionReadback", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp));
        try
        {
            Shader.SetGlobalInteger(stageId, 4);
            // A conflicting global must not disable the material-owned switch.
            Shader.SetGlobalFloat(rimId, 0f);
            // Four directed connections, a cross with four concave corners,
            // and an isolated rounded cell.
            for (int side = 0; side < 6; side++)
            {
                for (int y = 0; y < 3; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        bool neighbor = side == 4 ? x == 1 || y == 1
                            : side == 5 ? false : side == 0 ? x == 2 && y == 1
                            : side == 1 ? x == 0 && y == 1
                            : side == 2 ? x == 1 && y == 2 : x == 1 && y == 0;
                        _cellData.SetCell(x, y, TerrainCellData.PackCell(x == 1 && y == 1 || neighbor ? type : 0));
                    }
                }

                _cellData.MarkAllDirty();
                _cellData.Apply();
                _cellData.BindGlobals(2f, 0, 0, 3, 3, TerrainCellFormat.DistortionStyleOff);
                Shader.SetGlobalVector(TerrainCellBuffers.ViewOffsetId, Vector4.zero);
                using (var commands = new CommandBuffer())
                {
                    commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(2, 4, 2, 4, -10, 10));
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(true, true, Color.clear);
                    commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, material.FindPass("Universal2D"));
                    Graphics.ExecuteCommandBuffer(commands);
                }

                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                    pixels.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.temporaryCachePath,
                    $"kern-rim-side-{side}.png"), pixels.EncodeToPNG());

                for (int y = 2; y <= 29; y += 27)
                {
                    for (int x = 2; x <= 29; x += 27)
                    {
                        bool connected = side == 4 ? true : side == 5 ? false
                            : side == 0 ? x > 16 : side == 1 ? x < 16
                            : side == 2 ? y > 16 : y < 16;
                        Assert.That(pixels.GetPixel(x, y).a, connected ? Is.GreaterThan(0.9f) : Is.LessThan(0.1f),
                            $"World-oriented corner mismatch: side={side}, pixel={x},{y}.");
                    }
                }

                float darkestSurface = 1f;
                foreach (Color pixel in pixels.GetPixels())
                {
                    if (pixel.a > 0.9f)
                    {
                        darkestSurface = Mathf.Min(darkestSurface, pixel.r);
                        Assert.That(pixel.r, Is.GreaterThanOrEqualTo(0.49f),
                            "A 50% bevel must not compound into a darker corner.");
                    }
                }

                Assert.That(darkestSurface, Is.LessThan(0.85f),
                    $"The production rim must actually darken an exposed edge: pattern={side}.");

            }
        }
        finally
        {
            Shader.SetGlobalInteger(stageId, previousStage);
            Shader.SetGlobalFloat(rimId, previousRim);
        }

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
        mesh.SetVertexBufferParams(4, TerrainCellIdMesh.VertexLayout);
        mesh.SetVertexBufferData(new[]
        {
            new TerrainCellIdVertex(0, 0, 0, 0),
            new TerrainCellIdVertex(0, 0, 0, 1),
            new TerrainCellIdVertex(0, 0, 0, 2),
            new TerrainCellIdVertex(0, 0, 0, 3),
        }, 0, 0, 4);
        mesh.triangles = new[] {
0, 1, 2, 0, 2, 3
 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private Color RenderAndReadCenter(Mesh mesh, Material material, int pass, RenderTexture target)
    {
        if (_renderCamera == null)
        {
            GameObject cameraObject = Own(new GameObject("Terrain animation production camera"));
            _renderCamera = cameraObject.AddComponent<Camera>();
            _renderCamera.enabled = false;
            _renderCamera.orthographic = true;
            _renderCamera.orthographicSize = 1f;
            _renderCamera.aspect = 1f;
            _renderCamera.transform.position = new Vector3(1f, 1f, -10f);
            _renderCamera.cullingMask = 1 << 31;
            _renderCamera.clearFlags = CameraClearFlags.SolidColor;
            _renderCamera.backgroundColor = Color.black;
            _renderCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            GameObject meshObject = Own(new GameObject("Terrain animation production mesh"));
            meshObject.layer = 31;
            _renderMesh = meshObject.AddComponent<MeshFilter>();
            _renderRenderer = meshObject.AddComponent<MeshRenderer>();
        }
        _renderMesh!.sharedMesh = mesh;
        _renderRenderer!.sharedMaterial = material;
        // GPU-generated positions cover the cell even though the carrier stores addresses.
        mesh.bounds = new Bounds(new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 1f));
        _renderCamera.targetTexture = target;
        RenderPipeline.SubmitRenderRequest(_renderCamera, new UniversalRenderPipeline.SingleCameraRequest
        {
            destination = target,
        });

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
