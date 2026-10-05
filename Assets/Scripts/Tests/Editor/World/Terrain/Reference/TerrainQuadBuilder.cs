#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

// Прежняя CPU-сборка квада в вершины — эталон вида для тестов
// эквивалентности. Рабочий путь вершин больше не строит: шейдер собирает квад
// из буфера клеток (TerrainCellData.hlsl), а этот код замороженно
// воспроизводит то, что раньше уходило на GPU вершинами.
internal static class TerrainQuadBuilder
{
    /// <summary>
    /// Собрать квад одного слоя клетки в четыре вершины.
    /// </summary>
    ///
    /// Буфер клеток вершин не хранит: шейдер собирает квад из клетки и её
    /// соседей. Вершины нужны накладке дверей и тестам-оракулам, для которых
    /// это и есть эталон вида.
    ///
    /// Слой решает почти всё: фон берёт тип подложки и остаётся
    /// прямоугольным, передний план берёт тип клетки и несёт смещённую
    /// геометрию, свет и кайму. Поэтому слой — это перечисление, а не булево
    /// «isBackground» с индексом вершины, по которому раньше приходилось
    /// угадывать, в какую половину буфера пишут.
    public static TerrainQuadResult FillQuad(
        in TerrainCellSources sources,
        in TerrainQuadSite site,
        TerrainQuadLayer layer,
        Span<TerrainVertex> quad)
    {
        ITerrainCellDataSource cellCache = sources.CellCache;
        TerrainDistortionSettings distortion = sources.Distortion;
        IReadOnlyList<IAtlasDescriptor> atlases = sources.Atlases;
        int worldWidth = sources.WorldWidth;
        int worldHeight = sources.WorldHeight;
        int x = site.LocalX;
        int y = site.LocalY;
        int gridX = site.GridX;
        int unityY = site.UnityY;
        float cellSize = site.CellSize;
        bool isBackground = layer == TerrainQuadLayer.Background;

        if (unityY < 0 || unityY >= worldHeight || gridX < 0 || gridX >= worldWidth)
        {
            quad.Clear();
            return TerrainQuadResult.None;
        }

        int cx = x + 1;
        int cy = y + 1;
        int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

        CachedCellData ccd = cellCache.GetCellData(cx, cy);
        CellType cellFgType = ccd.Type;
        CellVisualProperties foregroundVisuals = MapCellConfigCatalog.GetVisualProperties(cellFgType);

        bool isDoor = !isBackground && foregroundVisuals.IsBuildingDoor;

        if (ccd.State != TerrainCellState.Loaded)
        {
            quad.Clear();
            return TerrainQuadResult.NoAtlas(isDoor);
        }

        // Соседство клетки: маски, рельеф и автотайл считаются здесь же — тем
        // же правилом, что шейдер выводит из соседей в буфере клеток.
        CachedCellData left = cellCache.GetCellData(cx - 1, cy);
        CachedCellData bottomLeft = cellCache.GetCellData(cx - 1, cy - 1);
        CachedCellData bottom = cellCache.GetCellData(cx, cy - 1);
        CachedCellData bottomRight = cellCache.GetCellData(cx + 1, cy - 1);
        CachedCellData right = cellCache.GetCellData(cx + 1, cy);
        CachedCellData topRight = cellCache.GetCellData(cx + 1, cy + 1);
        CachedCellData top = cellCache.GetCellData(cx, cy + 1);
        CachedCellData topLeft = cellCache.GetCellData(cx - 1, cy + 1);

        bool organicTerrain = distortion.EnableDistortion &&
            distortion.DistortionStyle == TerrainDistortionStyle.Organic &&
            TerrainVertexDistortionCalculator.IsCause(ccd);
        bool organicCause = !isBackground && organicTerrain;
        int organicEdges = 0;
        bool needsOrganicUnderlay = false;
        if (organicTerrain)
        {
            needsOrganicUnderlay =
                IsOrganicEmptyEdge(bottom) ||
                IsOrganicEmptyEdge(right) ||
                IsOrganicEmptyEdge(top) ||
                IsOrganicEmptyEdge(left);
            if (organicCause)
            {
                organicEdges = TerrainCellGeometry.EncodeOrganicEdges(
                    OrganicEdgeBend(bottom, gridX, unityY, false, 1),
                    OrganicEdgeBend(right, gridX + 1, unityY, true, -1),
                    OrganicEdgeBend(top, gridX, unityY + 1, false, -1),
                    OrganicEdgeBend(left, gridX, unityY, true, 1));
            }
        }

        CellType backgroundType = isBackground
            ? TerrainCellLayers.ResolveBackground(cellFgType, ccd.Properties)
            : cellFgType;

        // Силуэт переднего плана считается до выбора слоя: от него зависит,
        // нужна ли под ним подложка.
        //
        // Общие смещённые рёбра закрывают друг друга. Подложка нужна только
        // там, где Organic врезает открытый край внутрь собственной клетки;
        // внутри сплошного массива второй слой не рисуем.
        bool foregroundFillsCell = !foregroundVisuals.IsRound && !needsOrganicUnderlay;

        if (!TerrainCellLayers.TryGetType(
            cellFgType, backgroundType, isBackground, foregroundFillsCell, out CellType cellType))
        {
            quad.Clear();
            return TerrainQuadResult.NoAtlas(isDoor);
        }

        bool isSameCell = !isBackground || cellType == cellFgType;

        TerrainTypeSurface type = TerrainCellPacker.ResolveTypeSurface(
            cellType,
            GetRenderProperties(isSameCell, in ccd, cellType, sources.MetadataLookup),
            atlases);
        int atlasIndex = type.AtlasSlot;

        // Shared lattice offsets describe the deformable mass, not every
        // surface sharing its vertices. Roads/ground can occupy the foreground
        // layer too; a neighboring Cause must not warp their carrier or UVs.
        bool applyDistortion = !isBackground && distortion.EnableDistortion &&
            TerrainVertexDistortionCalculator.IsCause(ccd);
        Vector3 off00 = applyDistortion ? Node(sources, x, y) : Vector3.zero;
        Vector3 off10 = applyDistortion ? Node(sources, x + 1, y) : Vector3.zero;
        Vector3 off01 = applyDistortion ? Node(sources, x, y + 1) : Vector3.zero;
        Vector3 off11 = applyDistortion ? Node(sources, x + 1, y + 1) : Vector3.zero;

        TerrainCellGeometry geometry = TerrainCellGeometry.FromOffsets(
            off00,
            off10,
            off11,
            off01);
        bool anchored = geometry.IsAnchored || organicCause;

        // Backgrounds have their own adjacency. Reusing the
        // foreground descriptor (or zero when the types differ) assigns the
        // wrong autotile variant under exposed edges and organic underlays.
        int descriptor = isBackground
            ? TerrainBackgroundTileResolver.ResolveDescriptor(
                sources,
                x,
                y,
                type.HasTileGroup,
                type.TileGroupId)
            : TerrainCellMaskCalculator.CalculateTilingDescriptor(
                ccd, left, bottomLeft, bottom, bottomRight, right, topRight, top, topLeft);
        int cornerSideMask = TerrainCellMaskCalculator.CalculateCornerSideMask(ccd, left, right, top, bottom);
        bool useNeighborVariants =
            !isBackground &&
            foregroundVisuals.IsBuildingWall &&
            cornerSideMask != 0;
        bool tiling = type.HasTileGroup || useNeighborVariants;

        if (useNeighborVariants)
        {
            descriptor = ResolveBuildingWallVariant(descriptor, cornerSideMask);
        }

        bool hasRoundedPhysicalContour = !isBackground && type.ForegroundRoundable;

        // Маска соседства кладётся и фоновым квадам тоже.
        //
        // ЗАЧЕМ. Клетке пола нужно знать, что над ней твёрдый блок, — иначе
        // шейдеру нечем нарисовать падающую от блока тень, а без тени
        // выдавленность блока читается как обводка, а не как высота. Маска в
        // (x, y) описывает твёрдость соседей этой клетки в переднем плане, что
        // фону и требуется: бит 1 означает «сверху блок».
        //
        // ПОЧЕМУ ЭТО БЕЗОПАСНО. Единственный прежний потребитель битов 0-3 —
        // ветка скругления контура, а она включается флагом isRoundable,
        // который у фона всегда снят. Поле материалов трогает маску только под
        // тем же флагом. Так что до этой правки у фона стоял ноль не по
        // смыслу, а потому что читать его было некому.
        byte solidConnectivityMask = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(top, left, bottom, right);
        // Кайма рельефа — только у переднего плана: фон её не рисует, а
        // ring-адрес у фонового текселя тот же, и чужой код рельефа въехал бы
        // в соседний слой.
        TerrainCellMaskCalculator.CalculateReliefMasks(
            ccd, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight,
            out byte reliefMask,
            out byte reliefCornerMask);
        // Серверная группа определяет наличие фаски. Без проверки группа 0
        // получает reliefCode=1 и рисует фаску по всем четырём сторонам.
        bool hasRelief = !isBackground &&
            ccd.ReliefGroup != 0;
        // Масса для света — твёрдый (непроходимый) передний план.
        bool isPhysicalMass = !isBackground && type.Solid;
        TerrainLightingData lightingData = TerrainLightingData.Pack(
            solidConnectivityMask,
            type.IsGlowing,
            hasRoundedPhysicalContour,
            isPhysicalMass,
            type.EmissionPower,
            reliefMask,
            hasRelief,
            reliefCornerMask);
        float zOffset = isBackground ? 0.1f : 0.0f;
        float lx = x * cellSize;
        float ly = y * cellSize;
        quad[0].Position = new Vector3(lx, ly, zOffset) + off00;
        quad[1].Position = new Vector3(lx + cellSize, ly, zOffset) + off10;
        quad[2].Position = new Vector3(lx + cellSize, ly + cellSize, zOffset) + off11;
        quad[3].Position = new Vector3(lx, ly + cellSize, zOffset) + off01;

        var uvs = TerrainQuadUvs.Canonical;
        if (tiling && descriptor != 0)
        {
            uvs = uvs.Transform(descriptor);
        }

        quad[0].UV0 = uvs.C0;
        quad[1].UV0 = uvs.C1;
        quad[2].UV0 = uvs.C2;
        quad[3].UV0 = uvs.C3;

        // Текстуры нет — шейдер рисует клетку диагностическим шумом по
        // мировой клетке (SampleMissingTexture), а не тихой дырой в кадре.
        float animOffset = ResolveAnimationOffset(
            type.AnimationSettings, type.Animation, type.HasAtlasRect, gridX, serverY);

        Vector4 animDataVec = new(
            (float)type.Animation,
            type.AnimationSettings.Speed,
            animOffset,
            (float)type.AnimationSettings.Profile);
        Vector4 tileSizeVec = new Vector4(type.TileSize, type.TileSize, (float)type.FrameCount, type.FrameHeightTiles);
        // Признак сплошного листа — бит 5 в z, над колонкой тайлгруппы
        // (она занимает биты 0-4). В w его класть нельзя: там значение
        // больше 1.5 уже означает «отбросить», и Terrain.shader вместе с
        // TerrainCellBuilder выкидывали по нему всю породу и все кристаллы.
        int packedColumn = descriptor & 0x1F;
        if (type.ContinuousSheet)
        {
            packedColumn |= 32;
        }

        Vector4 worldPosVec = new Vector4(gridX, serverY, packedColumn, tiling ? 1f : 0f);

        // Read RGB directly from Color32 bytes — no intermediate Color allocation
        Color32 lightColor = type.LightColor;
        int packedLightingColor = lightColor.r |
            (lightColor.g << 8) |
            (lightColor.b << 16);

        float decalPlacement = TerrainDecalCatalog.Place(
            isBackground ? TerrainDecalCatalog.GroundRule : TerrainDecalCatalog.RuleOf(type.ForegroundDecal),
            gridX,
            serverY);
        Vector4 glowVec = new Vector4(
            packedLightingColor,
            lightingData.PackedFlags,
            lightingData.PackedContour,
            decalPlacement);

        // Surface data is identical at every corner. Quantize it once;
        // keep position, transformed UV0 and geometry anchors per vertex.
        TerrainVertex surface = default;
        surface.UV1 = type.AtlasRect;
        surface.UV2 = tileSizeVec;
        surface.UV3 = worldPosVec;
        surface.UV4 = animDataVec;
        surface.UV6 = glowVec;
        for (int i = 0; i < 4; i++)
        {
            ref TerrainVertex vertex = ref quad[i];
            vertex.CopySurfaceFrom(in surface);
            Vector2 anchor = geometry.GetCorner(i);
            vertex.UV5 = new Vector4(anchored ? 1f : 0f, anchor.x, anchor.y, organicEdges);
        }

        return new TerrainQuadResult(atlasIndex, isDoor);
    }

    private static Vector3 Node(in TerrainCellSources sources, int x, int y) =>
        TerrainVertexDistortionCalculator.ComputeNode(
            sources.CellCache, sources.Distortion, x, y, sources.WorldWidth, sources.WorldHeight).ToVector3();

    private static int OrganicEdgeBend(
        CachedCellData neighbor,
        int edgeX,
        int edgeY,
        bool vertical,
        int inwardSign)
    {
        if (neighbor.State != TerrainCellState.Loaded ||
            TerrainVertexDistortionCalculator.IsBlock(neighbor) ||
            (!TerrainVertexDistortionCalculator.IsCause(neighbor) && !IsOrganicEmptyEdge(neighbor)))
        {
            return 0;
        }

        int bend = TerrainVertexDistortionCalculator.ComputeOrganicEdgeBend(edgeX, edgeY, vertical);
        return TerrainVertexDistortionCalculator.IsCause(neighbor)
            ? bend
            : Math.Min(Math.Abs(bend), 1) * inwardSign;
    }

    private static bool IsOrganicEmptyEdge(CachedCellData neighbor) =>
        neighbor.State == TerrainCellState.Loaded &&
        neighbor.Type == CellType.Empty &&
        !TerrainVertexDistortionCalculator.IsBlock(neighbor) &&
        !TerrainVertexDistortionCalculator.IsCause(neighbor);

    /// <summary>
    /// Вариант стены здания по соседним углам.
    /// </summary>
    ///
    /// Стена выбирает колонку тайла по числу примыкающих углов, а отражения
    /// берёт из собственного дескриптора автотайлинга. Одиночный угол справа
    /// или снизу — это тот же тайл, отражённый по горизонтали; два угла по
    /// вертикали — повёрнутый.
    private static int ResolveBuildingWallVariant(int descriptor, int cornerSideMask)
    {
        bool hasLeft = (cornerSideMask & 1) != 0;
        bool hasRight = (cornerSideMask & 2) != 0;
        bool hasTop = (cornerSideMask & 4) != 0;
        bool hasBottom = (cornerSideMask & 8) != 0;
        int cornerCount =
            (hasLeft ? 1 : 0) +
            (hasRight ? 1 : 0) +
            (hasTop ? 1 : 0) +
            (hasBottom ? 1 : 0);
        int column = RenderingConstants.BUILDING_WALL_VARIANT_BASE_TILE +
            Math.Min(cornerCount, 2);
        byte transforms = (byte)(descriptor & 0xE0);

        if ((cornerCount == 1 && hasRight) ||
            (cornerCount == 1 && hasBottom))
        {
            transforms ^= 0x40;
        }

        if (cornerCount >= 2 && !hasLeft && !hasRight)
        {
            transforms ^= 0x80;
        }

        return transforms | (column & 0x1F);
    }

    /// <summary>
    /// Фаза анимации клетки: константа профиля или разброс по её координате.
    /// </summary>
    ///
    /// Разброс нужен, чтобы соседние клетки одного типа не мигали и не
    /// переливались в такт. Он детерминирован от мировой координаты, поэтому
    /// одна и та же клетка всегда получает одну и ту же фазу — при сдвиге
    /// окна и при пересборке она не перескакивает.
    ///
    /// Без текстуры разброса нет: клетка рисуется плоским цветом миникарты, и
    /// анимировать в ней нечего.
    private static float ResolveAnimationOffset(
        TerrainAnimationSettings animationSettings,
        CellAnimationType animType,
        bool hasAtlasRect,
        int gridX,
        int serverY)
    {
        if (!hasAtlasRect)
        {
            return animationSettings.PaletteIndex;
        }

        if (animationSettings.Profile == TerrainAnimationProfile.Default &&
            animType == CellAnimationType.Blinking)
        {
            uint seed = HashCell(gridX, serverY);
            return (seed % 6283) / 1000f;
        }

        if (animationSettings.Profile == TerrainAnimationProfile.FacetedCrystal)
        {
            return (HashCell(gridX, serverY) & 0xFFFF) / 65536f;
        }

        return animationSettings.PaletteIndex;
    }

    private static uint HashCell(int gridX, int serverY)
    {
        uint seed = (uint)((gridX * 374761397) + (serverY * 668265263));
        seed = (seed ^ (seed >> 13)) * 1274126177;
        return seed ^ (seed >> 16);
    }

    private static TerrainCellPacker.CellRenderProperties GetRenderProperties(
        bool isSameCell,
        in CachedCellData ccd,
        CellType cellType,
        ITerrainMetadataLookup metadataLookup)
    {
        if (isSameCell)
        {
            return new TerrainCellPacker.CellRenderProperties(
                ccd.AtlasRect,
                ccd.UVTileSize,
                ccd.Animation,
                ccd.AnimationSpeed,
                ccd.AnimationFrameCount,
                ccd.FrameHeightTiles,
                ccd.HasTileGroup,
                ccd.TileGroupId,
                ccd.Properties,
                ccd.MinimapColor,
                ccd.AtlasIndex,
                ccd.Distortion,
                ccd.ReliefGroup);
        }

        // Без фолбеков: промах означает, что прогрев не покрыл тип. Тихая
        // подстановка пустой метаданности нарисовала бы правдоподобную
        // подделку вместо того, чтобы показать дефект.
        if (!metadataLookup.TryGet(cellType, out CellMetadata meta))
        {
            throw new InvalidOperationException(
                $"Terrain metadata for cell type '{cellType}' was not warmed before the build.");
        }

        return TerrainCellPacker.FromMetadata(in meta);
    }

}
