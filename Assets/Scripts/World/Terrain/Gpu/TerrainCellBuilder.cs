#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.World.Terrain.Background;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World.Terrain;

// Всё, из чего собирается клетка террейна.
public readonly record struct TerrainCellSources(
    TerrainCellCache CellCache,
    TerrainPrecalculator Precalc,
    BackgroundFloodFill FloodFill,
    int WorldWidth,
    int WorldHeight,
    IReadOnlyList<IAtlasDescriptor> Atlases,
    bool UseColorLod,
    MapManager MapManager,
    ITextureService TextureService);

// Сборщик террейна в тексели данных клетки.
//
// Раньше каждая клетка превращалась в 8 вершин по 84 байта, буфер сдвигался
// целиком на каждом шаге камеры и целиком же уезжал на GPU. Теперь клетка
// пишется в тексели по кольцевому адресу (мировая клетка по модулю размера
// сетки): при сдвиге камеры уже записанные клетки остаются на своих местах,
// и собирается только вошедшая полоса.
//
// Раскладка клетки не дублируется: TerrainQuadBuilder по-прежнему заполняет
// квад, но во временные 8 вершин на поток, из которых упаковываются тексели.
public sealed class TerrainCellBuilder : IDisposable
{
    private sealed class Scratch
    {
        public readonly TerrainVertex[] Vertices = new TerrainVertex[8];
        public readonly bool[] Door = new bool[1];
    }

    private readonly TerrainCellDataTextures _textures = new();
    private readonly Scratch _mainScratch = new();
    private int[] _foregroundAtlases = [];
    private bool[] _doorFlags = [];
    private int _width;
    private int _height;
    private float _cellSize;
    private bool _doorsTouched;

    public TerrainCellDataTextures Textures => _textures;

    // Последняя сборка задела двери: накладку надо пересобрать.
    public bool DoorsTouched => _doorsTouched;

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellSize = cellSize;
        if (_width == meshWidth && _height == meshHeight && _textures.IsAllocated)
        {
            return;
        }

        _width = meshWidth;
        _height = meshHeight;
        _foregroundAtlases = new int[meshWidth * meshHeight];
        _doorFlags = new bool[meshWidth * meshHeight];
        Array.Fill(_foregroundAtlases, -1);
        _textures.EnsureCapacity(meshWidth, meshHeight);
    }

    public void BuildFull(TerrainCellSources sources, int minX, int minY)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        _doorsTouched = true;
        _textures.MarkAllDirty();
        Parallel.For(
            0,
            _width,
            static () => new Scratch(),
            (x, _, scratch) =>
            {
                for (int y = 0; y < _height; y++)
                {
                    FillCell(x, y, minX, minY, sources, scratch);
                }

                return scratch;
            },
            static _ => { });
    }

    public void ScrollAndBuildBand(TerrainCellSources sources, int minX, int minY, int dx, int dy)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        // Сдвиг во всё окно не оставляет ничего годного, и полосы выродились
        // бы в ту же полную сборку.
        if (Math.Abs(dx) >= _width || Math.Abs(dy) >= _height)
        {
            BuildFull(sources, minX, minY);
            return;
        }

        // Тексели по кольцевому адресу не двигаются. Двигаются только
        // локальные массивы дверей, и накладка встаёт на новые координаты.
        _doorsTouched = true;
        if (dx != 0 || dy != 0)
        {
            TerrainMeshScroller.Scroll(_foregroundAtlases, _width, _height, 1, dx, dy);
            TerrainMeshScroller.Scroll(_doorFlags, _width, _height, 1, dx, dy);
        }

        TerrainMeshScroller.GetBandExtents(_width, dx, out int bandXStart, out int bandXLength);
        TerrainMeshScroller.GetBandExtents(_height, dy, out int bandYStart, out int bandYLength);

        if (bandXLength > 0)
        {
            FillRect(bandXStart, bandXStart + bandXLength, 0, _height, minX, minY, sources);
        }

        if (bandYLength > 0 && bandXLength < _width)
        {
            // Полоса по y берёт только ширину, не покрытую полосой по x:
            // угол иначе был бы собран дважды.
            int remainingStart = dx > 0 ? 0 : bandXLength;
            int remainingEnd = dx > 0 ? bandXStart : _width;
            if (remainingStart < remainingEnd)
            {
                FillRect(remainingStart, remainingEnd, bandYStart, bandYStart + bandYLength, minX, minY, sources);
            }
        }
    }

    public void BuildRegion(
        TerrainCellSources sources,
        int minX,
        int minY,
        int startX,
        int startY,
        int countX,
        int countY)
    {
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        FillRect(
            Mathf.Clamp(startX, 0, _width),
            Mathf.Clamp(startX + countX, 0, _width),
            Mathf.Clamp(startY, 0, _height),
            Mathf.Clamp(startY + countY, 0, _height),
            minX,
            minY,
            sources);
    }

    public void BuildTextureCells(HashSet<CellType> cellTypes, TerrainCellSources sources, int minX, int minY)
    {
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                CellType foregroundType = sources.CellCache.GetCellData(x + 1, y + 1).Type;
                CellType backgroundType = sources.FloodFill.Buffer[x, y];
                if (cellTypes.Contains(foregroundType) || cellTypes.Contains(backgroundType))
                {
                    FillCell(x, y, minX, minY, sources, _mainScratch);
                    _textures.MarkCells(
                        TerrainCellDataTextures.Ring(minX + x, _width),
                        TerrainCellDataTextures.Ring(minY + y, _height),
                        1,
                        1);
                }
            }
        }
    }

    // Вершины накладки дверей: клеток с дверью мало, поэтому их квады
    // собираются заново по требованию, а не хранятся для всей сетки.
    public void BuildDoorOverlay(
        TerrainCellSources sources,
        int minX,
        int minY,
        List<TerrainVertex> vertices,
        List<int>[] indicesPerAtlas)
    {
        vertices.Clear();
        foreach (List<int> indices in indicesPerAtlas)
        {
            indices.Clear();
        }

        for (int quad = 0; quad < _doorFlags.Length; quad++)
        {
            int atlas = _foregroundAtlases[quad];
            if (!_doorFlags[quad] || atlas < 0 || atlas >= indicesPerAtlas.Length)
            {
                continue;
            }

            int x = quad / _height;
            int y = quad % _height;
            TerrainQuadBuilder.FillQuadData(
                _mainScratch.Vertices, _mainScratch.Door, _cellSize,
                x, y, minX + x, minY + y, sources.CellCache, sources.Precalc, sources.FloodFill,
                sources.WorldWidth, sources.WorldHeight, false, 4, sources.Atlases, sources.UseColorLod,
                sources.MapManager, sources.TextureService);

            int baseVertex = vertices.Count;
            for (int corner = 4; corner < 8; corner++)
            {
                vertices.Add(_mainScratch.Vertices[corner]);
            }

            List<int> target = indicesPerAtlas[atlas];
            target.Add(baseVertex);
            target.Add(baseVertex + 3);
            target.Add(baseVertex + 2);
            target.Add(baseVertex + 2);
            target.Add(baseVertex + 1);
            target.Add(baseVertex);
        }
    }

    // Выгрузка на GPU и адрес окна для шейдера. Смещения искажения
    // переписываются целиком: предрасчёт двигает их вместе с окном.
    public void Commit(TerrainPrecalculator precalc, int minX, int minY)
    {
        if (precalc.EnableDistortion)
        {
            _textures.WriteGridOffsets(precalc.GridVertexOffsets, minX, minY);
        }

        _textures.Apply();
        _textures.BindGlobals(_cellSize, minX, minY, precalc.EnableDistortion);
    }

    public void Dispose()
    {
        _textures.Dispose();
        _width = 0;
        _height = 0;
    }

    // Квад переднего плана даёт альфу 1 на каждом пикселе клетки, только если
    // у него есть текстура, она непрозрачна вся, цвет без прозрачности, нет
    // скругления контура и шейдер не выводит квад прозрачным.
    private static bool ForegroundCoversCell(int x, int y, int foreground, Scratch scratch, TerrainCellSources sources)
    {
        if (foreground < 0 || foreground >= sources.Atlases.Count)
        {
            return false;
        }

        ref TerrainVertex vertex = ref scratch.Vertices[4];
        const int RoundableFlag = 2;
        bool hasTexture = vertex.UV1z != 0 && Mathf.HalfToFloat(vertex.UV1z) > 0.0001f;
        bool roundable = (Mathf.RoundToInt(vertex.UV6.z) & RoundableFlag) != 0;
        if (!hasTexture || roundable || vertex.Color.a < 255 || vertex.UV3.w > 1.5f)
        {
            return false;
        }

        CellType foregroundType = sources.CellCache.GetCellData(x + 1, y + 1).Type;
        return sources.Atlases[foreground].IsFullyOpaque(foregroundType);
    }

    private bool CanBuild(TerrainCellSources sources) =>
        _textures.IsAllocated && sources.Atlases != null && sources.Atlases.Count > 0;

    private void FillRect(int startX, int endX, int startY, int endY, int minX, int minY, TerrainCellSources sources)
    {
        if (endX <= startX || endY <= startY)
        {
            return;
        }

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                FillCell(x, y, minX, minY, sources, _mainScratch);
            }
        }

        _textures.MarkCells(
            TerrainCellDataTextures.Ring(minX + startX, _width),
            TerrainCellDataTextures.Ring(minY + startY, _height),
            endX - startX,
            endY - startY);
    }

    private void FillCell(int x, int y, int minX, int minY, TerrainCellSources sources, Scratch scratch)
    {
        int gridX = minX + x;
        int unityY = minY + y;
        int quad = (x * _height) + y;
        scratch.Door[0] = false;

        int background = TerrainQuadBuilder.FillQuadData(
            scratch.Vertices, scratch.Door, _cellSize,
            x, y, gridX, unityY, sources.CellCache, sources.Precalc, sources.FloodFill,
            sources.WorldWidth, sources.WorldHeight, true, 0, sources.Atlases, sources.UseColorLod,
            sources.MapManager, sources.TextureService);
        int foreground = TerrainQuadBuilder.FillQuadData(
            scratch.Vertices, scratch.Door, _cellSize,
            x, y, gridX, unityY, sources.CellCache, sources.Precalc, sources.FloodFill,
            sources.WorldWidth, sources.WorldHeight, false, 4, sources.Atlases, sources.UseColorLod,
            sources.MapManager, sources.TextureService);

        bool door = scratch.Door[0];
        if (door || _doorFlags[quad])
        {
            _doorsTouched = true;
        }

        _foregroundAtlases[quad] = foreground;
        _doorFlags[quad] = door;

        int ringX = TerrainCellDataTextures.Ring(gridX, _width);
        int ringY = TerrainCellDataTextures.Ring(unityY, _height);
        TerrainCellTexels backgroundTexels = TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(0, 4), background);
        if (background >= 0 && ForegroundCoversCell(x, y, foreground, scratch, sources))
        {
            // Сплошной передний план закрывает фон целиком: смешивание
            // полностью непрозрачного пикселя не оставляет от фона ни цвета,
            // ни света, ни тени. Квад фона отбрасывается в вершинном шейдере.
            Color32 meta = backgroundTexels.Meta;
            backgroundTexels = backgroundTexels with { Meta = new Color32(meta.r, meta.g, 1, meta.a) };
        }

        _textures.SetCell(ringX, ringY, TerrainCellDataPacker.BackgroundLayer, backgroundTexels);
        _textures.SetCell(
            ringX, ringY, TerrainCellDataPacker.ForegroundLayer,
            TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(4, 4), foreground));
    }
}
