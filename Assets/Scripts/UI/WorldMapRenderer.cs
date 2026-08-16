#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer;

namespace Fodinae.UI
{
    public class WorldMapRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField]
        private float _renderInterval = 0.1f;
        [SerializeField]
        private float _dragSpeed = 0.5f;

        private const int MaxChunkCacheEntries = 4096;

        private int _texWidth;
        private int _texHeight;
        private Canvas? _canvas;
        private RawImage? _rawImage;
        private Texture2D? _mapTexture;
        private Color32[]? _pixelBuffer;
        private Color32[] _cellColorTable = new Color32[256];
        private static readonly Color32 UnloadedColor = new(0, 0, 0, 255);
        private Color32 _defaultColor = UnloadedColor;
        private WorldLayer<CellType>? _cellLayer;
        private int _chunkSize = 32;
        private readonly MapCellSampler _cellSampler = new();

        private float _viewCenterX;
        private float _viewCenterY;
        private float _cellsPerPixel = 1f;
        private float _maxCellsPerPixel = 10f;

        [Inject]
        private IWorldDataStorage? _storage;

        [Inject]
        private MapManager? _manager;
        private PlayerMovementController? _player;
        private InputAction? _scrollAction;

        private bool _isDragging;
        private Vector2 _lastMousePos;
        private Vector2Int _lastPlayerPos;
        private float _lastRenderTime;
        private bool _initialRenderDone;
        private bool _renderRequested;
        private long _lastRenderedStorageRevision = -1;
        private bool _followPlayer = true;
        private WorldLayer<CellType>? _subscribedCellLayer;
        private int _boundWorldWidth;
        private int _boundWorldHeight;
        private string _boundWorldCodeName = string.Empty;
        private bool _initialized;
        private bool _playerSpawnSubscription;
        private bool _playerMoveSubscription;

        private float _playerBlinkTimer;
        private bool _playerBlinkState = true;

        protected void Awake()
        {
            if (!Fodinae.Core.ServiceLocator.IsInitialized)
            {
                return;
            }

            _storage ??= Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>();
            _manager ??= Fodinae.Core.ServiceLocator.Resolve<MapManager>();
        }

        protected void Start()
        {
            TryInitialize();
        }

        protected void OnEnable()
        {
            if (_initialized)
            {
                RebindRuntimeSources();
                EnsureScrollAction();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                EnsureScrollAction();
                return;
            }

            if (!ServiceLocator.IsInitialized)
            {
                return;
            }

            _storage ??= ServiceLocator.Resolve<IWorldDataStorage>() ??
                throw new InvalidOperationException(
                    "WorldMapRenderer requires IWorldDataStorage after the resolver was initialized.");
            _manager ??= ServiceLocator.Resolve<MapManager>() ??
                throw new InvalidOperationException(
                    "WorldMapRenderer requires MapManager after the resolver was initialized.");
            if (!_manager.IsWorldInitialized || !_storage.IsReady)
            {
                return;
            }

            EnsurePlayerBinding();

            CreateCanvas();
            InitColorTable();
            InitTexture();

            int w = _manager.WorldWidth;
            int h = _manager.WorldHeight;
            BindWorldDimensions(w, h);
            if (_storage is MapStorage mapStorage && mapStorage.CellLayer != null)
            {
                BindCellLayer(mapStorage.CellLayer);
            }

            // Start at a local view (1 world cell = 1 pixel) centered on the player,
            // not at whole-world zoom. Whole-world zoom on a large map would force
            // loading every chunk into the disk LRU at once (OOM / stall).
            _cellsPerPixel = 1f;
            _maxCellsPerPixel = ComputeMaxZoomOut(w, h);
            _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);
            if (_player is { HasServerPosition: true })
            {
                _viewCenterX = _player.Position.x;
                _viewCenterY = _player.Position.y;
            }
            else
            {
                _viewCenterX = w / 2f;
                _viewCenterY = h / 2f;
            }

            EnsureScrollAction();

            if (_canvas != null && !_canvas.gameObject.activeSelf)
            {
                Hide();
            }

            _initialized = true;
        }

        protected void OnDestroy()
        {
            DisposeScrollAction();
            if (_mapTexture != null)
            {
                Destroy(_mapTexture);
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            PlayerMovementController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
            if (_player != null)
            {
                UnsubscribeFromPlayer(_player);
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                _subscribedCellLayer = null;
            }
        }

        protected void OnDisable()
        {
            DisposeScrollAction();
        }

        private void DisposeScrollAction()
        {
            if (_scrollAction == null)
            {
                return;
            }

            _scrollAction.performed -= OnScroll;
            _scrollAction.Disable();
            _scrollAction.Dispose();
            _scrollAction = null;
        }

        private void EnsureScrollAction()
        {
            if (_scrollAction != null)
            {
                return;
            }

            _scrollAction = new InputAction("MapScroll", binding: "<Mouse>/scroll");
            _scrollAction.performed += OnScroll;
            _scrollAction.Enable();
        }

        private void SubscribeToPlayer(PlayerMovementController player)
        {
            if (_playerMoveSubscription && ReferenceEquals(_player, player))
            {
                return;
            }

            if (_playerMoveSubscription && _player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
            }

            _player = player;
            _player.OnPlayerMoved += OnPlayerMoved;
            _playerMoveSubscription = true;
        }

        private void EnsurePlayerBinding()
        {
            if (_playerSpawnSubscription)
            {
                PlayerMovementController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
                _playerSpawnSubscription = false;
            }

            PlayerMovementController? player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                SubscribeToPlayer(player);
                return;
            }

            PlayerMovementController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
            _playerSpawnSubscription = true;
        }

        private void RebindRuntimeSources()
        {
            if (!ServiceLocator.IsInitialized)
            {
                _initialized = false;
                return;
            }

            _storage = ServiceLocator.Resolve<IWorldDataStorage>();
            _manager = ServiceLocator.Resolve<MapManager>();
            if (_storage == null || _manager == null)
            {
                _initialized = false;
                return;
            }

            EnsurePlayerBinding();

            if (_storage is not MapStorage mapStorage || mapStorage.CellLayer == null)
            {
                BindCellLayer(null);
                return;
            }

            WorldLayer<CellType> cellLayer = mapStorage.CellLayer;
            if (!ReferenceEquals(_subscribedCellLayer, cellLayer))
            {
                BindCellLayer(cellLayer);
                return;
            }

            cellLayer.ChunkLoaded -= OnChunkLoaded;
            cellLayer.ChunkLoaded += OnChunkLoaded;
            _cellSampler.Bind(cellLayer);
            _cellSampler.Invalidate();
        }

        private void UnsubscribeFromPlayer(PlayerMovementController player)
        {
            if (!_playerMoveSubscription)
            {
                return;
            }

            player.OnPlayerMoved -= OnPlayerMoved;
            _playerMoveSubscription = false;
        }

        private void OnLocalPlayerSpawned(PlayerMovementController player)
        {
            PlayerMovementController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
            _playerSpawnSubscription = false;
            SubscribeToPlayer(player);
            _lastPlayerPos = new Vector2Int(int.MinValue, int.MinValue);
            _renderRequested = true;
        }

        private void OnPlayerMoved(Vector2Int oldPosition, Vector2Int newPosition)
        {
            _lastPlayerPos = newPosition;
            if (_followPlayer)
            {
                _viewCenterX = newPosition.x;
                _viewCenterY = newPosition.y;
                _renderRequested = true;
            }
        }

        private void OnChunkLoaded(int serverX, int serverY, int width, int height)
        {
            _cellSampler.Invalidate();
            _renderRequested = true;
        }

        private void BindCellLayer(WorldLayer<CellType>? cellLayer)
        {
            if (ReferenceEquals(_subscribedCellLayer, cellLayer))
            {
                return;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
            }

            _subscribedCellLayer = cellLayer;
            _cellLayer = cellLayer;
            _cellSampler.Bind(cellLayer);
            _cellSampler.Invalidate();

            if (_subscribedCellLayer != null)
            {
                _chunkSize = _subscribedCellLayer.ChunkSize;
                _subscribedCellLayer.ChunkLoaded += OnChunkLoaded;
            }
            else
            {
                _chunkSize = 0;
            }
        }

        private void BindWorldDimensions(int worldWidth, int worldHeight)
        {
            if (worldWidth <= 0 || worldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"[WorldMapRenderer] Invalid world dimensions: {worldWidth}x{worldHeight}.");
            }

            _boundWorldWidth = worldWidth;
            _boundWorldHeight = worldHeight;
            MapManager manager = _manager ??
                throw new InvalidOperationException(
                    "[WorldMapRenderer] MapManager is required before binding world dimensions.");
            if (string.IsNullOrWhiteSpace(manager.WorldCodeName))
            {
                throw new InvalidOperationException(
                    "[WorldMapRenderer] World code name is required before binding map state.");
            }

            _boundWorldCodeName = manager.WorldCodeName;
        }

        protected void Update()
        {
            if (!enabled)
            {
                return;
            }

            if (!_initialized)
            {
                TryInitialize();
                if (!_initialized)
                {
                    return;
                }
            }

            if (_manager == null || _storage == null ||
                !_manager.IsWorldInitialized || !_storage.IsReady)
            {
                BindCellLayer(null);
                _initialRenderDone = false;
                _renderRequested = false;
                return;
            }

            HandleDrag();
            HandleFollowPlayer();
            HandleQueuedRender();

            _playerBlinkTimer += Time.deltaTime;
            if (_playerBlinkTimer >= 0.5f)
            {
                _playerBlinkTimer = 0f;
                _playerBlinkState = !_playerBlinkState;
                _renderRequested = true;
            }
        }

        public void Show()
        {
            if (_storage == null || _manager == null)
            {
                if (!Fodinae.Core.ServiceLocator.IsInitialized)
                {
                    return;
                }

                _storage ??= Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>();
                _manager ??= Fodinae.Core.ServiceLocator.Resolve<MapManager>();
                if (_storage == null || _manager == null)
                {
                    return;
                }
            }

            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(true);
            }

            enabled = true;
            _lastRenderTime = -1f;
            _initialRenderDone = false;
            _renderRequested = true;
            _lastRenderedStorageRevision = -1;
            _followPlayer = true;
            _playerBlinkState = true;
            _playerBlinkTimer = 0f;
            _lastPlayerPos = new Vector2Int(int.MinValue, int.MinValue);
        }

        public void Hide()
        {
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(false);
            }

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

        private void CreateCanvas()
        {
            _canvas = new GameObject("MapCanvas").AddComponent<Canvas>();
            _canvas.transform.SetParent(transform, false);
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            _canvas.gameObject.AddComponent<CanvasScaler>();

            var go = new GameObject("MapRawImage");
            go.transform.SetParent(_canvas.transform, false);
            _rawImage = go.AddComponent<RawImage>();
            _rawImage.color = Color.white;
            _rawImage.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private void InitColorTable()
        {
            var manager = _manager;
            if (manager == null)
            {
                throw new InvalidOperationException("[WorldMapRenderer] Cannot build color table: map manager is not initialized");
            }

            for (int i = 0; i < 256; i++)
            {
                CellType type = (CellType)i;
                _cellColorTable[i] = (Color32)manager.GetCellMinimapColor(type);
            }
        }

        private void InitTexture()
        {
            const int BASE_RES = 512;
            Canvas canvas = _canvas ?? throw new InvalidOperationException(
                "[WorldMapRenderer] Canvas must be created before the map texture.");
            Canvas.ForceUpdateCanvases();
            Rect canvasRect = canvas.pixelRect;
            if (canvasRect.width <= 0f || canvasRect.height <= 0f)
            {
                throw new InvalidOperationException(
                    $"[WorldMapRenderer] Canvas has invalid layout {canvasRect.width}x{canvasRect.height}.");
            }

            _texHeight = BASE_RES;
            int canvasHeight = Mathf.RoundToInt(canvasRect.height);
            int canvasWidth = Mathf.RoundToInt(canvasRect.width);
            _texWidth = Mathf.Max(
                1,
                Mathf.RoundToInt(BASE_RES * ((float)canvasWidth / canvasHeight)));

            // This texture is categorical map data: one texel represents one
            // sampled world cell. Bilinear filtering fabricates blended terrain
            // types and makes chunk availability boundaries look loaded.
            _mapTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                _texWidth,
                _texHeight,
                "WorldMapTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            if (_rawImage != null)
            {
                _rawImage.texture = _mapTexture;
            }
        }

        private void HandleDrag()
        {
            if (Mouse.current == null)
            {
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                _isDragging = true;
                _followPlayer = false;
                _lastMousePos = Mouse.current.position.ReadValue();
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                _isDragging = false;
            }
            else if (_isDragging && Mouse.current.leftButton.isPressed)
            {
                Vector2 currentPos = Mouse.current.position.ReadValue();
                Vector2 delta = currentPos - _lastMousePos;
                _lastMousePos = currentPos;

                if (delta.sqrMagnitude > 1f)
                {
                    // Screen-space: +X right, +Y up. World: +X right, +Y down.
                    // Grab-style drag: content follows the cursor. Dragging right
                    // moves the view left (X +), dragging up moves the view "north"
                    // (smaller server Y), so centerY must INCREASE when delta.y is +.
                    _viewCenterX -= delta.x * _cellsPerPixel * _dragSpeed;
                    _viewCenterY += delta.y * _cellsPerPixel * _dragSpeed;
                    ClampViewCenter();
                    _renderRequested = true;
                }
            }
        }

        private void HandleFollowPlayer()
        {
            if (_player == null)
            {
                return;
            }

            var pos = _player.Position;
            bool moved = pos.x != _lastPlayerPos.x || pos.y != _lastPlayerPos.y;
            if (!moved)
            {
                return;
            }

            _lastPlayerPos = pos;

            if (_followPlayer)
            {
                _viewCenterX = pos.x;
                _viewCenterY = pos.y;
                _renderRequested = true;
            }
        }

        private void HandleQueuedRender()
        {
            IWorldDataStorage storage = _storage ??
                throw new InvalidOperationException("WorldMapRenderer storage is not initialized.");
            if (storage.Revision != _lastRenderedStorageRevision)
            {
                _cellSampler.Invalidate();
                _renderRequested = true;
            }

            if (!ReferenceEquals(_cellLayer, storage.CellLayer))
            {
                BindCellLayer(storage.CellLayer);
                _renderRequested = true;
                _initialRenderDone = false;
                _lastRenderedStorageRevision = -1;
            }

            if (_manager != null &&
                (_manager.WorldWidth != _boundWorldWidth ||
                 _manager.WorldHeight != _boundWorldHeight ||
                 !string.Equals(_manager.WorldCodeName, _boundWorldCodeName, StringComparison.Ordinal)))
            {
                BindWorldDimensions(_manager.WorldWidth, _manager.WorldHeight);
                InitColorTable();
                BindCellLayer(storage.CellLayer);
                _cellsPerPixel = 1f;
                _maxCellsPerPixel = ComputeMaxZoomOut(_boundWorldWidth, _boundWorldHeight);
                if (_player is { HasServerPosition: true })
                {
                    _viewCenterX = _player.Position.x;
                    _viewCenterY = _player.Position.y;
                }
                else
                {
                    _viewCenterX = _boundWorldWidth * 0.5f;
                    _viewCenterY = _boundWorldHeight * 0.5f;
                }

                _lastRenderedStorageRevision = -1;
                _initialRenderDone = false;
                _renderRequested = true;
            }

            if (!_renderRequested)
            {
                return;
            }

            if (_initialRenderDone && Time.time - _lastRenderTime < _renderInterval)
            {
                return;
            }

            if (!RenderViewport())
            {
                return;
            }

            _lastRenderTime = Time.time;
            _initialRenderDone = true;
        }

        private bool RenderViewport()
        {
            if (_manager == null || _storage == null)
            {
                return false;
            }

            int worldW = _manager.WorldWidth;
            int worldH = _manager.WorldHeight;
            float cp = _cellsPerPixel;
            float cx = _viewCenterX;
            float cy = _viewCenterY;
            int texW = _texWidth;
            int texH = _texHeight;

            Color32 defaultCol = _defaultColor;
            if (_pixelBuffer == null || _pixelBuffer.Length != texW * texH)
            {
                _pixelBuffer = new Color32[texW * texH];
            }

            for (int i = 0; i < _pixelBuffer.Length; i++)
            {
                _pixelBuffer[i] = defaultCol;
            }

            // Sample from screen pixels instead of iterating over every world
            // cell. When zoomed out, the old implementation walked the entire
            // world and then painted the same pixel many times. A 10k x 10k
            // world could therefore trigger 100 million GetCell calls for a
            // texture that contains only ~500k pixels. GetCell loads chunks
            // lazily through a bounded cache, so memory stays flat at any zoom.
            for (int py = 0; py < texH; py++)
            {
                int rowStart = py * texW;

                // Texture2D row zero is the bottom of the displayed RawImage.
                // Server coordinates use a top-left origin, so the bottom texture
                // row must sample the largest server Y in the viewport.
                float screenRowFromTop = (texH - 1 - py) + 0.5f;
                float worldY = cy + ((screenRowFromTop - (texH * 0.5f)) * cp);
                int serverY = Mathf.FloorToInt(worldY);

                for (int px = 0; px < texW; px++)
                {
                    float worldX = cx + ((px + 0.5f - (texW * 0.5f)) * cp);
                    int serverX = Mathf.FloorToInt(worldX);
                    Color32 color = _defaultColor;

                    if (serverX >= 0 && serverX < worldW && serverY >= 0 && serverY < worldH)
                    {
                        CellType type = GetCell(serverX, serverY);
                        color = type == CellType.Unloaded
                            ? UnloadedColor
                            : _cellColorTable[(byte)type];
                    }

                    _pixelBuffer[rowStart + px] = color;
                }
            }

            if (_player != null && _playerBlinkState)
            {
                Vector2Int playerPos = _player.Position;

                float halfW = texW * 0.5f * cp;
                float halfH = texH * 0.5f * cp;
                float leftX = cx - halfW;
                float rightX = cx + halfW;
                float topServerY = cy - halfH;
                float bottomServerY = cy + halfH;

                if (playerPos.x + 1f >= leftX && playerPos.x <= rightX &&
                    playerPos.y + 1f >= topServerY && playerPos.y <= bottomServerY)
                {
                    float pixelX = ((playerPos.x - cx) / cp) + (texW * 0.5f);
                    float pixelY = (texH * 0.5f) - 1f - ((playerPos.y - cy) / cp);
                    float markerSize = Mathf.Max(1f, 1f / cp);

                    int pxStart = Mathf.Clamp(Mathf.RoundToInt(pixelX), 0, texW - 1);
                    int pxEnd = Mathf.Clamp(Mathf.RoundToInt(pixelX + markerSize), 0, texW - 1);
                    int pyStart = Mathf.Clamp(Mathf.RoundToInt(pixelY), 0, texH - 1);
                    int pyEnd = Mathf.Clamp(Mathf.RoundToInt(pixelY + markerSize), 0, texH - 1);

                    Color32 playerColor = new Color32(255, 0, 0, 255);
                    for (int py = pyStart; py <= pyEnd; py++)
                    {
                        int rowStart = py * texW;
                        for (int px = pxStart; px <= pxEnd; px++)
                        {
                            _pixelBuffer[rowStart + px] = playerColor;
                        }
                    }
                }
            }

            if (_mapTexture != null)
            {
                _mapTexture.SetPixels32(_pixelBuffer);
                _mapTexture.Apply(false);
            }

            _renderRequested = false;
            _lastRenderedStorageRevision = _storage?.Revision ??
                throw new InvalidOperationException(
                    "WorldMapRenderer storage disappeared while rendering the map.");
            return true;
        }

        private CellType GetCell(int serverX, int serverY)
        {
            return _cellSampler.TryGetCell(serverX, serverY, out CellType cellType)
                ? cellType
                : CellType.Unloaded;
        }

        private float ComputeMaxZoomOut(int worldW, int worldH)
        {
            if (_texWidth <= 0 || _texHeight <= 0 || _chunkSize <= 0)
            {
                return 10f;
            }

            // Bound the number of chunks a single render pass may hold at once so
            // that zooming out on a huge world can never pin the whole map into
            // memory. Visible cells = texW * cp * texH * cp; each chunk holds
            // _chunkSize * _chunkSize cells, so cap cp by the chunk-cache budget.
            int visibleCellBudget = MaxChunkCacheEntries * _chunkSize * _chunkSize;
            float maxCp = Mathf.Sqrt((float)visibleCellBudget / (_texWidth * _texHeight));
            return Mathf.Max(1f, maxCp);
        }

        private void OnScroll(InputAction.CallbackContext ctx)
        {
            if (!enabled || _canvas == null || !_canvas.gameObject.activeSelf)
            {
                return;
            }

            float delta = ctx.ReadValue<Vector2>().y;
            if (Mathf.Abs(delta) < 0.01f)
            {
                return;
            }

            float oldCellsPerPixel = _cellsPerPixel;
            float cursorWorldX = 0f;
            float cursorWorldY = 0f;
            bool hasCursorAnchor = TryGetCursorWorldPosition(
                out cursorWorldX,
                out cursorWorldY);

            // Mouse-wheel values differ by platform: some backends report one
            // line per notch while others report 120. A bounded exponential
            // step gives the same usable zoom response in both cases and never
            // jumps directly to a clamp boundary.
            float zoomSteps = Mathf.Clamp(delta, -4f, 4f);
            _cellsPerPixel = Mathf.Clamp(
                oldCellsPerPixel * Mathf.Pow(0.85f, zoomSteps),
                0.25f,
                _maxCellsPerPixel);

            if (hasCursorAnchor && oldCellsPerPixel > 0f)
            {
                ApplyCursorAnchor(cursorWorldX, cursorWorldY);
            }

            ClampViewCenter();
            _renderRequested = true;
        }

        private bool TryGetCursorWorldPosition(out float worldX, out float worldY)
        {
            worldX = 0f;
            worldY = 0f;
            if (Mouse.current == null || _rawImage == null ||
                _texWidth <= 0 || _texHeight <= 0)
            {
                return false;
            }

            RectTransform rectTransform = _rawImage.rectTransform;
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f ||
                float.IsNaN(rect.width) || float.IsNaN(rect.height) ||
                float.IsInfinity(rect.width) || float.IsInfinity(rect.height))
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                Mouse.current.position.ReadValue(),
                null,
                out Vector2 localPoint))
            {
                return false;
            }

            float pixelX = ((localPoint.x - rect.xMin) / rect.width) * _texWidth;
            float pixelY = ((localPoint.y - rect.yMin) / rect.height) * _texHeight;
            worldX = _viewCenterX +
                ((pixelX - (_texWidth * 0.5f)) * _cellsPerPixel);
            worldY = _viewCenterY +
                (((_texHeight - pixelY) - (_texHeight * 0.5f)) * _cellsPerPixel);
            return true;
        }

        private void ApplyCursorAnchor(float worldX, float worldY)
        {
            if (Mouse.current == null || _rawImage == null ||
                _texWidth <= 0 || _texHeight <= 0)
            {
                return;
            }

            RectTransform rectTransform = _rawImage.rectTransform;
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                Mouse.current.position.ReadValue(),
                null,
                out Vector2 localPoint))
            {
                return;
            }

            float pixelX = ((localPoint.x - rect.xMin) / rect.width) * _texWidth;
            float pixelY = ((localPoint.y - rect.yMin) / rect.height) * _texHeight;
            _viewCenterX = worldX -
                ((pixelX - (_texWidth * 0.5f)) * _cellsPerPixel);
            _viewCenterY = worldY -
                (((_texHeight - pixelY) - (_texHeight * 0.5f)) * _cellsPerPixel);
        }

        private void ClampViewCenter()
        {
            if (_manager == null || _texWidth <= 0 || _texHeight <= 0)
            {
                return;
            }

            float halfWidth = _texWidth * 0.5f * _cellsPerPixel;
            float halfHeight = _texHeight * 0.5f * _cellsPerPixel;
            _viewCenterX = ClampCenter(_viewCenterX, halfWidth, _manager.WorldWidth);
            _viewCenterY = ClampCenter(_viewCenterY, halfHeight, _manager.WorldHeight);
        }

        private static float ClampCenter(float center, float halfViewport, int worldSize)
        {
            float worldCenter = worldSize * 0.5f;
            if (halfViewport * 2f >= worldSize)
            {
                return worldCenter;
            }

            return Mathf.Clamp(center, halfViewport, worldSize - halfViewport);
        }
    }
}
