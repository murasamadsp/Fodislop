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

        private int _texWidth;
        private int _texHeight;
        private Canvas? _canvas;
        private RawImage? _rawImage;
        private Texture2D? _mapTexture;
        private Color32[]? _pixelBuffer;
        private Color32[] _cellColorTable = new Color32[256];
        private Color32 _defaultColor = new Color32(48, 48, 48, 255);
        private WorldLayer<CellType>? _cellLayer;
        private int _chunkSize = 32;
        private int _heightChunks;
        private readonly Dictionary<int, CellType[]?> _chunkCache = new();

        private float _viewCenterX;
        private float _viewCenterY;
        private float _cellsPerPixel = 1f;

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

        private float _playerBlinkTimer;
        private bool _playerBlinkState = true;

        protected void Awake()
        {
            _storage ??= Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>();
            _manager ??= Fodinae.Core.ServiceLocator.Resolve<MapManager>();
        }

        protected void Start()
        {
            _player = UnityEngine.Object.FindAnyObjectByType<PlayerMovementController>();
            if (_storage == null || _manager == null)
            {
                Debug.LogError("[WorldMapRenderer] MapStorage or MapManager not available");
                enabled = false;
                return;
            }

            CreateCanvas();
            InitColorTable();
            InitTexture();

            int w = _manager.WorldWidth;
            int h = _manager.WorldHeight;
            if (_storage is MapStorage mapStorage && mapStorage.CellLayer != null)
            {
                _cellLayer = mapStorage.CellLayer;
                _chunkSize = _cellLayer.ChunkSize;
                _heightChunks = _cellLayer.HeightChunks;
            }

            _cellsPerPixel = Mathf.Max((float)w / _texWidth, (float)h / _texHeight, 0.05f);
            _viewCenterX = w / 2f;
            _viewCenterY = h / 2f;

            _scrollAction = new InputAction("MapScroll", binding: "<Mouse>/scroll");
            _scrollAction.performed += OnScroll;
            _scrollAction.Enable();

            if (_canvas != null && !_canvas.gameObject.activeSelf)
            {
                Hide();
            }
        }

        protected void OnDestroy()
        {
            _scrollAction?.Dispose();
            if (_mapTexture != null)
            {
                Destroy(_mapTexture);
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        protected void Update()
        {
            if (!enabled)
            {
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
            _texHeight = BASE_RES;
            _texWidth = Mathf.RoundToInt(BASE_RES * ((float)Screen.width / Screen.height));
            _mapTexture = new Texture2D(_texWidth, _texHeight, TextureFormat.RGBA32, false);
            _mapTexture.filterMode = FilterMode.Point;
            _mapTexture.wrapMode = TextureWrapMode.Clamp;
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
                    _viewCenterX -= delta.x * _cellsPerPixel * _dragSpeed;
                    _viewCenterY -= delta.y * _cellsPerPixel * _dragSpeed;
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
            if (_storage is MapStorage mapStorage &&
                mapStorage.Revision != _lastRenderedStorageRevision)
            {
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
            // texture that contains only ~500k pixels.
            if (!PrepareViewportChunks(worldW, worldH, cp, cx, cy, texW, texH))
            {
                return false;
            }

            for (int py = 0; py < texH; py++)
            {
                int rowStart = py * texW;
                float worldY = cy - ((py + 0.5f - (texH * 0.5f)) * cp);
                int serverY = Mathf.FloorToInt(worldY);

                for (int px = 0; px < texW; px++)
                {
                    float worldX = cx + ((px + 0.5f - (texW * 0.5f)) * cp);
                    int serverX = Mathf.FloorToInt(worldX);
                    Color32 color = _defaultColor;

                    if (serverX >= 0 && serverX < worldW && serverY >= 0 && serverY < worldH)
                    {
                        CellType type = GetCell(serverX, serverY);
                        color = _cellColorTable[(byte)type];
                    }

                    _pixelBuffer[rowStart + px] = color;
                }
            }

            if (_player != null && _playerBlinkState)
            {
                Vector2Int playerPos = _player.Position;

                float visibleLeft = cx - (texW * 0.5f * cp);
                float visibleRight = cx + (texW * 0.5f * cp);
                float visibleBottom = cy - (texH * 0.5f * cp);
                float visibleTop = cy + (texH * 0.5f * cp);
                if (playerPos.x + 1f >= visibleLeft && playerPos.x <= visibleRight &&
                    playerPos.y + 1f >= visibleBottom && playerPos.y <= visibleTop)
                {
                    float worldX_left = playerPos.x;
                    float worldX_right = playerPos.x + 1f;

                    float pixelX_left = ((worldX_left - cx) / cp) + (texW * 0.5f);
                    float pixelX_right = ((worldX_right - cx) / cp) + (texW * 0.5f);
                    float pixelY_top = ((cy - playerPos.y) / cp) + (texH * 0.5f);
                    float pixelY_bottom = ((cy - (playerPos.y + 1f)) / cp) + (texH * 0.5f);

                    int pixX_start = Mathf.Clamp(Mathf.RoundToInt(pixelX_left), 0, texW - 1);
                    int pixX_end = Mathf.Clamp(Mathf.RoundToInt(pixelX_right), 0, texW - 1);
                    int pixY_start = Mathf.Clamp(Mathf.RoundToInt(pixelY_bottom), 0, texH - 1);
                    int pixY_end = Mathf.Clamp(Mathf.RoundToInt(pixelY_top), 0, texH - 1);

                    Color32 playerColor = new Color32(255, 0, 0, 255);

                    for (int py = pixY_start; py <= pixY_end; py++)
                    {
                        int rowStart = py * texW;
                        for (int px = pixX_start; px <= pixX_end; px++)
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
            _lastRenderedStorageRevision = (_storage as MapStorage)?.Revision ?? -1;
            return true;
        }

        private bool PrepareViewportChunks(
            int worldWidth,
            int worldHeight,
            float cellsPerPixel,
            float centerX,
            float centerY,
            int textureWidth,
            int textureHeight)
        {
            if (_cellLayer == null || _chunkSize <= 0 || _heightChunks <= 0)
            {
                return true;
            }

            int minX = Mathf.Clamp(
                Mathf.FloorToInt(centerX - (textureWidth * 0.5f * cellsPerPixel)),
                0,
                worldWidth - 1);
            int maxX = Mathf.Clamp(
                Mathf.CeilToInt(centerX + (textureWidth * 0.5f * cellsPerPixel)),
                0,
                worldWidth - 1);
            int minY = Mathf.Clamp(
                Mathf.FloorToInt(centerY - (textureHeight * 0.5f * cellsPerPixel)),
                0,
                worldHeight - 1);
            int maxY = Mathf.Clamp(
                Mathf.CeilToInt(centerY + (textureHeight * 0.5f * cellsPerPixel)),
                0,
                worldHeight - 1);

            _chunkCache.Clear();
            int firstChunkX = minX / _chunkSize;
            int lastChunkX = maxX / _chunkSize;
            int firstChunkY = minY / _chunkSize;
            int lastChunkY = maxY / _chunkSize;
            for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
            {
                for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
                {
                    int chunkIndex = chunkY + (chunkX * _heightChunks);
                    CellType[]? chunk = _cellLayer.GetChunk(
                        chunkIndex,
                        createIfMissing: false,
                        touchLru: true);
                    if (chunk == null)
                    {
                        continue;
                    }

                    _chunkCache[chunkIndex] = chunk;
                }
            }

            return true;
        }

        private CellType GetCell(int serverX, int serverY)
        {
            if (_cellLayer == null || _chunkSize <= 0 || _heightChunks <= 0)
            {
                var storage = _storage;
                if (storage == null)
                {
                    throw new InvalidOperationException("[WorldMapRenderer] Cannot read cells: storage is not initialized");
                }

                return storage.GetCell(serverX, serverY);
            }

            int chunkX = serverX / _chunkSize;
            int chunkY = serverY / _chunkSize;
            int chunkIndex = chunkY + (chunkX * _heightChunks);
            if (!_chunkCache.TryGetValue(chunkIndex, out CellType[]? chunk))
            {
                chunk = _cellLayer.GetChunk(chunkIndex, createIfMissing: false, touchLru: false);
                _chunkCache[chunkIndex] = chunk;
            }

            if (chunk == null)
            {
                return CellType.Unloaded;
            }

            int localX = serverX % _chunkSize;
            int localY = serverY % _chunkSize;
            return chunk[localY + (localX * _chunkSize)];
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

            _cellsPerPixel *= 1f - (delta * 0.1f);
            _cellsPerPixel = Mathf.Clamp(_cellsPerPixel, 0.02f, 10f);
            _renderRequested = true;
        }
    }
}
