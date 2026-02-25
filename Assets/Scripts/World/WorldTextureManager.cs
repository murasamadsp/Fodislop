using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Manages texture loading, caching, and atlas management for world cells.
    /// Integrates with WorldLayer to provide texture coordinates for terrain rendering.
    /// </summary>
    public class WorldTextureManager : MonoBehaviour
    {
        private static WorldTextureManager _instance;
        public static WorldTextureManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<WorldTextureManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[WorldTextureManager]");
                        _instance = go.AddComponent<WorldTextureManager>();
                    }
                }
                return _instance;
            }
        }

        [Header("Atlas Configuration")]
        [Tooltip("Initial atlas size in pixels")]
        [SerializeField] private int _initialAtlasSize = 512;
        [Tooltip("Maximum atlas size in pixels")]
        [SerializeField] private int _maxAtlasSize = 4096;
        [Tooltip("Texture padding between cells in atlas")]
        [SerializeField] private int _texturePadding = 2;

        [Header("Performance")]
        [Tooltip("Texture size for world cells")]
        [SerializeField] private int _cellTextureSize = 32;
        [Tooltip("Enable texture compression")]
        [SerializeField] private bool _enableCompression = true;

        private TextureAtlas _currentAtlas;
        private CellTextureCache _textureCache;
        private readonly ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = new();
        private readonly List<TextureAtlas> _atlases = new();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Initialize()
        {
            _textureCache = new CellTextureCache();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);
            _atlases.Add(_currentAtlas);
            
            // Register for asset loading completion
            ClientAssetLoader.Instance.OnTextureLoaded += OnTextureLoaded;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                ClientAssetLoader.Instance.OnTextureLoaded -= OnTextureLoaded;
            }
        }

        /// <summary>
        /// Event handler for when textures are loaded
        /// </summary>
        private void OnTextureLoadedHandler(string filename, Texture2D texture)
        {
            // Notify any listeners that textures have been loaded
            OnTextureLoaded?.Invoke(filename, texture);
        }

        /// <summary>
        /// Event raised when a texture is loaded
        /// </summary>
        public event Action<string, Texture2D> OnTextureLoaded;

        /// <summary>
        /// Get texture coordinates for a specific cell type and position.
        /// If texture is not loaded, it will be requested from the server.
        /// </summary>
        /// <param name="cellType">The type of cell</param>
        /// <param name="globalX">Global X position for variation calculation</param>
        /// <param name="globalY">Global Y position for variation calculation</param>
        /// <returns>Atlas coordinates and texture info</returns>
        public async UniTask<AtlasCoordinate> GetCellTextureCoordinate(CellType cellType, int globalX, int globalY)
        {
            if (cellType == CellType.Unloaded || cellType == CellType.Pregener)
            {
                return AtlasCoordinate.Empty;
            }

            // Check if we already have this texture in cache
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                var variation = CalculateVariation(textureInfo, globalX, globalY);
                return _currentAtlas.GetCoordinate(cellType, variation);
            }

            // Check if we're already requesting this texture
            if (_pendingRequests.TryGetValue(cellType, out var request))
            {
                await request.Task;
                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    var variation = CalculateVariation(textureInfo, globalX, globalY);
                    return _currentAtlas.GetCoordinate(cellType, variation);
                }
            }

            // Request the texture
            request = new TextureRequest(cellType);
            _pendingRequests.TryAdd(cellType, request);

            try
            {
                await LoadTexture(cellType);
                request.SetResult(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load texture for cell type {cellType}: {ex.Message}");
                request.SetResult(false);
                return AtlasCoordinate.Empty;
            }
            finally
            {
                _pendingRequests.TryRemove(cellType, out _);
            }

            if (_textureCache.TryGetTexture(cellType, out textureInfo))
            {
                var variation = CalculateVariation(textureInfo, globalX, globalY);
                return _currentAtlas.GetCoordinate(cellType, variation);
            }

            return AtlasCoordinate.Empty;
        }

        private async UniTask LoadTexture(CellType cellType)
        {
            var filename = $"/cells/{(int)cellType}.png";
            
            // Check cache first
            var cachedTexture = _textureCache.GetCachedTexture(cellType);
            if (cachedTexture != null)
            {
                await AddTextureToAtlas(cellType, cachedTexture);
                return;
            }

            // Request from server
            var tcs = new UniTaskCompletionSource<Texture2D>();
            ClientAssetLoader.Instance.LoadAndApplyTexture(
                (texture) => tcs.TrySetResult(texture),
                filename,
                default
            );

            var texture = await tcs.Task;
            if (texture != null)
            {
                await AddTextureToAtlas(cellType, texture);
            }
            else
            {
                Debug.LogWarning($"WorldTextureManager: Failed to load texture for cell type {cellType}");
            }
        }

        private async UniTask AddTextureToAtlas(CellType cellType, Texture2D texture)
        {
            // Get animation frame height from server configuration
            int frameHeight = 0;
            int animationSpeed = 0;
            bool hasAnimation = false;
            
            // Safely get animation info from MapManager
            if (MapManager.Instance != null)
            {
                try
                {
                    frameHeight = MapManager.Instance.GetAnimationFrameHeight(cellType);
                    animationSpeed = MapManager.Instance.GetAnimationSpeed(cellType);
                    hasAnimation = MapManager.Instance.HasAnimation(cellType);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to get animation info for cell type {cellType}: {ex.Message}");
                }
            }

            // Calculate actual cell size based on animation frames
            int actualCellSize = _cellTextureSize;
            if (hasAnimation && frameHeight > 0)
            {
                // If texture height is greater than frame height, it contains multiple frames
                if (texture.height > frameHeight)
                {
                    actualCellSize = frameHeight;
                }
            }

            // Ensure texture is the correct size for the first frame
            if (texture.width != actualCellSize || texture.height < actualCellSize)
            {
                texture = ResizeTexture(texture, actualCellSize, actualCellSize);
            }

            // Create texture info
            var textureInfo = new CellTextureInfo
            {
                CellType = cellType,
                BaseTexture = texture,
                HasVariations = true, // Assume variations exist for now
                VariationCount = 4,   // Default 2x2 variations
                AnimationFrames = hasAnimation && frameHeight > 0 ? texture.height / frameHeight : 1,
                FramesPerRow = 1,     // Default for single frame textures
                FrameSize = actualCellSize
            };

            // Try to add to current atlas
            if (!_currentAtlas.TryAddTexture(cellType, texture, out var coordinate))
            {
                // Atlas is full, create a new one
                var newSize = Mathf.Min(_currentAtlas.Size * 2, _maxAtlasSize);
                if (newSize > _currentAtlas.Size)
                {
                    var newAtlas = new TextureAtlas(newSize, actualCellSize, _texturePadding);
                    _atlases.Add(newAtlas);
                    _currentAtlas = newAtlas;
                    
                    // Add to new atlas
                    if (!_currentAtlas.TryAddTexture(cellType, texture, out coordinate))
                    {
                        Debug.LogError($"Failed to add texture to new atlas of size {newSize}");
                        return;
                    }
                }
                else
                {
                    Debug.LogError($"Atlas size limit reached ({_maxAtlasSize}). Cannot add more textures.");
                    return;
                }
            }

            // Cache the texture info
            _textureCache.AddTexture(cellType, textureInfo);
            
            // Update atlas texture
            await _currentAtlas.UpdateAtlasTexture();
        }

        private CellVariation CalculateVariation(CellTextureInfo textureInfo, int globalX, int globalY)
        {
            if (!textureInfo.HasVariations)
                return CellVariation.None;

            // Calculate variation based on global position (donut topology)
            // This creates seamless variations across the world
            int variationX = (globalX % 2 + 2) % 2; // 0 or 1
            int variationY = (globalY % 2 + 2) % 2; // 0 or 1
            
            return new CellVariation
            {
                Horizontal = variationX == 1,
                Vertical = variationY == 1
            };
        }

        /// <summary>
        /// Resize a texture to the specified dimensions
        /// </summary>
        private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
            Graphics.Blit(source, rt);
            
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D result = new Texture2D(targetWidth, targetHeight);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }

        /// <summary>
        /// Get all active atlases for rendering
        /// </summary>
        public List<TextureAtlas> GetAllAtlases()
        {
            return _atlases;
        }

        /// <summary>
        /// Clear all cached textures and atlases (for cleanup)
        /// </summary>
        public void Clear()
        {
            _textureCache.Clear();
            foreach (var atlas in _atlases)
            {
                atlas.Clear();
            }
            _atlases.Clear();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);
            _atlases.Add(_currentAtlas);
        }

        /// <summary>
        /// Get texture cache statistics
        /// </summary>
        /// <returns>Cache statistics string</returns>
        public string GetCacheStats()
        {
            return _textureCache.GetCacheStats();
        }
    }

    /// <summary>
    /// Represents a texture request in progress
    /// </summary>
    internal class TextureRequest
    {
        public CellType CellType { get; }
        public UniTaskCompletionSource<bool> TaskSource { get; }

        public UniTask<bool> Task => TaskSource.Task;

        public TextureRequest(CellType cellType)
        {
            CellType = cellType;
            TaskSource = new UniTaskCompletionSource<bool>();
        }

        public void SetResult(bool success)
        {
            TaskSource.TrySetResult(success);
        }
    }
}