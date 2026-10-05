#nullable enable

using System;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Независимая расшифровка клетки, её восьми соседей и строк их типов по
// таблице битов из TerrainCellData.cs — так же, как её читает
// TerrainCellData.hlsl.
//
// Эталон — вершины того же квада, собранные TerrainQuadBuilder.FillQuad: до
// перехода на данные клетки шейдер получал ровно их. Совпадение проверяется
// по битам: half сравниваются как ushort, float — как uint. Исключение —
// углы геометрии: вершина несёт -0.0 там, где смещение получено как -0f, а
// узел в единицах 1/256 знака нуля не знает; для шейдера это одно число.
// Узлы оракул берёт из nodeAt (правило TerrainVertexDistortionCalculator):
// его HLSL-копию сверяет растровый шим на тех же мирах.
internal sealed class TerrainCellOracle(
    Func<int, int, TerrainCell> cellAt,
    Func<int, int, TerrainVertexOffset> nodeAt,
    Func<uint, TerrainTypeRow> typeOf,
    uint[] tileDescriptors,
    uint groundDecalRule,
    uint rockDecalRule,
    int worldHeight,
    bool organicEdges,
    uint horizontalSeed,
    uint verticalSeed)
{
    // L, BL, B, BR, R, TR, T, TL — порядок битов маски автотайла.
    private static readonly int[] s_dx = [-1, -1, 0, 1, 1, 1, 0, -1];
    private static readonly int[] s_dy = [0, -1, -1, -1, 0, 1, 1, 1];
    private const int Left = 0, BottomLeft = 1, Bottom = 2, BottomRight = 3, Right = 4, TopRight = 5, Top = 6, TopLeft = 7;

    public void AssertMatchesQuad(
        ReadOnlySpan<TerrainVertex> quad,
        TerrainQuadResult result,
        int gridX,
        int unityY,
        int layer,
        string where)
    {
        bool foreground = layer == 1;
        uint cell = cellAt(gridX, unityY).Bits;
        uint fgType = cell & 0xFFu;
        var nb = new uint[8];
        var nbType = new uint[8];
        for (int i = 0; i < 8; i++)
        {
            nb[i] = cellAt(gridX + s_dx[i], unityY + s_dy[i]).Bits;
            nbType[i] = nb[i] & 0xFFu;
        }

        uint fgFlags = typeOf(fgType).BZ;
        bool fgEmpty = (fgFlags & (1u << 22)) != 0;
        bool fgCause = (fgFlags & (1u << 20)) != 0;
        bool fgRoundable = (fgFlags & (1u << 19)) != 0;
        bool organicTerrain = organicEdges && fgCause;

        uint typeIndex = 0;
        if (fgType != 0)
        {
            if (foreground)
            {
                typeIndex = fgEmpty ? 0u : fgType;
            }
            else
            {
                uint bg = (cell >> 8) & 0xFFu;
                bool underlay = organicTerrain &&
                    (EmptyEdge(nbType[Bottom]) || EmptyEdge(nbType[Right]) || EmptyEdge(nbType[Top]) || EmptyEdge(nbType[Left]));
                bool fills = !fgRoundable && !underlay;
                typeIndex = fgEmpty || (bg != 0 && (bg != fgType || !fills)) ? bg : 0u;
            }
        }

        float[] cornersX = [0f, 1f, 1f, 0f];
        float[] cornersY = [0f, 0f, 1f, 1f];
        if (fgCause && fgType != 0)
        {
            (int Dx, int Dy)[] owners = [(0, 0), (1, 0), (1, 1), (0, 1)];
            for (int corner = 0; corner < 4; corner++)
            {
                TerrainVertexOffset node = nodeAt(gridX + owners[corner].Dx, unityY + owners[corner].Dy);
                cornersX[corner] += node.XSteps / TerrainVertexOffset.GridSize;
                cornersY[corner] += node.YSteps / TerrainVertexOffset.GridSize;
            }
        }

        bool fgAnchored = organicTerrain ||
            cornersX[0] != 0f || cornersX[1] != 1f || cornersX[2] != 1f || cornersX[3] != 0f ||
            cornersY[0] != 0f || cornersY[1] != 0f || cornersY[2] != 1f || cornersY[3] != 1f;

        if (typeIndex != 0 && !foreground)
        {
            bool occluded = !fgEmpty &&
                (fgFlags & ((1u << 23) | (1u << 19))) == 1u << 23 &&
                (typeOf(fgType).BW & (1u << 5)) != 0;
            if (occluded && fgAnchored)
            {
                for (int i = 0; i < 8; i++)
                {
                    occluded &= SolidMass(nbType[i]);
                }
            }

            typeIndex = occluded ? 0u : typeIndex;
        }

        TerrainTypeRow type = typeOf(typeIndex);
        int atlasIndex = typeIndex != 0 ? (int)(type.BX >> 24) : -1;
        Assert.That(atlasIndex, Is.EqualTo(result.AtlasIndex), $"{where}: слот атласа");
        if (typeIndex == 0)
        {
            return;
        }

        uint flags = type.BZ;
        uint near = type.BW;
        int serverY = worldHeight - 1 - unityY;

        var layerTypes = new uint[8];
        for (int i = 0; i < 8; i++)
        {
            layerTypes[i] = foreground ? nbType[i] : (nb[i] >> 8) & 0xFFu;
        }

        uint descriptor = 0;
        bool tiling = (near & (1u << 2)) != 0;
        if (tiling)
        {
            uint mask = 0;
            for (int i = 0; i < 8; i++)
            {
                uint other = typeOf(layerTypes[i]).BW;
                if ((other & (1u << 2)) != 0 && other >> 24 == near >> 24)
                {
                    mask |= 1u << i;
                }
            }

            descriptor = (tileDescriptors[mask >> 2] >> (int)((mask & 3u) * 8u)) & 0xFFu;
        }

        if (foreground && (near & (1u << 3)) != 0)
        {
            uint side = (Corner(nbType[Left]) ? 1u : 0u) | (Corner(nbType[Right]) ? 2u : 0u) |
                (Corner(nbType[Top]) ? 4u : 0u) | (Corner(nbType[Bottom]) ? 8u : 0u);
            if (side != 0)
            {
                descriptor = WallVariant(descriptor, side);
                tiling = true;
            }
        }

        uint solid = (Solid(nbType[Top]) ? 1u : 0u) | (Solid(nbType[Left]) ? 2u : 0u) |
            (Solid(nbType[Bottom]) ? 4u : 0u) | (Solid(nbType[Right]) ? 8u : 0u);
        uint lightingFlags = solid |
            (((flags >> 16) & 1u) << 4) |
            (foreground && ((flags >> 17) & 1u) != 0 ? 32u : 0u);
        uint contour = 0;
        if (foreground)
        {
            contour = ((flags >> 19) & 1u) != 0 ? 1u : 0u;
            if (((near >> 16) & 0xFFu) != 0)
            {
                bool t = Same(near, nbType[Top]), l = Same(near, nbType[Left]);
                bool b = Same(near, nbType[Bottom]), r = Same(near, nbType[Right]);
                uint relief = (t ? 1u : 0u) | (l ? 2u : 0u) | (b ? 4u : 0u) | (r ? 8u : 0u);
                uint corners = (b && l && !Same(near, nbType[BottomLeft]) ? 1u : 0u) |
                    (b && r && !Same(near, nbType[BottomRight]) ? 2u : 0u) |
                    (t && r && !Same(near, nbType[TopRight]) ? 4u : 0u) |
                    (t && l && !Same(near, nbType[TopLeft]) ? 8u : 0u);
                contour += ((relief + 1) * 32) + (corners << 10);
            }
        }

        float packedFlags = lightingFlags + BitConverter.Int32BitsToSingle(unchecked((int)type.BY));
        uint decalRule = !foreground ? groundDecalRule
            : (near & 3u) == 1 ? groundDecalRule
            : (near & 3u) == 2 ? rockDecalRule
            : 0u;

        uint organic = 0;
        if (foreground && organicTerrain)
        {
            int bottom = Bend(nbType[Bottom], gridX, unityY, false, 1);
            int right = Bend(nbType[Right], gridX + 1, unityY, true, -1);
            int top = Bend(nbType[Top], gridX, unityY + 1, false, -1);
            int left = Bend(nbType[Left], gridX, unityY, true, 1);
            organic = (uint)(1 + (bottom + 2) + ((right + 2) * 5) + ((top + 2) * 25) + ((left + 2) * 125));
        }

        bool anchored = foreground && fgAnchored;
        if (!foreground)
        {
            cornersX = [0f, 1f, 1f, 0f];
            cornersY = [0f, 0f, 1f, 1f];
        }

        uint uvBits = CornerUvBits(descriptor, tiling);
        float phase = Phase(flags, gridX, serverY);
        for (int corner = 0; corner < 4; corner++)
        {
            TerrainVertex v = quad[corner];
            string at = $"{where}, угол {corner}";

            Assert.That(v.UV0x, Is.EqualTo(TerrainVertex.H((uvBits >> (corner * 2)) & 1u)), $"{at}: u");
            Assert.That(v.UV0y, Is.EqualTo(TerrainVertex.H((uvBits >> ((corner * 2) + 1)) & 1u)), $"{at}: v");

            Assert.That(v.UV1x, Is.EqualTo((ushort)type.AX), $"{at}: атлас x");
            Assert.That(v.UV1y, Is.EqualTo((ushort)(type.AX >> 16)), $"{at}: атлас y");
            Assert.That(v.UV1z, Is.EqualTo((ushort)type.AY), $"{at}: атлас z");
            Assert.That(v.UV1w, Is.EqualTo((ushort)(type.AY >> 16)), $"{at}: атлас w");

            Assert.That(v.UV2x, Is.EqualTo((ushort)type.AZ), $"{at}: тайл x");
            Assert.That(v.UV2y, Is.EqualTo((ushort)type.AZ), $"{at}: тайл y");
            Assert.That(v.UV2z, Is.EqualTo((ushort)(type.AZ >> 16)), $"{at}: кадры");
            Assert.That(v.UV2w, Is.EqualTo((ushort)type.AW), $"{at}: высота кадра");

            AssertFloat(v.UV3.x, gridX, $"{at}: мировой x");
            AssertFloat(v.UV3.y, serverY, $"{at}: серверный y");
            AssertFloat(v.UV3.z, (descriptor & 31u) | (((near >> 8) & 1u) != 0 ? 32u : 0u), $"{at}: колонка");
            AssertFloat(v.UV3.w, tiling ? 1f : 0f, $"{at}: автотайл");

            Assert.That(v.UV4x, Is.EqualTo(TerrainVertex.H(flags & 0xFFu)), $"{at}: тип анимации");
            Assert.That(v.UV4y, Is.EqualTo((ushort)(type.AW >> 16)), $"{at}: скорость");
            Assert.That(v.UV4z, Is.EqualTo(TerrainVertex.H(phase)), $"{at}: фаза");
            Assert.That(v.UV4w, Is.EqualTo(TerrainVertex.H((flags >> 8) & 0xFFu)), $"{at}: профиль");

            Assert.That(v.UV5x, Is.EqualTo(TerrainVertex.H(anchored ? 1f : 0f)), $"{at}: якорь");
            Assert.That(Mathf.HalfToFloat(v.UV5y), Is.EqualTo(cornersX[corner]), $"{at}: угол x");
            Assert.That(Mathf.HalfToFloat(v.UV5z), Is.EqualTo(cornersY[corner]), $"{at}: угол y");
            Assert.That(v.UV5w, Is.EqualTo(TerrainVertex.H(organic)), $"{at}: органика");

            AssertFloat(v.UV6.x, type.BX & 0xFFFFFFu, $"{at}: цвет света");
            AssertFloat(v.UV6.y, packedFlags, $"{at}: флаги света");
            AssertFloat(v.UV6.z, contour, $"{at}: контур");
            AssertFloat(v.UV6.w, Decal(decalRule, gridX, serverY), $"{at}: декаль");
        }
    }

    private bool EmptyEdge(uint type) =>
        type != 0 && (typeOf(type).BZ & (1u << 22)) != 0 && (typeOf(type).BZ & ((1u << 20) | (1u << 21))) == 0;

    private bool SolidMass(uint type) =>
        type != 0 &&
        (typeOf(type).BZ & ((1u << 20) | (1u << 19))) == 1u << 20 &&
        (typeOf(type).BW & (1u << 6)) != 0;

    private bool Corner(uint type) => (typeOf(type).BW & (1u << 4)) != 0;

    private bool Solid(uint type) => (typeOf(type).BZ & (1u << 17)) != 0;

    private bool Same(uint own, uint otherType)
    {
        uint other = typeOf(otherType).BW;
        uint otherGroup = (other >> 16) & 0xFFu;
        if (otherGroup == 0)
        {
            return false;
        }

        return ((own >> 16) & 0xFFu) == otherGroup;
    }

    private static uint WallVariant(uint descriptor, uint side)
    {
        bool hasLeft = (side & 1) != 0, hasRight = (side & 2) != 0, hasTop = (side & 4) != 0, hasBottom = (side & 8) != 0;
        uint count = (hasLeft ? 1u : 0u) + (hasRight ? 1u : 0u) + (hasTop ? 1u : 0u) + (hasBottom ? 1u : 0u);
        uint transforms = descriptor & 0xE0u;
        if (count == 1 && (hasRight || hasBottom))
        {
            transforms ^= 0x40u;
        }

        if (count >= 2 && !hasLeft && !hasRight)
        {
            transforms ^= 0x80u;
        }

        return transforms | ((8u + Math.Min(count, 2u)) & 0x1Fu);
    }

    // Сосед по ребру — по типу его переднего плана: 0 — не загружен.
    private int Bend(uint type, int edgeX, int edgeY, bool vertical, int inwardSign)
    {
        if (type == 0)
        {
            return 0;
        }

        uint flags = typeOf(type).BZ;
        bool cause = (flags & (1u << 20)) != 0;
        bool block = (flags & (1u << 21)) != 0;
        bool emptyEdge = (flags & (1u << 22)) != 0 && !block && !cause;
        if (block || (!cause && !emptyEdge))
        {
            return 0;
        }

        uint hash = unchecked(((uint)edgeX * 0x9E3779B9u) ^ ((uint)edgeY * 0x85EBCA6Bu) ^ (vertical ? verticalSeed : horizontalSeed));
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7FEB352Du);
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x846CA68Bu);
        hash ^= hash >> 16;
        float noise = (hash & 0x00FFFFFFu) / 16777216f;
        int bend = Math.Min((int)(noise * 5f), 4) - 2;
        return cause ? bend : Math.Min(Math.Abs(bend), 1) * inwardSign;
    }

    private static float Phase(uint flags, int gridX, int serverY)
    {
        float palette = flags >> 24;
        if ((flags & (1u << 23)) == 0)
        {
            return palette;
        }

        uint seed = unchecked(((uint)gridX * 374761397u) + ((uint)serverY * 668265263u));
        seed = unchecked((seed ^ (seed >> 13)) * 1274126177u);
        seed ^= seed >> 16;
        uint animation = flags & 0xFFu;
        uint profile = (flags >> 8) & 0xFFu;
        if (profile == 0 && animation == 1)
        {
            return (seed % 6283) / 1000f;
        }

        return profile == 3 ? (seed & 0xFFFF) / 65536f : palette;
    }

    private static uint CornerUvBits(uint descriptor, bool tiling)
    {
        uint transforms = tiling ? descriptor : 0u;
        uint flipU = (transforms >> 6) & 1u;
        uint flipV = (transforms >> 5) & 1u;
        uint turn = (transforms >> 7) & 1u;
        uint bits = 0;
        for (uint corner = 0; corner < 4; corner++)
        {
            uint source = (corner + turn) & 3u;
            uint u = (source is 1 or 2 ? 1u : 0u) ^ flipU;
            uint v = (source >= 2 ? 1u : 0u) ^ flipV;
            bits |= (u | (v << 1)) << (int)(corner * 2);
        }

        return bits;
    }

    private static float Decal(uint rule, int worldX, int serverY)
    {
        uint percent = rule & 0x7Fu;
        if (percent == 0)
        {
            return 0f;
        }

        uint hash = unchecked((uint)worldX * 374761393u);
        hash = unchecked(hash + ((uint)serverY * 668265263u));
        hash ^= unchecked(((rule >> 7) & 0xFFu) * 2246822519u);
        hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
        hash ^= hash >> 16;
        if (hash % 100u >= percent)
        {
            return 0f;
        }

        uint packed = 1u + (hash % 16u) +
            (((hash >> 8) & 3u) << 4) +
            (((hash >> 10) & 1u) << 6) +
            (((hash >> 12) & 3u) << 7) +
            (((hash >> 14) & 3u) << 9);
        return (rule & 0x8000u) != 0 ? packed | 4096u : packed;
    }

    private static void AssertFloat(float actual, float expected, string what) =>
        Assert.That(
            BitConverter.SingleToInt32Bits(actual),
            Is.EqualTo(BitConverter.SingleToInt32Bits(expected)),
            $"{what}: {actual} ≠ {expected}");
}
