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

        private static readonly ProfilerMarker _TerrainLateUpdateMarker =
            new("Kern.Terrain.LateUpdate.CPU");

        private static readonly AllocationLedger.Entry _AllocationEntry =
            AllocationLedger.Register("Террейн — LateUpdate");

        private readonly TerrainWindow _window = new();
        private readonly TerrainFramePlanner _planner = new();
        private readonly TerrainMeshManager _meshManager = new();
        private readonly TerrainPresentationWindow _presentation = new();
        private TerrainFrameDiagnostics? _diagnostics;
        private TerrainClientConfigApplier? _configApplier;

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

        private TerrainFrameDiagnostics Diagnostics => _diagnostics ??= new(_window);

        private TerrainClientConfigApplier ConfigApplier => _configApplier ??= new(_window);

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

            ConfigApplier.Apply(config);
            _terrainContentRevision++;
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

            Diagnostics.Mark(1 << 1, "[TerrainDiag] gate passed: storage ready");
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
                _fatalBuildError = Diagnostics.ReportBuildFailure(
                    failure,
                    framePlan.ActiveWindow.Origin,
                    _mapManager,
                    _textureService,
                    _storage);
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

            Diagnostics.Record(
                stallStart,
                _telemetry,
                new TerrainFrameTimings(
                    planMs,
                    dimensionsMs,
                    refreshTextureMs,
                    processMs,
                    uploadMs,
                    dirtyRectCount,
                    dirtyArea));
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
            Diagnostics.Mark(1 << 9, $"[TerrainDiag] first texture arrived: {filename}");

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
                Diagnostics.Mark(1 << 2, "[TerrainDiag] camera NULL");
                return false;
            }

            Diagnostics.Mark(1 << 3, $"[TerrainDiag] camera ok: {_mainCamera.name} at {_mainCamera.transform.position}");
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
