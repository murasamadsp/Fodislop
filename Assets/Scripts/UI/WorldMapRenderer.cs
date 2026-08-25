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
using UnityEngine.UIElements;
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
        private int _lastPanelWidth = -1;
        private int _lastPanelHeight = -1;
        private UIDocument? _document;
        private VisualElement? _mapOverlay;
        private Image? _mapImage;
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
        [Inject]
        private UIDocument? _injectedDocument;
        private PlayerMovementController? _player;

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

        protected void Start()
        {
            TryInitialize();
        }

        protected void OnEnable()
        {
            if (_initialized)
            {
                RebindRuntimeSources();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            if (_storage == null || _manager == null)
            {
                throw new InvalidOperationException(
                    "WorldMapRenderer requires injected IWorldDataStorage and MapManager dependencies.");
            }
            if (!_manager.IsWorldInitialized || !_storage.IsReady)
            {
                return;
            }

            EnsurePlayerBinding();

            if (!BindUi())
            {
                // PlayerHUDView attaches PlayerHUD.uxml after the shared
                // UIDocument has been injected. Keep this renderer idle until
                // that one-time UI build completes instead of treating the
                // transient empty root as a broken scene contract.
                return;
            }
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

            if (_mapOverlay != null)
            {
                Hide();
            }

            _initialized = true;
        }

        protected void OnDestroy()
        {
            if (_mapTexture != null)
            {
                Destroy(_mapTexture);
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
            // Dependencies are injected once by the game resolver. Reinitialize
            // only transient map bindings when the component is re-enabled.
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
            _cellSampler.InvalidateChunk(serverX, serverY);
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

            if (_mapOverlay != null)
            {
                Rect panelRect = _mapOverlay.worldBound;
                int curW = panelRect.width > 0f ? Mathf.RoundToInt(panelRect.width) : 0;
                int curH = panelRect.height > 0f ? Mathf.RoundToInt(panelRect.height) : 0;
                if (curW > 0 && curH > 0 && (curW != _lastPanelWidth || curH != _lastPanelHeight))
                {
                    InitTexture();
                    _renderRequested = true;
                }
            }

            HandleMouseScroll();
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
            if (_storage == null || _manager == null || _mapOverlay == null)
            {
                return;
            }

            _mapOverlay.style.display = DisplayStyle.Flex;

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
            if (_mapOverlay != null)
            {
                _mapOverlay.style.display = DisplayStyle.None;
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

        private bool BindUi()
        {
            _document = _injectedDocument ??
                throw new InvalidOperationException(
                    "WorldMapRenderer requires an injected UIDocument.");
            VisualElement? overlay = _document.rootVisualElement.Q<VisualElement>("WorldMapOverlay");
            Image? image = overlay?.Q<Image>("WorldMapImage");
            if (overlay == null || image == null)
            {
                return false;
            }

            _mapOverlay = overlay;
            _mapImage = image;
            _mapOverlay.style.display = DisplayStyle.Flex;
            _mapImage.image = null;
            return true;
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
            VisualElement overlay = _mapOverlay ?? throw new InvalidOperationException(
                "[WorldMapRenderer] UI must be bound before the map texture.");
            Rect panelRect = overlay.worldBound;
            int width = panelRect.width > 0f ? Mathf.RoundToInt(panelRect.width) : 1920;
            int height = panelRect.height > 0f ? Mathf.RoundToInt(panelRect.height) : 1080;

            // Bound map texture resolution to prevent high-DPI Retina allocations (e.g. 7.3M texels).
            // UI Toolkit scales this buffer through the WorldMapImage USS layout.
            const int MAX_MAP_WIDTH = 960;
            const int MAX_MAP_HEIGHT = 540;

            float aspect = (float)width / Mathf.Max(1, height);
            int targetWidth = MAX_MAP_WIDTH;
            int targetHeight = Mathf.RoundToInt(targetWidth / aspect);
            if (targetHeight > MAX_MAP_HEIGHT)
            {
                targetHeight = MAX_MAP_HEIGHT;
                targetWidth = Mathf.RoundToInt(targetHeight * aspect);
            }

            _texWidth = Mathf.Max(16, targetWidth);
            _texHeight = Mathf.Max(16, targetHeight);
            _lastPanelWidth = width;
            _lastPanelHeight = height;

            if (_mapTexture != null)
            {
                Destroy(_mapTexture);
            }

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

            _pixelBuffer = new Color32[_texWidth * _texHeight];

            if (_mapImage != null)
            {
                _mapImage.image = _mapTexture;
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
                    // Dragging right moves view left (decrease centerX).
                    // Dragging up moves view up towards surface (decrease centerY).
                    _viewCenterX -= delta.x * _cellsPerPixel * _dragSpeed;
                    _viewCenterY -= delta.y * _cellsPerPixel * _dragSpeed;
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

                // Texture2D row zero is the bottom of the displayed map image.
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

        private void HandleMouseScroll()
        {
            if (!enabled || _mapOverlay == null ||
                _mapOverlay.resolvedStyle.display == DisplayStyle.None || Mouse.current == null)
            {
                return;
            }

            float delta = Mouse.current.scroll.ReadValue().y;
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
            if (Mouse.current == null || _mapImage == null || _document?.rootVisualElement.panel == null ||
                _texWidth <= 0 || _texHeight <= 0)
            {
                return false;
            }

            Rect rect = _mapImage.worldBound;
            if (rect.width <= 0f || rect.height <= 0f ||
                float.IsNaN(rect.width) || float.IsNaN(rect.height) ||
                float.IsInfinity(rect.width) || float.IsInfinity(rect.height))
            {
                return false;
            }

            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(
                _document.rootVisualElement.panel,
                Mouse.current.position.ReadValue());
            float pixelX = ((panelPoint.x - rect.xMin) / rect.width) * _texWidth;
            float pixelY = ((panelPoint.y - rect.yMin) / rect.height) * _texHeight;
            worldX = _viewCenterX +
                ((pixelX - (_texWidth * 0.5f)) * _cellsPerPixel);
            worldY = _viewCenterY +
                (((_texHeight - pixelY) - (_texHeight * 0.5f)) * _cellsPerPixel);
            return true;
        }

        private void ApplyCursorAnchor(float worldX, float worldY)
        {
            if (Mouse.current == null || _mapImage == null || _document?.rootVisualElement.panel == null ||
                _texWidth <= 0 || _texHeight <= 0)
            {
                return;
            }

            Rect rect = _mapImage.worldBound;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(
                _document.rootVisualElement.panel,
                Mouse.current.position.ReadValue());
            float pixelX = ((panelPoint.x - rect.xMin) / rect.width) * _texWidth;
            float pixelY = ((panelPoint.y - rect.yMin) / rect.height) * _texHeight;
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
