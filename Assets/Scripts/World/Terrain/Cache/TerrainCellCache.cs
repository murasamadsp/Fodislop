#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
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

        // IsPopulated flag lives inside CellMetadata itself — one array instead of two
        private readonly CellMetadata[] _metadataLookup = new CellMetadata[65536];

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

        /// <summary>
        /// Returns true if any loaded terrain cell in the active viewport cache is still waiting for its atlas texture.
        /// </summary>
        public bool HasMissingTextures
        {
            get
            {
                if (_cellCache == null)
                {
                    return false;
                }

                int w = _cacheWidth;
                int h = _cacheHeight;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        var cell = _cellCache[x, y];
                        if (cell.State == TerrainCellState.Loaded &&
                            cell.Type != CellType.Empty &&
                            cell.Type != CellType.Unloaded &&
                            !cell.IsTextureReady)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

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
            // Array.Clear zeros all bytes → IsPopulated = false for every entry (bool default = false).
            // Faster than a manual loop: runtime uses SIMD memset internally.
            Array.Clear(_metadataLookup, 0, _metadataLookup.Length);
        }

        public void RefreshTextureMetadata(
            HashSet<CellType> cellTypes,
            MapManager mapManager,
            ITextureService textureService,
            IReadOnlyList<IAtlasDescriptor> atlases)
        {
            foreach (CellType cellType in cellTypes)
            {
                int index = (int)cellType;
                if ((uint)index < (uint)_metadataLookup.Length)
                {
                    _metadataLookup[index].IsPopulated = false;
                }
            }

            for (int x = 0; x < _cacheWidth; x++)
            {
                for (int y = 0; y < _cacheHeight; y++)
                {
                    CellType cellType = _cellCache[x, y].Type;
                    if (!cellTypes.Contains(cellType))
                    {
                        continue;
                    }

                    _cellCache[x, y] = CreateCachedData(
                        cellType,
                        GetMetadata(cellType, mapManager, textureService, atlases));
                }
            }
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

        public void PopulateFull(int minX, int minY, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
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

            for (int x = 0; x < _cacheWidth; x++)
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
            }

            wtm.RequestTexture(CellType.Empty);
        }

        public void UpdateRegion(int gridMinX, int unityMinY, int width, int height, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
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

        public void ScrollAndFill(int dx, int dy, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
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

        private CellType GetCellType(int gridX, int unityY, int worldWidth, int worldHeight, IWorldLayer<CellType> layer, ref int lastChunkIndex, ref CellType[]? currentChunk)
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
                ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLru: false);
                currentChunk = result.Status == ChunkReadStatus.Available
                    ? result.Data
                    : null;
                lastChunkIndex = chunkIndex;
            }

            return currentChunk != null ? currentChunk[localIndex] : CellType.Unloaded;
        }

        public CellMetadata GetMetadata(CellType type, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
        {
            int idx = (int)type;
            if ((uint)idx < (uint)_metadataLookup.Length && _metadataLookup[idx].IsPopulated)
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
                MinimapColor = (Color32)mm.GetCellMinimapColor(type),
                Animation = config.Animation,
                AnimationSpeed = wtm.GetAnimationSpeedForCell(type),
                AtlasRect = atlasRect,
                AtlasIndex = atlasIndex,
                UVTileSize = atlasIndex >= 0 && atlasIndex < atlases.Count
                    ? (float)RenderingConstants.CELL_SIZE / atlases[atlasIndex].Size
                    : 0f,
                AnimationFrameCount = frameCount,
                FrameHeightTiles = (float)frameSize / RenderingConstants.CELL_SIZE,
                IsTextureReady = atlasIndex >= 0 && atlasRect.z > 0f,
                IsPopulated = true,
            };

            // The metadata is always fully populated (IsPopulated = true) once built here.
            // Only the fast _metadataLookup cache entry is skipped while the atlas texture is
            // not yet ready, so callers fall through to RequestTexture instead of caching an
            // unready rect. The per-cell IsTextureReady flag (read by HasMissingTextures) is
            // what actually gates drawing of not-yet-loaded cells.
            if (meta.IsTextureReady && (uint)idx < (uint)_metadataLookup.Length)
            {
                _metadataLookup[idx] = meta;
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
                MinimapColor = meta.MinimapColor, // Color32 = Color32, no conversion
                Animation = meta.Animation,
                AnimationSpeed = meta.AnimationSpeed,
                AtlasRect = meta.AtlasRect,
                AtlasIndex = meta.AtlasIndex,
                UVTileSize = meta.UVTileSize,
                AnimationFrameCount = meta.AnimationFrameCount,
                FrameHeightTiles = meta.FrameHeightTiles,
                IsTextureReady = meta.IsTextureReady,
            };
        }

        public CachedCellData GetNeighborCacheEntry(
            CellType type,
            int cx,
            int cy,
            MapManager mm,
            ITextureService wtm,
            IReadOnlyList<IAtlasDescriptor> atlases)
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
            TerrainCacheArrayScroller.Scroll(buffer, w, h, dx, dy);
        }
    }
}
