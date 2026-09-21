#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Kern.World.Terrain
{
    /// <summary>
    /// Жизненный цикл террейна в сцене и порядок одного кадра.
    /// </summary>
    ///
    /// Здесь не считается ничего. Кадр — это последовательность вызовов:
    /// разрешить камеру → выбрать план кадра (<see cref="TerrainFramePlanner"/>)
    /// → довести окно до плана (<see cref="TerrainWindow"/>) → выгрузить
    /// тексели → поставить меш показа
    /// (<see cref="TerrainPresentationWindow"/>) → отдать окно освещению.
    /// Каждый шаг живёт отдельным типом, и искать его надо там.
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    public class TerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField]
        private float _cellSize = ProjectRuntimeContracts.World.CellSize;
        [SerializeField]
        private Shader? _terrainShader;
        [SerializeField]
        private string _sortingLayerName = "Default";
        [SerializeField]
        private int _sortingOrder = ProjectRuntimeContracts.RequiredLayers.TerrainSortingOrder;
        [SerializeField]
        private int _doorOverlaySortingOrder = 500;
        [SerializeField]
        private int _viewportPadding = 2;

        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private ITextureService _textureService = null!;
        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        private static readonly int _reliefRimEnabledID =
            Shader.PropertyToID("_TerrainReliefRimEnabled");

        private static readonly ProfilerMarker _TerrainLateUpdateMarker =
            new("Kern.Terrain.LateUpdate.CPU");

        private static readonly AllocationLedger.Entry _AllocationEntry =
            AllocationLedger.Register("Террейн — LateUpdate");

        private readonly TerrainWindow _window = new();
        private readonly TerrainFramePlanner _planner = new();
        private readonly TerrainMeshManager _meshManager = new();
        private readonly TerrainPresentationWindow _presentation = new();
        private readonly TerrainDiagnosticLog _diag = new();
        private readonly TerrainStallReport _stall = new();

        private MeshFilter? _meshFilter;
        private MeshRenderer? _meshRenderer;
        private Camera? _mainCamera;
        private TerrainSubscriptions? _subscriptions;

        private RectInt _lightingViewport;
        private bool _fatalBuildError;
        private ulong _terrainContentRevision = 1;

        public bool BypassCpuMeshRebuild
        {
            get => _debugSettings.BypassCpuMeshRebuild;
            set => _debugSettings.BypassCpuMeshRebuild = value;
        }

        public bool BypassTerrainDraw
        {
            get => _debugSettings.BypassTerrainDraw;
            set => _debugSettings.BypassTerrainDraw = value;
        }

        public ulong TerrainContentRevision => _terrainContentRevision;

        // Exposes the production builder's geometry evidence to PlayMode
        // contract tests. A hand-authored cell-data texture can pass a shader
        // test while the live scene still renders a rectangular CPU path; the
        // count makes that divergence observable without a second renderer.
        internal int LastFullBuildAnchoredForegroundCellCount =>
            _window.Driver.Pipeline.CellBuilder.LastFullBuildAnchoredForegroundCellCount;

        public bool IsReadyForGameplay =>
            _window.IsInitialized &&
            _window.CellIDMesh != null &&
            _window.CellsCommitted &&
            _window.Driver.Materials.Materials.Length > 0 &&
            _window.PendingTextureCellTypes.Count == 0;

        public void ApplyClientConfig()
        {
            IClientConfigManager clientConfigManager = _clientConfigManager ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires IClientConfigManager injection.");
            ClientConfig config = clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires an initialized ClientConfig.");

            bool enableDistortion = config.Terrain.EnableDistortion;
            if (_window.Driver.Pipeline.EnableDistortion != enableDistortion)
            {
                _window.Driver.Pipeline.EnableDistortion = enableDistortion;
                _window.NeedsRefresh = true;
            }

            // Кайма живёт глобалью шейдера: маска и транспорт от тумблера не
            // зависят, выключенная кайма просто перестаёт умножать кадр.
            bool enableReliefRim = config.Terrain.EnableReliefRim;
            Shader.SetGlobalFloat(_reliefRimEnabledID, enableReliefRim ? 1f : 0f);

            _window.Driver.Materials.ApplyClientConfig(config);
            _terrainContentRevision++;
            Debug.Log(
                $"[TerrainRenderer] ApplyClientConfig: distortion={enableDistortion}, " +
                $"reliefRim={enableReliefRim}");
        }

        public void InitializeEditorPreview(
            IWorldDataStorage storage,
            MapManager mapManager,
            ITextureService textureService)
        {
            _storage = storage;
            _mapManager = mapManager;
            _textureService = textureService;
            InitializeSceneBindings();
            EnsureSubscriptions();
            _window.NeedsRefresh = true;
        }

        public void EnsureSubscriptions()
        {
            _subscriptions ??= new TerrainSubscriptions(
                HandleCellChanged,
                HandleRegionChanged,
                OnTextureLoaded,
                OnWorldDataLoaded,
                OnCellLayerChunkLoaded);
            _subscriptions.Bind(_storage, _textureService, _mapManager);
        }

        public void RenderLightingMaterialFields(
            CommandBuffer commandBuffer,
            RenderTexture materialField,
            RenderTexture emissionField,
            Vector4 worldRect) =>
            _meshManager.RenderLightingMaterialFields(
                commandBuffer,
                materialField,
                emissionField,
                worldRect,
                transform.localToWorldMatrix,
                _window.Driver.Materials.CellMaterials,
                _window.CellIDMesh,
                _presentation.ViewOffset);

        protected void Awake() => InitializeSceneBindings();

        protected void Start() => _mainCamera = _gameplayCamera?.Camera;

        protected void OnDestroy()
        {
            _subscriptions?.Dispose();
            _subscriptions = null;
            _presentation.Dispose();
            _window.Dispose();
        }

        protected void LateUpdate()
        {
            if (_fatalBuildError)
            {
                return;
            }

            using var terrainLateUpdateMarker = _TerrainLateUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
            long stallStart = TerrainStallReport.Begin();
            if (_mapManager == null || _storage == null || !_storage.IsReady)
            {
                return;
            }

            if (_localPlayer is not { Current: { HasServerPosition: true } })
            {
                return;
            }

            _diag.Once(1 << 1, "[TerrainDiag] gate passed: storage ready");
            if (!TryResolveCamera())
            {
                return;
            }

            LightingEngine? lightingEngine = ResolveLightingEngine();
            if (lightingEngine == null)
            {
                return;
            }

            long planStart = TerrainStallReport.Begin();
            TerrainFramePlan framePlan = _planner.Plan(
                _mainCamera!,
                _cellSize,
                _viewportPadding,
                lightingEngine.RequiredTerrainPadding,
                lightingEngine.StableRegionPaddingCells,
                _window.Origin,
                _window.Width,
                _window.Height,
                _window.IsInitialized,
                _window.CellsCommitted,
                _lightingViewport,
                _storage,
                _mapManager,
                _connectionService,
                _telemetry);
            float planMs = TerrainStallReport.ElapsedMs(planStart);
            if (!framePlan.ShouldProcess)
            {
                return;
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw;
            }

            float refreshTextureMs = 0f;
            if (_window.PendingTextureCellTypes.Count > 0 &&
                !BypassCpuMeshRebuild &&
                _window.CellsCommitted)
            {
                // Поле материалов семплит альбедо и эмиссию из атласа: новые
                // rect'ы меняют его содержимое без смены геометрии. Без бампа
                // ревизии поле осталось бы с чёрным/старым альбедо (и без
                // свечения) до первой копки или сдвига региона — светящиеся
                // кристаллы гасли навсегда.
                long refreshStart = TerrainStallReport.Begin();
                if (_window.RefreshPendingTextureCells(
                    Services,
                    _window.NeedsRefresh || framePlan.DimensionsChanged))
                {
                    _terrainContentRevision++;
                }

                refreshTextureMs = TerrainStallReport.ElapsedMs(refreshStart);
            }

            long dimensionsStart = TerrainStallReport.Begin();
            _window.ApplyDimensions(framePlan.ActiveWindow.Size, framePlan.DimensionsChanged);
            _window.CoalesceDirtyRects();
            float dimensionsMs = TerrainStallReport.ElapsedMs(dimensionsStart);

            // Снимок до Process: он чистит набор заплаток, а в отчёт нужно
            // то, чем кадр был занят, а не то, что от него осталось.
            int dirtyRectCount = _window.Dirty.Rects.Count;
            long dirtyArea = _window.Dirty.Rects.TotalArea;
            long processStart = TerrainStallReport.Begin();
            if (!_window.Process(
                Services,
                _clientConfigManager,
                framePlan.ActiveWindow.Origin,
                framePlan.DimensionsChanged,
                BypassCpuMeshRebuild,
                _meshRenderer,
                out Exception? failure))
            {
                ReportBuildFailure(failure, framePlan.ActiveWindow.Origin);
                return;
            }

            float processMs = TerrainStallReport.ElapsedMs(processStart);
            float uploadMs = _window.Commit();
            if (uploadMs > 0f)
            {
                _telemetry.TerrainGpuUploadTimeMs = uploadMs;
            }

            // Меш показа ставится только по собранному окну: до первой
            // выгрузки текселей его размеры не с чем согласовывать.
            if (_window.CellsCommitted && _window.HasOrigin &&
                _window.Width > 0 && _window.Height > 0)
            {
                _presentation.Update(
                    _planner.Policy,
                    framePlan.CameraViewport,
                    _window.Origin,
                    _window.Width,
                    _window.Height,
                    _cellSize,
                    _meshFilter);
            }

            // Terrain cache и lighting cache имеют разные окна жизни. Terrain
            // может сдвинуться на выровненную границу, пока камера всё ещё
            // находится внутри стабильного lighting region.
            PublishLightingUpdate(lightingEngine, framePlan.LightingViewport);
            _lightingViewport = framePlan.LightingViewport;
            lightingEngine.CaptureBudgetViolationIfNeeded();

            TerrainBuildPipeline pipeline = _window.Driver.Pipeline;
            _stall.Record(
                stallStart,
                _telemetry,
                new TerrainStallFrame(
                    pipeline.LastBuildScrolled,
                    pipeline.LastScrollDelta,
                    _window.Origin,
                    new Vector2Int(_window.Width, _window.Height),
                    dirtyRectCount,
                    dirtyArea,
                    refreshTextureMs,
                    processMs,
                    uploadMs,
                    pipeline.CellBuilder.LastScrollMs,
                    pipeline.CellBuilder.LastIndexRemoveMs,
                    pipeline.CellBuilder.LastWarmupMs,
                    pipeline.CellBuilder.LastFillMs,
                    pipeline.CellBuilder.LastFilledCells,
                    pipeline.CellBuilder.Textures.LastUploadRectCount,
                    pipeline.CellBuilder.Textures.LastUploadTexels,
                    pipeline.CellBuilder.LastQuadMs,
                    pipeline.CellBuilder.LastPackMs,
                    pipeline.CellBuilder.Textures.LastStageMs,
                    pipeline.CellBuilder.Textures.LastStageCopyMs,
                    pipeline.CellBuilder.Textures.LastStageApplyMs,
                    pipeline.CellBuilder.Textures.LastUploadStrips,
                    planMs,
                    dimensionsMs));
        }

        private TerrainBuildServices Services =>
            new(
                _storage,
                _mapManager,
                _textureService ?? throw new InvalidOperationException(
                    "TerrainRenderer requires ITextureService injection."),
                _telemetry);

        private void InitializeSceneBindings()
        {
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            _mainCamera ??= _gameplayCamera?.Camera;

            _window.Driver.Materials.TerrainShader = _terrainShader;
            _window.Driver.Materials.InitializeShader();
            _window.Attach(
                transform,
                _sceneObjects,
                _sortingLayerName,
                _doorOverlaySortingOrder,
                _cellSize);

            if (_meshRenderer == null)
            {
                return;
            }

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
        }

        /// <summary>
        /// Сборка упала: террейн замолкает до перезапуска сцены. Продолжать
        /// кадрами по частично собранному окну значит показывать дыры и
        /// приписывать их чему угодно, кроме настоящей причины.
        /// </summary>
        private void ReportBuildFailure(Exception? failure, Vector2Int origin)
        {
            if (failure == null)
            {
                // Не отказ, а «ещё нечем»: атласы не приехали. Кадр пропущен,
                // террейн жив и попробует снова.
                _diag.Once(1 << 6, "[TerrainDiag] BAIL: build sources not ready");
                return;
            }

            _fatalBuildError = true;
            Debug.LogException(new InvalidOperationException(
                $"[TerrainRenderer] Build failed: grid={origin} " +
                $"size={_window.Width}x{_window.Height}, world=" +
                $"{_mapManager?.WorldWidth ?? 0}x{_mapManager?.WorldHeight ?? 0}, " +
                $"atlases={_textureService?.GetAllAtlases().Count ?? 0}, " +
                $"storageReady={_storage?.IsReady ?? false}.",
                failure));
        }

        private void HandleCellChanged(int serverX, int serverY) =>
            HandleRegionChanged(serverX, serverY, 1, 1);

        private void HandleRegionChanged(int serverX, int serverY, int width, int height)
        {
            if (_mapManager == null || !_window.HasOrigin)
            {
                _window.NeedsRefresh = true;
                return;
            }

            RectInt? changed = _window.Dirty.Add(
                serverX, serverY, width, height,
                _window.Origin, _window.Width, _window.Height, _mapManager.WorldHeight);
            if (changed is { } region)
            {
                _lightingEngine?.InvalidateRegion(
                    region.x, region.y, region.width, region.height);
            }
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            _diag.Once(1 << 9, $"[TerrainDiag] first texture arrived: {filename}");

            if (TerrainCellTextureName.TryParseCellType(filename, out CellType cellType))
            {
                _window.Driver.Materials.TerrainShader = _terrainShader;
                _window.Driver.Materials.InitializeShader();
                _window.PendingTextureCellTypes.Add(cellType);
            }
            else if (TerrainCellTextureName.IsDecalAtlas(filename))
            {
                if (_textureService != null)
                {
                    _window.Driver.Materials.BindAtlasTextures(
                        _textureService.GetAllAtlases(), _textureService);
                }

                _window.NeedsRefresh = true;
            }
        }

        private void OnWorldDataLoaded()
        {
            EnsureSubscriptions();
            _window.NeedsRefresh = true;
            _terrainContentRevision++;
            _lightingEngine?.InvalidateStaticCache();
        }

        private void OnCellLayerChunkLoaded(int serverX, int serverY, int width, int height)
        {
            _telemetry.TerrainChunkLoadCount++;
            HandleRegionChanged(serverX, serverY, width, height);
        }

        private bool TryResolveCamera()
        {
            Camera? resolvedCam = _gameplayCamera?.Camera;
            if (resolvedCam != null)
            {
                _mainCamera = resolvedCam;
            }

            if (_mainCamera == null)
            {
                _diag.Once(1 << 2, "[TerrainDiag] camera NULL");
                return false;
            }

            _diag.Once(1 << 3, $"[TerrainDiag] camera ok: {_mainCamera.name} at {_mainCamera.transform.position}");
            return true;
        }

        private LightingEngine? ResolveLightingEngine()
        {
            LightingEngine? lightingEngine = _lightingEngine;
            if (lightingEngine == null)
            {
                if (!Application.isPlaying)
                {
                    return null;
                }

                throw new InvalidOperationException(
                    "LightingEngine was not initialized by GameLifetimeScope.");
            }

            return lightingEngine;
        }

        private void PublishLightingUpdate(LightingEngine lightingEngine, RectInt viewport)
        {
            if (_mainCamera == null ||
                !_mainCamera.orthographic ||
                lightingEngine.ActiveLightingQuality == LightingQualityMode.Off)
            {
                return;
            }

            lightingEngine.UpdateLighting(
                viewport.x,
                viewport.y,
                viewport.width,
                viewport.height,
                _mainCamera,
                _storage,
                _mapManager,
                this);
            _window.Driver.Materials.ValidateLightingBinding();
        }
    }
}
