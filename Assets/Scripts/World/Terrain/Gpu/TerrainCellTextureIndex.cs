#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>
/// Какие клетки окна надо перечитать, когда приехала текстура типа.
/// </summary>
///
/// Слоя два, и у каждого свой тип в одной и той же клетке: фон берёт тип из
/// карты заливки, передний план — из карты мира. Текстура приезжает для типа,
/// поэтому оба индекса спрашиваются отдельно, а результат склеивается: квад
/// перечитывается один раз, даже если оба его слоя одного типа.
internal sealed class TerrainCellTextureIndex
{
    private readonly CellTypeSpatialIndex _background = new();
    private readonly CellTypeSpatialIndex _foreground = new();
    private readonly List<int> _textureRefreshQuads = [];
    private readonly HashSet<long> _textureRefreshMarks = [];
    private int _textureRefreshWindowX;
    private int _textureRefreshWindowY;

    public List<int> TextureRefreshQuads => _textureRefreshQuads;

    public void Clear()
    {
        _background.Clear();
        _foreground.Clear();
        _textureRefreshQuads.Clear();
        _textureRefreshMarks.Clear();
    }

    public void Rebuild(TerrainCellSources sources, int minX, int minY, int width, int height)
    {
        _background.Clear();
        _foreground.Clear();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                UpdateCell(
                    minX + x,
                    minY + y,
                    sources.FloodFill.Buffer[x, y],
                    sources.CellCache.GetCellData(x + 1, y + 1).Type);
            }
        }
    }

    public void UpdateCell(int gridX, int unityY, CellType backgroundType, CellType foregroundType)
    {
        long key = TerrainCoordinateKey.Pack(gridX, unityY);
        _background.Set(key, backgroundType);
        _foreground.Set(key, foregroundType);
    }

    /// <summary>
    /// Собрать квады окна, задетые перечисленными типами. Список отсортирован:
    /// сборка идёт по нему подряд и метит соседние клетки одним прямоугольником.
    /// </summary>
    public void CollectRefreshQuads(HashSet<CellType> cellTypes, int minX, int minY, int width, int height)
    {
        _textureRefreshQuads.Clear();
        _textureRefreshMarks.Clear();
        _textureRefreshWindowX = minX;
        _textureRefreshWindowY = minY;
        foreach (CellType cellType in cellTypes)
        {
            AddTextureRefreshQuads(_background, cellType, width, height);
            AddTextureRefreshQuads(_foreground, cellType, width, height);
        }

        _textureRefreshQuads.Sort();
    }

    public void RemoveScrolledOutCells(int minX, int minY, int dx, int dy, int width, int height)
    {
        int oldMinX = minX - dx;
        int oldMinY = minY - dy;
        if (dx > 0)
        {
            RemoveRect(oldMinX, oldMinX + dx, oldMinY, oldMinY + height);
        }
        else if (dx < 0)
        {
            RemoveRect(minX + width, oldMinX + width, oldMinY, oldMinY + height);
        }

        if (dy > 0)
        {
            RemoveRect(minX, minX + width, oldMinY, oldMinY + dy);
        }
        else if (dy < 0)
        {
            RemoveRect(minX, minX + width, minY + height, oldMinY + height);
        }
    }

    private void RemoveRect(int startX, int endX, int startY, int endY)
    {
        _background.RemoveRect(startX, endX, startY, endY);
        _foreground.RemoveRect(startX, endX, startY, endY);
    }

    private void AddTextureRefreshQuads(
        CellTypeSpatialIndex index,
        CellType cellType,
        int width,
        int height)
    {
        foreach (long key in index.KeysOf(cellType))
        {
            int worldX = TerrainCoordinateKey.UnpackX(key);
            int worldY = TerrainCoordinateKey.UnpackY(key);
            if ((uint)(worldX - _textureRefreshWindowX) >= (uint)width ||
                (uint)(worldY - _textureRefreshWindowY) >= (uint)height)
            {
                continue;
            }

            int quad = ((worldX - _textureRefreshWindowX) * height) +
                (worldY - _textureRefreshWindowY);
            if (_textureRefreshMarks.Add(key))
            {
                _textureRefreshQuads.Add(quad);
            }
        }
    }
}
