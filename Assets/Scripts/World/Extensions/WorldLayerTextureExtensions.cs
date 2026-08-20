#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World.Extensions
{
    /// <summary>
    /// Extension methods for WorldLayer to integrate with WorldTextureManager.
    /// </summary>
    public static class WorldLayerTextureExtensions
    {
        /// <summary>
        /// Get texture coordinates for a world cell at the specified position.
        /// </summary>
        /// <param name="worldLayer">The world layer.</param>
        /// <param name="x">X coordinate in world space.</param>
        /// <param name="y">Y coordinate in world space.</param>
        /// <returns>Atlas coordinates for the cell texture.</returns>
        public static async UniTask<AtlasCoordinate> GetCellTextureCoordinate(this WorldLayer<CellType> worldLayer, int x, int y)
        {
            var cellType = worldLayer[x, y];
            ITextureService manager = SessionAccess.Resolve()?.TryResolve<ITextureService>() ??
                throw new InvalidOperationException(
                    "World texture coordinates require a registered ITextureService.");
            return await manager.GetCellTextureCoordinate(cellType, x, y);
        }

        /// <summary>
        /// Get texture coordinates for a rectangular region of the world.
        /// </summary>
        /// <param name="worldLayer">The world layer.</param>
        /// <param name="x">Starting X coordinate.</param>
        /// <param name="y">Starting Y coordinate.</param>
        /// <param name="width">Region width.</param>
        /// <param name="height">Region height.</param>
        /// <returns>Dictionary mapping positions to atlas coordinates.</returns>
        public static async UniTask<Dictionary<Vector2Int, AtlasCoordinate>> GetRegionTextureCoordinates(
            this WorldLayer<CellType> worldLayer,
            int x, int y, int width, int height)
        {
            var coordinates = new Dictionary<Vector2Int, AtlasCoordinate>();
            var tasks = new List<UniTask<AtlasCoordinate>>();

            // Collect all texture requests
            ITextureService manager = SessionAccess.Resolve()?.TryResolve<ITextureService>() ??
                throw new InvalidOperationException(
                    "World texture coordinates require a registered ITextureService.");

            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                {
                    var cellType = worldLayer[xx, yy];
                    if (cellType != CellType.Unloaded && cellType != CellType.Pregener)
                    {
                        var task = manager.GetCellTextureCoordinate(cellType, xx, yy);
                        tasks.Add(task);
                        coordinates[new Vector2Int(xx, yy)] = AtlasCoordinate.Empty;
                    }
                }
            }

            // Wait for all texture requests to complete
            if (tasks.Count > 0)
            {
                var results = await UniTask.WhenAll(tasks);
                int index = 0;

                for (int yy = y; yy < y + height; yy++)
                {
                    for (int xx = x; xx < x + width; xx++)
                    {
                        var cellType = worldLayer[xx, yy];
                        if (cellType != CellType.Unloaded && cellType != CellType.Pregener)
                        {
                            coordinates[new Vector2Int(xx, yy)] = results[index++];
                        }
                    }
                }
            }

            return coordinates;
        }

        /// <summary>
        /// Preload textures for a region to improve performance.
        /// </summary>
        /// <param name="worldLayer">The world layer.</param>
        /// <param name="x">Starting X coordinate.</param>
        /// <param name="y">Starting Y coordinate.</param>
        /// <param name="width">Region width.</param>
        /// <param name="height">Region height.</param>
        /// <returns>Task representing the preload operation.</returns>
        public static async UniTask PreloadRegionTextures(
            this WorldLayer<CellType> worldLayer,
            int x, int y, int width, int height)
        {
            var uniqueCellTypes = new HashSet<CellType>();

            // Collect unique cell types in the region
            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                {
                    var cellType = worldLayer[xx, yy];
                    if (cellType != CellType.Unloaded && cellType != CellType.Pregener)
                    {
                        uniqueCellTypes.Add(cellType);
                    }
                }
            }

            // Preload textures for unique cell types
            ITextureService manager = SessionAccess.Resolve()?.TryResolve<ITextureService>() ??
                throw new InvalidOperationException(
                    "World texture preloading requires a registered ITextureService.");

            var tasks = new List<UniTask<AtlasCoordinate>>();
            foreach (var cellType in uniqueCellTypes)
            {
                var task = manager.GetCellTextureCoordinate(cellType, x, y);
                tasks.Add(task);
            }

            await UniTask.WhenAll(tasks);
        }

        /// <summary>
        /// Get all active texture atlases from the WorldTextureManager.
        /// </summary>
        /// <param name="worldLayer">The world layer.</param>
        /// <returns>List of active texture atlases.</returns>
        public static List<TextureAtlas> GetActiveAtlases(this WorldLayer<CellType> worldLayer)
        {
            ITextureService manager = SessionAccess.Resolve()?.TryResolve<ITextureService>() ??
                throw new InvalidOperationException(
                    "World texture atlases require a registered ITextureService.");
            return manager.GetAllAtlases();
        }

        /// <summary>
        /// Clear all cached textures and atlases.
        /// </summary>
        /// <param name="worldLayer">The world layer.</param>
        public static void ClearTextureCache(this WorldLayer<CellType> worldLayer)
        {
            ITextureService manager = SessionAccess.Resolve()?.TryResolve<ITextureService>() ??
                throw new InvalidOperationException(
                    "World texture cache clearing requires a registered ITextureService.");
            manager.Clear();
        }

        /// <summary>
        /// Get texture cache statistics.
        /// </summary>
        /// <param name="worldLayer">The world layer.</param>
        /// <returns>Cache statistics string.</returns>
        public static string GetTextureCacheStats(this WorldLayer<CellType> worldLayer)
        {
            ITextureService manager = SessionAccess.Resolve()?.TryResolve<ITextureService>() ??
                throw new InvalidOperationException(
                    "World texture cache statistics require a registered ITextureService.");
            return manager.GetCacheStats();
        }
    }
}
