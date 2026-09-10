#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain;
public class TerrainMeshBuilder
{
    private TerrainVertex[] _vertexBuffer = Array.Empty<TerrainVertex>();
    private float _cellSize;
    public TerrainVertex[] VertexBuffer => _vertexBuffer;

    private int[] _bgAtlasIndices = Array.Empty<int>();
    private int[] _fgAtlasIndices = Array.Empty<int>();
    private bool[] _foregroundOverlayFlags = Array.Empty<bool>();

    /// <summary>Сколько вершин занимает одна клетка: два слоя по четыре.</summary>
    private const int VerticesPerCell = 8;

    private readonly TerrainSubMeshIndexBuilder _indexBuilder = new();

    /// <summary>
    /// Whether the last <see cref="BuildRegion"/> changed which submesh any
    /// quad belongs to, and so requires the index lists to be re-uploaded.
    /// </summary>
    /// <remarks>
    /// Almost always false. A quad's submesh is its texture atlas, and a
    /// streamed chunk or a mined cell changes the cell's appearance far
    /// more often than it moves that cell onto a different atlas. Rebuilding
    /// the lists regardless meant every incremental patch, however small,
    /// cleared every submesh list and re-appended twelve ints for every quad
    /// in the viewport - the whole grid's worth of List&lt;int&gt;.Add on the
    /// main thread, to usually reproduce the identical lists.
    /// </remarks>
    public bool IndicesChanged { get; private set; }

    public bool OverlayIndicesChanged { get; private set; }

    /// <summary>
    /// The span of <see cref="VertexBuffer"/> the last
    /// <see cref="BuildRegion"/> actually wrote, as a vertex offset and
    /// count. Zero count means it wrote nothing.
    /// </summary>
    /// <remarks>
    /// Quads are indexed x-major (<c>x * meshHeight + y</c>), so a
    /// rectangle occupies one contiguous run per column and this span is
    /// the smallest range covering all of them - tight when the dirty rect
    /// is narrow in x, which is the common case for a walking player.
    /// </remarks>
    public int DirtyVertexStart { get; private set; }

    public int DirtyVertexCount { get; private set; }

    /// <summary>
    /// True when the last <see cref="BuildRegion"/> rewrote a quad that
    /// carries a foreground overlay, before or after the write.
    /// </summary>
    /// <remarks>
    /// Накладка дверей — отдельная сетка, собранная копированием вершин
    /// оверлейных квадов из этого буфера. Её надо пересобирать не только
    /// когда список квадов поменялся, но и когда у прежнего квада
    /// переписали вершины: копия иначе останется от старого кадра.
    /// Флаг снят прямо в цикле записи, где обе стороны и так под рукой.
    /// </remarks>
    public bool OverlayQuadsTouched { get; private set; }

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellSize = cellSize;
        int quadCount = meshWidth * meshHeight * 2;
        int vertCount = quadCount * 4;

        if (_vertexBuffer == null || _vertexBuffer.Length != vertCount)
        {
            _vertexBuffer = new TerrainVertex[vertCount];
        }

        int singleLayerQuads = meshWidth * meshHeight;
        if (_bgAtlasIndices.Length != singleLayerQuads)
        {
            _bgAtlasIndices = new int[singleLayerQuads];
            _fgAtlasIndices = new int[singleLayerQuads];
            _foregroundOverlayFlags = new bool[singleLayerQuads];
        }
    }

    public void BuildFull(TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
        int minX, int minY, int meshWidth, int meshHeight, int worldWidth, int worldHeight,
        IReadOnlyList<IAtlasDescriptor> atlases, List<int>[] subMeshIndices, bool useColorLod,
        MapManager mapManager, ITextureService textureManager)
    {
        if (atlases == null || atlases.Count == 0 || subMeshIndices == null || subMeshIndices.Length == 0)
        {
            return;
        }

        EnsureCapacity(meshWidth, meshHeight, _cellSize);

        // A full build rewrites everything, so the incremental bookkeeping
        // reports exactly that to anyone who reads it after this call.
        IndicesChanged = true;
        OverlayIndicesChanged = true;
        DirtyVertexStart = 0;
        DirtyVertexCount = _vertexBuffer.Length;

        System.Threading.Tasks.Parallel.For(0, meshWidth, x =>
        {
            int gridX = minX + x;
            for (int y = 0; y < meshHeight; y++)
            {
                int unityY = minY + y;
                int quadIdx = (x * meshHeight) + y;
                int baseIdx = quadIdx * 8;
                _bgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, true, baseIdx, atlases, useColorLod,
                    mapManager, textureManager);
                _fgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, false, baseIdx + 4, atlases, useColorLod,
                    mapManager, textureManager);
            }
        });

        RebuildSubMeshIndices(meshWidth, meshHeight, subMeshIndices);
    }

    /// <summary>
    /// Переносит уцелевшую часть сетки на новое место и пересобирает только
    /// открывшуюся кайму.
    /// </summary>
    /// <remarks>
    /// ЗАЧЕМ. Переход через границу региона снимался полной пересборкой:
    /// <see cref="BuildFull"/> считал <c>FillQuadData</c> для каждой клетки
    /// окна. Сетка снапится по восемь клеток, а окно доходит до 384 — сдвиг
    /// открывает порядка двух процентов площади, и девяносто восемь
    /// пересчитывались, чтобы получить ровно то, что уже лежало в буфере.
    /// Кэш клеток, предрасчёт и заливка фона к этому моменту уже приехали
    /// сдвигом; полными оставались только сборка меша и выгрузка.
    ///
    /// ПОЧЕМУ ПОЗИЦИЮ ПРАВИМ ВЫЧИТАНИЕМ. Из всех полей вершины на локальные
    /// координаты завязана одна <c>Position</c>: она равна
    /// <c>(x, y) * cellSize</c> плюс смещение искажения, а смещение живёт при
    /// клетке мира и едет вместе с данными. Клетка, переехавшая из
    /// <c>x + dx</c> в <c>x</c>, обязана потерять ровно <c>dx * cellSize</c>.
    /// Остальные поля — мировые координаты, атлас, маски — верны на новом
    /// месте без правки.
    ///
    /// ПОЧЕМУ КАЙМА ШИРЕ СДВИГА НА КЛЕТКУ. Маски соседства пересчитываются с
    /// тем же запасом (<c>TerrainCellMaskCalculator.PrecalculateIncremental</c>):
    /// у клетки, ставшей крайней внутри, сосед снаружи сменился, и её вид
    /// вместе с ним. Кайма здесь обязана совпадать с каймой предрасчёта,
    /// иначе шов останется отрисованным по старым маскам.
    ///
    /// ЧТО ОСТАЁТСЯ ПОЛНЫМ. Списки индексов: приписка клетки к атласу
    /// привязана к месту в буфере, а сдвинулись все места сразу. Выгрузка
    /// вершин — тоже: сдвиг меняет содержимое почти каждого слота.
    /// </remarks>
    public void ScrollAndBuildBand(
        TerrainCellCache cellCache,
        TerrainPrecalculator precalc,
        BackgroundFloodFill bgFloodFill,
        int minX,
        int minY,
        int meshWidth,
        int meshHeight,
        int dx,
        int dy,
        int worldWidth,
        int worldHeight,
        IReadOnlyList<IAtlasDescriptor> atlases,
        List<int>[] subMeshIndices,
        bool useColorLod,
        MapManager mapManager,
        ITextureService textureManager)
    {
        if (atlases == null || atlases.Count == 0 || subMeshIndices == null || subMeshIndices.Length == 0)
        {
            return;
        }

        EnsureCapacity(meshWidth, meshHeight, _cellSize);

        // Сдвиг во всё окно не оставляет ничего годного для переноса, и
        // разбор каймы выродился бы в ту же полную сборку с лишним копированием.
        if (Mathf.Abs(dx) >= meshWidth || Mathf.Abs(dy) >= meshHeight)
        {
            BuildFull(
                cellCache, precalc, bgFloodFill, minX, minY, meshWidth, meshHeight,
                worldWidth, worldHeight, atlases, subMeshIndices, useColorLod,
                mapManager, textureManager);
            return;
        }

        // Сдвиг переписывает почти каждый слот буфера, поэтому наружу
        // сообщаем ровно это: выгружать придётся весь диапазон.
        IndicesChanged = true;
        OverlayIndicesChanged = true;
        OverlayQuadsTouched = true;
        DirtyVertexStart = 0;
        DirtyVertexCount = _vertexBuffer.Length;

        if (dx != 0 || dy != 0)
        {
            TerrainMeshScroller.Scroll(_vertexBuffer, meshWidth, meshHeight, VerticesPerCell, dx, dy);
            TerrainMeshScroller.Scroll(_bgAtlasIndices, meshWidth, meshHeight, 1, dx, dy);
            TerrainMeshScroller.Scroll(_fgAtlasIndices, meshWidth, meshHeight, 1, dx, dy);
            TerrainMeshScroller.Scroll(_foregroundOverlayFlags, meshWidth, meshHeight, 1, dx, dy);
            TerrainMeshScroller.ShiftPositions(
                _vertexBuffer, meshWidth, meshHeight, VerticesPerCell, _cellSize, dx, dy);
        }

        TerrainMeshScroller.GetBandExtents(meshWidth, dx, out int bandXStart, out int bandXLength);
        TerrainMeshScroller.GetBandExtents(meshHeight, dy, out int bandYStart, out int bandYLength);

        if (bandXLength > 0)
        {
            FillBand(
                bandXStart, bandXStart + bandXLength, 0, meshHeight,
                cellCache, precalc, bgFloodFill, minX, minY, meshHeight,
                worldWidth, worldHeight, atlases, useColorLod, mapManager, textureManager);
        }

        if (bandYLength > 0 && bandXLength < meshWidth)
        {
            // Полоса по y берёт только ширину, не покрытую полосой по x:
            // угол иначе был бы собран дважды.
            int remainingStart = dx > 0 ? 0 : bandXLength;
            int remainingEnd = dx > 0 ? bandXStart : meshWidth;

            if (remainingStart < remainingEnd)
            {
                FillBand(
                    remainingStart, remainingEnd, bandYStart, bandYStart + bandYLength,
                    cellCache, precalc, bgFloodFill, minX, minY, meshHeight,
                    worldWidth, worldHeight, atlases, useColorLod, mapManager, textureManager);
            }
        }

        RebuildSubMeshIndices(meshWidth, meshHeight, subMeshIndices);
    }

    private void FillBand(
        int startX,
        int endX,
        int startY,
        int endY,
        TerrainCellCache cellCache,
        TerrainPrecalculator precalc,
        BackgroundFloodFill bgFloodFill,
        int minX,
        int minY,
        int meshHeight,
        int worldWidth,
        int worldHeight,
        IReadOnlyList<IAtlasDescriptor> atlases,
        bool useColorLod,
        MapManager mapManager,
        ITextureService textureManager)
    {
        for (int x = startX; x < endX; x++)
        {
            int gridX = minX + x;
            for (int y = startY; y < endY; y++)
            {
                int unityY = minY + y;
                int quadIndex = (x * meshHeight) + y;
                int baseIndex = quadIndex * VerticesPerCell;
                _bgAtlasIndices[quadIndex] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, true, baseIndex, atlases, useColorLod,
                    mapManager, textureManager);
                _fgAtlasIndices[quadIndex] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, false, baseIndex + 4, atlases, useColorLod,
                    mapManager, textureManager);
            }
        }
    }

    public void BuildRegion(TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
        int minX, int minY, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY, int worldWidth, int worldHeight,
        IReadOnlyList<IAtlasDescriptor> atlases, List<int>[] subMeshIndices, bool useColorLod,
        MapManager mapManager, ITextureService textureManager)
    {
        if (atlases == null || atlases.Count == 0 || subMeshIndices == null || subMeshIndices.Length == 0)
        {
            return;
        }

        int endX = Mathf.Clamp(startX + countX, 0, meshWidth);
        int endY = Mathf.Clamp(startY + countY, 0, meshHeight);
        int clampedStartX = Mathf.Clamp(startX, 0, meshWidth);
        int clampedStartY = Mathf.Clamp(startY, 0, meshHeight);

        if (endX <= clampedStartX || endY <= clampedStartY)
        {
            IndicesChanged = false;
            OverlayIndicesChanged = false;
            OverlayQuadsTouched = false;
            DirtyVertexStart = 0;
            DirtyVertexCount = 0;
            return;
        }

        int firstQuad = (clampedStartX * meshHeight) + clampedStartY;
        int lastQuad = ((endX - 1) * meshHeight) + (endY - 1);
        DirtyVertexStart = firstQuad * 8;
        DirtyVertexCount = ((lastQuad + 1) * 8) - DirtyVertexStart;

        bool atlasAssignmentChanged = false;
        bool overlayAssignmentChanged = false;
        bool overlayQuadsTouched = false;
        for (int x = clampedStartX; x < endX; x++)
        {
            int gridX = minX + x;
            for (int y = clampedStartY; y < endY; y++)
            {
                int unityY = minY + y;
                int quadIdx = (x * meshHeight) + y;
                int baseIdx = quadIdx * 8;
                int previousBackgroundAtlas = _bgAtlasIndices[quadIdx];
                int previousForegroundAtlas = _fgAtlasIndices[quadIdx];
                bool previousOverlay = _foregroundOverlayFlags[quadIdx];
                _bgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, true, baseIdx, atlases, useColorLod,
                    mapManager, textureManager);
                _fgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, false, baseIdx + 4, atlases, useColorLod,
                    mapManager, textureManager);
                if (_bgAtlasIndices[quadIdx] != previousBackgroundAtlas ||
                    _fgAtlasIndices[quadIdx] != previousForegroundAtlas)
                {
                    atlasAssignmentChanged = true;
                }

                overlayAssignmentChanged |=
                    previousOverlay != _foregroundOverlayFlags[quadIdx];
                overlayQuadsTouched |=
                    previousOverlay || _foregroundOverlayFlags[quadIdx];
            }
        }

        IndicesChanged = atlasAssignmentChanged;
        OverlayIndicesChanged = overlayAssignmentChanged || atlasAssignmentChanged;
        OverlayQuadsTouched = overlayQuadsTouched;
        if (!atlasAssignmentChanged)
        {
            // The vertices moved onto different textures within the same
            // atlases, so every triangle still belongs to the submesh it
            // already belonged to. Rebuilding the lists would reproduce
            // them byte for byte.
            return;
        }

        RebuildSubMeshIndices(meshWidth, meshHeight, subMeshIndices);
    }

    public void BuildTextureCells(
        HashSet<CellType> cellTypes,
        TerrainCellCache cellCache,
        TerrainPrecalculator precalc,
        BackgroundFloodFill bgFloodFill,
        int minX,
        int minY,
        int meshWidth,
        int meshHeight,
        int worldWidth,
        int worldHeight,
        IReadOnlyList<IAtlasDescriptor> atlases,
        List<int>[] subMeshIndices,
        bool useColorLod,
        MapManager mapManager,
        ITextureService textureManager)
    {
        bool atlasAssignmentChanged = false;
        int firstDirtyQuad = int.MaxValue;
        int lastDirtyQuad = -1;
        for (int x = 0; x < meshWidth; x++)
        {
            int gridX = minX + x;
            for (int y = 0; y < meshHeight; y++)
            {
                CellType foregroundType = cellCache.GetCellData(x + 1, y + 1).Type;
                CellType backgroundType = bgFloodFill.Buffer[x, y];
                if (!cellTypes.Contains(foregroundType) && !cellTypes.Contains(backgroundType))
                {
                    continue;
                }

                int quadIndex = (x * meshHeight) + y;
                int baseIndex = quadIndex * 8;
                int previousBackgroundAtlas = _bgAtlasIndices[quadIndex];
                int previousForegroundAtlas = _fgAtlasIndices[quadIndex];
                _bgAtlasIndices[quadIndex] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, minY + y, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, true, baseIndex, atlases, useColorLod,
                    mapManager, textureManager);
                _fgAtlasIndices[quadIndex] = TerrainQuadBuilder.FillQuadData(
                    _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                    x, y, gridX, minY + y, cellCache, precalc, bgFloodFill,
                    worldWidth, worldHeight, false, baseIndex + 4, atlases, useColorLod,
                    mapManager, textureManager);
                atlasAssignmentChanged |=
                    _bgAtlasIndices[quadIndex] != previousBackgroundAtlas ||
                    _fgAtlasIndices[quadIndex] != previousForegroundAtlas;
                firstDirtyQuad = Mathf.Min(firstDirtyQuad, quadIndex);
                lastDirtyQuad = Mathf.Max(lastDirtyQuad, quadIndex);
            }
        }

        IndicesChanged = atlasAssignmentChanged;
        OverlayIndicesChanged = atlasAssignmentChanged;
        // Флаг обязан быть выставлен и здесь: иначе следующая заплатка
        // прочитает его от прошлого вызова BuildRegion и решит судьбу
        // накладки по чужому кадру.
        OverlayQuadsTouched = lastDirtyQuad >= 0;
        DirtyVertexStart = lastDirtyQuad >= 0 ? firstDirtyQuad * 8 : 0;
        DirtyVertexCount = lastDirtyQuad >= 0 ? ((lastDirtyQuad + 1) * 8) - DirtyVertexStart : 0;
        if (!atlasAssignmentChanged)
        {
            return;
        }

        RebuildSubMeshIndices(meshWidth, meshHeight, subMeshIndices);
    }

    private void RebuildSubMeshIndices(
        int meshWidth,
        int meshHeight,
        List<int>[] subMeshIndices)
    {
        _indexBuilder.Rebuild(
            _bgAtlasIndices,
            _fgAtlasIndices,
            meshWidth,
            meshHeight,
            VerticesPerCell,
            subMeshIndices);
    }

    public void RebuildOverlaySubMeshIndices(
        int meshWidth,
        int meshHeight,
        List<int>[] overlaySubMeshIndices)
    {
        TerrainSubMeshIndexBuilder.RebuildOverlay(
            _fgAtlasIndices,
            _foregroundOverlayFlags,
            meshWidth,
            meshHeight,
            VerticesPerCell,
            overlaySubMeshIndices);
    }
}
