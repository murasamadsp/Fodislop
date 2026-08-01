#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting;

[DisallowMultipleComponent]
public class TerrariaLightingEngine : MonoBehaviour
{
    public enum QualityPreset
    {
        Low,
        Medium,
        High,
        Ultra
    }

    public enum DebugView
    {
        Composite,
        AmbientOcclusion,
        Occlusion,
        DirectLight
    }

    private const string ComputeResourcePath = "Shaders/Lighting/WorldLighting";
    private const string QualityPreferenceKey = "WorldLightingQuality";
    private const int GpuLightStride = sizeof(float) * 8;
    private const int TileRangeStride = sizeof(int) * 2;
    private const int TileIndexStride = sizeof(int);
    private const int CoveragePixelsPerCell = 8;
    private const int LightingCacheAnchorCells = 4;
    private const int StaticLightClusterSize = 2;
    private static readonly Vector3 LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);

    private static readonly int OcclusionTextureId = Shader.PropertyToID("_OcclusionTexture");
    private static readonly int SdfTextureId = Shader.PropertyToID("_SdfTexture");
    private static readonly int SdfSeedInputId = Shader.PropertyToID("_SdfSeedInput");
    private static readonly int SdfSeedOutputId = Shader.PropertyToID("_SdfSeedOutput");
    private static readonly int SdfOutputId = Shader.PropertyToID("_SdfOutput");
    private static readonly int SdfSizeId = Shader.PropertyToID("_SdfSize");
    private static readonly int SdfJumpStepId = Shader.PropertyToID("_SdfJumpStep");
    private static readonly int ResultId = Shader.PropertyToID("_Result");
    private static readonly int StaticLightingTextureId = Shader.PropertyToID("_StaticLightingTexture");
    private static readonly int LightingPassModeId = Shader.PropertyToID("_LightingPassMode");
    private static readonly int LightFilterInputId = Shader.PropertyToID("_LightFilterInput");
    private static readonly int LightFilterOutputId = Shader.PropertyToID("_LightFilterOutput");
    private static readonly int LightFilterDirectionId = Shader.PropertyToID("_LightFilterDirection");
    private static readonly int LightFilterStrengthId = Shader.PropertyToID("_LightFilterStrength");
    private static readonly int LightFilterOcclusionSharpnessId = Shader.PropertyToID("_LightFilterOcclusionSharpness");
    private static readonly int LightsId = Shader.PropertyToID("_Lights");
    private static readonly int LightTileRangesId = Shader.PropertyToID("_LightTileRanges");
    private static readonly int LightTileIndicesId = Shader.PropertyToID("_LightTileIndices");
    private static readonly int LightTileGridSizeId = Shader.PropertyToID("_LightTileGridSize");
    private static readonly int LightTileWorldSizeId = Shader.PropertyToID("_LightTileWorldSize");
    private static readonly int GridSizeId = Shader.PropertyToID("_GridSize");
    private static readonly int OcclusionSizeId = Shader.PropertyToID("_OcclusionSize");
    private static readonly int OcclusionYFlipId = Shader.PropertyToID("_OcclusionYFlip");
    private static readonly int OutputSizeId = Shader.PropertyToID("_OutputSize");
    private static readonly int WorldRectId = Shader.PropertyToID("_WorldRect");
    private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    private static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");
    private static readonly int AoStrengthId = Shader.PropertyToID("_AoStrength");
    private static readonly int MaxRayStepsId = Shader.PropertyToID("_MaxRaySteps");
    private static readonly int AttenuationFactorId = Shader.PropertyToID("_AttenuationFactor");
    private static readonly int MinimumTransmissionId = Shader.PropertyToID("_MinimumTransmission");
    private static readonly int OpaqueOcclusionThresholdId = Shader.PropertyToID("_OpaqueOcclusionThreshold");
    private static readonly int RaymarchedShadowStrengthId = Shader.PropertyToID("_RaymarchedShadowStrength");
    private static readonly int ShadowSoftnessId = Shader.PropertyToID("_ShadowSoftness");
    private static readonly int OccluderHeightId = Shader.PropertyToID("_OccluderHeight");
    private static readonly int ShadowHeightSoftnessId = Shader.PropertyToID("_ShadowHeightSoftness");
    private static readonly int ShadowDensityId = Shader.PropertyToID("_ShadowDensity");
    private static readonly int MinimumTraceStepId = Shader.PropertyToID("_MinimumTraceStep");
    private static readonly int MaximumTraceStepId = Shader.PropertyToID("_MaximumTraceStep");
    private static readonly int SdfSolidThresholdId = Shader.PropertyToID("_SdfSolidThreshold");
    private static readonly int CornerSealRadiusId = Shader.PropertyToID("_CornerSealRadius");
    private static readonly int CoveragePixelsPerCellId = Shader.PropertyToID("_CoveragePixelsPerCell");
    private static readonly int AoCardinalDistanceId = Shader.PropertyToID("_AoCardinalDistance");
    private static readonly int AoDiagonalDistanceId = Shader.PropertyToID("_AoDiagonalDistance");
    private static readonly int AoFarDistanceId = Shader.PropertyToID("_AoFarDistance");
    private static readonly int AoFarWeightId = Shader.PropertyToID("_AoFarWeight");
    private static readonly int AoSolidInteriorWeightId = Shader.PropertyToID("_AoSolidInteriorWeight");
    private static readonly int AoBoundaryWeightId = Shader.PropertyToID("_AoBoundaryWeight");
    private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
    private static readonly int WorldLightTextureId = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int WorldLightRectId = Shader.PropertyToID("_WorldLightRect");

    private static TerrariaLightingEngine? s_instance;
    public static TerrariaLightingEngine? Instance => s_instance;

    private readonly record struct QualitySettings(
        int PixelsPerCell,
        int MaximumTextureDimension,
        int MaximumLightCount,
        int MaximumRaySteps,
        float UpdatesPerSecond);

    private readonly record struct CellLightConfig(Vector3 Emission);

    private struct ClusteredLight
    {
        public Vector2 WeightedPosition;
        public Vector3 WeightedColor;
        public float Weight;

        public void Add(Vector2 position, Vector3 color)
        {
            float luminance = Mathf.Max(0.01f, Vector3.Dot(color, LuminanceWeights));
            WeightedPosition += position * luminance;
            WeightedColor += color * luminance;
            Weight += luminance;
        }
    }

    private readonly struct ClusterCandidate
    {
        public readonly long Key;
        public readonly ClusteredLight Light;
        public readonly float DistanceSquared;

        public ClusterCandidate(long key, ClusteredLight light, float distanceSquared)
        {
            Key = key;
            Light = light;
            DistanceSquared = distanceSquared;
        }
    }

    private readonly struct EmissiveCell
    {
        public readonly int WorldX;
        public readonly int WorldY;
        public readonly Vector2 Position;
        public readonly Vector3 Color;

        public EmissiveCell(int worldX, int worldY, Vector2 position, Vector3 color)
        {
            WorldX = worldX;
            WorldY = worldY;
            Position = position;
            Color = color;
        }
    }

    private struct GpuLight
    {
        public Vector4 PositionRadius;
        public Vector4 ColorIntensity;

        public GpuLight(Vector2 position, float height, float radius, Vector3 color, float intensity)
        {
            PositionRadius = new Vector4(position.x, position.y, height, radius);
            ColorIntensity = new Vector4(color.x, color.y, color.z, intensity);
        }
    }

    [Header("Quality")]
    [SerializeField]
    private QualityPreset _quality = QualityPreset.Ultra;

    [Header("Light tuning")]
    [SerializeField, Tooltip("Base light present even when no direct light reaches the pixel.")]
    private Color _ambientColor = new(0.055f, 0.06f, 0.08f, 1f);
    [SerializeField, Min(0.1f), Tooltip("Radius, in world units, of light emitted by glowing cells.")]
    private float _staticLightRadius = 8f;
    [SerializeField, Min(0), Tooltip("Extra cells scanned outside the viewport in addition to the static light radius.")]
    private int _lightSafeBorder = 2;
    [SerializeField, Min(2), Tooltip("Culling tile size in cells. A pixel only inspects lights overlapping its tile.")]
    private int _lightCullingTileSize = 8;
    [SerializeField, Min(0f), Tooltip("Intensity multiplier for every glowing-cell light.")]
    private float _staticLightIntensity = 1f;
    [SerializeField, Range(0.1f, 4f), Tooltip("Height of glowing-cell lights above the terrain plane, in cells.")]
    private float _staticLightHeight = 2.5f;
    [SerializeField, Range(0.1f, 10f), Tooltip("Distance attenuation. Larger values make lights fall off faster.")]
    private float _attenuationFactor = 2.5f;
    [Header("Height-aware SDF shadows")]
    [SerializeField, Range(0.0001f, 0.1f), Tooltip("Stops cone tracing once visibility falls below this value.")]
    private float _minimumTransmission = 0.008f;
    [SerializeField, Range(0f, 1f), Tooltip("Coverage required to treat the receiver as part of a connected opaque mass.")]
    private float _opaqueOcclusionThreshold = 0.9f;
    [SerializeField, Range(0f, 1f), Tooltip("Maximum direct-light loss in a projected terrain shadow.")]
    private float _raymarchedShadowStrength = 0.95f;
    [SerializeField, Range(0f, 2f), Tooltip("Area-light cone radius used to create a smooth penumbra.")]
    private float _shadowSoftness = 0.7f;
    [SerializeField, Range(0.1f, 2f), Tooltip("Common height of terrain occluders above the ground plane, in cells.")]
    private float _occluderHeight = 0.65f;
    [SerializeField, Range(0.01f, 1f), Tooltip("Vertical penumbra that prevents a hard cutoff at the end of finite shadows.")]
    private float _shadowHeightSoftness = 0.16f;
    [SerializeField, Range(0.1f, 8f), Tooltip("Optical density accumulated through an occluder. Unlike shadow strength, this controls how quickly thick blockers become opaque.")]
    private float _shadowDensity = 4f;
    [SerializeField, Range(0.03f, 0.5f), Tooltip("Smallest SDF cone-tracing step, in cells.")]
    private float _minimumTraceStep = 0.125f;
    [SerializeField, Range(0.125f, 1f), Tooltip("Largest tracing step, in cells. Capping the step preserves thin and translucent texture details.")]
    private float _maximumTraceStep = 0.5f;
    [SerializeField, Range(0.05f, 0.95f), Tooltip("Alpha that contributes a hard SDF silhouette. Lower alpha still attenuates rays continuously without becoming solid geometry.")]
    private float _sdfSolidThreshold = 0.5f;
    [SerializeField, Range(0f, 0.35f), Tooltip("Chebyshev-radius, in cells, that seals a vertex shared only by two diagonal opaque cells.")]
    private float _cornerSealRadius = 0.1875f;
    [SerializeField, Range(0f, 1f), Tooltip("Edge-aware reconstruction that removes pixel steps and discrete ray bands from shadows.")]
    private float _shadowFilterStrength = 0.25f;
    [SerializeField, Range(1f, 64f), Tooltip("Prevents shadow filtering from bleeding light across opaque cell boundaries.")]
    private float _shadowFilterOcclusionSharpness = 24f;

    [Header("Ambient occlusion")]
    [SerializeField, Range(0f, 1f), Tooltip("Overall AO darkening. Set to zero to disable AO visually.")]
    private float _ambientOcclusionStrength = 0.68f;
    [SerializeField, Range(0.1f, 2f), Tooltip("Distance of the four horizontal and vertical AO taps, in cells.")]
    private float _aoCardinalDistance = 0.72f;
    [SerializeField, Range(0.1f, 2f), Tooltip("Distance of the four diagonal AO taps, in cells.")]
    private float _aoDiagonalDistance = 0.58f;
    [SerializeField, Range(0.5f, 4f), Tooltip("Distance of the wider four-tap AO sample, in cells.")]
    private float _aoFarDistance = 1.35f;
    [SerializeField, Range(0f, 1f), Tooltip("Blend between near and far AO samples.")]
    private float _aoFarWeight = 0.22f;
    [SerializeField, Range(0f, 1f), Tooltip("AO inside connected masses of opaque cells.")]
    private float _aoSolidInteriorWeight = 0.55f;
    [SerializeField, Range(0f, 2f), Tooltip("Additional AO where open and opaque areas meet.")]
    private float _aoBoundaryWeight = 0.45f;

    [Header("Diagnostics")]
    [SerializeField, Tooltip("Displays the selected lighting buffer through the terrain material.")]
    private DebugView _debugView;

    private readonly Dictionary<long, ClusteredLight> _clusteredLights = new();
    private readonly List<ClusterCandidate> _clusterCandidates = new();
    private readonly List<EmissiveCell> _emissiveCells = new();
    private readonly List<GpuLight> _staticLights = new();
    private QualitySettings _qualitySettings;

    private ComputeShader? _lightingCompute;
    private ComputeBuffer? _lightBuffer;
    private ComputeBuffer? _lightTileRangeBuffer;
    private ComputeBuffer? _lightTileIndexBuffer;
    private RenderTexture? _occlusionTexture;
    private RenderTexture? _sdfSeedTextureA;
    private RenderTexture? _sdfSeedTextureB;
    private RenderTexture? _sdfTexture;
    private RenderTexture? _staticLightmapTexture;
    private RenderTexture? _lightmapTexture;
    private RenderTexture? _lightmapScratchTexture;
    private GpuLight[] _gpuLights = Array.Empty<GpuLight>();
    private List<int>[] _lightTileLists = Array.Empty<List<int>>();
    private Vector2Int[] _lightTileRanges = Array.Empty<Vector2Int>();
    private int[] _lightTileIndices = Array.Empty<int>();

    private int _kernel;
    private int _filterKernel;
    private int _initializeSdfKernel;
    private int _jumpFloodSdfKernel;
    private int _finalizeSdfKernel;
    private int _gridWidth;
    private int _gridHeight;
    private int _occlusionWidth;
    private int _occlusionHeight;
    private int _outputWidth;
    private int _outputHeight;
    private int _lightTileGridWidth;
    private int _lightTileGridHeight;
    private bool _staticRegionDirty = true;
    private float _nextUpdateTime;
    private Vector4 _lastVisibleRegion = new(float.NaN, float.NaN, float.NaN, float.NaN);

    private Vector2 _playerAuraPos;
    private float _playerAuraRadius = 12f;
    private float _playerAuraIntensity = 1f;
    private float _playerAuraHeight = 2.5f;
    private bool _playerAuraEnabled = true;
    private bool _hasRenderedAuraState;
    private bool _lastRenderedAuraEnabled;
    private Vector2 _lastRenderedAuraPosition;
    private float _lastRenderedAuraRadius;
    private float _lastRenderedAuraIntensity;
    private float _lastRenderedAuraHeight;

    public QualityPreset Quality => _quality;

    public int RequiredTerrainPadding
    {
        get
        {
            const float cellSize = GameConstants.World.CELLSIZE;
            const float clusteredSourcePadding = 2f * cellSize * 0.70710678f;
            int staticPadding = Mathf.CeilToInt(
                (_staticLightRadius + _shadowSoftness + clusteredSourcePadding) / cellSize);
            int playerPadding = Mathf.CeilToInt(
                (_playerAuraRadius + _shadowSoftness) / cellSize);
            return Mathf.Max(
                1,
                Mathf.Max(staticPadding, playerPadding) + _lightSafeBorder + LightingCacheAnchorCells);
        }
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            DestroyLightingObject(this);
            return;
        }

        s_instance = this;
        int savedQuality = PlayerPrefs.GetInt(QualityPreferenceKey, (int)QualityPreset.Ultra);
        ApplyQualityPreset((QualityPreset)Mathf.Clamp(savedQuality, 0, (int)QualityPreset.Ultra), save: false);
        LoadComputeShader();
    }

    private void OnValidate()
    {
        ApplyQualityPreset(_quality, save: false);
        _staticRegionDirty = true;
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }

        _lightBuffer?.Release();
        _lightBuffer = null;
        _lightTileRangeBuffer?.Release();
        _lightTileRangeBuffer = null;
        _lightTileIndexBuffer?.Release();
        _lightTileIndexBuffer = null;

        if (_occlusionTexture != null)
        {
            _occlusionTexture.Release();
            DestroyLightingObject(_occlusionTexture);
            _occlusionTexture = null;
        }

        ReleaseRenderTexture(ref _sdfSeedTextureA);
        ReleaseRenderTexture(ref _sdfSeedTextureB);
        ReleaseRenderTexture(ref _sdfTexture);
        ReleaseRenderTexture(ref _staticLightmapTexture);

        if (_lightmapTexture != null)
        {
            _lightmapTexture.Release();
            DestroyLightingObject(_lightmapTexture);
            _lightmapTexture = null;
        }

        if (_lightmapScratchTexture != null)
        {
            _lightmapScratchTexture.Release();
            DestroyLightingObject(_lightmapScratchTexture);
            _lightmapScratchTexture = null;
        }
    }

    public void SetPlayerAura(Vector2 position, float radius, float intensity, float height = 2.5f)
    {
        _playerAuraPos = position;
        _playerAuraRadius = Mathf.Max(0.1f, radius);
        _playerAuraIntensity = Mathf.Max(0f, intensity);
        _playerAuraHeight = Mathf.Max(0.1f, height);
        _playerAuraEnabled = true;
    }

    public void DisablePlayerAura()
    {
        _playerAuraEnabled = false;
    }

    public void InvalidateStaticCache()
    {
        _staticRegionDirty = true;
    }

    public void InvalidateCell(int worldX, int worldY)
    {
        if (float.IsNaN(_lastVisibleRegion.x) ||
            worldX < _lastVisibleRegion.x - 1f ||
            worldX > _lastVisibleRegion.x + _lastVisibleRegion.z + 1f ||
            worldY < _lastVisibleRegion.y - 1f ||
            worldY > _lastVisibleRegion.y + _lastVisibleRegion.w + 1f)
        {
            return;
        }

        _staticRegionDirty = true;
    }

    public void SetQuality(QualityPreset quality)
    {
        int clampedQuality = Mathf.Clamp((int)quality, 0, (int)QualityPreset.Ultra);
        ApplyQualityPreset((QualityPreset)clampedQuality, save: true);
    }

    public void UpdateLighting(
        int visibleMinX,
        int visibleMinY,
        int visibleWidth,
        int visibleHeight,
        IWorldDataStorage? storage,
        MapManager? mapManager)
    {
        if (visibleWidth <= 0 || visibleHeight <= 0 || storage == null || mapManager == null)
        {
            return;
        }

        int gridMinX = SnapLightingRegion(visibleMinX + LightingCacheAnchorCells);
        int gridMinY = SnapLightingRegion(visibleMinY + LightingCacheAnchorCells);
        int gridWidth = Mathf.Max(2, visibleWidth - (LightingCacheAnchorCells * 2));
        int gridHeight = Mathf.Max(2, visibleHeight - (LightingCacheAnchorCells * 2));
        Vector4 visibleRegion = new(gridMinX, gridMinY, gridWidth, gridHeight);
        bool regionChanged = visibleRegion != _lastVisibleRegion;
        if (Application.isPlaying && !regionChanged && Time.unscaledTime < _nextUpdateTime)
        {
            return;
        }

        _lastVisibleRegion = visibleRegion;
        _nextUpdateTime = Time.unscaledTime + (1f / _qualitySettings.UpdatesPerSecond);

        if (!LoadComputeShader())
        {
            SetNeutralLighting(visibleMinX, visibleMinY, visibleWidth, visibleHeight);
            return;
        }

        const float cellSize = GameConstants.World.CELLSIZE;
        Vector4 worldRect = new(
            gridMinX * cellSize,
            gridMinY * cellSize,
            gridWidth * cellSize,
            gridHeight * cellSize);

        EnsureResources(gridWidth, gridHeight);
        if (_occlusionTexture == null || _sdfTexture == null ||
            _lightmapTexture == null || _lightmapScratchTexture == null ||
            _lightBuffer == null || _lightingCompute == null)
        {
            SetNeutralLighting(gridMinX, gridMinY, gridWidth, gridHeight);
            return;
        }

        bool rebuildStaticRegion =
            _staticRegionDirty ||
            regionChanged;
        if (rebuildStaticRegion)
        {
            BuildStaticLights(
                gridMinX,
                gridMinY,
                gridWidth,
                gridHeight,
                storage,
                mapManager);
            TerrainRenderer? terrainRenderer = TerrainRenderer.Instance;
            if (terrainRenderer == null || !terrainRenderer.RenderLightingCoverage(_occlusionTexture, worldRect))
            {
                ClearRenderTexture(_occlusionTexture);
            }

            if (_raymarchedShadowStrength > 0.001f)
            {
                BuildSignedDistanceField();
            }

            _staticRegionDirty = false;
        }

        bool auraStateChanged = HasAuraStateChanged(cellSize);
        if (!rebuildStaticRegion && !auraStateChanged)
        {
            return;
        }

        ConfigureLightingShader(worldRect, gridWidth, gridHeight, cellSize);

        if (rebuildStaticRegion)
        {
            int staticLightCount = UploadStaticLights();
            BuildAndUploadLightTiles(staticLightCount, worldRect, gridWidth, gridHeight, cellSize);
            if (!CanDispatchLighting())
            {
                SetNeutralLighting(gridMinX, gridMinY, gridWidth, gridHeight);
                return;
            }

            DispatchLighting(_staticLightmapTexture!, passMode: 0);
        }

        int dynamicLightCount = UploadPlayerLight();
        BuildAndUploadLightTiles(dynamicLightCount, worldRect, gridWidth, gridHeight, cellSize);
        if (!CanDispatchLighting())
        {
            SetNeutralLighting(gridMinX, gridMinY, gridWidth, gridHeight);
            return;
        }

        DispatchLighting(_lightmapTexture, passMode: 1);

        if (_shadowFilterStrength > 0.001f && _debugView == DebugView.Composite)
        {
            int filterGroupsX = Mathf.CeilToInt(_outputWidth / 8f);
            int filterGroupsY = Mathf.CeilToInt(_outputHeight / 8f);
            _lightingCompute.SetTexture(_filterKernel, OcclusionTextureId, _occlusionTexture);
            _lightingCompute.SetFloat(LightFilterStrengthId, _shadowFilterStrength);
            _lightingCompute.SetFloat(LightFilterOcclusionSharpnessId, _shadowFilterOcclusionSharpness);

            _lightingCompute.SetInts(LightFilterDirectionId, 1, 0);
            _lightingCompute.SetTexture(_filterKernel, LightFilterInputId, _lightmapTexture);
            _lightingCompute.SetTexture(_filterKernel, LightFilterOutputId, _lightmapScratchTexture);
            _lightingCompute.Dispatch(_filterKernel, filterGroupsX, filterGroupsY, 1);

            _lightingCompute.SetInts(LightFilterDirectionId, 0, 1);
            _lightingCompute.SetTexture(_filterKernel, LightFilterInputId, _lightmapScratchTexture);
            _lightingCompute.SetTexture(_filterKernel, LightFilterOutputId, _lightmapTexture);
            _lightingCompute.Dispatch(_filterKernel, filterGroupsX, filterGroupsY, 1);
        }

        Shader.SetGlobalTexture(WorldLightTextureId, _lightmapTexture);
        Shader.SetGlobalVector(WorldLightRectId, worldRect);
        RememberAuraState();
    }

    private void ConfigureLightingShader(Vector4 worldRect, int gridWidth, int gridHeight, float cellSize)
    {
        _lightingCompute!.SetTexture(_kernel, OcclusionTextureId, _occlusionTexture);
        _lightingCompute.SetTexture(_kernel, SdfTextureId, _sdfTexture);
        _lightingCompute.SetInts(GridSizeId, gridWidth, gridHeight);
        _lightingCompute.SetInts(OcclusionSizeId, _occlusionWidth, _occlusionHeight);
        _lightingCompute.SetInt(OcclusionYFlipId, SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        _lightingCompute.SetInts(OutputSizeId, _outputWidth, _outputHeight);
        _lightingCompute.SetVector(WorldRectId, worldRect);
        _lightingCompute.SetFloat(CellSizeId, cellSize);
        _lightingCompute.SetVector(AmbientColorId, _ambientColor);
        _lightingCompute.SetFloat(AoStrengthId, _ambientOcclusionStrength);
        _lightingCompute.SetInt(MaxRayStepsId, _qualitySettings.MaximumRaySteps);
        _lightingCompute.SetFloat(AttenuationFactorId, _attenuationFactor);
        _lightingCompute.SetFloat(MinimumTransmissionId, _minimumTransmission);
        _lightingCompute.SetFloat(OpaqueOcclusionThresholdId, _opaqueOcclusionThreshold);
        _lightingCompute.SetFloat(RaymarchedShadowStrengthId, _raymarchedShadowStrength);
        _lightingCompute.SetFloat(ShadowSoftnessId, _shadowSoftness);
        _lightingCompute.SetFloat(OccluderHeightId, _occluderHeight);
        _lightingCompute.SetFloat(ShadowHeightSoftnessId, _shadowHeightSoftness);
        _lightingCompute.SetFloat(ShadowDensityId, _shadowDensity);
        _lightingCompute.SetFloat(MinimumTraceStepId, _minimumTraceStep);
        _lightingCompute.SetFloat(MaximumTraceStepId, Mathf.Max(_minimumTraceStep, _maximumTraceStep));
        _lightingCompute.SetFloat(SdfSolidThresholdId, _sdfSolidThreshold);
        _lightingCompute.SetFloat(CornerSealRadiusId, _cornerSealRadius);
        _lightingCompute.SetFloat(CoveragePixelsPerCellId, CoveragePixelsPerCell);
        _lightingCompute.SetFloat(AoCardinalDistanceId, _aoCardinalDistance);
        _lightingCompute.SetFloat(AoDiagonalDistanceId, _aoDiagonalDistance);
        _lightingCompute.SetFloat(AoFarDistanceId, _aoFarDistance);
        _lightingCompute.SetFloat(AoFarWeightId, _aoFarWeight);
        _lightingCompute.SetFloat(AoSolidInteriorWeightId, _aoSolidInteriorWeight);
        _lightingCompute.SetFloat(AoBoundaryWeightId, _aoBoundaryWeight);
        _lightingCompute.SetInt(DebugViewId, (int)_debugView);
    }

    private static int SnapLightingRegion(int coordinate)
    {
        return Mathf.FloorToInt(coordinate / (float)LightingCacheAnchorCells) * LightingCacheAnchorCells;
    }

    private bool CanDispatchLighting()
    {
        return _lightingCompute != null &&
            _lightBuffer != null &&
            _lightTileRangeBuffer != null &&
            _lightTileIndexBuffer != null &&
            _staticLightmapTexture != null;
    }

    private void DispatchLighting(RenderTexture target, int passMode)
    {
        _lightingCompute!.SetTexture(_kernel, ResultId, target);
        _lightingCompute.SetTexture(
            _kernel,
            StaticLightingTextureId,
            passMode == 0 ? _lightmapScratchTexture : _staticLightmapTexture);
        _lightingCompute.SetBuffer(_kernel, LightsId, _lightBuffer);
        _lightingCompute.SetBuffer(_kernel, LightTileRangesId, _lightTileRangeBuffer);
        _lightingCompute.SetBuffer(_kernel, LightTileIndicesId, _lightTileIndexBuffer);
        _lightingCompute.SetInts(LightTileGridSizeId, _lightTileGridWidth, _lightTileGridHeight);
        _lightingCompute.SetFloat(
            LightTileWorldSizeId,
            Mathf.Max(2, _lightCullingTileSize) * GameConstants.World.CELLSIZE);
        _lightingCompute.SetInt(LightingPassModeId, passMode);
        _lightingCompute.Dispatch(
            _kernel,
            Mathf.CeilToInt(_outputWidth / 8f),
            Mathf.CeilToInt(_outputHeight / 8f),
            1);
    }

    private bool HasAuraStateChanged(float cellSize)
    {
        if (!_hasRenderedAuraState || _lastRenderedAuraEnabled != _playerAuraEnabled)
        {
            return true;
        }

        float positionThreshold = cellSize / Mathf.Max(1, _qualitySettings.PixelsPerCell) * 0.5f;
        return (_playerAuraPos - _lastRenderedAuraPosition).sqrMagnitude > positionThreshold * positionThreshold ||
            !Mathf.Approximately(_playerAuraRadius, _lastRenderedAuraRadius) ||
            !Mathf.Approximately(_playerAuraIntensity, _lastRenderedAuraIntensity) ||
            !Mathf.Approximately(_playerAuraHeight, _lastRenderedAuraHeight);
    }

    private void RememberAuraState()
    {
        _hasRenderedAuraState = true;
        _lastRenderedAuraEnabled = _playerAuraEnabled;
        _lastRenderedAuraPosition = _playerAuraPos;
        _lastRenderedAuraRadius = _playerAuraRadius;
        _lastRenderedAuraIntensity = _playerAuraIntensity;
        _lastRenderedAuraHeight = _playerAuraHeight;
    }

    private void BuildStaticLights(
        int minX,
        int minY,
        int width,
        int height,
        IWorldDataStorage storage,
        MapManager mapManager)
    {
        _clusteredLights.Clear();
        _emissiveCells.Clear();
        _staticLights.Clear();

        int availableStaticLights = Mathf.Max(1, _qualitySettings.MaximumLightCount);
        const float cellSize = GameConstants.World.CELLSIZE;

        for (int y = 0; y < height; y++)
        {
            int unityY = minY + y;
            int serverY = CoordinateUtils.UnityToServerY(unityY, mapManager.WorldHeight);
            for (int x = 0; x < width; x++)
            {
                int worldX = minX + x;
                CellType type = storage.GetCell(worldX, serverY);
                CellLightConfig config = GetCellLightConfig(type, mapManager);

                if (config.Emission.sqrMagnitude < 0.0025f)
                {
                    continue;
                }

                _emissiveCells.Add(new EmissiveCell(
                    worldX,
                    unityY,
                    new Vector2((worldX + 0.5f) * cellSize, (unityY + 0.5f) * cellSize),
                    config.Emission));
            }
        }

        BuildWorldAnchoredLightClusters();
        Vector2 regionCenter = new(
            (minX + (width * 0.5f)) * cellSize,
            (minY + (height * 0.5f)) * cellSize);
        _clusterCandidates.Clear();

        foreach (KeyValuePair<long, ClusteredLight> pair in _clusteredLights)
        {
            ClusteredLight cluster = pair.Value;
            Vector2 position = cluster.WeightedPosition / Mathf.Max(cluster.Weight, 0.001f);
            _clusterCandidates.Add(new ClusterCandidate(
                pair.Key,
                cluster,
                (position - regionCenter).sqrMagnitude));
        }

        _clusterCandidates.Sort(CompareClusterCandidates);
        int clusterCount = Mathf.Min(availableStaticLights, _clusterCandidates.Count);
        for (int index = 0; index < clusterCount; index++)
        {
            ClusteredLight cluster = _clusterCandidates[index].Light;
            Vector2 position = cluster.WeightedPosition / Mathf.Max(cluster.Weight, 0.001f);
            Vector3 color = cluster.WeightedColor / Mathf.Max(cluster.Weight, 0.001f);
            const float clusterRadiusPadding = StaticLightClusterSize * cellSize * 0.70710678f;
            _staticLights.Add(new GpuLight(
                position,
                _staticLightHeight,
                _staticLightRadius + clusterRadiusPadding,
                color,
                _staticLightIntensity));
        }
    }

    private static int CompareClusterCandidates(ClusterCandidate left, ClusterCandidate right)
    {
        int distanceComparison = left.DistanceSquared.CompareTo(right.DistanceSquared);
        return distanceComparison != 0 ? distanceComparison : left.Key.CompareTo(right.Key);
    }

    private void BuildWorldAnchoredLightClusters()
    {
        _clusteredLights.Clear();
        for (int index = 0; index < _emissiveCells.Count; index++)
        {
            EmissiveCell cell = _emissiveCells[index];
            int clusterX = Mathf.FloorToInt(cell.WorldX / (float)StaticLightClusterSize);
            int clusterY = Mathf.FloorToInt(cell.WorldY / (float)StaticLightClusterSize);
            long key = ((long)clusterX << 32) ^ (uint)clusterY;
            _clusteredLights.TryGetValue(key, out ClusteredLight cluster);
            cluster.Add(cell.Position, cell.Color);
            _clusteredLights[key] = cluster;
        }
    }

    private int UploadStaticLights()
    {
        if (_lightBuffer == null)
        {
            return 0;
        }

        int maximumLightCount = _qualitySettings.MaximumLightCount;
        int staticLightCount = Mathf.Min(_staticLights.Count, maximumLightCount);
        for (int i = 0; i < staticLightCount; i++)
        {
            _gpuLights[i] = _staticLights[i];
        }

        if (staticLightCount > 0)
        {
            _lightBuffer.SetData(_gpuLights, 0, 0, staticLightCount);
        }

        return staticLightCount;
    }

    private int UploadPlayerLight()
    {
        if (_lightBuffer == null || !_playerAuraEnabled || _playerAuraIntensity <= 0.001f)
        {
            return 0;
        }

        _gpuLights[0] = new GpuLight(
            _playerAuraPos,
            _playerAuraHeight,
            _playerAuraRadius,
            new Vector3(1f, 0.92f, 0.78f),
            _playerAuraIntensity);
        _lightBuffer.SetData(_gpuLights, 0, 0, 1);
        return 1;
    }

    private void BuildAndUploadLightTiles(
        int lightCount,
        Vector4 worldRect,
        int gridWidth,
        int gridHeight,
        float cellSize)
    {
        int tileSizeCells = Mathf.Max(2, _lightCullingTileSize);
        float tileWorldSize = tileSizeCells * cellSize;
        _lightTileGridWidth = Mathf.CeilToInt(gridWidth / (float)tileSizeCells);
        _lightTileGridHeight = Mathf.CeilToInt(gridHeight / (float)tileSizeCells);
        int tileCount = _lightTileGridWidth * _lightTileGridHeight;

        EnsureLightTileStorage(tileCount);
        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            _lightTileLists[tileIndex].Clear();
        }

        for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            Vector4 positionRadius = _gpuLights[lightIndex].PositionRadius;
            float localX = positionRadius.x - worldRect.x;
            float localY = positionRadius.y - worldRect.y;
            float radius = positionRadius.w;
            int minimumTileX = Mathf.Clamp(
                Mathf.FloorToInt((localX - radius) / tileWorldSize),
                0,
                _lightTileGridWidth - 1);
            int maximumTileX = Mathf.Clamp(
                Mathf.FloorToInt((localX + radius) / tileWorldSize),
                0,
                _lightTileGridWidth - 1);
            int minimumTileY = Mathf.Clamp(
                Mathf.FloorToInt((localY - radius) / tileWorldSize),
                0,
                _lightTileGridHeight - 1);
            int maximumTileY = Mathf.Clamp(
                Mathf.FloorToInt((localY + radius) / tileWorldSize),
                0,
                _lightTileGridHeight - 1);

            for (int tileY = minimumTileY; tileY <= maximumTileY; tileY++)
            {
                int rowOffset = tileY * _lightTileGridWidth;
                for (int tileX = minimumTileX; tileX <= maximumTileX; tileX++)
                {
                    _lightTileLists[rowOffset + tileX].Add(lightIndex);
                }
            }
        }

        int indexCount = 0;
        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            List<int> tileLights = _lightTileLists[tileIndex];
            _lightTileRanges[tileIndex] = new Vector2Int(indexCount, tileLights.Count);
            indexCount += tileLights.Count;
        }

        int requiredIndexCapacity = Mathf.Max(1, indexCount);
        if (_lightTileIndices.Length < requiredIndexCapacity)
        {
            Array.Resize(ref _lightTileIndices, Mathf.NextPowerOfTwo(requiredIndexCapacity));
        }

        int writeIndex = 0;
        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            List<int> tileLights = _lightTileLists[tileIndex];
            for (int localIndex = 0; localIndex < tileLights.Count; localIndex++)
            {
                _lightTileIndices[writeIndex++] = tileLights[localIndex];
            }
        }

        EnsureComputeBuffer(ref _lightTileRangeBuffer, tileCount, TileRangeStride);
        EnsureComputeBuffer(ref _lightTileIndexBuffer, _lightTileIndices.Length, TileIndexStride);
        _lightTileRangeBuffer!.SetData(_lightTileRanges, 0, 0, tileCount);
        _lightTileIndexBuffer!.SetData(_lightTileIndices, 0, 0, requiredIndexCapacity);
    }

    private void EnsureLightTileStorage(int tileCount)
    {
        if (_lightTileLists.Length == tileCount)
        {
            return;
        }

        _lightTileLists = new List<int>[tileCount];
        _lightTileRanges = new Vector2Int[tileCount];
        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            _lightTileLists[tileIndex] = new List<int>(16);
        }
    }

    private void BuildSignedDistanceField()
    {
        if (_lightingCompute == null || _occlusionTexture == null ||
            _sdfSeedTextureA == null || _sdfSeedTextureB == null || _sdfTexture == null)
        {
            return;
        }

        int groupsX = Mathf.CeilToInt(_occlusionWidth / 8f);
        int groupsY = Mathf.CeilToInt(_occlusionHeight / 8f);
        _lightingCompute.SetInts(SdfSizeId, _occlusionWidth, _occlusionHeight);
        _lightingCompute.SetFloat(CoveragePixelsPerCellId, CoveragePixelsPerCell);
        _lightingCompute.SetFloat(SdfSolidThresholdId, _sdfSolidThreshold);
        _lightingCompute.SetTexture(_initializeSdfKernel, OcclusionTextureId, _occlusionTexture);
        _lightingCompute.SetTexture(_initializeSdfKernel, SdfSeedOutputId, _sdfSeedTextureA);
        _lightingCompute.Dispatch(_initializeSdfKernel, groupsX, groupsY, 1);

        RenderTexture input = _sdfSeedTextureA;
        RenderTexture output = _sdfSeedTextureB;
        int jumpStep = Mathf.Max(1, Mathf.NextPowerOfTwo(Mathf.Max(_occlusionWidth, _occlusionHeight)) / 2);
        while (jumpStep >= 1)
        {
            _lightingCompute.SetInt(SdfJumpStepId, jumpStep);
            _lightingCompute.SetTexture(_jumpFloodSdfKernel, SdfSeedInputId, input);
            _lightingCompute.SetTexture(_jumpFloodSdfKernel, SdfSeedOutputId, output);
            _lightingCompute.Dispatch(_jumpFloodSdfKernel, groupsX, groupsY, 1);
            (input, output) = (output, input);
            jumpStep /= 2;
        }

        _lightingCompute.SetTexture(_finalizeSdfKernel, SdfSeedInputId, input);
        _lightingCompute.SetTexture(_finalizeSdfKernel, SdfOutputId, _sdfTexture);
        _lightingCompute.Dispatch(_finalizeSdfKernel, groupsX, groupsY, 1);
    }

    private static void EnsureComputeBuffer(ref ComputeBuffer? buffer, int count, int stride)
    {
        if (buffer != null && buffer.count == count && buffer.stride == stride)
        {
            return;
        }

        buffer?.Release();
        buffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured);
    }

    private static RenderTexture CreateRandomWriteTexture(
        int width,
        int height,
        RenderTextureFormat format,
        FilterMode filterMode,
        string name)
    {
        var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
            name = name,
        };
        texture.Create();
        return texture;
    }

    private static void ReleaseRenderTexture(ref RenderTexture? texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.Release();
        DestroyLightingObject(texture);
        texture = null;
    }

    private bool LoadComputeShader()
    {
        if (_lightingCompute != null)
        {
            return true;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            return false;
        }

        _lightingCompute = Resources.Load<ComputeShader>(ComputeResourcePath);
        if (_lightingCompute == null)
        {
            return false;
        }

        _kernel = _lightingCompute.FindKernel("CSMain");
        _filterKernel = _lightingCompute.FindKernel("FilterLighting");
        _initializeSdfKernel = _lightingCompute.FindKernel("InitializeSdfSeeds");
        _jumpFloodSdfKernel = _lightingCompute.FindKernel("JumpFloodSdf");
        _finalizeSdfKernel = _lightingCompute.FindKernel("FinalizeSdf");
        return true;
    }

    private void ApplyQualityPreset(QualityPreset quality, bool save)
    {
        _quality = quality;
        _qualitySettings = quality switch
        {
            QualityPreset.Low => new QualitySettings(1, 512, 128, 20, 20f),
            QualityPreset.Medium => new QualitySettings(2, 768, 256, 28, 24f),
            QualityPreset.High => new QualitySettings(4, 1536, 512, 40, 30f),
            _ => new QualitySettings(8, 2048, 1024, 64, 30f),
        };

        _nextUpdateTime = 0f;
        _lastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
        _staticRegionDirty = true;
        _hasRenderedAuraState = false;

        if (save)
        {
            PlayerPrefs.SetInt(QualityPreferenceKey, (int)_quality);
            PlayerPrefs.Save();
        }
    }

    private void EnsureResources(int width, int height)
    {
        int maximumLightCount = _qualitySettings.MaximumLightCount;
        if (_lightBuffer == null || _lightBuffer.count != maximumLightCount)
        {
            _lightBuffer?.Release();
            _lightBuffer = new ComputeBuffer(maximumLightCount, GpuLightStride, ComputeBufferType.Structured);
            _gpuLights = new GpuLight[maximumLightCount];
        }

        float scale = Mathf.Min(
            _qualitySettings.PixelsPerCell,
            Mathf.Min(
                _qualitySettings.MaximumTextureDimension / (float)width,
                _qualitySettings.MaximumTextureDimension / (float)height));
        int outputWidth = Mathf.Max(1, Mathf.CeilToInt(width * scale));
        int outputHeight = Mathf.Max(1, Mathf.CeilToInt(height * scale));

        if (_gridWidth == width && _gridHeight == height &&
            _outputWidth == outputWidth && _outputHeight == outputHeight &&
            _occlusionTexture != null && _sdfSeedTextureA != null && _sdfSeedTextureB != null &&
            _sdfTexture != null && _staticLightmapTexture != null &&
            _lightmapTexture != null && _lightmapScratchTexture != null)
        {
            return;
        }

        _gridWidth = width;
        _gridHeight = height;
        _occlusionWidth = width * CoveragePixelsPerCell;
        _occlusionHeight = height * CoveragePixelsPerCell;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        _staticRegionDirty = true;

        if (_occlusionTexture != null)
        {
            _occlusionTexture.Release();
            DestroyLightingObject(_occlusionTexture);
        }

        RenderTextureFormat coverageFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
            ? RenderTextureFormat.R8
            : RenderTextureFormat.RHalf;
        _occlusionTexture = new RenderTexture(
            _occlusionWidth,
            _occlusionHeight,
            0,
            coverageFormat,
            RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = false,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "_WorldOcclusionTexture",
        };
        _occlusionTexture.Create();

        ReleaseRenderTexture(ref _sdfSeedTextureA);
        ReleaseRenderTexture(ref _sdfSeedTextureB);
        ReleaseRenderTexture(ref _sdfTexture);

        RenderTextureFormat sdfSeedFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf)
            ? RenderTextureFormat.RGHalf
            : RenderTextureFormat.RGFloat;
        _sdfSeedTextureA = CreateRandomWriteTexture(
            _occlusionWidth,
            _occlusionHeight,
            sdfSeedFormat,
            FilterMode.Point,
            "_WorldSdfSeedsA");
        _sdfSeedTextureB = CreateRandomWriteTexture(
            _occlusionWidth,
            _occlusionHeight,
            sdfSeedFormat,
            FilterMode.Point,
            "_WorldSdfSeedsB");
        _sdfTexture = CreateRandomWriteTexture(
            _occlusionWidth,
            _occlusionHeight,
            RenderTextureFormat.RHalf,
            FilterMode.Bilinear,
            "_WorldSdfTexture");

        if (_lightmapTexture != null)
        {
            _lightmapTexture.Release();
            DestroyLightingObject(_lightmapTexture);
        }

        if (_lightmapScratchTexture != null)
        {
            _lightmapScratchTexture.Release();
            DestroyLightingObject(_lightmapScratchTexture);
        }

        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf
            : RenderTextureFormat.ARGB32;
        ReleaseRenderTexture(ref _staticLightmapTexture);
        _staticLightmapTexture = CreateRandomWriteTexture(
            outputWidth,
            outputHeight,
            format,
            FilterMode.Bilinear,
            "_WorldStaticLightTexture");

        _lightmapTexture = new RenderTexture(outputWidth, outputHeight, 0, format, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "_WorldLightTexture",
        };
        _lightmapTexture.Create();

        _lightmapScratchTexture = new RenderTexture(outputWidth, outputHeight, 0, format, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "_WorldLightScratchTexture",
        };
        _lightmapScratchTexture.Create();
    }

    private static void SetNeutralLighting(int minX, int minY, int width, int height)
    {
        const float cellSize = GameConstants.World.CELLSIZE;
        Shader.SetGlobalTexture(WorldLightTextureId, Texture2D.whiteTexture);
        Shader.SetGlobalVector(WorldLightRectId, new Vector4(
            minX * cellSize,
            minY * cellSize,
            width * cellSize,
            height * cellSize));
    }

    private static void ClearRenderTexture(RenderTexture target)
    {
        CommandBuffer commandBuffer = CommandBufferPool.Get("Clear World Coverage");
        try
        {
            commandBuffer.SetRenderTarget(target);
            commandBuffer.ClearRenderTarget(clearDepth: false, clearColor: true, backgroundColor: Color.clear);
            Graphics.ExecuteCommandBuffer(commandBuffer);
        }
        finally
        {
            CommandBufferPool.Release(commandBuffer);
        }
    }

    private static void DestroyLightingObject(UnityEngine.Object target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static CellLightConfig GetCellLightConfig(CellType type, MapManager mapManager)
    {
        CellConfigurationPacket cellConfig = mapManager.GetCellConfig(type);
        if ((cellConfig.Properties & CellConfigProperties.Glowing) == 0)
        {
            return new CellLightConfig(Vector3.zero);
        }

        Vector3 emission = GetFallbackGlowColor(type);
        Color cellColor = mapManager.GetCellMinimapColor(type);
        if (HasUsableEmissionColor(cellColor))
        {
            Vector3 configuredColor = new(cellColor.r, cellColor.g, cellColor.b);
            float brightestChannel = Mathf.Max(configuredColor.x, Mathf.Max(configuredColor.y, configuredColor.z));
            emission = configuredColor / Mathf.Max(0.35f, brightestChannel);
        }

        return new CellLightConfig(emission);
    }

    private static bool HasUsableEmissionColor(Color color)
    {
        if (color.a <= 0.05f)
        {
            return false;
        }

        const float defaultChannel = 128f / 255f;
        bool isDefaultPlaceholder =
            Mathf.Abs(color.r - defaultChannel) < 0.002f &&
            Mathf.Abs(color.g - defaultChannel) < 0.002f &&
            Mathf.Abs(color.b - defaultChannel) < 0.002f;
        return !isDefaultPlaceholder && Mathf.Max(color.r, Mathf.Max(color.g, color.b)) > 0.1f;
    }

    private static Vector3 GetFallbackGlowColor(CellType type)
    {
        switch (type)
        {
            case CellType.Lava:
            case CellType.DeepMagmaBoulder:
            case CellType.VolcanoBackground:
                return new Vector3(1f, 0.38f, 0.08f);
            case CellType.AliveCyan:
            case CellType.XCyan:
            case CellType.Cyan:
            case CellType.DeepTurquoiseRock:
                return new Vector3(0.12f, 0.78f, 1f);
            case CellType.XGreen:
            case CellType.Green:
                return new Vector3(0.12f, 1f, 0.24f);
            case CellType.AliveRed:
            case CellType.XRed:
            case CellType.Red:
                return new Vector3(1f, 0.16f, 0.12f);
            case CellType.AliveBlue:
            case CellType.XBlue:
            case CellType.Blue:
            case CellType.DeepLazuriteSand:
                return new Vector3(0.16f, 0.38f, 1f);
            case CellType.AliveViol:
            case CellType.XViolet:
            case CellType.Violet:
            case CellType.PurpleAcid:
            case CellType.PassiveAcid:
            case CellType.AcidRock:
            case CellType.HypnoRock:
                return new Vector3(0.75f, 0.12f, 0.92f);
            case CellType.AliveRainbow:
            case CellType.DeepRainbowRock:
            case CellType.SuperRainbow:
                return new Vector3(1f, 0.35f, 0.9f);
            case CellType.AliveNigger:
                return new Vector3(0.5f, 0.18f, 0.72f);
            case CellType.AliveWhite:
            case CellType.White:
            case CellType.Pearl:
                return new Vector3(0.92f, 0.96f, 1f);
            case CellType.GrayAcid:
            case CellType.LivingActiveAcid:
            case CellType.CorrosiveActiveAcid:
                return new Vector3(0.24f, 0.88f, 0.12f);
            case CellType.Box:
                return new Vector3(1f, 0.62f, 0.18f);

            default:
                return new Vector3(0.72f, 0.82f, 1f);
        }
    }
}
