#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World
{
    public class TextureAtlas : IDisposable
    {
        public int Size { get; }
        public int CELL_SIZE { get; }
        public int Padding { get; }

        private Texture2D? _atlasTexture;

        public Texture2D? Texture => _atlasTexture;

        private Color32[]? _atlasPixels;
        private readonly ConcurrentDictionary<CellType, AtlasCell> _cells = new();
        private readonly List<Rectangle> _freeRectangles = new();
        private readonly List<Rectangle> _usedRectangles = new();
        private readonly Dictionary<CellType, Texture2D> _placeholderTextures = new();
        private readonly HashSet<CellType> _dirtyCells = new();

        private bool _isDirty = false;

        public bool IsDirty => _isDirty;

        private readonly object _lock = new object();

        public TextureAtlas(int size, int cellSize, int padding)
        {
            Size = size;
            CELL_SIZE = cellSize;
            Padding = padding;

            _atlasTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _atlasTexture.filterMode = FilterMode.Point;
            _atlasTexture.wrapMode = TextureWrapMode.Clamp;

            // The CPU fallback needs an initialized pixel store. On platforms
            // with GPU texture copies, every occupied rectangle is uploaded
            // directly and allocating a full-size zero buffer here only adds
            // a large startup memory spike (64 MiB for a 4096² atlas).
            if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
            {
                _atlasTexture.SetPixels32(new Color32[size * size]);
                _atlasTexture.Apply(false, false);
            }

            _freeRectangles.Add(new Rectangle(0, 0, size, size));
        }

        public void Dispose()
        {
            foreach (var placeholder in _placeholderTextures.Values)
            {
                if (placeholder == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(placeholder);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(placeholder);
                }
            }

            _placeholderTextures.Clear();
            _cells.Clear();
            _dirtyCells.Clear();

            if (_atlasTexture != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_atlasTexture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_atlasTexture);
                }

                _atlasTexture = null;
            }

            _atlasPixels = null;
        }

        private void EnsurePixelBuffer()
        {
            if (_atlasPixels == null)
            {
                _atlasPixels = new Color32[Size * Size];
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cells.Clear();
                _dirtyCells.Clear();
                _usedRectangles.Clear();
                _freeRectangles.Clear();
                _freeRectangles.Add(new Rectangle(0, 0, Size, Size));

                foreach (var placeholder in _placeholderTextures.Values)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(placeholder);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(placeholder);
                    }
                }

                _placeholderTextures.Clear();

                EnsurePixelBuffer();
                if (_atlasPixels != null && _atlasTexture != null)
                {
                    Array.Clear(_atlasPixels, 0, _atlasPixels.Length);
                    _atlasTexture.SetPixels32(_atlasPixels);
                    _atlasTexture.Apply();
                }

                _isDirty = false;
            }
        }

        public AtlasCoordinate GetCoordinate(CellType cellType, CellVariation variation)
        {
            if (!_cells.TryGetValue(cellType, out var cell))
            {
                return AtlasCoordinate.Empty;
            }

            return cell.BaseCoordinate;
        }

        public AtlasCoordinate GetCoordinate(CellType cellType)
        {
            return GetCoordinate(cellType, CellVariation.None);
        }

        public bool ContainsCell(CellType cellType)
        {
            return _cells.ContainsKey(cellType);
        }

        public AtlasCoordinate GetWrappedCoordinate(CellType cellType, int globalX, int globalY, CellVariation variation, int frameHeightPixels = 0, int frameIndex = 0)
        {
            if (!_cells.TryGetValue(cellType, out var cell))
            {
                return AtlasCoordinate.Empty;
            }

            int subAtlasX = cell.Rectangle.X;
            int subAtlasY = cell.Rectangle.Y;
            int subAtlasWidth = cell.Rectangle.Width;
            int subAtlasHeight = cell.Rectangle.Height;

            const int TERRAIN_TILE_SIZE = RenderingConstants.CELL_SIZE;
            int tilesPerRow = subAtlasWidth / TERRAIN_TILE_SIZE;
            int effectiveSubAtlasHeight = frameHeightPixels > 0 ? frameHeightPixels : subAtlasHeight;
            int tilesPerColumn = effectiveSubAtlasHeight / TERRAIN_TILE_SIZE;

            if (tilesPerRow <= 0)
            {
                tilesPerRow = 1;
            }

            if (tilesPerColumn <= 0)
            {
                tilesPerColumn = 1;
            }

            int wrappedX = ((globalX % tilesPerRow) + tilesPerRow) % tilesPerRow;
            int wrappedY = (tilesPerColumn - 1) - (((globalY % tilesPerColumn) + tilesPerColumn) % tilesPerColumn);

            int atlasX = subAtlasX + (wrappedX * TERRAIN_TILE_SIZE);
            int atlasY = subAtlasY + (wrappedY * TERRAIN_TILE_SIZE) + (frameIndex * (frameHeightPixels > 0 ? frameHeightPixels : 0));

            return new AtlasCoordinate(
                atlasX,
                atlasY,
                TERRAIN_TILE_SIZE,
                TERRAIN_TILE_SIZE,
                Size,
                Size);
        }

        public AtlasCoordinate GetWrappedCoordinate(CellType cellType, int globalX, int globalY)
        {
            return GetWrappedCoordinate(cellType, globalX, globalY, CellVariation.None);
        }

        public bool TryAddTexture(CellType cellType, Texture2D texture, out AtlasCoordinate coordinate)
        {
            coordinate = AtlasCoordinate.Empty;

            lock (_lock)
            {
                var bestFit = FindBestFit(texture.width, texture.height);
                if (bestFit == null)
                {
                    return false;
                }

                var atlasCell = new AtlasCell
                {
                    CellType = cellType,
                    Rectangle = bestFit.Value,
                    BaseCoordinate = new AtlasCoordinate(
                        bestFit.Value.X,
                        bestFit.Value.Y,
                        texture.width,
                        texture.height,
                        Size,
                        Size),
                };

                Rectangle rectWithPadding = new Rectangle(bestFit.Value.X, bestFit.Value.Y, bestFit.Value.Width + Padding, bestFit.Value.Height + Padding);
                _usedRectangles.Add(rectWithPadding);
                SplitFreeRectangles(rectWithPadding);
                _cells.TryAdd(cellType, atlasCell);
                _dirtyCells.Add(cellType);
                _isDirty = true;

                coordinate = atlasCell.BaseCoordinate;
                return true;
            }
        }

        public void CopyTextureToAtlas(CellType cellType, Texture2D texture)
        {
            if (!_cells.TryGetValue(cellType, out var cell))
            {
                Debug.LogError($"[TextureAtlas] Cell type {cellType} not found in atlas. Call TryAddTexture first.");
                return;
            }

            var rect = cell.Rectangle;
            if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
            {
                EnsurePixelBuffer();
                var sourcePixels = texture.GetPixels32();
                CopyPixelsToAtlasArray(sourcePixels, texture.width, texture.height, rect);
            }
            lock (_lock)
            {
                _dirtyCells.Add(cellType);
                _isDirty = true;
            }
        }

        public void SyncApply()
        {
            if (!_isDirty || _atlasTexture == null)
            {
                return;
            }

            List<(CellType type, Texture2D texture, Rectangle rect)> dirtyTextures = new();
            lock (_lock)
            {
                foreach (CellType type in _dirtyCells)
                {
                    if (_cells.TryGetValue(type, out AtlasCell cell))
                    {
                        Texture2D? texture = GetBaseTexture(type);
                        if (texture != null)
                        {
                            dirtyTextures.Add((type, texture, cell.Rectangle));
                        }
                    }
                }
            }

            bool uploadedDirectly = dirtyTextures.Count > 0 &&
                SystemInfo.copyTextureSupport != CopyTextureSupport.None;
            if (uploadedDirectly)
            {
                try
                {
                    foreach (var (_, texture, rect) in dirtyTextures)
                    {
                        Graphics.CopyTexture(
                            texture, 0, 0, 0, 0, texture.width, texture.height,
                            _atlasTexture, 0, 0, rect.X, rect.Y);
                    }
                }
                catch (Exception)
                {
                    // Some platforms reject CopyTexture for a format pair even
                    // when the device advertises support. The CPU buffer is
                    // kept as a safe compatibility fallback.
                    uploadedDirectly = false;
                }
            }

            if (!uploadedDirectly)
            {
                if (_atlasPixels == null)
                {
                    return;
                }

                _atlasTexture.SetPixels32(_atlasPixels);
                _atlasTexture.Apply(false, false);
            }

            lock (_lock)
            {
                foreach (var (type, _, _) in dirtyTextures)
                {
                    _dirtyCells.Remove(type);
                }

                _isDirty = _dirtyCells.Count > 0;
            }
        }

        public async UniTask<Texture2D?> GetAtlasTexture()
        {
            if (_isDirty)
            {
                await UpdateAtlasTexture();
            }

            return _atlasTexture;
        }

        public async UniTask UpdateAtlasTexture()
        {
            await UniTask.SwitchToMainThread();

            if (!_isDirty)
            {
                return;
            }

            List<(Texture2D texture, Rectangle rect)> texturesToCopy;

            lock (_lock)
            {
                if (!_isDirty)
                {
                    return;
                }

                texturesToCopy = new List<(Texture2D texture, Rectangle rect)>();

                foreach (var cell in _cells.Values)
                {
                    var baseTexture = GetBaseTexture(cell.CellType);
                    if (baseTexture != null)
                    {
                        texturesToCopy.Add((baseTexture, cell.Rectangle));
                    }
                }
            }

            await CopyTexturesToAtlas(texturesToCopy);

            lock (_lock)
            {
                _dirtyCells.Clear();
                _isDirty = false;
            }
        }

        private async UniTask CopyTexturesToAtlas(List<(Texture2D texture, Rectangle rect)> textures)
        {
            if (SystemInfo.copyTextureSupport != CopyTextureSupport.None)
            {
                await UniTask.SwitchToMainThread();
                if (_atlasTexture != null)
                {
                    foreach (var (texture, rect) in textures)
                    {
                        Graphics.CopyTexture(
                            texture, 0, 0, 0, 0, texture.width, texture.height,
                            _atlasTexture, 0, 0, rect.X, rect.Y);
                    }
                }

                return;
            }

            const int BATCH_SIZE = 10;

            EnsurePixelBuffer();

            for (int i = 0; i < textures.Count; i += BATCH_SIZE)
            {
                int batchEnd = Math.Min(i + BATCH_SIZE, textures.Count);
                var pixelDataList = new List<(Color32[] pixels, int width, int height, Rectangle rect)>(batchEnd - i);

                for (int textureIndex = i; textureIndex < batchEnd; textureIndex++)
                {
                    var (tex, rect) = textures[textureIndex];
                    if (tex != null)
                    {
                        pixelDataList.Add((tex.GetPixels32(), tex.width, tex.height, rect));
                    }
                }

                await UniTask.SwitchToThreadPool();

                foreach (var data in pixelDataList)
                {
                    CopyPixelsToAtlasArray(data.pixels, data.width, data.height, data.rect);
                }

                await UniTask.SwitchToMainThread();
            }

            if (_atlasTexture != null && _atlasPixels != null)
            {
                _atlasTexture.SetPixels32(_atlasPixels);
                _atlasTexture.Apply();
            }
        }

        private void CopyPixelsToAtlasArray(Color32[] sourcePixels, int width, int height, Rectangle destination)
        {
            if (_atlasPixels == null)
            {
                return;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sourceIndex = (y * width) + x;
                    int destX = destination.X + x;
                    int destY = destination.Y + y;
                    int destIndex = (destY * Size) + destX;

                    if (destIndex >= 0 && destIndex < _atlasPixels.Length && sourceIndex < sourcePixels.Length)
                    {
                        _atlasPixels[destIndex] = sourcePixels[sourceIndex];
                    }
                }
            }
        }

        private Texture2D GetBaseTexture(CellType cellType)
        {
            var textureService = ServiceLocator.Resolve<ITextureService>();
            if (textureService is WorldTextureManager manager)
            {
                var cachedTexture = manager.GetCachedTexture(cellType);
                if (cachedTexture != null)
                {
                    return cachedTexture;
                }
            }

            return CreatePlaceholderTexture(cellType);
        }

        private Texture2D CreatePlaceholderTexture(CellType cellType)
        {
            if (_placeholderTextures.TryGetValue(cellType, out var cached))
            {
                return cached;
            }

            var texture = new Texture2D(CELL_SIZE, CELL_SIZE);
            var color = GetCellColor(cellType);
            var pixels = new Color[CELL_SIZE * CELL_SIZE];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, SystemInfo.copyTextureSupport != CopyTextureSupport.None);
            _placeholderTextures[cellType] = texture;
            return texture;
        }

        private static Color GetCellColor(CellType cellType)
        {
            var mapManager = ServiceLocator.Resolve<MapManager>();
            if (mapManager != null)
            {
                var serverColor = mapManager.GetCellMinimapColor(cellType);
                if (serverColor.a > 0)
                {
                    return serverColor;
                }
            }

            return cellType switch
            {
                CellType.Empty => new Color(0.2f, 0.2f, 0.2f),
                CellType.Road => new Color(0.8f, 0.8f, 0.8f),
                CellType.Boulder1 => Color.black,
                CellType.WhiteSand => new Color(1f, 0.92f, 0.8f),
                CellType.GrayAcid => new Color(0f, 1f, 0f),
                _ => Color.magenta,
            };
        }

        private Rectangle? FindBestFit(int width, int height)
        {
            Rectangle? bestFit = null;
            int bestScore = int.MaxValue;
            foreach (var freeRect in _freeRectangles)
            {
                if (freeRect.Width >= width + Padding && freeRect.Height >= height + Padding)
                {
                    int score = (freeRect.Width - width) * (freeRect.Height - height);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestFit = new Rectangle(freeRect.X, freeRect.Y, width, height);
                    }
                }
            }

            return bestFit;
        }

        private void SplitFreeRectangles(Rectangle usedRect)
        {
            var newFree = new List<Rectangle>();
            foreach (var free in _freeRectangles)
            {
                if (Intersects(free, usedRect))
                {
                    SplitRectangle(free, usedRect, newFree);
                }
                else
                {
                    newFree.Add(free);
                }
            }

            _freeRectangles.Clear();
            _freeRectangles.AddRange(newFree);
        }

        private static void SplitRectangle(Rectangle free, Rectangle used, List<Rectangle> newFree)
        {
            if (used.Y > free.Y)
            {
                newFree.Add(new Rectangle(free.X, free.Y, free.Width, used.Y - free.Y));
            }

            if (used.Y + used.Height < free.Y + free.Height)
            {
                newFree.Add(new Rectangle(free.X, used.Y + used.Height, free.Width, (free.Y + free.Height) - (used.Y + used.Height)));
            }

            if (used.X > free.X)
            {
                newFree.Add(new Rectangle(free.X, free.Y, used.X - free.X, free.Height));
            }

            if (used.X + used.Width < free.X + free.Width)
            {
                newFree.Add(new Rectangle(used.X + used.Width, free.Y, (free.X + free.Width) - (used.X + used.Width), free.Height));
            }
        }

        private static bool Intersects(Rectangle a, Rectangle b)
        {
            return a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
        }
    }
}
