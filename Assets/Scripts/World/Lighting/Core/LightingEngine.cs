#nullable enable

using Fodinae.Core.Interfaces.Diagnostics;
using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Pipeline;
using Fodinae.World.Lighting.Pipeline.Stages;
using Fodinae.World.Lighting.Quality;
using Fodinae.World.Terrain;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace Fodinae.World.Lighting
{
    [DisallowMultipleComponent]
    public class LightingEngine : MonoBehaviour
    {
        public enum DebugView
        {
            FinalLighting = 0,
            Occupancy = 1,
            Albedo = 2,
            Emission = 3,
            Transmission = 4,
            StaticDirect = 5,
            DynamicDirect = 6,
            DirectRadiance = 7,
            DiffuseBounce = 8,
            Exposure = 9,

            AmbientOcclusion = 10,
        }

        private const int DynamicLightStride = sizeof(float) * 8;
        private const int RadianceStride = sizeof(uint) * 3;
        private const int MaximumDispatchGroupsPerDimension = 65535;
        private const string WorldLightingKeyword = "FODINAE_WORLD_LIGHTING";
        private static readonly int _WorldLightTextureID = Shader.PropertyToID("_WorldLightTexture");
        private static readonly int _WorldLightRectID = Shader.PropertyToID("_WorldLightRect");
        private static readonly int _WorldLightDebugViewID =
            Shader.PropertyToID("_WorldLightDebugView");
        private static readonly int _WorldLightTextureSizeID =
            Shader.PropertyToID("_WorldLightTextureSize");
        private static readonly int _WorldEmissionScaleID =
            Shader.PropertyToID("_WorldEmissionScale");
        private static readonly int _WorldLightPerBlockID =
            Shader.PropertyToID("_WorldLightPerBlock");

        // Поле занятости для падающей тени в террейне. Y-переворот отдаётся
        // отдельно: компьют читает это поле с поправкой, террейн обязан так же.
        private static readonly int _WorldOccupancyTextureID =
            Shader.PropertyToID("_WorldOccupancyTexture");
        private static readonly int _WorldOccupancyYFlipID =
            Shader.PropertyToID("_WorldOccupancyYFlip");
        private static readonly ProfilerMarker _LightingUpdateMarker =
            new("Fodinae.Lighting.UpdateLighting.CPU");

        private static readonly AllocationLedger.Entry _AllocationEntry =
            AllocationLedger.Register("Свет — обновление");
        private static readonly ProfilerMarker _BuildCommandsMarker =
            new("Fodinae.Lighting.BuildCommands.CPU");
        private static readonly ProfilerMarker _ExecuteCommandsMarker =
            new("Fodinae.Lighting.ExecuteCommands.CPU");
        private static readonly ProfilerMarker _DynamicUploadMarker =
            new("Fodinae.Lighting.DynamicLights.Upload.CPU");
        private static readonly ProfilerMarker _CascadeMarker =
            new("Fodinae.Lighting.Cascades.Record.CPU");
        private static readonly ProfilerMarker _ResolveMarker =
            new("Fodinae.Lighting.Resolve.Record.CPU");
        private static readonly ProfilerMarker _CompositeMarker =
            new("Fodinae.Lighting.Composite.Record.CPU");
        private static readonly ProfilerMarker _BlockLightingMarker =
            new("Fodinae.Lighting.BlockPropagation.Record.CPU");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Shader.DisableKeyword(WorldLightingKeyword);
        }

        [Header("Quality")]

        // Quality is selected by ClientConfig.GraphicsPreset at runtime.
        private GraphicsPreset _graphicsPreset;
        private LightingQualityMode _lightingQualityMode = LightingQualityMode.PerBlock;
        private TerrainRenderer? _activeTerrainRenderer;


        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Debug view для проверки отдельных lighting-слоёв без скрытого AO/exposure влияния.")]
        private DebugView _debugView;

        private readonly LightingResourceManager _resources = new();
        private readonly DynamicLightManager _dynamicLightManager = new();
        private GraphicsQualitySettings _qualitySettings;

        private List<CascadeLayout> _cascades => _resources.Cascades;
        private ComputeShader? _lightingCompute => _resources.LightingCompute;
        private ComputeBuffer? _dynamicLightBuffer => _resources.DynamicLightBuffer;
        private ComputeBuffer? _radianceAtlas => _resources.RadianceAtlas;
        private CommandBuffer? _lightingCommandBuffer => _resources.LightingCommandBuffer;
        private RenderTexture? _materialField => _resources.MaterialField;
        private RenderTexture? _staticEmissionField => _resources.StaticEmissionField;
        private RenderTexture? _dynamicEmissionField => _resources.DynamicEmissionField;
        private Material? _dynamicEmissionMaterial => _resources.DynamicEmissionMaterial;
        private RenderTexture? _directTexture => _resources.DirectTexture;
        private RenderTexture? _staticDirectTexture => _resources.StaticDirectTexture;
        private RenderTexture? _bounceTexture => _resources.BounceTexture;
        private RenderTexture? _lightmapTexture => _resources.LightmapTexture;
        private int _solveCascadeKernel => _resources.SolveCascadeKernel;
        private int _resolveDirectKernel => _resources.ResolveDirectKernel;
        private int _solveDiffuseBounceKernel => _resources.SolveDiffuseBounceKernel;
        private int _compositeLightingKernel => _resources.CompositeLightingKernel;
        private int _seedBlockLightingKernel => _resources.SeedBlockLightingKernel;
        private int _propagateBlockLightingKernel => _resources.PropagateBlockLightingKernel;
        private int _fieldWidth => _resources.FieldWidth;
        private int _fieldHeight => _resources.FieldHeight;
        private int _bounceWidth => _resources.BounceWidth;
        private int _bounceHeight => _resources.BounceHeight;
        private int _atlasCapacity => _resources.AtlasCapacity;
        private int _atlasEntryCount => _resources.AtlasEntryCount;
        private LightingPipeline? _compositePipeline => _resources.CompositePipeline;
        private LightingPipeline? _diffuseBouncePipeline => _resources.DiffuseBouncePipeline;
        private LightingPipeline? _dynamicEmissionCompositionPipeline => _resources.DynamicEmissionCompositionPipeline;
        private LightingPipeline? _materialFieldPipeline => _resources.MaterialFieldPipeline;
        private bool _gpuPipelineInitialized => _resources.GpuPipelineInitialized;

        private float _requestedPixelsPerCell;
        private float _effectivePixelsPerCell;
        private bool _textureDimensionLimited;
        private bool _cascadeBudgetLimited;
        private bool _fieldDirtyState = true;

        private bool _fieldDirty
        {
            get => _fieldDirtyState;
            set => _fieldDirtyState = value;
        }

        private bool _compositeDirty = true;
        private bool _bounceDirty = true;
        private bool _wasLightingBypassed;


        private ulong _solveCount;
        private ulong _lastTerrainGeometryRevision;
        private ulong _lastContributorGeometryRevision;
        [Inject]
        private LightingGeometryRegistry _lightingGeometryRegistry = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;
        private Vector4 _lastVisibleRegion = new(float.NaN, float.NaN, float.NaN, float.NaN);

        private bool _hasRenderedLightState;
        private bool _initialized;
        private bool _lightingDisabledStatePublished;

        public bool IsInitialized => _initialized;

        public event Action? OnInitialized;
        private bool _hasStaticRadianceState;
        private bool _hasDynamicRadianceState;
        private bool _dynamicSolveInProgress;
        private bool _forceBypassLighting;

        public bool BypassLightingCompute
        {
            get => _forceBypassLighting || (_debugSettings != null && _debugSettings.BypassLightingCompute);
            set
            {
                _forceBypassLighting = value;
                if (_debugSettings != null)
                {
                    _debugSettings.BypassLightingCompute = value;
                }
            }
        }

        public GraphicsPreset ActiveGraphicsPreset => _graphicsPreset;

        public LightingQualityMode ActiveLightingQuality => _lightingQualityMode;

        public DebugView ActiveDebugView => _debugView;

        public bool DiffuseBounceEnabled =>
            LightingConfigHolder.BounceEnabled && LightingConfigHolder.BounceStrength > 0f;

        public float AmbientIntensity => LightingConfigHolder.AmbientIntensity;

        public Color AmbientColor => LightingConfigHolder.AmbientColor;

        public float EmissionScale => LightingConfigHolder.EmissionScale;

        public Color EmptyExtinctionRGB => LightingConfigHolder.EmptyExtinctionRGB;

        public Color SolidExtinctionRGB => LightingConfigHolder.SolidExtinctionRGB;

        public float EmptyExtinctionMultiplier => LightingConfigHolder.EmptyExtinctionMultiplier;

        public float SolidExtinctionMultiplier => LightingConfigHolder.SolidExtinctionMultiplier;

        public float BounceStrength => LightingConfigHolder.BounceStrength;

        public float MaximumLightMultiplier => LightingConfigHolder.MaximumLightMultiplier;

        public float TransmittanceDebugDistanceCells =>
            LightingComputeBinder.ResolveTransmittanceDebugDistance();

        public float MinimumTransmission => LightingConfigHolder.MinimumTransmission;

        public float DynamicLightIntensity => LightingConfigHolder.DynamicLightIntensity;

        public Color DynamicLightColor => LightingConfigHolder.DynamicLightColor;

        public bool IsRuntimeConfigReady => true;

        public string RuntimeConfigFilePath => "constants";

        public int LightSafeBorder => 2;

        public int DynamicLightCount => _dynamicLightManager.Count;

        public uint DynamicLightGeneration => _dynamicLightManager.Generation;

        public int UploadedDynamicLightCount => _dynamicLightManager.UploadedCount;

        public int DroppedDynamicLightCount => _dynamicLightManager.DroppedCount;

        public IReadOnlyList<int> DroppedDynamicLightIds => _dynamicLightManager.DroppedLightIds;

        public ulong SolveCount => _solveCount;

        public int FieldWidth => _fieldWidth;

        public int FieldHeight => _fieldHeight;

        public float RequestedPixelsPerCell => _requestedPixelsPerCell;

        public float EffectivePixelsPerCell => _effectivePixelsPerCell;

        public bool TextureDimensionLimited => _textureDimensionLimited;

        public bool CascadeBudgetLimited => _cascadeBudgetLimited;

        public int BounceWidth => _bounceWidth;

        public int BounceHeight => _bounceHeight;

        public int CascadeCount => _cascades.Count;

        // One clipped DDA path crosses at most every row and column once.
        public int MaximumIntervalSteps => _fieldWidth + _fieldHeight + 1;

        public void CollectCascadeCosts(List<CascadeCostSample> destination)
        {
            if (_lightingQualityMode == LightingQualityMode.PerBlock)
            {
                destination.Clear();
                return;
            }

            CascadeCostCalculator.CollectCascadeCosts(_cascades, MaximumIntervalSteps, destination);
        }
        public int MaterialYFlip => SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

        public float CellSize => ProjectRuntimeContracts.World.CellSize;

        public Vector4 WorldRect => new(
            _lastVisibleRegion.x * ProjectRuntimeContracts.World.CellSize,
            _lastVisibleRegion.y * ProjectRuntimeContracts.World.CellSize,
            _lastVisibleRegion.z * ProjectRuntimeContracts.World.CellSize,
            _lastVisibleRegion.w * ProjectRuntimeContracts.World.CellSize);

        public IReadOnlyList<string> GetCascadeUniformSummaries()
        {
            var summaries = new List<string>(_cascades.Count);
            for (int index = 0; index < _cascades.Count; index++)
            {
                CascadeLayout cascade = _cascades[index];
                summaries.Add(
                    $"Cascade {index}: offset={cascade.Offset}, entries={cascade.EntryCount}, " +
                    $"probe={cascade.ProbeWidth}x{cascade.ProbeHeight}, spacing={cascade.ProbeSpacing}, " +
                    $"directions={cascade.DirectionCount}, interval={cascade.IntervalStart:F2}..{cascade.IntervalEnd:F2}");
            }

            return summaries;
        }

        public int AtlasEntryCount => _atlasEntryCount;

        public Color ComputeAmbientColor => LightingConfigHolder.AmbientColor * LightingConfigHolder.AmbientIntensity;

        public Color ComputeEmptyExtinction =>
            LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier;

        public Color ComputeSolidExtinction =>
            LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier;

        public int StableRegionPaddingCells => LightingRegionCalculator.LightingRegionPaddingCells;

        public int RequiredTerrainPadding
        {
            get
            {
                // Dynamic sources are rasterized as one-cell emitters. Their
                // propagation distance is solved by the same extinction and
                // cascade intervals as terrain emission, not by a source halo.
                return 3;
            }
        }

        private void Awake()
        {
        }

        private void Start()
        {
            // Scene instances run Start before GameBootstrap injects them. The
            // explicit PostStart resolution below performs the authoritative
            // initialization; do not throw every frame while that hand-off is
            // still pending.
            if (_DependenciesReady)
            {
                TryInitialize();
            }
        }

        private bool _DependenciesReady =>
            _clientConfig?.Config != null &&
            _lightingGeometryRegistry != null;

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (!_DependenciesReady)
            {
                throw new InvalidOperationException(
                    "LightingEngine requires all DI dependencies before initialization.");
            }

            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);

            _initialized = true;
            OnInitialized?.Invoke();

            if (_lightingQualityMode == LightingQualityMode.Off)
            {
                DisableGpuLighting();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            EnsureInitialized();
        }

        private void OnDestroy()
        {

            ReleaseGpuPipeline();
            Shader.DisableKeyword(WorldLightingKeyword);
        }

        private void Update()
        {
            if (!_initialized)
            {
                if (_DependenciesReady)
                {
                    TryInitialize();
                }

                return;
            }

        }

        private void OnApplicationQuit()
        {
        }

        public void SetDynamicLight(
            int id,
            Vector2 position,
            Color color,
            float intensity)
        {
            _dynamicLightManager.SetDynamicLight(id, position, color, intensity, _effectivePixelsPerCell);
            if (_dynamicLightManager.IsDirty)
            {
                _compositeDirty = true;
            }
        }

        public void RemoveDynamicLight(int id)
        {
            _dynamicLightManager.RemoveDynamicLight(id);
        }

        public void ClearDynamicLights()
        {
            _dynamicLightManager.ClearDynamicLights();
        }

        public void InvalidateStaticCache()
        {
            _fieldDirty = true;
        }

        public void InvalidateRegion(int worldX, int worldY, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int regionMaxX = worldX + width - 1;
            int regionMaxY = worldY + height - 1;
            if (float.IsNaN(_lastVisibleRegion.x) ||
                (regionMaxX >= _lastVisibleRegion.x - 1f &&
                worldX <= _lastVisibleRegion.x + _lastVisibleRegion.z + 1f &&
                regionMaxY >= _lastVisibleRegion.y - 1f &&
                worldY <= _lastVisibleRegion.y + _lastVisibleRegion.w + 1f))
            {
                _telemetry.LightingRegionInvalidationCount++;
                _fieldDirty = true;
            }
        }
        public void ApplyClientConfig()
        {
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _fieldDirty = true;
            _bounceDirty = true;
            _compositeDirty = true;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
            _hasRenderedLightState = false;
            _dynamicLightManager.IncrementGeneration();
            _dynamicLightManager.MarkDirty();
            Debug.Log($"[LightingEngine] Applied client config (Preset={_clientConfig.Config.GraphicsPreset})");
        }

        public void SetDebugView(DebugView debugView)
        {
            if (_debugView == debugView)
            {
                return;
            }

            _debugView = debugView;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
            _compositeDirty = true;
            Debug.Log($"[LightingEngine] SetDebugView: {debugView}");
        }


        public void ResetRuntimeLightingPreferences()
        {
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _fieldDirty = true;
            _compositeDirty = true;
            _bounceDirty = true;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }

        public void UpdateLighting(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight,
            Camera camera,
            IWorldDataStorage? storage,
            MapManager? mapManager,
            TerrainRenderer terrainRenderer)
        {
            using var lightingUpdateMarker = _LightingUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
            if (visibleWidth <= 0 || visibleHeight <= 0 || camera == null ||
                storage == null || mapManager == null)
            {
                return;
            }

            _activeTerrainRenderer = terrainRenderer ??
                throw new ArgumentNullException(nameof(terrainRenderer));

            if (camera == null || !camera.orthographic)
            {
                return;
            }

            if (BypassLightingCompute || _lightingQualityMode == LightingQualityMode.Off)
            {
                bool enteringBypass = !_wasLightingBypassed;
                _wasLightingBypassed = true;
                PublishLightingDisabledState();
                if (enteringBypass)
                {
                    ReleaseResources();
                }

                return;
            }

            if (_wasLightingBypassed || _lightingDisabledStatePublished)
            {
                _wasLightingBypassed = false;
                _lightingDisabledStatePublished = false;
                Shader.EnableKeyword(WorldLightingKeyword);
                Shader.SetGlobalInteger(_WorldLightDebugViewID, (int)_debugView);
                _fieldDirty = true;
                _compositeDirty = true;
                _bounceDirty = true;
                _hasRenderedLightState = false;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
                _lastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
                if (_lightmapTexture != null)
                {
                    Shader.SetGlobalTexture(_WorldLightTextureID, _lightmapTexture);
                }
            }

            EnsureGpuPipelineInitialized();

            // Регион света — прямоугольник сетки террейна как есть. Поле
            // препятствий рисует её меш, поэтому регион больше сетки давал
            // пустое поле за краем, а регион от кадра камеры менял размер при
            // зуме и движении. Сетка сама перепривязывается на 8 клеток и
            // держит постоянный размер (кадр максимального отдаления).
            Vector4 lightingRegion = new(visibleMinX, visibleMinY, visibleWidth, visibleHeight);

            bool regionChanged = lightingRegion != _lastVisibleRegion;
            _lastVisibleRegion = lightingRegion;

            int gridWidth = Mathf.RoundToInt(lightingRegion.z);
            int gridHeight = Mathf.RoundToInt(lightingRegion.w);
            EnsureResources(gridWidth, gridHeight, camera);

            bool dynamicLightsDirty = HasDynamicLightsChanged();
            ulong contributorGeometryRevision =
                _lightingGeometryRegistry.GeometryRevision;
            bool geometryChanged =
                _lastTerrainGeometryRevision != terrainRenderer.LightingGeometryRevision ||
                _lastContributorGeometryRevision != contributorGeometryRevision;
            if (!_fieldDirty && !regionChanged && !dynamicLightsDirty && !geometryChanged &&
                !_compositeDirty && !_bounceDirty)
            {
                return;
            }

            bool geometryUpdateRequired = _fieldDirty || regionChanged || geometryChanged;
            // Частотой обновление не ограничивается ни для ламп, ни для
            // геометрии: всё, что изменилось, пересчитывается в этом же кадре.
            if (geometryUpdateRequired || _bounceDirty || _compositeDirty)
            {
                _dynamicSolveInProgress = false;
            }

            const float cellSize = ProjectRuntimeContracts.World.CellSize;
            Vector4 worldRect = new(
                lightingRegion.x * cellSize,
                lightingRegion.y * cellSize,
                lightingRegion.z * cellSize,
                lightingRegion.w * cellSize);
            CommandBuffer commandBuffer = _lightingCommandBuffer ??
                throw new InvalidOperationException("Radiance Cascades command buffer is not initialized.");
                commandBuffer.Clear();
                int dynamicLightCount;
                bool dynamicLightsChanged;
                try
                {
                    long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    using (_BuildCommandsMarker.Auto())
                    {
                commandBuffer.BeginSample("Fodinae.RadianceCascades");
                bool rebuildFields = _fieldDirty || regionChanged || geometryChanged;
                if (rebuildFields)
                {
                    _materialFieldPipeline!.Record(
                        commandBuffer,
                        BuildFrameContext() with { WorldRect = worldRect });
                 }

                if (_dynamicSolveInProgress)
                {
                    dynamicLightCount = _dynamicLightManager.UploadedCount;
                    dynamicLightsChanged = false;
                }
                else
                {
                    dynamicLightCount = UploadDynamicLights(
                        commandBuffer,
                        worldRect,
                        cellSize,
                        out dynamicLightsChanged);
                }

                // Анимированное свечение сюда НЕ добавляется, и это намеренно.
                //
                // Условие «в кадре есть анимированная светящаяся клетка» верно
                // почти всегда, и кадр переставал уходить в ранний выход вовсе:
                // каскады, разрешение и сведение считались каждый кадр даже в
                // полном покое. Оживить пульсацию таким способом — значит
                // заплатить за неё всем конвейером освещения.
                if (!rebuildFields && !dynamicLightsChanged &&
                    !_compositeDirty && !_bounceDirty)
                {
                    commandBuffer.EndSample("Fodinae.RadianceCascades");
                    RememberDynamicLightState();
                    return;
                }

                if (_lightingQualityMode == LightingQualityMode.PerBlock)
                {
                    _telemetry.LightingStaticSolveCount++;
                    SolveBlockLighting(commandBuffer, worldRect, cellSize, dynamicLightCount, rebuildFields);
                }
                else
                {
                    // Dynamic lamps are integrated directly from their source
                    // cells; no emission-field rasterization or cascade atlas
                    // rebuild is needed when a lamp moves.
                    // Bound with the static field as the default. SolveRadianceComponent
                    // rebinds the cascade and resolve kernels per component; everything
                    // else - bounce, the composite's emission
                    // debug view - wants the terrain's emission, not the lamps'.
                    ConfigureSharedComputeParameters(
                        commandBuffer,
                        worldRect,
                        cellSize,
                        _staticEmissionField!);
                    PrepareGeometryCaches(commandBuffer, rebuildFields);
                    // Terrain emitters are re-solved only when the geometry they
                    // depend on changes - explicitly NOT when a lamp moves. That
                    // dependency was the whole reason walking cost a full solve per
                    // frame; the split below is what removes it.
                    bool staticRadianceChanged = rebuildFields || !_hasStaticRadianceState;

                    if (staticRadianceChanged)
                    {
                        _telemetry.LightingStaticSolveCount++;
                        SolveRadianceComponent(
                            commandBuffer,
                            _staticEmissionField!,
                            _staticDirectTexture!,
                            "Fodinae.Lighting.StaticRadiance");
                        _hasStaticRadianceState = true;
                    }

                    bool dynamicRadianceNeeded = dynamicLightCount > 0 &&
                        (dynamicLightsChanged || staticRadianceChanged || !_hasDynamicRadianceState);

                    if (dynamicRadianceNeeded)
                    {
                        _telemetry.LightingDynamicSolveCount++;
                        SolveDynamicLighting(commandBuffer, dynamicLightCount);
                        _hasDynamicRadianceState = true;
                    }
                    else if (dynamicLightCount == 0 && (dynamicLightsChanged || staticRadianceChanged || _hasDynamicRadianceState))
                    {
                        ClearDynamicDirect(commandBuffer);
                        _hasDynamicRadianceState = false;
                    }

                    // Diffuse bounce: direct radiance in _directTexture is scattered
                    // by surface albedo into the receiver hemisphere (SolveDiffuseBounce),
                    // then CompositeLighting adds it to ambient + direct.
                    if (LightingConfigHolder.BounceEnabled && LightingConfigHolder.BounceStrength > 0f)
                    {
                        _diffuseBouncePipeline!.Record(commandBuffer, BuildFrameContext());
                    }

                    // Final composite of ambient + direct radiance + diffuse bounce.
                    DispatchComposite(commandBuffer);
                }

                commandBuffer.EndSample("Fodinae.RadianceCascades");
                }
                _telemetry.LightingBuildCommandsTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - buildStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _telemetry.LightingCommandBufferBytes = commandBuffer.sizeInBytes;
                _telemetry.ActiveDynamicLights = dynamicLightCount;
                long executeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                using (_ExecuteCommandsMarker.Auto())
                {
                    Graphics.ExecuteCommandBuffer(commandBuffer);
                }
                _telemetry.LightingExecuteCommandsTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - executeStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                PublishLightingGlobals();
                _solveCount++;

                _fieldDirty = false;
                _compositeDirty = false;
                _bounceDirty = false;
                _lastTerrainGeometryRevision = terrainRenderer.LightingGeometryRevision;
                _lastContributorGeometryRevision = contributorGeometryRevision;
                RememberDynamicLightState();
            }
            finally
            {
                commandBuffer.Clear();
            }
        }

        private void PublishLightingDisabledState()
        {
            if (_lightingDisabledStatePublished)
            {
                return;
            }

            Shader.DisableKeyword(WorldLightingKeyword);
            Shader.SetGlobalTexture(_WorldLightTextureID, Texture2D.whiteTexture);
            Shader.SetGlobalVector(_WorldLightRectID, new Vector4(-1000f, -1000f, 2000f, 2000f));
            Shader.SetGlobalVector(_WorldLightTextureSizeID, new Vector4(1, 1, 1, 1));
            Shader.SetGlobalInteger(_WorldLightDebugViewID, 0);
            Shader.SetGlobalInteger(_WorldLightPerBlockID, 0);
            Shader.SetGlobalFloat(_WorldEmissionScaleID, LightingConfigHolder.EmissionScale);
            _lightingDisabledStatePublished = true;
        }

        private void PublishLightingGlobals()
        {
            if (_lightmapTexture == null || float.IsNaN(_lastVisibleRegion.x))
            {
                throw new InvalidOperationException(
                    "Enabled world lighting cannot publish before its lightmap and region exist.");
            }

            const float cellSize = ProjectRuntimeContracts.World.CellSize;
            Shader.EnableKeyword(WorldLightingKeyword);
            _lightingDisabledStatePublished = false;
            Shader.SetGlobalTexture(_WorldLightTextureID, _lightmapTexture);
            if (_materialField != null)
            {
                Shader.SetGlobalTexture(_WorldOccupancyTextureID, _materialField);
                Shader.SetGlobalInteger(
                    _WorldOccupancyYFlipID,
                    SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            }

            Shader.SetGlobalInteger(_WorldLightDebugViewID, (int)_debugView);
            Shader.SetGlobalInteger(
                _WorldLightPerBlockID,
                _lightingQualityMode == LightingQualityMode.PerBlock ? 1 : 0);
            Shader.SetGlobalFloat(_WorldEmissionScaleID, LightingConfigHolder.EmissionScale);
            Shader.SetGlobalVector(
                _WorldLightTextureSizeID,
                new Vector4(
                    _lightmapTexture.width,
                    _lightmapTexture.height,
                    1f / _lightmapTexture.width,
                    1f / _lightmapTexture.height));
            Shader.SetGlobalVector(
                _WorldLightRectID,
                new Vector4(
                    _lastVisibleRegion.x * cellSize,
                    _lastVisibleRegion.y * cellSize,
                    _lastVisibleRegion.z * cellSize,
                    _lastVisibleRegion.w * cellSize));
        }

        private void ConfigureSharedComputeParameters(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            float cellSize,
            RenderTexture emissionField)
        {
            LightingComputeBinder.BindSharedParameters(
                commandBuffer,
                _lightingCompute!,
                _fieldWidth,
                _fieldHeight,
                _bounceWidth,
                _bounceHeight,
                worldRect,
                cellSize,
                _lightingQualityMode,
                _debugView,
                _materialField!,
                emissionField,
                _solveCascadeKernel,
                _resolveDirectKernel,
                _solveDiffuseBounceKernel,
                _compositeLightingKernel);
        }

        private void BindFieldTextures(
            CommandBuffer commandBuffer,
            int kernel,
            RenderTexture emissionField)
        {
            LightingComputeBinder.BindFieldTextures(
                commandBuffer,
                _lightingCompute!,
                kernel,
                _materialField!,
                emissionField);
        }

        private LightingFrameContext BuildFrameContext()
        {
            return new LightingFrameContext(
                _lightingCompute!,
                _fieldWidth,
                _fieldHeight,
                _bounceWidth,
                _bounceHeight,
                _directTexture!,
                _staticDirectTexture!,
                _bounceTexture!,
                _lightmapTexture!,
                _materialField!,
                _staticEmissionField!,
                _dynamicEmissionField!,
                _dynamicEmissionMaterial!,
                _dynamicLightBuffer,
                _activeTerrainRenderer ??
                    throw new InvalidOperationException(
                        "Radiance Cascades requires an active TerrainRenderer."),
                _lightingGeometryRegistry);
        }

        private int UploadDynamicLights(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            float cellSize,
            out bool uploadedLightsChanged)
        {
            using var dynamicUploadMarker = _DynamicUploadMarker.Auto();
            return _dynamicLightManager.UploadDynamicLights(
                commandBuffer,
                _dynamicLightBuffer,
                worldRect,
                cellSize,
                out uploadedLightsChanged);
        }

        private void DispatchRadianceCascades(CommandBuffer commandBuffer, int maxCascades = -1)
        {
            using var cascadeMarker = _CascadeMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.RadianceCascades");
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeBufferParam(
                compute,
                _solveCascadeKernel,
                LightingComputeBinder.RadianceAtlasID,
                _radianceAtlas!);
            int cascadeCount = (maxCascades > 0 && maxCascades <= _cascades.Count)
                ? maxCascades
                : _cascades.Count;
            for (int cascadeIndex = cascadeCount - 1; cascadeIndex >= 0; cascadeIndex--)
            {
                DispatchRadianceCascade(commandBuffer, cascadeIndex, cascadeCount);
            }

            commandBuffer.EndSample("Fodinae.Lighting.RadianceCascades");
        }

        private void DispatchRadianceCascade(
            CommandBuffer commandBuffer,
            int cascadeIndex,
            int dispatchedCascadeCount)
        {
            string sampleName = cascadeIndex switch
            {
                3 => "Fodinae.Lighting.Cascade_3",
                2 => "Fodinae.Lighting.Cascade_2",
                1 => "Fodinae.Lighting.Cascade_1",
                _ => "Fodinae.Lighting.Cascade_0",
            };
            commandBuffer.BeginSample(sampleName);
            ComputeShader compute = _lightingCompute!;
            CascadeLayout cascade = _cascades[cascadeIndex];
            bool hasFarCascade = cascadeIndex + 1 < dispatchedCascadeCount;
            CascadeLayout farCascade = hasFarCascade
                ? _cascades[cascadeIndex + 1]
                : cascade;
            commandBuffer.SetComputeBufferParam(
                compute,
                _solveCascadeKernel,
                LightingComputeBinder.RadianceAtlasID,
                _radianceAtlas!);
            LightingComputeBinder.BindCascadeParameters(
                commandBuffer,
                compute,
                cascade,
                farCascade,
                hasFarCascade);
            int totalGroupCount = Mathf.CeilToInt(cascade.EntryCount / 64f);
            int groupCountX = Mathf.Min(
                MaximumDispatchGroupsPerDimension,
                totalGroupCount);
            int groupCountY = Mathf.CeilToInt(totalGroupCount / (float)groupCountX);
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.CascadeDispatchRowWidthID,
                groupCountX * 64);
            commandBuffer.DispatchCompute(
                compute,
                _solveCascadeKernel,
                groupCountX,
                groupCountY,
                1);
            commandBuffer.EndSample(sampleName);
        }

        // Solves one additive component of the light (e.g. terrain emission)
        // at full field resolution. Light is linear in emission, so components
        // solved separately sum to the whole.
        private void SolveRadianceComponent(
            CommandBuffer commandBuffer,
            RenderTexture emissionField,
            RenderTexture directTarget,
            string sampleName,
            int maxCascades = -1)
        {
            commandBuffer.BeginSample(sampleName);
            BindFieldTextures(commandBuffer, _solveCascadeKernel, emissionField);
            BindFieldTextures(commandBuffer, _resolveDirectKernel, emissionField);
            DispatchRadianceCascades(commandBuffer, maxCascades);
            DispatchResolveDirect(commandBuffer, directTarget);
            commandBuffer.EndSample(sampleName);
        }

        private void DispatchResolveDirect(CommandBuffer commandBuffer, RenderTexture directTarget)
        {
            using var resolveMarker = _ResolveMarker.Auto();
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.CascadeOffsetID, _cascades[0].Offset);
            commandBuffer.SetComputeBufferParam(
                compute,
                _resolveDirectKernel,
                LightingComputeBinder.RadianceAtlasID,
                _radianceAtlas!);
            commandBuffer.SetComputeTextureParam(
                compute,
                _resolveDirectKernel,
                LightingComputeBinder.DirectTextureID,
                directTarget);
            commandBuffer.DispatchCompute(
                compute,
                _resolveDirectKernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);
        }

        private void SolveDynamicLighting(CommandBuffer commandBuffer, int lightCount)
        {
            commandBuffer.BeginSample("Fodinae.Lighting.DynamicRadiance");
            ComputeShader compute = _lightingCompute!;
            int kernel = _resources.SolveDynamicLightingKernel;
            BindFieldTextures(commandBuffer, kernel, _staticEmissionField!);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightCountID, lightCount);
            commandBuffer.SetComputeBufferParam(compute, kernel, LightingComputeBinder.DynamicLightsID, _dynamicLightBuffer!);
            commandBuffer.SetComputeTextureParam(compute, kernel, LightingComputeBinder.DirectTextureID, _directTexture!);
            commandBuffer.DispatchCompute(
                compute,
                kernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);
            commandBuffer.EndSample("Fodinae.Lighting.DynamicRadiance");
        }

        private void ClearDynamicDirect(CommandBuffer commandBuffer)
        {
            commandBuffer.SetRenderTarget(_directTexture!);
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);
        }

        private void SolveBlockLighting(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            float cellSize,
            int dynamicLightCount,
            bool materialFieldRebuilt)
        {
            using var blockLightingMarker = _BlockLightingMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.BlockPropagation");

            ComputeShader compute = _lightingCompute!;
            int seedKernel = _seedBlockLightingKernel;
            int propagateKernel = _propagateBlockLightingKernel;

            LightingComputeBinder.BindExtinction(commandBuffer, compute);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.FieldSizeID, _fieldWidth, _fieldHeight);
            commandBuffer.SetComputeVectorParam(compute, LightingComputeBinder.WorldRectID, worldRect);
            commandBuffer.SetComputeFloatParam(compute, LightingComputeBinder.CellSizeID, cellSize);
            commandBuffer.SetComputeVectorParam(
                compute,
                LightingComputeBinder.AmbientColorID,
                LightingConfigHolder.AmbientColor * LightingConfigHolder.AmbientIntensity);
            commandBuffer.SetComputeFloatParam(compute, LightingComputeBinder.EmissionScaleID, LightingConfigHolder.EmissionScale);
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.MaterialYFlipID,
                SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightCountID, dynamicLightCount);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DebugViewID, (int)_debugView);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.BlockAveragedID, 1);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.BounceSizeID, _bounceWidth, _bounceHeight);
            // Composite's bounce debug views read the filter cache in this tier too.
            PrepareGeometryCaches(commandBuffer, materialFieldRebuilt);

            commandBuffer.SetComputeTextureParam(compute, seedKernel, LightingComputeBinder.MaterialFieldID, _materialField!);
            commandBuffer.SetComputeTextureParam(compute, seedKernel, LightingComputeBinder.EmissionFieldID, _staticEmissionField!);
            if (_dynamicLightBuffer != null)
            {
                commandBuffer.SetComputeBufferParam(compute, seedKernel, LightingComputeBinder.DynamicLightsID, _dynamicLightBuffer);
            }

            commandBuffer.SetComputeTextureParam(compute, seedKernel, LightingComputeBinder.BlockLightOutputID, _directTexture!);

            int dispatchX = Mathf.CeilToInt(_fieldWidth / 8f);
            int dispatchY = Mathf.CeilToInt(_fieldHeight / 8f);
            commandBuffer.DispatchCompute(compute, seedKernel, dispatchX, dispatchY, 1);

            commandBuffer.SetComputeTextureParam(compute, propagateKernel, LightingComputeBinder.EmissionFieldID, _staticEmissionField!);
            int iterations = LightingComputeBinder.ResolveBlockPropagationIterations(_fieldWidth, _fieldHeight);
            for (int i = 0; i < iterations; i++)
            {
                RenderTexture input = (i % 2 == 0) ? _directTexture! : _staticDirectTexture!;
                RenderTexture output = (i % 2 == 0) ? _staticDirectTexture! : _directTexture!;

                commandBuffer.SetComputeTextureParam(compute, propagateKernel, LightingComputeBinder.BlockLightInputID, input);
                commandBuffer.SetComputeTextureParam(compute, propagateKernel, LightingComputeBinder.BlockLightOutputID, output);
                commandBuffer.DispatchCompute(compute, propagateKernel, dispatchX, dispatchY, 1);
            }

            RenderTexture solved = iterations % 2 == 0 ? _directTexture! : _staticDirectTexture!;
            if (solved != _directTexture)
            {
                commandBuffer.CopyTexture(solved, _directTexture!);
            }

            int resolveKernel = _resources.ResolveBlockLightingKernel;
            commandBuffer.SetComputeTextureParam(compute, resolveKernel, LightingComputeBinder.BlockLightInputID, _directTexture!);
            commandBuffer.SetComputeTextureParam(compute, resolveKernel, LightingComputeBinder.ResultID, _lightmapTexture!);
            commandBuffer.DispatchCompute(compute, resolveKernel, dispatchX, dispatchY, 1);

            commandBuffer.EndSample("Fodinae.Lighting.BlockPropagation");

            if (_debugView != DebugView.FinalLighting)
            {
                DispatchComposite(commandBuffer);
            }
        }

        // Binds the geometry caches every solve and rebuilds them only together
        // with the material field they are derived from. The builders run the
        // exact per-frame expressions they replace, so nothing about the image
        // changes — the marching simply stops being repeated for unchanged walls.
        private void PrepareGeometryCaches(CommandBuffer commandBuffer, bool materialFieldRebuilt)
        {
            ComputeShader compute = _lightingCompute!;
            RenderTexture cellSolidMask = _resources.CellSolidMask!;
            ComputeBuffer bounceTaps = _resources.BounceTaps!;
            ComputeBuffer bounceFilterWeights = _resources.BounceFilterWeights!;
            int buildMaskKernel = _resources.BuildCellSolidMaskKernel;
            int buildTapsKernel = _resources.BuildBounceTapsKernel;
            int buildFilterKernel = _resources.BuildBounceFilterKernel;

            commandBuffer.SetComputeIntParams(
                compute,
                LightingComputeBinder.CellGridSizeID,
                _resources.CellGridWidth,
                _resources.CellGridHeight);
            commandBuffer.SetComputeTextureParam(compute, _solveCascadeKernel, LightingComputeBinder.CellSolidMaskID, cellSolidMask);
            commandBuffer.SetComputeTextureParam(compute, _resolveDirectKernel, LightingComputeBinder.CellSolidMaskID, cellSolidMask);
            commandBuffer.SetComputeTextureParam(compute, _resources.SolveDynamicLightingKernel, LightingComputeBinder.CellSolidMaskID, cellSolidMask);
            commandBuffer.SetComputeTextureParam(compute, buildTapsKernel, LightingComputeBinder.CellSolidMaskID, cellSolidMask);
            commandBuffer.SetComputeTextureParam(compute, buildFilterKernel, LightingComputeBinder.CellSolidMaskID, cellSolidMask);
            commandBuffer.SetComputeBufferParam(compute, buildTapsKernel, LightingComputeBinder.BounceTapsID, bounceTaps);
            commandBuffer.SetComputeBufferParam(compute, _solveDiffuseBounceKernel, LightingComputeBinder.BounceTapsID, bounceTaps);
            commandBuffer.SetComputeBufferParam(compute, buildFilterKernel, LightingComputeBinder.BounceFilterWeightsID, bounceFilterWeights);
            commandBuffer.SetComputeBufferParam(compute, _compositeLightingKernel, LightingComputeBinder.BounceFilterWeightsID, bounceFilterWeights);

            if (!materialFieldRebuilt && _resources.GeometryCachesValid)
            {
                return;
            }

            commandBuffer.BeginSample("Fodinae.Lighting.GeometryCaches");
            BindFieldTextures(commandBuffer, buildMaskKernel, _staticEmissionField!);
            BindFieldTextures(commandBuffer, buildTapsKernel, _staticEmissionField!);
            BindFieldTextures(commandBuffer, buildFilterKernel, _staticEmissionField!);
            commandBuffer.SetComputeTextureParam(compute, buildMaskKernel, LightingComputeBinder.CellSolidMaskOutputID, cellSolidMask);

            // Order matters: the tap and filter builders march through the mask.
            commandBuffer.DispatchCompute(
                compute,
                buildMaskKernel,
                Mathf.CeilToInt(_resources.CellGridWidth / 8f),
                Mathf.CeilToInt(_resources.CellGridHeight / 8f),
                1);
            commandBuffer.DispatchCompute(
                compute,
                buildTapsKernel,
                Mathf.CeilToInt(_bounceWidth / 8f),
                Mathf.CeilToInt(_bounceHeight / 8f),
                1);
            commandBuffer.DispatchCompute(
                compute,
                buildFilterKernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);
            commandBuffer.EndSample("Fodinae.Lighting.GeometryCaches");
            _resources.GeometryCachesValid = true;
        }

        private void DispatchComposite(CommandBuffer commandBuffer)
        {
            using var compositeMarker = _CompositeMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.Composite");
            _compositePipeline!.Record(commandBuffer, BuildFrameContext());
            commandBuffer.EndSample("Fodinae.Lighting.Composite");
        }

        private bool HasDynamicLightsChanged()
        {
            return !_hasRenderedLightState || _dynamicLightManager.IsDirty;
        }


        private void RememberDynamicLightState()
        {
            _hasRenderedLightState = true;
            _dynamicLightManager.ClearDirty();
        }

        private void EnsureResources(int gridWidth, int gridHeight, Camera camera)
        {
            int oldFieldWidth = _resources.FieldWidth;
            int oldFieldHeight = _resources.FieldHeight;

            _requestedPixelsPerCell = _lightingQualityMode == LightingQualityMode.PerBlock
                ? 1
                : Mathf.Clamp(_qualitySettings.LightingMinimumPixelsPerCell, 1, 16);

            _resources.EnsureResources(
                gridWidth,
                gridHeight,
                camera,
                in _qualitySettings,
                _lightingQualityMode,
                out _textureDimensionLimited,
                out _cascadeBudgetLimited,
                out int effectivePixelsPerCell);

            _effectivePixelsPerCell = effectivePixelsPerCell;

            _dynamicLightManager.EnsureCapacity(
                Mathf.Max(1, _qualitySettings.LightingMaximumLightCount));

            if (oldFieldWidth != _resources.FieldWidth || oldFieldHeight != _resources.FieldHeight)
            {
                _fieldDirty = true;
                _hasRenderedLightState = false;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
            }
        }

        private void EnsureGpuPipelineInitialized()
        {
            _resources.EnsureGpuPipelineInitialized();
        }

        private void DisableGpuLighting()
        {
            ReleaseGpuPipeline();
            PublishLightingDisabledState();
        }

        private void ReleaseGpuPipeline()
        {
            _resources.ReleaseGpuPipeline();
            _dynamicLightManager.ResetUploadState();
        }

        private void ApplyQualitySettings(
            GraphicsPreset preset,
            GraphicsQualitySettings settings)
        {
            GraphicsQualityProfile.ValidateSettings(settings, preset.ToString());
            bool technicalSettingsChanged = _qualitySettings != settings;
            LightingQualityMode previousQuality = _lightingQualityMode;
            if (technicalSettingsChanged && _gpuPipelineInitialized)
            {
                ReleaseResources();
            }

            _graphicsPreset = preset;
            ApplyUnityQualityLevel(preset);
            _qualitySettings = settings;
            LightingQualityMode resolvedQuality = LightingQualityResolver.Resolve(
                preset,
                settings.LightingQuality);
            if (resolvedQuality != _lightingQualityMode)
            {
                _lightingQualityMode = resolvedQuality;
            }

            if (resolvedQuality == LightingQualityMode.Off)
            {
                DisableGpuLighting();
            }
            else
            {
                _lightingDisabledStatePublished = false;
                Shader.EnableKeyword(WorldLightingKeyword);
            }

            ApplyUnityRenderingSettings(_qualitySettings);
            if (!technicalSettingsChanged && previousQuality == resolvedQuality)
            {
                return;
            }

            _lastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            _fieldDirty = true;
            _dynamicSolveInProgress = false;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }

        private static void ApplyUnityQualityLevel(GraphicsPreset preset)
        {
            if (!GraphicsQualityProfile.IsStandard(preset))
            {
                return;
            }

            string targetName = preset.ToString();
            string[] qualityNames = UnityEngine.QualitySettings.names;
            int qualityIndex = Array.IndexOf(qualityNames, targetName);
            if (qualityIndex < 0)
            {
                int presetIndex = (int)preset;
                if (presetIndex >= 0 && presetIndex < qualityNames.Length)
                {
                    qualityIndex = presetIndex;
                }
                else
                {
                    for (int i = 0; i < qualityNames.Length; i++)
                    {
                        if (string.Equals(qualityNames[i].Replace(" ", string.Empty), targetName, StringComparison.OrdinalIgnoreCase))
                        {
                            qualityIndex = i;
                            break;
                        }
                    }
                }
            }

            if (qualityIndex >= 0 && UnityEngine.QualitySettings.GetQualityLevel() != qualityIndex)
            {
                UnityEngine.QualitySettings.SetQualityLevel(qualityIndex, applyExpensiveChanges: true);
                Debug.Log($"[LightingEngine] Applied Unity QualityLevel: {qualityNames[qualityIndex]} ({qualityIndex})");
            }
        }

        private static void ApplyUnityRenderingSettings(GraphicsQualitySettings settings)
        {
            UnityEngine.QualitySettings.antiAliasing = Mathf.Clamp(settings.AntiAliasing, 0, 8);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                // Масштаб приводится к обратной величине целого: только на
                // них апскейл до окна остаётся целократным и не размазывает
                // выровненную сетку текселей. Авторские 0.65, 0.8 и 0.9 ей
                // не являются, поэтому подмена называется вслух — иначе
                // расхождение профиля и картинки пришлось бы искать глазами.
                float requested = Mathf.Clamp(settings.RenderScale, 0.5f, 1f);
                float quantized = PixelGrid.QuantizeRenderScale(requested, 0.5f, 1f);
                if (!Mathf.Approximately(requested, quantized))
                {
                    Debug.Log(
                        $"[LightingEngine] Масштаб рендера {requested:F2} приведён к {quantized:F2}: " +
                        "промежуточные значения дают дробный апскейл и муар на пиксель-арте.");
                }

                urp.renderScale = quantized;
                urp.msaaSampleCount = Mathf.Max(1, settings.AntiAliasing);
            }

            // Печатается применённый масштаб, а не запрошенный: строкой выше
            // он мог быть приведён, и лог показывал 0.65 при реальных 0.50.
            float appliedScale = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset applied
                ? applied.renderScale
                : settings.RenderScale;
            Debug.Log($"[LightingEngine] ApplyUnityRenderingSettings: AA={settings.AntiAliasing}, RenderScale={appliedScale} (запрошено {settings.RenderScale})");
        }

        private void ReleaseResources()
        {
            _resources.ReleaseResources();
            _dynamicLightManager.ResetUploadState();
            _dynamicSolveInProgress = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }
    }
}
