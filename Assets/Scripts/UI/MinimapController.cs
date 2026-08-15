#nullable enable

using System.Collections.Generic;
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
    /// <summary>
    /// Chunk-batched minimap renderer with time-throttled updates and async GPU upload.
    /// No coroutines, no per-cell WorldLayer.<T> indexer calls — reads whole chunks at once.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        [SerializeField]
        private int _uiSize = 200;

        // UI
        private Text? _coordinatesText;
        private RawImage? _minimapImage;
        private Texture2D? _minimapTexture;
        private GameObject? _minimapObj;
        private GameObject? _textObj;

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
        private long _lastRenderedStorageRevision = -1;
        private bool _chunkLoadRefreshRequested;
        private WorldLayer<CellType>? _subscribedCellLayer;

        // Toggle state
        private bool _isVisible = true;

        private const float UPDATE_DELAY = 0.1f; // 10 FPS — sufficient for minimap

        private static readonly Color32 UnloadedColor = new(0, 0, 0, 255);
        private static readonly Color32 OutOfBoundsColor = new(0, 0, 0, 255);
        private static readonly Color32 MarkerColor = Color.white;
        private static readonly Color32 CenterColor = Color.red;

        protected void Start()
        {
            // GameBootstrap (IPostStartable.PostStart) injects [Inject] fields only after
            // MonoBehaviour.Start, so _mapManager/_mapStorage are null here. Never disable
            // the component based on that: Update() -> TryInitialize() resolves them via
            // ServiceLocator and waits for the world to become ready. World dimensions are
            // computed there too (InitializeWorldState), so they are not duplicated here.

            // Render at the on-screen display size so 1 world cell = 1 screen pixel.
            // A lower-res texture upscaled with Point filtering produces jagged,
            // shimmering cells; matching the display resolution removes that
            // upscale aliasing entirely, and Bilinear covers canvas DPI scaling.
            _minimapTexture = new Texture2D(_uiSize, _uiSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            _pixelColors = new Color32[_uiSize * _uiSize];

            CreateUI();

            _player = PlayerMovementController.LocalPlayer;
            if (_player != null)
            {
                _player.OnPlayerMoved += OnPlayerMoved;
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
            if (_player != null)
            {
                _player.OnPlayerMoved += OnPlayerMoved;
                if (_ready)
                {
                    UpdateCoordinatesText(_player.Position.x, _player.Position.y);
                    if (_isVisible)
                    {
                        RefreshTexture(_player.Position.x, _player.Position.y);
                    }
                }
            }
        }

        /// <summary>
        /// One-time initialization check (replaces coroutine).
        /// Runs every frame until the world is ready, then becomes a no-op.
        /// </summary>
        protected void Update()
        {
            if (!_ready || _player == null || !_player.HasServerPosition)
            {
                TryInitialize();
            }

            if (_ready && _mapStorage != null &&
                !ReferenceEquals(_cellLayer, _mapStorage.CellLayer))
            {
                _ready = false;
                TryInitialize();
            }

            if (_ready && _isVisible && _player != null && _player.HasServerPosition &&
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
            }

            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                ToggleVisibility();
            }
        }

        private void TryInitialize()
        {
            if (!Fodinae.Core.ServiceLocator.IsInitialized)
            {
                return;
            }

            if (_mapManager == null)
            {
                _mapManager = Fodinae.Core.ServiceLocator.Resolve<MapManager>();
            }

            if (_mapStorage == null)
            {
                _mapStorage = Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            }

            if (_mapManager == null || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (_mapStorage == null || !_mapStorage.IsReady)
            {
                return;
            }

            _player ??= PlayerMovementController.LocalPlayer;

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
                _lastRenderedStorageRevision = _mapStorage?.Revision ?? -1;
                _initialRefreshDone = true;
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
            // UI Toolkit renders independently from uGUI. A dedicated overlay canvas
            // prevents a full-screen UIDocument from covering the minimap.
            GameObject canvasObj = new("MinimapCanvas");
            canvasObj.transform.SetParent(transform, false);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;

            // Runtime UI Toolkit occupies a full-screen panel. A negative
            // overlay order places uGUI behind that panel even where its
            // visual background is transparent.
            canvas.sortingOrder = 10;
            canvasObj.AddComponent<CanvasScaler>();

            // Minimap image
            _minimapObj = new GameObject("Minimap");
            _minimapObj.transform.SetParent(canvas.transform, false);
            _minimapImage = _minimapObj.AddComponent<RawImage>();
            _minimapImage.texture = _minimapTexture;
            _minimapImage.color = Color.white;

            RectTransform rt = _minimapObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(10, 10);
            rt.sizeDelta = new Vector2(_uiSize, _uiSize);

            // Coordinates text
            _textObj = new GameObject("PlayerCoordinates");
            _textObj.transform.SetParent(canvas.transform, false);
            _coordinatesText = _textObj.AddComponent<Text>();
            _coordinatesText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_coordinatesText.font == null)
            {
                _coordinatesText.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            }

            _coordinatesText.fontSize = 20;
            _coordinatesText.color = Color.white;
            _coordinatesText.alignment = TextAnchor.MiddleCenter;
            _coordinatesText.text = string.Empty;
            _coordinatesText.fontStyle = FontStyle.Bold;
            _coordinatesText.raycastTarget = false;

            Shadow shadow = _textObj.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(2, -2);

            RectTransform textRt = _textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.zero;
            textRt.pivot = new Vector2(0.5f, 1f);
            textRt.anchoredPosition = new Vector2(10 + (_uiSize * 0.5f), 10 + _uiSize + 5);
            textRt.sizeDelta = new Vector2(200, 30);
            _textObj.transform.SetAsLastSibling();

            // The minimap is a permanent in-game HUD element. It becomes visible
            // only once a world has been initialized.
            _isVisible = true;
            SetVisible(false);
        }

        protected void OnEnable()
        {
            if (_ready)
            {
                SetVisible(_isVisible);
            }
        }

        protected void OnDisable()
        {
            SetVisible(false);
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
                _lastRenderedStorageRevision = _mapStorage?.Revision ?? -1;
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
                        colors[index++] = cellColors[cellType];
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
        }

        private void UpdateCoordinatesText(int x, int y)
        {
            if (_coordinatesText != null)
            {
                _coordinatesText.text = $"{x}:{y}";
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
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                _subscribedCellLayer = null;
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
                _lastRenderedStorageRevision = _mapStorage?.Revision ?? -1;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_minimapObj != null)
            {
                _minimapObj.SetActive(visible);
            }

            if (_textObj != null)
            {
                _textObj.SetActive(visible);
            }
        }
    }
}
