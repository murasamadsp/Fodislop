#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using System.Collections.Generic;
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
        private Shader? _terrainShader = null;
        [SerializeField]
        private string _sortingLayerName = "Default";
        [SerializeField]
        private int _sortingOrder = ProjectRuntimeContracts.RequiredLayers.TerrainSortingOrder;
        [SerializeField]
        private int _doorOverlaySortingOrder = 500;
        [SerializeField]
        private int _viewportPadding = 2;

        [Inject]
        private Kern.World.Streaming.WorldViewTransition? _viewTransition = null;

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
        private readonly List<RectInt> _publishedChangedRegions = [];
        private readonly TerrainTexturePrefetch _texturePrefetch = new();
        private FrameStallReport? _frameStall;
        private bool _hasCameraSpeedSample;
        private Vector3 _lastCameraPosition;
        private float _cameraSpeedCellsPerSecond;

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

        public ulong PublishedTerrainContentRevision => _window.PublishedContentRevision;

        public bool HasPublishedTerrain => _window.CellsCommitted;

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
            _window.PendingTextureCellTypes.Count == 0 &&
            !_window.HasUnpublishedTextureRefresh &&
            _textureService.PendingCellTextureRequests == 0;

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

        protected void Awake()
        {
            InitializeSceneBindings();

            // Разбор провиса всего кадра: террейн — единственный компонент,
            // который гарантированно тикает каждый кадр игры.
            if (Application.isPlaying)
            {
                _frameStall = new FrameStallReport();
            }
        }

        protected void Start() => _mainCamera = _gameplayCamera?.Camera;

        protected void OnDestroy()
        {
            _subscriptions?.Dispose();
            _subscriptions = null;
            _presentation.Dispose();
            _window.Dispose();
            _frameStall?.Dispose();
            _frameStall = null;
        }

        protected void LateUpdate()
        {
            _frameStall?.Tick();
            if (_fatalBuildError)
            {
                return;
            }

            using var terrainLateUpdateMarker = _TerrainLateUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
            long stallStart = TerrainStallReport.Begin();
            _telemetry.ResetFrameTimers();
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

            UpdateCameraSpeedEstimate(_mainCamera!);

            LightingEngine? lightingEngine = ResolveLightingEngine();
            if (lightingEngine == null)
            {
                return;
            }

            // Готовый фоновый шаг забирается до плана и выгружается сразу:
            // так следующий шаг ставится уже в этом кадре по новому началу, а
            // тексели, начало окна и двери меняются вместе при любом раннем
            // выходе ниже.
            // Переход вида (телепорт): камера стоит на старом месте, окно
            // назначения собирается и не публикуется до кадра, в котором
            // камера на него встанет. CameraFollow обновляется раньше, поэтому
            // в кадре перестановки удержание здесь уже снято.
            bool holdingView = _viewTransition is { IsHolding: true };
            _window.HoldPublication = holdingView;
            if (_viewTransition != null)
            {
                _viewTransition.CanHold = _window.CellsCommitted;
            }

            long publishStart = TerrainStallReport.Begin();
            if (!_window.TryPublishCompleted(Services, out Exception? publishFailure))
            {
                _fatalBuildError = Diagnostics.ReportBuildFailure(
                    publishFailure,
                    _window.Origin,
                    _mapManager,
                    _textureService,
                    _storage);
                return;
            }

            float publishMs = TerrainStallReport.ElapsedMs(publishStart);
            float uploadMs = _window.Commit();
            if (uploadMs > 0f)
            {
                _telemetry.TerrainGpuUploadTimeMs = uploadMs;
            }

            long planStart = TerrainStallReport.Begin();
            Vector3 focusPosition = holdingView
                ? _viewTransition!.Destination
                : _mainCamera!.transform.position;
            TerrainFramePlan framePlan = _planner.Plan(
                _mainCamera!,
                focusPosition,
                _cellSize,
                _viewportPadding,
                lightingEngine.RequiredTerrainPadding,
                lightingEngine.StableRegionPaddingCells,
                _window.ProspectiveOrigin,
                _window.Width,
                _window.Height,
                _window.IsInitialized,
                _window.CellsCommitted,
                _window.HasCpuBuildInFlight,
                _cameraSpeedCellsPerSecond,
                _window.EstimatedPreparationSeconds,
                _lightingViewport,
                allowPartialAdvance: !holdingView,
                _storage,
                _mapManager,
                _connectionService,
                _telemetry);
            float planMs = TerrainStallReport.ElapsedMs(planStart);
            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw && _window.CellsCommitted;
            }

            if (!framePlan.ShouldProcess)
            {
                return;
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
                _terrainContentRevision,
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

            float processMs = publishMs + TerrainStallReport.ElapsedMs(processStart);

            // Окно рисуется только опубликованным: до первой публикации, после
            // смены мира или размера на GPU нет согласованной версии.
            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw && _window.CellsCommitted;
            }

            // Освещение узнаёт об изменённых клетках в кадре, когда их новая
            // геометрия действительно на экране, а не когда пришёл пакет.
            _publishedChangedRegions.Clear();
            _window.TakePublishedChangedRegions(_publishedChangedRegions);
            for (int index = 0; index < _publishedChangedRegions.Count; index++)
            {
                RectInt region = _publishedChangedRegions[index];
                lightingEngine.InvalidateRegion(region.x, region.y, region.width, region.height);
            }

            if (holdingView)
            {
                // Кадр по-прежнему показывает старое место: меш показа и
                // область освещения остаются прежними, план кадра считан для
                // места назначения.
                PublishViewTransitionReadiness(_viewTransition!);
                PublishLightingUpdate(lightingEngine, _lightingViewport);
                lightingEngine.CaptureBudgetViolationIfNeeded();
                return;
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
                    processMs,
                    uploadMs,
                    dirtyRectCount,
                    dirtyArea));
        }

        /// <summary>
        /// Готово ли место назначения: окно, которое окажется на экране после
        /// публикации, собрано, нового шага не идёт и текстуры его типов на
        /// месте. Готовая область сужена на запас меша показа — камера
        /// встанет только туда, где её кадр рисуется целиком.
        /// </summary>
        private void PublishViewTransitionReadiness(Kern.World.Streaming.WorldViewTransition transition)
        {
            bool ready =
                !_window.HasCpuBuildInFlight &&
                !_window.NeedsRefresh &&
                _window.PendingTextureCellTypes.Count == 0 &&
                !_window.HasUnpublishedTextureRefresh &&
                _textureService.PendingCellTextureRequests == 0 &&
                (_window.HeldOrigin != null || _window.CellsCommitted);
            if (!ready)
            {
                transition.ClearReady();
                return;
            }

            const int PresentationMarginCells = 4;
            Vector2Int origin = _window.ProspectiveOrigin;
            transition.MarkReady(new RectInt(
                origin.x + PresentationMarginCells,
                origin.y + PresentationMarginCells,
                _window.Width - (PresentationMarginCells * 2),
                _window.Height - (PresentationMarginCells * 2)));
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
            if (_mapManager == null)
            {
                _window.NeedsRefresh = true;
                _terrainContentRevision++;
                return;
            }

            // Ревизия — это «картинка окна изменится». Чанк на другом конце
            // карты её не меняет; поднять ревизию ради него значило бы
            // отправить свет в полный пересчёт статики при следующем шаге.
            if (_window.RecordWorldChange(serverX, serverY, width, height, _mapManager.WorldHeight))
            {
                _terrainContentRevision++;
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

                // Поле материалов семплит альбедо и эмиссию из атласа: новые
                // rect'ы меняют его содержимое без смены геометрии. Без бампа
                // ревизии поле осталось бы с чёрным/старым альбедо (и без
                // свечения) до первой копки или сдвига региона. Ревизия уйдёт
                // в свет вместе с публикацией шага, который перечитает тип.
                _terrainContentRevision++;
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
            _window.InvalidateWorld();
            _terrainContentRevision++;
            _lightingEngine?.InvalidateStaticCache();
        }

        private void OnCellLayerChunkLoaded(int serverX, int serverY, int width, int height)
        {
            _telemetry.TerrainChunkLoadCount++;

            // Предзаказ — только для чанков, в которые окно может въехать
            // ближайшим шагом. Дальний чанк заказал бы текстуры типов, которых
            // игрок, возможно, не увидит, и раздул бы атлас впустую.
            if (_storage != null && _textureService != null && _mapManager != null &&
                _storage.CellLayer is { } layer &&
                _window.IsNearBuildWindow(
                    TerrainDirtyTracker.ToUnityRect(serverX, serverY, width, height, _mapManager.WorldHeight),
                    layer.ChunkSize))
            {
                _texturePrefetch.PrefetchRegion(_storage, _textureService, serverX, serverY, width, height);
            }

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

        private void UpdateCameraSpeedEstimate(Camera camera)
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (!_hasCameraSpeedSample || deltaTime <= 0f || _cellSize <= 0f)
            {
                _lastCameraPosition = camera.transform.position;
                _hasCameraSpeedSample = true;
                _cameraSpeedCellsPerSecond = 0f;
                return;
            }

            Vector3 position = camera.transform.position;
            float distanceCells = Vector2.Distance(
                new Vector2(_lastCameraPosition.x, _lastCameraPosition.y),
                new Vector2(position.x, position.y)) /
                _cellSize;
            _lastCameraPosition = position;

            // Прыжок дальше окна — телепорт, а не скорость: окно всё равно
            // собирается заново, и раздувать им опережение ходьбы незачем.
            if (distanceCells >= Mathf.Max(_window.Width, _window.Height))
            {
                return;
            }

            // Сглаживание убирает дрожь кадра: запас переякоривания не
            // должен скакать от кадра к кадру при ровной ходьбе.
            _cameraSpeedCellsPerSecond = Mathf.Lerp(
                _cameraSpeedCellsPerSecond,
                distanceCells / deltaTime,
                0.2f);
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
            // LightingUpdateCoordinator waits for the first terrain upload
            // before solving. During the asynchronous initial terrain build,
            // there is intentionally no world-light texture to validate yet.
            if (_window.CellsCommitted)
            {
                _window.Driver.Materials.ValidateLightingBinding();
            }
        }
    }
}
