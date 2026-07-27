using System;
using System.Collections.Generic;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.World;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.World.Terrain
{
    public class TerrainCellCache
    {
        private CachedCellData[,] _cellCache;
        private int _cacheMinX = int.MinValue;
        private int _cacheMinY = int.MinValue;
        private int _cacheWidth;
        private int _cacheHeight;

        private readonly Dictionary<CellType, int> _atlasIndexCache = new();
        private readonly Dictionary<CellType, CellMetadata> _metadataCache = new();

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
            _atlasIndexCache.Clear();
            _metadataCache.Clear();
        }

        public CachedCellData GetCellData(int x, int y)
        {
            if (x < 0 || x >= _cacheWidth || y < 0 || y >= _cacheHeight)
            {
                return default;
            }

            return _cellCache[x, y];
        }

        public void PopulateFull(int minX, int minY, IWorldDataStorage mapStorage, MapManager mm, WorldTextureManager wtm, List<TextureAtlas> atlases)
        {
            if (mm == null || mapStorage == null || !mapStorage.IsReady)
            {
                return;
            }

            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;
            var layer = mapStorage.CellLayer;
            if (layer == null || atlases == null)
            {
                return;
            }

            _cacheMinX = minX - 1;
            _cacheMinY = minY - 1;

            for (int x = 0; x < _cacheWidth; x++)
            {
                int gridX = _cacheMinX + x;
                int lastChunkIndex = -1;
                CellType[] currentChunk = null;

                for (int y = 0; y < _cacheHeight; y++)
                {
                    int unityY = _cacheMinY + y;
                    CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);

                    var meta = GetMetadata(type, mm, wtm, atlases);
                    _cellCache[x, y] = CreateCachedData(type, meta);

                    if (Application.isPlaying && type != CellType.Unloaded && !meta.IsTextureReady)
                    {
                        wtm?.RequestTexture(type);
                    }
                }
            }

            wtm?.RequestTexture((CellType)0);
        }

        public void ScrollAndFill(int dx, int dy, IWorldDataStorage mapStorage, MapManager mm, WorldTextureManager wtm, List<TextureAtlas> atlases)
        {
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

            void FillCell(int cx, int cy)
            {
                int gridX = _cacheMinX + cx;
                int unityY = _cacheMinY + cy;
                int lastChunkIndex = -1;
                CellType[] currentChunk = null;

                CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);
                var meta = GetMetadata(type, mm, wtm, atlases);
                _cellCache[cx, cy] = CreateCachedData(type, meta);

                if (Application.isPlaying && type != CellType.Unloaded && !meta.IsTextureReady)
                {
                    wtm?.RequestTexture(type);
                }
            }

            if (dx > 0)
            {
                for (int x = _cacheWidth - dx; x < _cacheWidth; x++)
                {
                    for (int y = 0; y < _cacheHeight; y++)
                    {
                        FillCell(x, y);
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = 0; x < -dx; x++)
                {
                    for (int y = 0; y < _cacheHeight; y++)
                    {
                        FillCell(x, y);
                    }
                }
            }

            if (dy > 0)
            {
                for (int y = _cacheHeight - dy; y < _cacheHeight; y++)
                {
                    for (int x = 0; x < _cacheWidth; x++)
                    {
                        FillCell(x, y);
                    }
                }
            }
            else if (dy < 0)
            {
                for (int y = 0; y < -dy; y++)
                {
                    for (int x = 0; x < _cacheWidth; x++)
                    {
                        FillCell(x, y);
                    }
                }
            }

            wtm?.RequestTexture((CellType)0);
        }

        private CellType GetCellType(int gridX, int unityY, int worldWidth, int worldHeight, WorldLayer<CellType> layer, ref int lastChunkIndex, ref CellType[] currentChunk)
        {
            if (gridX < 0 || gridX >= worldWidth || unityY < 0 || unityY >= worldHeight)
            {
                return (gridX < 0 || gridX >= worldWidth || unityY < 0) ? (CellType)0 : CellType.Unloaded;
            }

            int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);
            if (!layer.GetChunkIndexAndLocal(gridX, serverY, out int chunkIndex, out int localIndex))
            {
                return CellType.Unloaded;
            }

            if (chunkIndex != lastChunkIndex)
            {
                currentChunk = layer.GetChunk(chunkIndex, false, true);
                lastChunkIndex = chunkIndex;
            }

            return currentChunk != null ? currentChunk[localIndex] : CellType.Unloaded;
        }

        public CellMetadata GetMetadata(CellType type, MapManager mm, WorldTextureManager wtm, List<TextureAtlas> atlases)
        {
            if (_metadataCache.TryGetValue(type, out var meta))
            {
                return meta;
            }

            if (mm == null || wtm == null)
            {
                return default;
            }

            var config = mm.GetCellConfig(type);

            if (!_atlasIndexCache.TryGetValue(type, out int atlasIndex))
            {
                for (int i = 0; i < atlases.Count; i++)
                {
                    if (atlases[i].ContainsCell(type))
                    {
                        atlasIndex = i;
                        _atlasIndexCache[type] = i;
                        break;
                    }
                }
            }

            Vector4 atlasRect = wtm.GetCellFrameRect(type);
            int frameCount = wtm.GetAnimationFrameCount(type);
            int frameSize = wtm.GetFrameSize(type);

            meta = new CellMetadata
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
                UVTileSize = (atlases.Count > atlasIndex) ? (float)RenderingConstants.CELL_SIZE / atlases[atlasIndex].Size : 0,
                AnimationFrameCount = frameCount,
                FrameHeightTiles = (float)frameSize / RenderingConstants.CELL_SIZE,
                IsTextureReady = atlasRect.z > 0.0001f,
            };
            _metadataCache[type] = meta;
            return meta;
        }

        private CachedCellData CreateCachedData(CellType type, CellMetadata meta)
        {
            return new CachedCellData
            {
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

        public ref CachedCellData GetNeighborCacheEntryRef(CellType type, int cx, int cy, MapManager mm, WorldTextureManager wtm, List<TextureAtlas> atlases, ref CachedCellData fallback)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (_cellCache[cx + dx, cy + dy].Type == type)
                    {
                        return ref _cellCache[cx + dx, cy + dy];
                    }
                }
            }

            var meta = GetMetadata(type, mm, wtm, atlases);
            fallback = CreateCachedData(type, meta);
            return ref fallback;
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
