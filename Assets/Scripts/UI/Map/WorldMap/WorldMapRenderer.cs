#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.World;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    public class WorldMapRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField]
        private float _dragSpeed = 1f;

        private readonly WorldMapPanel _panel = new();
        private readonly MapTextureController _textureController = new();
        [Inject]
        private MapCellSampler _cellSampler = null!;
        private readonly MapInteractionController _interaction = new();
        private WorldMapPointerBinder _pointerBinder = null!;
        private WorldMapInputDispatcher _inputDispatcher = null!;
        private readonly MapViewportRenderer _viewportRenderer = new();
        private readonly WorldMapMipSource _mipSource = new();
        private WorldMapLayerBinding _layerBinding = null!;
        private MapPlayerTracker _playerTracker = null!;
        private VisualElement? _documentRoot;

        private float _viewCenterX;
        private float _viewCenterY;
        private float _cellsPerPixel = 1f;
        private float _maxCellsPerPixel = 10f;

        // Remaining click route drawn over the map (transparent overlay texture).
        private readonly WorldMapPathOverlay _pathOverlay = new();
        private IClickPathWalker? _pathWalker;

        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private MapManager _manager = null!;
        [Inject]
        private UIDocument _injectedDocument = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;

        private bool _renderRequested;
        private long _lastRenderedStorageRevision = -1;
        private bool _followPlayer = true;
        private readonly WorldMapBounds _bounds = new();
        private bool _isInitialized;

        [Inject]
        private ILocalizationService _localization = null!;
        [Inject]
        private MapModeState _mapModeState = null!;

        public event Action? CloseRequested;

        protected void Start()
        {
            _documentRoot = _injectedDocument.rootVisualElement;
            if (_documentRoot.panel == null)
            {
                _documentRoot.RegisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            }

            _mipSource.SetRequestRenderCallback(RequestRender);
            _pointerBinder = new WorldMapPointerBinder(_panel);
            _inputDispatcher = new WorldMapInputDispatcher(
                _interaction, _panel, _textureController, ClampViewCenter, RequestRender);
            _layerBinding = new WorldMapLayerBinding(
                _cellSampler,
                _mipSource,
                RequestRender,
                RequestFullRender);
            _playerTracker = new MapPlayerTracker(_localPlayer);
            _playerTracker.OnPlayerSpawned += () =>
            {
                BindPathWalker(_localPlayer.Current as IClickPathWalker);
                _renderRequested = true;
            };
            _playerTracker.OnPlayerMoved += pos =>
            {
                if (_followPlayer)
                {
                    _viewCenterX = pos.x;
                    _viewCenterY = pos.y;
                    _renderRequested = true;
                }
            };
            _playerTracker.OnPlayerRelocated += pos =>
            {
                BindPathWalker(_localPlayer.Current as IClickPathWalker);
                _followPlayer = true;
                _viewCenterX = pos.x;
                _viewCenterY = pos.y;
                ClampViewCenter();
                _renderRequested = true;
            };
            _playerTracker.OnBlinkFlipped += UpdatePlayerMarker;

            TryInitialize();
            _manager.OnWorldInitialized += OnWorldReady;
            _manager.OnWorldDataLoaded += OnWorldReady;

            if (IsWorldReady())
            {
                OnWorldReady();
            }
        }

        private bool IsWorldReady() =>
            _manager.IsWorldInitialized && _storage.IsReady;

        private void OnWorldReady()
        {
            if (!IsWorldReady())
            {
                return;
            }

            if (!_isInitialized)
            {
                TryInitialize();
            }
            else if (_storage != null)
            {
                ResetWorldViewState(_storage);
            }
        }

        protected void OnEnable()
        {
            if (_isInitialized)
            {
                RebindRuntimeSources();
            }
        }

        private void TryInitialize()
        {
            if (_isInitialized || !IsWorldReady())
            {
                return;
            }

            if (!TryBindUI())
            {
                return;
            }

            _pointerBinder.Bind(OnMapPointerDown, OnMapPointerMove, OnMapPointerUp, OnMapClick);

            BindPathWalker(_localPlayer.Current as IClickPathWalker);

            _playerTracker ??= new MapPlayerTracker(_localPlayer);
            _playerTracker.EnsureBinding();

            InitTexture();
            ResetWorldViewState(_storage);

            _isInitialized = true;
            RebindRuntimeSources();

            if (_mapModeState != null && _mapModeState.IsOpen)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        private bool TryBindUI()
        {
            // The panel may not be ready on the first attempt; it retries on demand.
            return _panel.TryBind(
                _injectedDocument,
                OnCloseButtonClicked,
                FollowPlayer,
                OnWorldMapWheel,
                OnPanelGeometryChanged);
        }

        private void OnPanelGeometryChanged()
        {
            if (!_isInitialized || !enabled)
            {
                return;
            }

            if (_panel.Image != null && _textureController.CheckPanelResize(_panel.Image))
            {
                InitTexture();
                _maxCellsPerPixel = _bounds.ComputeMaxZoomOut(
                    _textureController.TexWidth,
                    _textureController.TexHeight);
                _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);
                ClampViewCenter();
                _renderRequested = true;
            }

            UpdatePlayerMarker();
        }

        private void BindPathWalker(IClickPathWalker? walker)
        {
            if (ReferenceEquals(_pathWalker, walker))
            {
                return;
            }

            UnbindPathWalker();

            if (walker != null)
            {
                _pathWalker = walker;
                walker.OnPathChanged += OnWalkerPathChanged;
            }
        }

        private void UnbindPathWalker()
        {
            if (_pathWalker != null)
            {
                _pathWalker.OnPathChanged -= OnWalkerPathChanged;
                _pathWalker = null;
            }
        }

        private void OnDocumentAttached(AttachToPanelEvent _)
        {
            if (_documentRoot?.panel == null)
            {
                return;
            }

            _documentRoot.UnregisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            OnWorldReady();
        }

        private void OnCloseButtonClicked() => CloseRequested?.Invoke();

        private void FollowPlayer()
        {
            if (_playerTracker.CurrentPlayer is not { HasServerPosition: true } player)
            {
                return;
            }

            _followPlayer = true;
            _viewCenterX = player.Position.x;
            _viewCenterY = player.Position.y;
            ClampViewCenter();
            _renderRequested = true;
        }

        private void RequestRender() => _renderRequested = true;

        private void RequestFullRender()
        {
            _viewportRenderer.InvalidateViewState();
            _renderRequested = true;
        }

        private void OnWorldMapWheel(WheelEvent evt) =>
            _inputDispatcher.HandleWheel(evt, _maxCellsPerPixel, ref _cellsPerPixel, ref _viewCenterX, ref _viewCenterY);

        // Click on the map (without dragging): pixel -> server cell relative to the
        // view centre -> click route, same as LMB on the world and the minimap.
        private void OnMapClick(ClickEvent evt)
        {
            Image? image = _panel.Image;
            if (_interaction.WasDragging || image == null)
            {
                return;
            }

            Rect rect = image.contentRect;
            Vector2 local = new(evt.localPosition.x, evt.localPosition.y);
            if (rect.width <= 0f || rect.height <= 0f || !rect.Contains(local))
            {
                return;
            }

            int texWidth = _textureController.TexWidth;
            int texHeight = _textureController.TexHeight;
            float pixelX = (local.x - rect.x) / rect.width * texWidth;
            float pixelY = (local.y - rect.y) / rect.height * texHeight;
            Vector2 server = MapProjection.MapPixelToServer(
                pixelX,
                pixelY,
                _viewCenterX,
                _viewCenterY,
                _cellsPerPixel,
                texWidth,
                texHeight);
            var target = new Vector2Int(Mathf.FloorToInt(server.x), Mathf.FloorToInt(server.y));

            (_localPlayer.Current as IClickPathWalker)?.TryStartPath(target);
        }

        private void OnMapPointerDown(PointerDownEvent evt) =>
            _inputDispatcher.HandlePointerDown(evt);

        private void OnMapPointerMove(PointerMoveEvent evt) =>
            _inputDispatcher.HandlePointerMove(evt, _cellsPerPixel, _dragSpeed, ref _viewCenterX, ref _viewCenterY, ref _followPlayer);

        private void OnMapPointerUp(PointerUpEvent evt) =>
            _inputDispatcher.HandlePointerUp(evt);

        private void ResetWorldViewState(IWorldDataStorage storage)
        {
            _bounds.Bind(_manager, _manager.WorldWidth, _manager.WorldHeight);
            _viewportRenderer.InitColorTable(_manager);
            _viewportRenderer.InvalidateViewState();
            _layerBinding.BindCellLayer(storage.CellLayer);
            _layerBinding.BindMipScan(_manager.WorldWidth, _manager.WorldHeight, _viewportRenderer.CellColorTable);
            _cellsPerPixel = 1f;
            _maxCellsPerPixel = _bounds.ComputeMaxZoomOut(
                _textureController.TexWidth,
                _textureController.TexHeight);
            _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);

            ILocalPlayer? player = _playerTracker.CurrentPlayer;
            if (player is { HasServerPosition: true })
            {
                _viewCenterX = player.Position.x;
                _viewCenterY = player.Position.y;
            }
            else
            {
                _viewCenterX = _bounds.Width * 0.5f;
                _viewCenterY = _bounds.Height * 0.5f;
            }

            _lastRenderedStorageRevision = -1;
            _renderRequested = true;
        }

        protected void OnDestroy()
        {
            if (_documentRoot != null)
            {
                _documentRoot.UnregisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            }

            _pointerBinder?.Dispose(OnMapPointerDown, OnMapPointerMove, OnMapPointerUp, OnMapClick);

            UnbindPathWalker();
            _pathOverlay.Dispose();

            _panel.Dispose();
            _textureController.DestroyTexture();
            _viewportRenderer.Dispose();

            _manager.OnWorldInitialized -= OnWorldReady;
            _manager.OnWorldDataLoaded -= OnWorldReady;

            _playerTracker?.Dispose();

            _layerBinding?.Dispose();
            _mipSource.Dispose();
        }

        private void RebindRuntimeSources()
        {
            if (_storage == null)
            {
                return;
            }

            MapManager manager = _manager ?? throw new InvalidOperationException(
                "WorldMapRenderer cannot rebind runtime sources before MapManager injection.");

            _playerTracker?.EnsureBinding();
            BindPathWalker(_localPlayer?.Current as IClickPathWalker);
            _layerBinding.BindStorage(_storage);

            if (_storage.CellLayer == null)
            {
                _layerBinding.BindCellLayer(null);
                return;
            }

            IWorldLayer<CellType> cellLayer = _storage.CellLayer;
            if (_layerBinding.BindCellLayer(cellLayer))
            {
                _layerBinding.BindMipScan(manager.WorldWidth, manager.WorldHeight, _viewportRenderer.CellColorTable);
                return;
            }

            _layerBinding.RebindCellEvents();
        }

        private bool RequiresMipForCurrentView() =>
            _manager != null &&
            MapViewportChunkBudget.ShouldUseMip(
                _manager.WorldWidth,
                _manager.WorldHeight,
                _textureController.TexWidth,
                _textureController.TexHeight,
                _cellsPerPixel,
                _viewCenterX,
                _viewCenterY);

        private void UpdateMipStatus(bool requiresMip)
        {
            _panel.UpdatePreparationStatus(
                _mipSource.IsReady,
                requiresMip,
                _mipSource.Failed,
                _mipSource.Progress,
                _mipSource.Total,
                _localization);
        }

        protected void Update()
        {
            if (!enabled || !_isInitialized)
            {
                return;
            }

            if (_panel.IsDocumentDisabled)
            {
                return;
            }

            if (_manager == null || _storage == null ||
                !_manager.IsWorldInitialized || !_storage.IsReady)
            {
                _layerBinding.BindCellLayer(null);
                _renderRequested = false;
                return;
            }

            if (_panel.Image != null && _textureController.CheckPanelResize(_panel.Image))
            {
                InitTexture();
                _maxCellsPerPixel = _bounds.ComputeMaxZoomOut(
                    _textureController.TexWidth,
                    _textureController.TexHeight);
                _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);
                ClampViewCenter();
                _renderRequested = true;
            }

            _playerTracker.Update(
                Time.deltaTime,
                _followPlayer,
                ref _viewCenterX,
                ref _viewCenterY,
                ref _renderRequested);

            _mipSource.ApplyPending();

            UpdatePlayerMarker();

            HandleQueuedRender();
        }

        public void Show()
        {
            if (!_isInitialized)
            {
                TryInitialize();
                if (!_isInitialized)
                {
                    return;
                }
            }

            if (_storage == null || _manager == null || _panel.Overlay == null)
            {
                return;
            }

            _panel.Show();

            enabled = true;
            _renderRequested = true;
            _lastRenderedStorageRevision = -1;
            _followPlayer = true;
            _playerTracker?.ResetState();
            UpdatePlayerMarker();
            UpdateMipStatus(RequiresMipForCurrentView());
        }

        public void Hide()
        {
            _panel.HidePlayerMarker();
            _panel.Hide();
            enabled = false;
        }

        public void SetViewCenter(float worldX, float worldY)
        {
            if (!Mathf.Approximately(_viewCenterX, worldX) ||
                !Mathf.Approximately(_viewCenterY, worldY))
            {
                _renderRequested = true;
            }

            _viewCenterX = worldX;
            _viewCenterY = worldY;
            ClampViewCenter();
        }

        private void InitTexture()
        {
            Image viewport = _panel.Image ?? throw new InvalidOperationException(
                "[WorldMapRenderer] UI must be bound before the map texture.");
            _viewportRenderer.InvalidateViewState();
            _textureController.InitTexture(viewport, viewport);
            _pathOverlay.Ensure(_panel, _textureController.TexWidth, _textureController.TexHeight);
        }

        private void OnWalkerPathChanged(IReadOnlyList<Vector2Int>? _) => _renderRequested = true;

        private void HandleQueuedRender()
        {
            IWorldDataStorage storage = _storage ??
                throw new InvalidOperationException("WorldMapRenderer storage is not initialized.");
            if (storage.Revision != _lastRenderedStorageRevision)
            {
                _renderRequested = true;
            }

            if (!ReferenceEquals(_layerBinding.CellLayer, storage.CellLayer))
            {
                _layerBinding.BindCellLayer(storage.CellLayer);
                _layerBinding.BindMipScan(_manager.WorldWidth, _manager.WorldHeight, _viewportRenderer.CellColorTable);
                _renderRequested = true;
                _lastRenderedStorageRevision = -1;
            }

            if (_manager != null && !_bounds.Matches(_manager))
            {
                ResetWorldViewState(storage);
            }

            if (!_renderRequested)
            {
                return;
            }

            if (_manager == null || _storage == null)
            {
                return;
            }

            bool requiresMip = RequiresMipForCurrentView();
            if (requiresMip && !_mipSource.IsReady && !_mipSource.Failed)
            {
                _mipSource.Begin();
            }

            UpdateMipStatus(requiresMip);

            _viewportRenderer.Render(
                _textureController.MapTexture,
                _manager.WorldWidth,
                _manager.WorldHeight,
                _cellSampler,
                _mipSource,
                _textureController.TexWidth,
                _textureController.TexHeight,
                _cellsPerPixel,
                _viewCenterX,
                _viewCenterY);

            _panel.Image?.MarkDirtyRepaint();
            _renderRequested = false;
            _lastRenderedStorageRevision = _storage.Revision;
            _pathOverlay.Draw(
                _pathWalker?.Path,
                _pathWalker?.PathIndex ?? 0,
                _playerTracker?.CurrentPlayer,
                _viewCenterX,
                _viewCenterY,
                _cellsPerPixel);
        }

        private void UpdatePlayerMarker()
        {
            ILocalPlayer? player = _playerTracker?.CurrentPlayer;
            if (player is { HasServerPosition: true })
            {
                Vector2Int pos = player.Position;
                _panel.UpdatePlayerMarker(
                    pos.x,
                    pos.y,
                    _viewCenterX,
                    _viewCenterY,
                    _cellsPerPixel,
                    _textureController.TexWidth,
                    _textureController.TexHeight,
                    _playerTracker!.PlayerBlinkState);
            }
            else
            {
                _panel.HidePlayerMarker();
            }
        }

        private void ClampViewCenter()
        {
            if (_manager == null)
            {
                return;
            }

            _bounds.Clamp(
                ref _viewCenterX,
                ref _viewCenterY,
                _cellsPerPixel,
                _textureController.TexWidth,
                _textureController.TexHeight);
        }
    }
}
