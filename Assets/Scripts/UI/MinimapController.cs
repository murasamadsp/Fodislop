#nullable enable

using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Chunk-batched minimap renderer with time-throttled updates and async GPU upload.
    /// No coroutines, no per-cell WorldLayer.<T> indexer calls — reads whole chunks at once.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        [SerializeField]
        private int _uiSize = 160;

        // UI Toolkit
        [Inject]
        private UIDocument? _doc;
        [Inject]
        private IObjectResolver _resolver = null!;
        private VisualElement? _minimapRoot;
        private Image? _minimapImageElement;
        private Label? _coordinatesLabel;
        private Texture2D? _minimapTexture;


        // World state
        private PlayerMovementController? _player;
        [Inject]
        private MapStorage? _mapStorage = null!;

        [Inject]
        private MapManager? _mapManager = null!;
        private WorldLayer<CellType>? _cellLayer;
        private int _worldWidth;
        private int _worldHeight;

        // Pixel buffer and cell color cache
        private Color32[]? _pixelColors;
        private readonly Dictionary<CellType, Color32> _cellColors = new(256);

        // Per-update chunk cache (reused, cleared each frame — allocation-free)
        private readonly MapCellSampler _cellSampler = new();

        // Throttle state
        private Vector2Int _lastUpdatePos; public Vector2Int LastUpdatePos => _lastUpdatePos;
        private float _lastUpdateTime;
        private bool _ready;
        private bool _initialRefreshDone;
        private bool _lastRefreshHadLoadedCells;
        private long _lastRenderedStorageRevision = -1;
        private bool _chunkLoadRefreshRequested;
        private WorldLayer<CellType>? _subscribedCellLayer;
        private bool _playerMoveSubscribed;

        // Toggle state
        private bool _isVisible = true;
        private bool _uiCreated;

        private const float UPDATE_DELAY = 0.1f; // 10 FPS — sufficient for minimap

        private static readonly Color32 UnloadedColor = new(0, 0, 0, 255);
        private static readonly Color32 OutOfBoundsColor = new(0, 0, 0, 255);
        private static readonly Color32 MarkerColor = Color.white;
        private static readonly Color32 CenterColor = Color.red;

        protected void Start()
        {
            if (_uiSize < 3)
            {
                throw new InvalidOperationException(
                    $"Minimap size must be at least 3 pixels for the player marker; got {_uiSize}.");
            }

            // GameBootstrap (IPostStartable.PostStart) injects [Inject] fields only after
            // MonoBehaviour.Start, so _mapManager/_mapStorage are null here. Never disable
            // the component based on that: Update() -> TryInitialize() resolves them via
            // the injected resolver and waits for the world to become ready. World
            // dimensions are computed there too (InitializeWorldState), so they are not
            // duplicated here.

            // Every texel is a discrete world-cell sample. Bilinear filtering
            // invents colors between adjacent cells and blurs unloaded chunk
            // boundaries, so the display must preserve nearest-neighbour data.
            _minimapTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                _uiSize,
                _uiSize,
                "MinimapTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            _pixelColors = new Color32[_uiSize * _uiSize];

            CreateUI();
            if (!_uiCreated)
            {
                // UIDocument не готов в Start (PostStart-инъекция позже) — ретраим
                // создание UI из Update, мир и плеер привязываем без падений.
            }

            _player = PlayerMovementController.LocalPlayer;
            if (_player != null)
            {
                BindPlayer(_player);
            }
            else
            {
                PlayerMovementController.OnLocalPlayerSpawned += OnPlayerSpawned;
            }
        }

        private void OnPlayerSpawned(PlayerMovementController player)
        {
            PlayerMovementController.OnLocalPlayerSpawned -= OnPlayerSpawned;
            _player = player;
            BindPlayer(player);
            if (_ready)
            {
                UpdateCoordinatesText(_player.Position.x, _player.Position.y);
                if (_isVisible)
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                }
            }
        }

        /// <summary>
        /// One-time initialization check (replaces coroutine).
        /// Runs every frame until the world is ready, then becomes a no-op.
        /// </summary>
        protected void Update()
        {
            if (!_uiCreated)
            {
                CreateUI();
            }

            if (!_ready || _player == null || !_player.HasServerPosition || !_initialRefreshDone)
            {
                TryInitialize();
            }

            if (_ready && _mapStorage != null &&
                !ReferenceEquals(_cellLayer, _mapStorage.CellLayer))
            {
                _ready = false;
                TryInitialize();
            }

            if (_ready && _initialRefreshDone && _isVisible &&
                _player != null && _player.HasServerPosition &&
                _mapStorage != null && _mapStorage.Revision != _lastRenderedStorageRevision)
            {
                _cellSampler.Invalidate();
                RefreshTexture(_player.Position.x, _player.Position.y);
                _lastRenderedStorageRevision = _mapStorage.Revision;
            }

            if (_chunkLoadRefreshRequested && _ready && _isVisible &&
                _player != null && _player.HasServerPosition)
            {
                _chunkLoadRefreshRequested = false;
                RefreshTexture(_player.Position.x, _player.Position.y);
                _initialRefreshDone = _lastRefreshHadLoadedCells;
                if (_initialRefreshDone)
                {
                    _lastRenderedStorageRevision = _mapStorage?.Revision ??
                        throw new InvalidOperationException(
                            "Minimap storage was lost after a chunk loaded.");
                }
            }

            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                ToggleVisibility();
            }
        }

        private void TryInitialize()
        {
            if (_resolver == null)
            {
                return;
            }

            _mapManager = _resolver.ResolveOrDefault<MapManager>();
            _mapStorage = _resolver.ResolveOrDefault<MapStorage>();

            if (_mapManager == null || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (_mapStorage == null || !_mapStorage.IsReady)
            {
                return;
            }

            PlayerMovementController? localPlayer = PlayerMovementController.LocalPlayer;
            if (localPlayer != null)
            {
                BindPlayer(localPlayer);
            }

            if (!_ready)
            {
                InitializeWorldState();
            }

            if (_player != null && _player.HasServerPosition && !_initialRefreshDone)
            {
                UpdateCoordinatesText(_player.Position.x, _player.Position.y);
                if (_isVisible)
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                }

                _lastUpdatePos = _player.Position;
                _lastUpdateTime = Time.time;
                _initialRefreshDone = !_isVisible || _lastRefreshHadLoadedCells;
                if (_initialRefreshDone)
                {
                    _lastRenderedStorageRevision = _mapStorage.Revision;
                }
            }
        }

        private void CacheCellColors()
        {
            if (_mapManager == null)
            {
                return;
            }

            for (int i = 0; i <= 255; i++)
            {
                CellType cellType = (CellType)i;
                if (cellType == CellType.Unloaded)
                {
                    _cellColors[cellType] = UnloadedColor;
                    continue;
                }

                Color color = _mapManager.GetCellMinimapColor(cellType);
                if (color.a < 0.01f)
                {
                    color = new Color(0.3f, 0.3f, 0.3f, 1f);
                }

                _cellColors[cellType] = (Color32)color;
            }
        }

        private void InitializeWorldState()
        {
            if (_mapStorage == null || _mapManager == null)
            {
                return;
            }

            _cellLayer = _mapStorage.CellLayer;
            if (_cellLayer == null)
            {
                return;
            }

            if (!ReferenceEquals(_subscribedCellLayer, _cellLayer))
            {
                if (_subscribedCellLayer != null)
                {
                    _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                }

                _subscribedCellLayer = _cellLayer;
                _subscribedCellLayer.ChunkLoaded += OnChunkLoaded;
                _cellSampler.Bind(_cellLayer);
                _cellSampler.Invalidate();
                _chunkLoadRefreshRequested = true;
                _initialRefreshDone = false;
                _lastRenderedStorageRevision = -1;
            }

            _worldWidth = _mapManager.WorldWidth;
            _worldHeight = _mapManager.WorldHeight;
            CacheCellColors();
            _ready = true;
            SetVisible(_isVisible);
        }

        private void CreateUI()
        {
            if (_uiCreated)
            {
                return;
            }

            _doc ??= _resolver?.Resolve<UIDocument>();
            if (_doc == null || _doc.rootVisualElement == null)
            {
                // Не бросаем: UIDocument может появиться после этого Start (PostStart-
                // инъекция или аддитивная загрузка сцены). Update ретраит CreateUI —
                // ждём молча, иначе первый кадр роняет клиент.
                return;
            }


            var root = _doc.rootVisualElement;
            _minimapRoot = new VisualElement();
            _minimapRoot.name = "MinimapPanel";
            _minimapRoot.AddToClassList("hud-minimap-panel");
            _minimapRoot.AddToClassList("sci-fi-panel");

            _coordinatesLabel = new Label(string.Empty);
            _coordinatesLabel.AddToClassList("hud-minimap-coords");
            _minimapRoot.Add(_coordinatesLabel);

            var imageContainer = new VisualElement();
            imageContainer.AddToClassList("hud-minimap-container");

            _minimapImageElement = new Image();
            _minimapImageElement.image = _minimapTexture;
            _minimapImageElement.AddToClassList("hud-minimap-image");
            imageContainer.Add(_minimapImageElement);

            _minimapRoot.Add(imageContainer);
            root.Add(_minimapRoot);

            _isVisible = true;
            SetVisible(false);
            _uiCreated = true;
        }


        protected void OnEnable()
        {
            if (_ready)
            {
                RebindRuntimeSources();
                SetVisible(_isVisible);
            }
        }

        protected void OnDisable()
        {
            SetVisible(false);
        }

        private void BindPlayer(PlayerMovementController player)
        {
            if (ReferenceEquals(_player, player) && _playerMoveSubscribed)
            {
                return;
            }

            if (_playerMoveSubscribed && _player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
            }

            _player = player;
            _player.OnPlayerMoved -= OnPlayerMoved;
            _player.OnPlayerMoved += OnPlayerMoved;
            _playerMoveSubscribed = true;
        }

        private void RebindRuntimeSources()
        {
            if (_resolver == null)
            {
                _ready = false;
                return;
            }

            _mapManager = _resolver.ResolveOrDefault<MapManager>();
            _mapStorage = _resolver.ResolveOrDefault<MapStorage>();
            if (_mapManager == null || _mapStorage == null)
            {
                _ready = false;
                return;
            }

            PlayerMovementController.OnLocalPlayerSpawned -= OnPlayerSpawned;
            if (_playerMoveSubscribed && _player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
                _playerMoveSubscribed = false;
            }

            _player = PlayerMovementController.LocalPlayer;
            if (_player != null)
            {
                BindPlayer(_player);
            }
            else
            {
                PlayerMovementController.OnLocalPlayerSpawned += OnPlayerSpawned;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                _subscribedCellLayer = null;
            }

            _cellLayer = null;
            _cellSampler.Bind(null);
            _cellSampler.Invalidate();
            _ready = false;
            InitializeWorldState();
        }

        private void OnPlayerMoved(Vector2Int oldPos, Vector2Int newPos)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!_ready)
            {
                TryInitialize();
                if (!_ready)
                {
                    return;
                }
            }

            if (_player != null)
            {
                UpdateCoordinatesText(_player.Position.x, _player.Position.y);
            }

            if (!_isVisible)
            {
                return;
            }

            float now = Time.time;
            if (now - _lastUpdateTime >= UPDATE_DELAY)
            {
                _lastUpdateTime = now;
                _lastUpdatePos = newPos;
                RefreshTexture(newPos.x, newPos.y);
                MapStorage storage = _mapStorage ??
                    throw new InvalidOperationException("Minimap storage was lost during refresh.");
                _lastRenderedStorageRevision = storage.Revision;
            }
        }

        private void OnChunkLoaded(int serverX, int serverY, int width, int height)
        {
            _cellSampler.Invalidate();
            _chunkLoadRefreshRequested = true;
        }

        private void RefreshTexture(int playerX, int playerY)
        {
            int halfSize = _uiSize / 2;
            int minX = playerX - halfSize;
            int texSize = _uiSize;
            Color32[]? colors = _pixelColors;
            if (colors == null)
            {
                return;
            }

            Dictionary<CellType, Color32> cellColors = _cellColors;

            int index = 0;
            bool hasLoadedCells = false;

            for (int texY = 0; texY < texSize; texY++)
            {
                // texY = 0 is bottom of screen (deeper underground, larger Server Y)
                // texY = texSize - 1 is top of screen (towards surface, smaller Server Y)
                int serverY = playerY + halfSize - texY;

                if (serverY < 0 || serverY >= _worldHeight)
                {
                    // Entire row is out of bounds
                    int end = index + texSize;
                    while (index < end)
                    {
                        colors[index++] = OutOfBoundsColor;
                    }

                    continue;
                }

                for (int texX = 0; texX < texSize; texX++)
                {
                    int serverX = minX + texX;

                    if (serverX < 0 || serverX >= _worldWidth)
                    {
                        colors[index++] = OutOfBoundsColor;
                        continue;
                    }

                    if (_cellSampler.TryGetCell(serverX, serverY, out CellType cellType))
                    {
                        hasLoadedCells = true;
                        colors[index++] = cellType == CellType.Unloaded
                            ? UnloadedColor
                            : cellColors[cellType];
                    }
                    else
                    {
                        colors[index++] = UnloadedColor;
                    }
                }
            }

            // Draw player marker (plus sign)
            int cx = halfSize;
            colors[(cx * texSize) + cx - 1] = MarkerColor;
            colors[(cx * texSize) + cx] = CenterColor;
            colors[(cx * texSize) + cx + 1] = MarkerColor;
            colors[((cx - 1) * texSize) + cx] = MarkerColor;
            colors[((cx + 1) * texSize) + cx] = MarkerColor;

            if (_minimapTexture != null)
            {
                _minimapTexture.SetPixels32(colors);

                // Keep the texture readable: this texture is updated again on
                // every throttled player movement. Passing true discards the
                // CPU copy and makes the next SetPixels32 fail/force a costly
                // reallocation.
                _minimapTexture.Apply(false); // Async GPU upload — non-blocking
            }

            _lastRefreshHadLoadedCells = hasLoadedCells;
        }

        private void UpdateCoordinatesText(int x, int y)
        {
            if (_coordinatesLabel != null)
            {
                _coordinatesLabel.text = $"{x}:{y}";
            }
        }

        public void ForceRefresh()
        {
            if (isActiveAndEnabled && _player != null && _ready && _isVisible)
            {
                RefreshTexture(_player.Position.x, _player.Position.y);
            }
        }

        protected void OnDestroy()
        {
            PlayerMovementController.OnLocalPlayerSpawned -= OnPlayerSpawned;

            if (_player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
                _playerMoveSubscribed = false;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                _subscribedCellLayer = null;
            }

            if (_minimapRoot != null && _minimapRoot.parent != null)
            {
                _minimapRoot.parent.Remove(_minimapRoot);
                _minimapRoot = null;
            }

            if (_minimapTexture != null)
            {
                Destroy(_minimapTexture);
            }
        }

        private void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            SetVisible(_isVisible);
            if (_isVisible && _player != null && _ready)
            {
                _lastUpdateTime = Time.time;
                _lastUpdatePos = _player.Position;
                RefreshTexture(_player.Position.x, _player.Position.y);
                MapStorage storage = _mapStorage ??
                    throw new InvalidOperationException("Minimap storage was lost while becoming visible.");
                _lastRenderedStorageRevision = storage.Revision;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_minimapRoot != null)
            {
                _minimapRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
