#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.World;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World.Terrain
{
    public class TerrainCellCache
    {
        private CachedCellData[,] _cellCache = new CachedCellData[0, 0];
        private int _cacheMinX = int.MinValue;
        private int _cacheMinY = int.MinValue;
        private int _cacheWidth;
        private int _cacheHeight;

        private readonly CellMetadata[] _metadataLookup = new CellMetadata[65536];
        private readonly bool[] _metadataReady = new bool[65536];

        private static CachedCellData UnloadedCellData => new()
        {
            State = TerrainCellState.Unloaded,
            Type = CellType.Unloaded,
            AtlasIndex = -1,
        };

        public int CacheMinX => _cacheMinX;
        public int CacheMinY => _cacheMinY;
        public int CacheWidth => _cacheWidth;
        public int CacheHeight => _cacheHeight;

        public void EnsureCapacity(int width, int height)
        {
            _cacheWidth = width + 2;
            _cacheHeight = height + 2;
            if (_cellCache == null || _cellCache.GetLength(0) != _cacheWidth || _cellCache.GetLength(1) != _cacheHeight)
            {
                _cellCache = new CachedCellData[_cacheWidth, _cacheHeight];
            }
        }

        public void ClearCaches()
        {
            Array.Clear(_metadataReady, 0, _metadataReady.Length);
        }

        public CachedCellData GetCellData(int x, int y)
        {
            if (x < 0 || x >= _cacheWidth || y < 0 || y >= _cacheHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Terrain cell cache index ({x}, {y}) is outside {_cacheWidth}x{_cacheHeight}.");
            }

            return _cellCache[x, y];
        }

        public void PopulateFull(int minX, int minY, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, List<TextureAtlas> atlases)
        {
            if (wtm == null)
            {
                throw new ArgumentNullException(nameof(wtm));
            }

            if (atlases == null)
            {
                throw new ArgumentNullException(nameof(atlases));
            }

            if (mm == null || mapStorage == null || !mapStorage.IsReady)
            {
                return;
            }

            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;
            var layer = mapStorage.CellLayer;
            if (layer == null)
            {
                return;
            }

            _cacheMinX = minX - 1;
            _cacheMinY = minY - 1;

            System.Threading.Tasks.Parallel.For(0, _cacheWidth, x =>
            {
                int gridX = _cacheMinX + x;
                int lastChunkIndex = -1;
                CellType[]? currentChunk = null;

                for (int y = 0; y < _cacheHeight; y++)
                {
                    int unityY = _cacheMinY + y;
                    CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);

                    if (type == CellType.Unloaded)
                    {
                        _cellCache[x, y] = UnloadedCellData;
                        continue;
                    }

                    var meta = GetMetadata(type, mm, wtm, atlases);
                    _cellCache[x, y] = CreateCachedData(type, meta);
                }
            });

            wtm.RequestTexture(CellType.Empty);
        }

        public void UpdateRegion(int gridMinX, int unityMinY, int width, int height, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, List<TextureAtlas> atlases)
        {
            if (wtm == null || atlases == null || mm == null || mapStorage == null || !mapStorage.IsReady)
            {
                return;
            }

            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;
            var layer = mapStorage.CellLayer;
            if (layer == null)
            {
                return;
            }

            int startX = Mathf.Clamp(gridMinX - _cacheMinX, 0, _cacheWidth);
            int endX = Mathf.Clamp(gridMinX + width - _cacheMinX, 0, _cacheWidth);
            int startY = Mathf.Clamp(unityMinY - _cacheMinY, 0, _cacheHeight);
            int endY = Mathf.Clamp(unityMinY + height - _cacheMinY, 0, _cacheHeight);

            for (int x = startX; x < endX; x++)
            {
                int gridX = _cacheMinX + x;
                int lastChunkIndex = -1;
                CellType[]? currentChunk = null;

                for (int y = startY; y < endY; y++)
                {
                    int unityY = _cacheMinY + y;
                    CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);

                    if (type == CellType.Unloaded)
                    {
                        _cellCache[x, y] = UnloadedCellData;
                        continue;
                    }

                    var meta = GetMetadata(type, mm, wtm, atlases);
                    _cellCache[x, y] = CreateCachedData(type, meta);
                }
            }
        }

        public void ScrollAndFill(int dx, int dy, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, List<TextureAtlas> atlases)
        {
            if (wtm == null)
            {
                throw new ArgumentNullException(nameof(wtm));
            }

            if (atlases == null)
            {
                throw new ArgumentNullException(nameof(atlases));
            }

            if (mm == null || mapStorage == null || !mapStorage.IsReady)
            {
                return;
            }

            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;
            var layer = mapStorage.CellLayer;
            if (layer == null)
            {
                return;
            }

            _cacheMinX += dx;
            _cacheMinY += dy;

            Scroll2DArray(_cellCache, _cacheWidth, _cacheHeight, dx, dy);

            int lastChunkIndex = -1;
            CellType[]? currentChunk = null;

            void FillCell(int cx, int cy, ref int chunkIdx, ref CellType[]? chunk)
            {
                int gridX = _cacheMinX + cx;
                int unityY = _cacheMinY + cy;

                CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref chunkIdx, ref chunk);

                if (type == CellType.Unloaded)
                {
                    _cellCache[cx, cy] = UnloadedCellData;
                    return;
                }

                var meta = GetMetadata(type, mm, wtm, atlases);
                _cellCache[cx, cy] = CreateCachedData(type, meta);
            }

            if (dx > 0)
            {
                for (int x = _cacheWidth - dx; x < _cacheWidth; x++)
                {
                    for (int y = 0; y < _cacheHeight; y++)
                    {
                        FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = 0; x < -dx; x++)
                {
                    for (int y = 0; y < _cacheHeight; y++)
                    {
                        FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                    }
                }
            }

            if (dy > 0)
            {
                for (int y = _cacheHeight - dy; y < _cacheHeight; y++)
                {
                    for (int x = 0; x < _cacheWidth; x++)
                    {
                        FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                    }
                }
            }
            else if (dy < 0)
            {
                for (int y = 0; y < -dy; y++)
                {
                    for (int x = 0; x < _cacheWidth; x++)
                    {
                        FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                    }
                }
            }

            wtm.RequestTexture(CellType.Empty);
        }

        private CellType GetCellType(int gridX, int unityY, int worldWidth, int worldHeight, WorldLayer<CellType> layer, ref int lastChunkIndex, ref CellType[]? currentChunk)
        {
            if (unityY >= worldHeight)
            {
                return CellType.Unloaded;
            }

            if (gridX < 0 || gridX >= worldWidth || unityY < 0)
            {
                // The infinite redrock shell is rendered by SurfaceRenderer's
                // boundary shader. It is not terrain data and must never be
                // converted into a server CellType: doing so asks the texture
                // cache for RedRock metadata/animation outside the world and
                // can fail when the server has not configured that cell type.
                return CellType.Unloaded;
            }

            int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);
            if (!layer.GetChunkIndexAndLocal(gridX, serverY, out int chunkIndex, out int localIndex))
            {
                return CellType.Unloaded;
            }

            if (chunkIndex != lastChunkIndex)
            {
                currentChunk = layer.GetChunk(chunkIndex, false, false);
                lastChunkIndex = chunkIndex;
            }

            return currentChunk != null ? currentChunk[localIndex] : CellType.Unloaded;
        }

        public CellMetadata GetMetadata(CellType type, MapManager mm, ITextureService wtm, List<TextureAtlas> atlases)
        {
            int idx = (int)type;
            if ((uint)idx < (uint)_metadataReady.Length && _metadataReady[idx])
            {
                return _metadataLookup[idx];
            }

            var config = mm.GetCellConfig(type);

            int atlasIndex = -1;
            for (int i = 0; i < atlases.Count; i++)
            {
                if (atlases[i].ContainsCell(type))
                {
                    atlasIndex = i;
                    break;
                }
            }

            Vector4 atlasRect = wtm.GetCellFrameRect(type);
            int frameCount = wtm.GetAnimationFrameCount(type);
            int frameSize = wtm.GetFrameSize(type);

            var meta = new CellMetadata
            {
                Properties = config.Properties,
                ReliefGroup = config.ReliefGroup,
                Distortion = config.Distortion,
                HasTileGroup = mm.TryGetTileGroup(type, out int gid),
                TileGroupId = gid,
                MinimapColor = mm.GetCellMinimapColor(type),
                Animation = config.Animation,
                AnimationSpeed = wtm.GetAnimationSpeedForCell(type),
                AtlasRect = atlasRect,
                AtlasIndex = atlasIndex,
                UVTileSize = atlasIndex >= 0 && atlasIndex < atlases.Count
                    ? (float)RenderingConstants.CELL_SIZE / atlases[atlasIndex].Size
                    : 0f,
                AnimationFrameCount = frameCount,
                FrameHeightTiles = (float)frameSize / RenderingConstants.CELL_SIZE,
                IsTextureReady = atlasRect.z > 0.0001f,
            };

            if ((uint)idx < (uint)_metadataReady.Length)
            {
                _metadataLookup[idx] = meta;
                _metadataReady[idx] = true;
            }

            if (!meta.IsTextureReady)
            {
                wtm.RequestTexture(type);
            }

            return meta;
        }

        public CachedCellData CreateCachedData(CellType type, CellMetadata meta)
        {
            return new CachedCellData
            {
                State = TerrainCellState.Loaded,
                Type = type,
                Properties = meta.Properties,
                ReliefGroup = meta.ReliefGroup,
                Distortion = meta.Distortion,
                HasTileGroup = meta.HasTileGroup,
                TileGroupId = meta.TileGroupId,
                MinimapColor = meta.MinimapColor,
                Animation = meta.Animation,
                AnimationSpeed = meta.AnimationSpeed,
                AtlasRect = meta.AtlasRect,
                AtlasIndex = meta.AtlasIndex,
                UVTileSize = meta.UVTileSize,
                AnimationFrameCount = meta.AnimationFrameCount,
                FrameHeightTiles = meta.FrameHeightTiles,
            };
        }

        public CachedCellData GetNeighborCacheEntry(
            CellType type,
            int cx,
            int cy,
            MapManager mm,
            ITextureService wtm,
            List<TextureAtlas> atlases)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (_cellCache[cx + dx, cy + dy].Type == type)
                    {
                        return _cellCache[cx + dx, cy + dy];
                    }
                }
            }

            return CreateCachedData(type, GetMetadata(type, mm, wtm, atlases));
        }

        public static void Scroll2DArray<T>(T[,] buffer, int w, int h, int dx, int dy)
        {
            if (dx > 0)
            {
                for (int x = 0; x < w - dx; x++)
                {
                    int srcX = x + dx;
                    if (dy > 0)
                    {
                        for (int y = 0; y < h - dy; y++)
                        {
                            buffer[x, y] = buffer[srcX, y + dy];
                        }
                    }
                    else if (dy < 0)
                    {
                        for (int y = h - 1; y >= -dy; y--)
                        {
                            buffer[x, y] = buffer[srcX, y + dy];
                        }
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                        {
                            buffer[x, y] = buffer[srcX, y];
                        }
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = w - 1; x >= -dx; x--)
                {
                    int srcX = x + dx;
                    if (dy > 0)
                    {
                        for (int y = 0; y < h - dy; y++)
                        {
                            buffer[x, y] = buffer[srcX, y + dy];
                        }
                    }
                    else if (dy < 0)
                    {
                        for (int y = h - 1; y >= -dy; y--)
                        {
                            buffer[x, y] = buffer[srcX, y + dy];
                        }
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                        {
                            buffer[x, y] = buffer[srcX, y];
                        }
                    }
                }
            }
            else if (dy != 0)
            {
                if (dy > 0)
                {
                    for (int x = 0; x < w; x++)
                    {
                        for (int y = 0; y < h - dy; y++)
                        {
                            buffer[x, y] = buffer[x, y + dy];
                        }
                    }
                }
                else
                {
                    for (int x = 0; x < w; x++)
                    {
                        for (int y = h - 1; y >= -dy; y--)
                        {
                            buffer[x, y] = buffer[x, y + dy];
                        }
                    }
                }
            }
        }
    }
}
