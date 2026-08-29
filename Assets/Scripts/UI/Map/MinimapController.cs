#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
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
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        [SerializeField]
        private int _uiSize = 160;

        // UI Toolkit
        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private MapModeState _mapModeState = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        private TemplateContainer? _minimapTree;
        private VisualElement? _minimapRoot;
        private Image? _minimapImageElement;
        private Label? _coordinatesLabel;
        private Texture2D? _minimapTexture;

        // World state
        private ILocalPlayer? _player;
        [Inject]
        private MapStorage _mapStorage = null!;

        [Inject]
        private MapManager _mapManager = null!;
        private WorldLayer<CellType>? _cellLayer;
        private int _worldWidth;
        private int _worldHeight;

        private MinimapTextureRenderer? _textureRenderer;
        private readonly MapCellSampler _cellSampler = new();

        // Throttle state
        private Vector2Int _lastUpdatePos;
        public Vector2Int LastUpdatePos => _lastUpdatePos;
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

        private const float UPDATE_DELAY = 0.1f;

        protected void Start()
        {
            if (_uiSize < 3)
            {
                throw new InvalidOperationException(
                    $"Minimap size must be at least 3 pixels for the player marker; got {_uiSize}.");
            }

            _textureRenderer = new MinimapTextureRenderer(_uiSize);

            _minimapTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                _uiSize,
                _uiSize,
                "MinimapTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            CreateUI();
            _mapModeState.Changed += OnMapModeChanged;

            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized += OnWorldReady;
                _mapManager.OnWorldDataLoaded += OnWorldReady;
            }

            if (IsWorldReady())
            {
                OnWorldReady();
            }

            _player = _localPlayer.Current;
            if (_player != null)
            {
                BindPlayer(_player);
            }
            else
            {
                _localPlayer.Changed += OnPlayerChanged;
            }
        }

        private bool IsWorldReady() =>
            _mapManager != null && _mapManager.IsWorldInitialized &&
            _mapStorage != null && _mapStorage.IsReady;

        private void OnWorldReady()
        {
            if (!IsWorldReady())
            {
                return;
            }

            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized -= OnWorldReady;
                _mapManager.OnWorldDataLoaded -= OnWorldReady;
            }

            TryInitialize();
        }

        private void OnPlayerChanged(ILocalPlayer? player)
        {
            _localPlayer.Changed -= OnPlayerChanged;
            if (player == null)
            {
                return;
            }

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

        protected void Update()
        {
            if (_ready && _mapStorage != null &&
                !ReferenceEquals(_cellLayer, _mapStorage.CellLayer))
            {
                _ready = false;
                InitializeWorldState();
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
            if (_mapManager == null || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (_mapStorage == null || !_mapStorage.IsReady)
            {
                return;
            }

            ILocalPlayer? localPlayer = _localPlayer.Current;
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
            _textureRenderer?.CacheCellColors(_mapManager);
            _ready = true;
            SetVisible(_isVisible);
        }

        private void CreateUI()
        {
            if (_uiCreated)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null)
            {
                // Не бросаем: UIDocument может появиться после этого Start (PostStart-
                // инъекция или аддитивная загрузка сцены); Update ретраит CreateUI —
                // ждём молча, иначе первый кадр роняет клиент.
                return;
            }

            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/Minimap") ??
                throw new InvalidOperationException("[Minimap] Resources/UI/Minimap.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            tree.AddToClassList("ui-fullscreen");
            _minimapTree = tree;
            _minimapRoot = tree.Q<VisualElement>("MinimapPanel") ??
                throw new InvalidOperationException("[Minimap] MinimapPanel is missing from Minimap.uxml.");
            _coordinatesLabel = tree.Q<Label>("MinimapCoordinates") ??
                throw new InvalidOperationException("[Minimap] MinimapCoordinates is missing from Minimap.uxml.");
            _minimapImageElement = tree.Q<Image>("MinimapImage") ??
                throw new InvalidOperationException("[Minimap] MinimapImage is missing from Minimap.uxml.");
            _minimapImageElement.image = _minimapTexture;

            _minimapRoot.RegisterCallback<ClickEvent>(evt =>
            {
                _mapModeState.SetOpen(true);
                evt.StopPropagation();
            });
            _doc.rootVisualElement.Add(tree);

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

        private void BindPlayer(ILocalPlayer player)
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
            if (_mapManager == null || _mapStorage == null)
            {
                _ready = false;
                return;
            }

            if (_localPlayer == null)
            {
                _ready = false;
                return;
            }

            _localPlayer.Changed -= OnPlayerChanged;
            if (_playerMoveSubscribed && _player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
                _playerMoveSubscribed = false;
            }

            _player = _localPlayer.Current;
            if (_player != null)
            {
                BindPlayer(_player);
            }
            else
            {
                _localPlayer.Changed += OnPlayerChanged;
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
            if (!isActiveAndEnabled || !_ready)
            {
                return;
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
            if (_textureRenderer == null)
            {
                return;
            }

            _lastRefreshHadLoadedCells = _textureRenderer.Render(
                _minimapTexture,
                playerX,
                playerY,
                _worldWidth,
                _worldHeight,
                _cellSampler);
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
            if (_mapModeState != null)
            {
                _mapModeState.Changed -= OnMapModeChanged;
            }

            _localPlayer.Changed -= OnPlayerChanged;

            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized -= OnWorldReady;
                _mapManager.OnWorldDataLoaded -= OnWorldReady;
            }

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

            if (_minimapTree != null && _minimapTree.parent != null)
            {
                _minimapTree.parent.Remove(_minimapTree);
                _minimapTree = null;
            }

            _minimapRoot = null;

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

        private void OnMapModeChanged(bool mapModeEnabled)
        {
            SetVisible(!mapModeEnabled && _isVisible);
        }
    }
}
