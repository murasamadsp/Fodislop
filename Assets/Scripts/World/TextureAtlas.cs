using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Manages a single texture atlas for world cell textures.
    /// Uses a 2D bin packing algorithm to efficiently place textures.
    /// </summary>
    public class TextureAtlas
    {
        public int Size { get; }
        public int CellSize { get; }
        public int Padding { get; }
        
        private Texture2D _atlasTexture;
        private Color32[] _atlasPixels;
        private readonly ConcurrentDictionary<CellType, AtlasCell> _cells = new();
        private readonly List<Rectangle> _freeRectangles = new();
        private readonly List<Rectangle> _usedRectangles = new();
        
        private bool _isDirty = false;
        private readonly object _lock = new object();

        public TextureAtlas(int size, int cellSize, int padding)
        {
            Size = size;
            CellSize = cellSize;
            Padding = padding;
            
            _atlasTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _atlasTexture.filterMode = FilterMode.Bilinear;
            _atlasTexture.wrapMode = TextureWrapMode.Clamp;
            _atlasPixels = new Color32[size * size];
            
            // Initialize with transparent pixels
            for (int i = 0; i < _atlasPixels.Length; i++)
            {
                _atlasPixels[i] = new Color32(0, 0, 0, 0);
            }
            
            _atlasTexture.SetPixels32(_atlasPixels);
            _atlasTexture.Apply();
            
            // Start with one large free rectangle
            _freeRectangles.Add(new Rectangle(0, 0, size, size));
        }

        /// <summary>
        /// Try to add a texture to the atlas
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <param name="texture">The texture to add</param>
        /// <param name="coordinate">Output coordinate if successful</param>
        /// <returns>True if successfully added, false if no space available</returns>
        public bool TryAddTexture(CellType cellType, Texture2D texture, out AtlasCoordinate coordinate)
        {
            coordinate = AtlasCoordinate.Empty;
            
            lock (_lock)
            {
                // Find the best fit rectangle
                var bestFit = FindBestFit(texture.width, texture.height);
                if (bestFit == null)
                {
                    return false;
                }

                // Create atlas cell
                var atlasCell = new AtlasCell
                {
                    CellType = cellType,
                    Rectangle = bestFit.Value,
                    BaseCoordinate = new AtlasCoordinate(
                        bestFit.Value.X, bestFit.Value.Y, 
                        texture.width, texture.height, 
                        Size, Size)
                };

                // Add to used rectangles and remove from free
                _usedRectangles.Add(bestFit.Value);
                SplitFreeRectangles(bestFit.Value);
                
                // Store the cell
                _cells.TryAdd(cellType, atlasCell);
                
                // Mark as dirty for texture update
                _isDirty = true;
                
                coordinate = atlasCell.BaseCoordinate;
                return true;
            }
        }

        /// <summary>
        /// Get the atlas texture (updates if dirty)
        /// </summary>
        public async UniTask<Texture2D> GetAtlasTexture()
        {
            if (_isDirty)
            {
                await UpdateAtlasTexture();
            }
            return _atlasTexture;
        }

        /// <summary>
        /// Update the atlas texture with all current cells
        /// </summary>
        public async UniTask UpdateAtlasTexture()
        {
            if (!_isDirty) return;

            // Get all textures to copy
            List<(Texture2D texture, Rectangle rect)> texturesToCopy;
            
            lock (_lock)
            {
                if (!_isDirty) return; // Double-check after acquiring lock

                texturesToCopy = new List<(Texture2D texture, Rectangle rect)>();
                
                foreach (var cell in _cells.Values)
                {
                    // For now, just use the base texture
                    // In the future, this could handle variations and animations
                    var baseTexture = GetBaseTexture(cell.CellType);
                    if (baseTexture != null)
                    {
                        texturesToCopy.Add((baseTexture, cell.Rectangle));
                    }
                }
            }

            // Copy textures to atlas in batches for performance
            await CopyTexturesToAtlas(texturesToCopy);
            
            lock (_lock)
            {
                _isDirty = false;
            }
        }

        /// <summary>
        /// Get coordinate for a specific cell type and variation
        /// </summary>
        public AtlasCoordinate GetCoordinate(CellType cellType, CellVariation variation)
        {
            if (!_cells.TryGetValue(cellType, out var cell))
            {
                return AtlasCoordinate.Empty;
            }

            // For now, return the base coordinate
            // In the future, this would calculate variation offsets
            return cell.BaseCoordinate;
        }

        /// <summary>
        /// Get coordinate for a specific cell type (no variation)
        /// </summary>
        public AtlasCoordinate GetCoordinate(CellType cellType)
        {
            return GetCoordinate(cellType, CellVariation.None);
        }

        /// <summary>
        /// Check if a cell type is in this atlas
        /// </summary>
        public bool ContainsCell(CellType cellType)
        {
            return _cells.ContainsKey(cellType);
        }

        /// <summary>
        /// Clear all textures from the atlas
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _cells.Clear();
                _usedRectangles.Clear();
                _freeRectangles.Clear();
                _freeRectangles.Add(new Rectangle(0, 0, Size, Size));
                
                // Clear atlas texture
                for (int i = 0; i < _atlasPixels.Length; i++)
                {
                    _atlasPixels[i] = new Color32(0, 0, 0, 0);
                }
                _atlasTexture.SetPixels32(_atlasPixels);
                _atlasTexture.Apply();
                
                _isDirty = false;
            }
        }

        /// <summary>
        /// Find the best fitting rectangle for a texture using the MaxRects algorithm
        /// </summary>
        private Rectangle? FindBestFit(int width, int height)
        {
            Rectangle? bestFit = null;
            int bestScore = int.MaxValue;

            foreach (var freeRect in _freeRectangles)
            {
                // Check if texture fits
                if (freeRect.Width >= width + Padding && freeRect.Height >= height + Padding)
                {
                    // Calculate score (smaller remaining area is better)
                    int remainingWidth = freeRect.Width - width;
                    int remainingHeight = freeRect.Height - height;
                    int score = remainingWidth * remainingHeight;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestFit = new Rectangle(freeRect.X, freeRect.Y, width, height);
                    }
                }
            }

            return bestFit;
        }

        /// <summary>
        /// Split free rectangles when a new rectangle is placed
        /// </summary>
        private void SplitFreeRectangles(Rectangle usedRect)
        {
            var newFreeRectangles = new List<Rectangle>();
            
            foreach (var freeRect in _freeRectangles)
            {
                if (Intersects(freeRect, usedRect))
                {
                    // Split the free rectangle
                    SplitRectangle(freeRect, usedRect, newFreeRectangles);
                }
                else
                {
                    newFreeRectangles.Add(freeRect);
                }
            }
            
            _freeRectangles.Clear();
            _freeRectangles.AddRange(newFreeRectangles);
        }

        /// <summary>
        /// Split a rectangle around an used rectangle
        /// </summary>
        private void SplitRectangle(Rectangle freeRect, Rectangle usedRect, List<Rectangle> newFreeRectangles)
        {
            // Create rectangles for the remaining space
            // Top rectangle
            if (usedRect.Y > freeRect.Y)
            {
                newFreeRectangles.Add(new Rectangle(
                    freeRect.X, freeRect.Y,
                    freeRect.Width, usedRect.Y - freeRect.Y));
            }
            
            // Bottom rectangle
            if (usedRect.Y + usedRect.Height < freeRect.Y + freeRect.Height)
            {
                newFreeRectangles.Add(new Rectangle(
                    freeRect.X, usedRect.Y + usedRect.Height,
                    freeRect.Width, (freeRect.Y + freeRect.Height) - (usedRect.Y + usedRect.Height)));
            }
            
            // Left rectangle
            if (usedRect.X > freeRect.X)
            {
                newFreeRectangles.Add(new Rectangle(
                    freeRect.X, freeRect.Y,
                    usedRect.X - freeRect.X, freeRect.Height));
            }
            
            // Right rectangle
            if (usedRect.X + usedRect.Width < freeRect.X + freeRect.Width)
            {
                newFreeRectangles.Add(new Rectangle(
                    usedRect.X + usedRect.Width, freeRect.Y,
                    (freeRect.X + freeRect.Width) - (usedRect.X + usedRect.Width), freeRect.Height));
            }
        }

        /// <summary>
        /// Check if two rectangles intersect
        /// </summary>
        private bool Intersects(Rectangle a, Rectangle b)
        {
            return a.X < b.X + b.Width &&
                   a.X + a.Width > b.X &&
                   a.Y < b.Y + b.Height &&
                   a.Y + a.Height > b.Y;
        }

        /// <summary>
        /// Copy textures to the atlas in batches
        /// </summary>
        private async UniTask CopyTexturesToAtlas(List<(Texture2D texture, Rectangle rect)> textures)
        {
            // Process textures in batches to avoid blocking the main thread
            const int batchSize = 4;
            for (int i = 0; i < textures.Count; i += batchSize)
            {
                var batch = textures.Skip(i).Take(batchSize).ToList();
                
                await UniTask.SwitchToThreadPool();
                
                foreach (var (texture, rect) in batch)
                {
                    CopyTextureToAtlas(texture, rect);
                }
                
                await UniTask.SwitchToMainThread();
            }
            
            _atlasTexture.SetPixels32(_atlasPixels);
            _atlasTexture.Apply();
        }

        /// <summary>
        /// Copy a single texture to the atlas
        /// </summary>
        private void CopyTextureToAtlas(Texture2D source, Rectangle destination)
        {
            var sourcePixels = source.GetPixels32();
            
            for (int y = 0; y < source.height; y++)
            {
                for (int x = 0; x < source.width; x++)
                {
                    int sourceIndex = y * source.width + x;
                    int destX = destination.X + x;
                    int destY = destination.Y + y;
                    int destIndex = destY * Size + destX;
                    
                    if (destIndex >= 0 && destIndex < _atlasPixels.Length)
                    {
                        _atlasPixels[destIndex] = sourcePixels[sourceIndex];
                    }
                }
            }
        }

        /// <summary>
        /// Get the base texture for a cell type (placeholder for now)
        /// </summary>
        private Texture2D GetBaseTexture(CellType cellType)
        {
            // This would normally get the texture from the cache
            // For now, return a placeholder
            return CreatePlaceholderTexture(cellType);
        }

        /// <summary>
        /// Create a placeholder texture for testing
        /// </summary>
        private Texture2D CreatePlaceholderTexture(CellType cellType)
        {
            var texture = new Texture2D(CellSize, CellSize);
            var color = GetCellColor(cellType);
            
            for (int i = 0; i < CellSize * CellSize; i++)
            {
                texture.SetPixel(i % CellSize, i / CellSize, color);
            }
            
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Get a color based on cell type for placeholder textures
        /// Uses server-provided colors when available, falls back to hardcoded colors
        /// </summary>
        private Color GetCellColor(CellType cellType)
        {
            // Try to get color from server configuration first
            if (MapManager.Instance != null)
            {
                var serverColor = MapManager.Instance.GetCellMinimapColor(cellType);
                if (serverColor.a > 0) // Check if a valid color was returned
                {
                    return serverColor;
                }
            }

            // Fallback to hardcoded colors if server data is not available
            return cellType switch
            {
                CellType.Empty => Color.gray,
                CellType.Road => new Color(0.8f, 0.8f, 0.8f),
                CellType.Boulder1 or CellType.Boulder2 or CellType.Boulder3 => Color.black,
                CellType.WhiteSand or CellType.DarkWhiteSand => Color.yellow,
                CellType.GrayAcid or CellType.PurpleAcid => Color.green,
                _ => Color.magenta // Default color
            };
        }
    }

    /// <summary>
    /// Represents a rectangle in 2D space
    /// </summary>
    public struct Rectangle
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Represents a cell in the atlas
    /// </summary>
    internal struct AtlasCell
    {
        public CellType CellType { get; set; }
        public Rectangle Rectangle { get; set; }
        public AtlasCoordinate BaseCoordinate { get; set; }
    }
}