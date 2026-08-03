#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World
{
    public class WorldTextureManager : MonoBehaviour, ITextureService
    {
        [Header("Atlas Configuration")]
        [SerializeField]
        private int _initialAtlasSize = 2048;
        [SerializeField]
        private int _maxAtlasSize = 4096;
        [SerializeField]
        private int _texturePadding = 2;

        [Header("Performance")]
        [SerializeField]
        private int _cellTextureSize = RenderingConstants.CELL_SIZE;

        [System.NonSerialized]
        public TextureAtlas _currentAtlas = null!;
        private CellTextureCache _textureCache = null!;
        private Texture2D? _flowMapTexture;
        public Texture2D? FlowMapTexture => _flowMapTexture;
        private ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = null!;
        private List<TextureAtlas> _atlases = null!;

        private Texture2D? _cachedEmptyTexture;

        public uint TextureRevision { get; private set; }

        protected void Awake()
        {
            // The atlas is created lazily on the first world texture request.
            // Creating a 4096² RGBA texture during scene startup caused a large
            // CPU/GPU allocation before the world was even initialized.
            Debug.Log("[WorldTextureManager] Awake — deferred atlas initialization");
        }

        protected void OnDestroy()
        {
            _textureCache?.Clear();
            if (_atlases != null)
            {
                foreach (var atlas in _atlases)
                {
                    atlas?.Dispose();
                }

                _atlases.Clear();
            }

            if (_flowMapTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_flowMapTexture);
                }
                else
                {
                    DestroyImmediate(_flowMapTexture);
                }

                _flowMapTexture = null;
            }
        }

        private void Initialize()
        {
            if (_textureCache != null && _atlases != null && _pendingRequests != null)
            {
                return;
            }

            _textureCache = new CellTextureCache();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);

            _atlases = new List<TextureAtlas>();
            _atlases.Add(_currentAtlas);

            _pendingRequests = new ConcurrentDictionary<CellType, TextureRequest>();

            GenerateFlowMap();
        }

        private void EnsureInitialized()
        {
            if (_textureCache == null || _atlases == null || _pendingRequests == null)
            {
                Initialize();
            }
        }

        private void GenerateFlowMap()
        {
            if (_flowMapTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_flowMapTexture);
                }
                else
                {
                    DestroyImmediate(_flowMapTexture);
                }
            }

            _flowMapTexture = new Texture2D(12, 10, TextureFormat.RGBA32, false);
            _flowMapTexture.name = "ShimmerFlowMap";
            _flowMapTexture.filterMode = FilterMode.Bilinear;
            _flowMapTexture.wrapMode = TextureWrapMode.Repeat;

            var random = new System.Random(42);
            var pixels = new Color[12 * 10];
            for (int i = 0; i < pixels.Length; i++)
            {
                float h = (float)random.NextDouble();
                pixels[i] = Color.HSVToRGB(h, 1f, 1f);
            }

            _flowMapTexture.SetPixels(pixels);
            _flowMapTexture.Apply(false, SystemInfo.copyTextureSupport != CopyTextureSupport.None);
        }

        public event Action<string, Texture2D>? OnTextureLoaded;

        public void RequestTexture(CellType cellType)
        {
            EnsureInitialized();
            GetCellTextureCoordinate(cellType, 0, 0).Forget();
        }

        public AtlasCoordinate GetCellTextureCoordinate(CellType cellType)
        {
            EnsureInitialized();
            return GetCellTextureCoordinateSync(cellType, 0, 0);
        }

        public bool HasAnimations(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                return textureInfo.AnimationFrames > 1;
            }

            return false;
        }

        public AtlasCoordinate GetCellTextureCoordinateSync(CellType cellType, int globalX, int globalY)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                var variation = CalculateVariation(textureInfo, globalX, globalY);

                int frameIndex = 0;
                int frameHeight = 0;

                if (textureInfo.AnimationFrames > 1)
                {
                    var mmForAnim = ServiceLocator.Resolve<MapManager>();
                    float speed = textureInfo.ContainerFPS > 0 ? textureInfo.ContainerFPS : (mmForAnim != null ? mmForAnim.GetAnimationSpeed(cellType) : 5f);
                    if (speed <= 0)
                    {
                        speed = 5;
                    }

                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % textureInfo.AnimationFrames;
                    frameHeight = textureInfo.ContainerFPS > 0 ? textureInfo.FrameSize : (mmForAnim != null ? mmForAnim.GetAnimationFrameHeight(cellType) : textureInfo.FrameSize);
                }

                foreach (var atlas in _atlases)
                {
                    if (atlas.ContainsCell(cellType))
                    {
                        return atlas.GetWrappedCoordinate(cellType, globalX, globalY, variation, frameHeight, frameIndex);
                    }
                }
            }

            return AtlasCoordinate.Empty;
        }

        public Vector4 GetCellFrameRect(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                var atlas = GetAtlasForCell(cellType);
                if (atlas != null)
                {
                    AtlasCoordinate baseCoord = atlas.GetCoordinate(cellType);
                    float atlasSize = atlas.Size;
                    int frameHeight = textureInfo.FrameSize;
                    return new Vector4(
                        (float)baseCoord.AtlasX / atlasSize,
                        (float)baseCoord.AtlasY / atlasSize,
                        (float)baseCoord.Width / atlasSize,
                        (float)frameHeight / atlasSize);
                }
            }

            return Vector4.zero;
        }

        public int GetAnimationFrameCount(CellType cellType)
        {
            EnsureInitialized();
            return _textureCache.TryGetTexture(cellType, out var info) ? info.AnimationFrames : 1;
        }

        public float GetAnimationSpeedForCell(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var info))
            {
                if (info.ContainerFPS > 0)
                {
                    return info.ContainerFPS;
                }

                var mmForSpeed = ServiceLocator.Resolve<MapManager>();
                var speed = mmForSpeed != null ? mmForSpeed.GetAnimationSpeed(cellType) : 5;
                return speed > 0 ? speed : 5;
            }

            return 5f;
        }

        public int GetFrameSize(CellType cellType)
        {
            EnsureInitialized();
            return _textureCache.TryGetTexture(cellType, out var info) ? info.FrameSize : 0;
        }

        public async UniTask<AtlasCoordinate> GetCellTextureCoordinate(CellType cellType, int globalX, int globalY)
        {
            await UniTask.SwitchToMainThread();
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                return GetCellTextureCoordinateSync(cellType, globalX, globalY);
            }

            if (_pendingRequests.TryGetValue(cellType, out var existingRequest))
            {
                await existingRequest.Task;
                await UniTask.SwitchToMainThread();
                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }
            }

            var request = new TextureRequest(cellType);
            if (!_pendingRequests.TryAdd(cellType, request))
            {
                // A request can arrive between the initial lookup above and
                // TryAdd. Join that request instead of decoding the same
                // texture a second time.
                if (_pendingRequests.TryGetValue(cellType, out var racingRequest))
                {
                    await racingRequest.Task;
                }

                await UniTask.SwitchToMainThread();
                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }

                throw new InvalidOperationException($"Failed to load texture for cell type {cellType} (joined racing request).");
            }

            try
            {
                await LoadTexture(cellType);
                await UniTask.SwitchToMainThread();
                request.SetResult(true);

                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }

                throw new InvalidOperationException($"Failed to load texture for cell type {cellType}: texture is not cached after load");
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
                request.SetResult(false);
                throw new InvalidOperationException($"Failed to load texture for cell type {cellType}: {ex.Message}", ex);
            }
            finally
            {
                _pendingRequests.TryRemove(cellType, out _);
            }
        }

        private async UniTask LoadTexture(CellType cellType)
        {
            var filename = $"Cells/{(int)cellType}";

            if (cellType == CellType.Empty)
            {
                filename = "Cells/32";
            }


            var cachedTexture = _textureCache.GetCachedTexture(cellType);
            if (cachedTexture != null)
            {
                bool alreadyInAtlas = false;
                foreach (var atlas in _atlases)
                {
                    if (atlas.ContainsCell(cellType))
                    {
                        alreadyInAtlas = true;
                        break;
                    }
                }

                if (!alreadyInAtlas)
                {
                    AddTextureToAtlas(cellType, cachedTexture);
                }

                return;
            }

            Texture2D? texture = null;
            try
            {
                var loader = ServiceLocator.Resolve<IAssetLoader>() as ClientAssetLoader;
                if (loader != null)
                {
                    texture = await loader.GetTextureAsync(filename);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldTextureManager] Warning loading {filename}: {ex.Message}");
            }

            if (texture != null)
            {
                if (cellType == CellType.Empty)
                {
                    _cachedEmptyTexture = texture;
                }

                await UniTask.SwitchToMainThread();
                AddTextureToAtlas(cellType, texture);
            }
            else
            {
                Debug.LogWarning($"[AssetDiag] TEXFAIL {filename} — texture null");
                throw new Exception($"Failed to load texture for {cellType}");
            }
        }

        private void AddTextureToAtlas(CellType cellType, Texture2D texture)
        {
            foreach (var atlas in _atlases)
            {
                if (atlas.ContainsCell(cellType))
                {
                    return;
                }
            }

            var mmForFrame = ServiceLocator.Resolve<MapManager>();
            int frameHeight = mmForFrame != null ? mmForFrame.GetAnimationFrameHeight(cellType) : texture.height;
            float containerFPS = 0;

            if (texture.name.Contains("|"))
            {
                string[] parts = texture.name.Split('|');
                foreach (string part in parts)
                {
                    if (part.StartsWith("FPS="))
                    {
                        float.TryParse(part.Substring(4), out containerFPS);
                    }
                    else if (part.StartsWith("FrameHeight="))
                    {
                        int.TryParse(part.Substring(12), out frameHeight);
                    }
                }
            }

            int effectiveFrameHeight = frameHeight > 0 ? frameHeight : texture.height;

            var textureInfo = new CellTextureInfo
            {
                CellType = cellType,
                BaseTexture = texture,
                HasVariations = texture.width > _cellTextureSize || effectiveFrameHeight > _cellTextureSize,
                VariationCount = 1,
                AnimationFrames = frameHeight > 0 ? texture.height / frameHeight : 1,
                FramesPerRow = 1,
                FrameSize = effectiveFrameHeight,
                ContainerFPS = containerFPS,
            };

            if (_currentAtlas == null)
            {
                return;
            }

            if (!_currentAtlas.TryAddTexture(cellType, texture, out var coordinate))
            {
                var newSize = Mathf.Min(_currentAtlas.Size * 2, _maxAtlasSize);
                if (newSize > _currentAtlas.Size)
                {
                    var newAtlas = new TextureAtlas(newSize, _cellTextureSize, _texturePadding);
                    _atlases.Add(newAtlas);
                    _currentAtlas = newAtlas;

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

            _currentAtlas.CopyTextureToAtlas(cellType, texture);
            _textureCache.AddTexture(cellType, textureInfo);
            TextureRevision++;
            OnTextureLoaded?.Invoke($"Cells/{(int)cellType}.png", texture);
        }

        private static CellVariation CalculateVariation(CellTextureInfo textureInfo, int globalX, int globalY)
        {
            if (!textureInfo.HasVariations)
            {
                return CellVariation.None;
            }

            int variationX = ((globalX % 2) + 2) % 2;
            int variationY = ((globalY % 2) + 2) % 2;

            return new CellVariation
            {
                Horizontal = variationX == 1,
                Vertical = variationY == 1,
            };
        }

        public List<TextureAtlas> GetAllAtlases()
        {
            EnsureInitialized();
            return _atlases;
        }

        public void FlushDirtyAtlases()
        {
            for (int i = 0; i < _atlases.Count; i++)
            {
                if (_atlases[i].IsDirty)
                {
                    _atlases[i].SyncApply();
                }
            }
        }

        public TextureAtlas? GetAtlasForCell(CellType cellType)
        {
            EnsureInitialized();
            foreach (var atlas in _atlases)
            {
                if (atlas.ContainsCell(cellType))
                {
                    return atlas;
                }
            }

            return null;
        }

        public void Clear()
        {
            EnsureInitialized();
            _textureCache.Clear();
            foreach (var atlas in _atlases)
            {
                // Clear() used to drop the atlas list without disposing the
                // GPU textures. Repeated world reloads therefore leaked every
                // previous atlas until Unity's native cleanup caught up.
                atlas.Dispose();
            }

            _atlases.Clear();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);
            _atlases.Add(_currentAtlas);
            GenerateFlowMap();
            _cachedEmptyTexture = null;
            TextureRevision++;
        }

        public Texture2D? GetCachedTexture(CellType cellType)
        {
            EnsureInitialized();
            return _textureCache?.GetCachedTexture(cellType);
        }

        public string GetCacheStats()
        {
            EnsureInitialized();
            return _textureCache != null ? _textureCache.GetCacheStats() : string.Empty;
        }

        public Texture2D? GetEmptyTexture()
        {
            EnsureInitialized();
            return _cachedEmptyTexture;
        }

        public class TextureRequest
        {
            private readonly UniTaskCompletionSource<bool> _taskSource;

            public TextureRequest(CellType cellType)
            {
                CellType = cellType;
                _taskSource = new UniTaskCompletionSource<bool>();
            }

            public CellType CellType { get; }

            public UniTask<bool> Task => _taskSource.Task;

            public void SetResult(bool success)
            {
                _taskSource.TrySetResult(success);
            }
        }
    }
}
