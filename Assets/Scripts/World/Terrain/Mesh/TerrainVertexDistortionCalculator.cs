#nullable enable

using Kern.Core;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public readonly record struct TerrainVertexOffset(float XSteps, float YSteps, float ZSteps)
{
    public const int GridSize = 32;

    public static TerrainVertexOffset Zero => new(0, 0, 0);

    public Vector3 ToVector3()
    {
        return new Vector3(
            XSteps / (float)GridSize,
            YSteps / (float)GridSize,
            ZSteps / (float)GridSize);
    }
}

/// <summary>Включено ли искажение сетки и каким стилем.</summary>
public sealed class TerrainDistortionSettings
{
    public bool EnableDistortion { get; set; } = true;

    public TerrainDistortionStyle DistortionStyle { get; set; } = TerrainDistortionStyle.Organic;

    public bool OrganicEdges => EnableDistortion && DistortionStyle == TerrainDistortionStyle.Organic;
}

// Смещение узла сетки — левого нижнего угла клетки — по четырём клеткам
// вокруг узла и его мировой координате.
//
// Узлы не хранятся: шейдер считает их тем же правилом из соседей в буфере
// клеток (TerrainCellData.hlsl, TerrainNode). Здесь — CPU-сторона того же
// правила для эталона тестов. Всё целыми числами, поэтому CPU и GPU дают
// один и тот же узел бит в бит.
public static class TerrainVertexDistortionCalculator
{
    // Совместимое имя для существующих проверок. Авторское значение живёт
    // вместе с остальными параметрами дисторшена в TerrainConfigHolder.
    public const int DistortionStrengthSteps = TerrainConfigHolder.ClassicDistortionStrengthSteps;

    // Единица узла — 1/8 шага (1/256 клетки): в ней узел точен на CPU и GPU.
    public const int UnitsPerStep = TerrainCellData.GeometryUnitsPerCell / TerrainVertexOffset.GridSize;

    // Шум органики: 11-битный хэш, интерполяция в целых, масштаб октавы
    // HashMax << 1. Период ≤ 10 клеток держит промежуточные значения в 32 битах.
    public const int OrganicHashBits = 11;
    public const uint OrganicHashMax = (1u << OrganicHashBits) - 1u;
    public const uint OrganicOctaveScale = OrganicHashMax << 1;

    private const int OrganicFreeJitterCenterUnits = TerrainConfigHolder.OrganicMaximumOffsetSteps * UnitsPerStep / 2;

    // Центрируется по общему диапазону классического хэша.
    private const int FreeJitterCenterUnits =
        ((TerrainConfigHolder.ClassicJitterRange - 1) / 2) * DistortionStrengthSteps * UnitsPerStep;

    static TerrainVertexDistortionCalculator()
    {
        if (TerrainConfigHolder.OrganicNoiseBroadPeriodCells > 10 ||
            TerrainConfigHolder.OrganicNoiseMediumPeriodCells > 10 ||
            TerrainConfigHolder.OrganicNoiseFinePeriodCells > 10 ||
            TerrainConfigHolder.OrganicNoiseBroadWeightPercent +
                TerrainConfigHolder.OrganicNoiseMediumWeightPercent +
                TerrainConfigHolder.OrganicNoiseFineWeightPercent != 100)
        {
            throw new System.InvalidOperationException(
                "Organic noise periods must be at most 10 cells and weights must sum to 100%.");
        }
    }

    /// <summary>Узел (x, y) окна: углы клеток кэша x..x+1, y..y+1.</summary>
    public static TerrainVertexOffset ComputeNode(
        ITerrainCellDataSource cellCache,
        TerrainDistortionSettings settings,
        int x,
        int y,
        int worldWidth,
        int worldHeight)
    {
        if (!settings.EnableDistortion)
        {
            return TerrainVertexOffset.Zero;
        }

        int cx = x + 1;
        int cy = y + 1;
        CachedCellData tl = cellCache.GetCellData(x, cy);
        CachedCellData tr = cellCache.GetCellData(cx, cy);
        CachedCellData bl = cellCache.GetCellData(x, y);
        CachedCellData br = cellCache.GetCellData(cx, y);
        int worldX = cellCache.CacheMinX + x;
        int worldY = cellCache.CacheMinY + y;
        return settings.DistortionStyle == TerrainDistortionStyle.Organic
            ? ComputeOrganicOffset(tl, tr, bl, br, worldX, worldY, worldWidth, worldHeight)
            : ComputeOffset(tl, tr, bl, br, worldX, worldY, worldWidth, worldHeight);
    }

    public static TerrainVertexOffset ComputeOffset(
        CachedCellData tl,
        CachedCellData tr,
        CachedCellData bl,
        CachedCellData br,
        int worldX,
        int worldY,
        int worldWidth = int.MaxValue,
        int worldHeight = int.MaxValue)
    {
        if (worldX <= 0 || worldX >= worldWidth || worldY <= 0 || worldY >= worldHeight)
        {
            return TerrainVertexOffset.Zero;
        }

        int rx = RandXd(worldX, worldY) * DistortionStrengthSteps * UnitsPerStep;
        int ry = RandYd(worldX, worldY) * DistortionStrengthSteps * UnitsPerStep;
        return ComputeOffsetFromJitter(tl, tr, bl, br, worldY, rx, ry, FreeJitterCenterUnits);
    }

    public static TerrainVertexOffset ComputeOrganicOffset(
        CachedCellData tl,
        CachedCellData tr,
        CachedCellData bl,
        CachedCellData br,
        int worldX,
        int worldY,
        int worldWidth = int.MaxValue,
        int worldHeight = int.MaxValue)
    {
        if (worldX <= 0 || worldX >= worldWidth || worldY <= 0 || worldY >= worldHeight)
        {
            return TerrainVertexOffset.Zero;
        }

        // Узел на внешней границе массива остаётся в своей клеточной сетке.
        // Иначе смещение угла увеличивает силуэт даже при врезанных рёбрах.
        if (!IsWavy(tl) || !IsWavy(tr) || !IsWavy(bl) || !IsWavy(br))
        {
            return TerrainVertexOffset.Zero;
        }

        // Плавный шум задаёт крупную форму; дополнительные точки на рёбрах
        // задаются отдельно и не превращают весь край в прямую линию.
        int rx = OrganicOffsetUnits(
            worldX,
            worldY,
            TerrainConfigHolder.OrganicNoiseBroadXSeed,
            TerrainConfigHolder.OrganicNoiseMediumXSeed,
            TerrainConfigHolder.OrganicNoiseFineXSeed);
        int ry = OrganicOffsetUnits(
            worldX,
            worldY,
            TerrainConfigHolder.OrganicNoiseBroadYSeed,
            TerrainConfigHolder.OrganicNoiseMediumYSeed,
            TerrainConfigHolder.OrganicNoiseFineYSeed);
        return ComputeOffsetFromJitter(tl, tr, bl, br, worldY, rx, ry, OrganicFreeJitterCenterUnits);
    }

    // Три октавы с весами в процентах; контраст и центр — в той же шкале.
    // Результат — смещение 0..OrganicMaximumOffsetSteps в единицах узла,
    // округлённое до ближайшей.
    private static int OrganicOffsetUnits(int worldX, int worldY, uint broadSeed, uint mediumSeed, uint fineSeed)
    {
        long noise =
            (TerrainConfigHolder.OrganicNoiseBroadWeightPercent *
                (long)ValueNoise(worldX, worldY, TerrainConfigHolder.OrganicNoiseBroadPeriodCells, broadSeed)) +
            (TerrainConfigHolder.OrganicNoiseMediumWeightPercent *
                (long)ValueNoise(worldX, worldY, TerrainConfigHolder.OrganicNoiseMediumPeriodCells, mediumSeed)) +
            (TerrainConfigHolder.OrganicNoiseFineWeightPercent *
                (long)ValueNoise(worldX, worldY, TerrainConfigHolder.OrganicNoiseFinePeriodCells, fineSeed));
        long full = 100L * OrganicOctaveScale;
        long contrasted = (TerrainConfigHolder.OrganicNoiseContrast * noise) -
            (TerrainConfigHolder.OrganicNoiseCenterPercent * (long)OrganicOctaveScale);
        long clamped = System.Math.Clamp(contrasted, 0L, full);
        long maximumUnits = TerrainConfigHolder.OrganicMaximumOffsetSteps * UnitsPerStep;
        return (int)(((clamped * maximumUnits) + (full / 2)) / full);
    }

    // Билинейный шум по узлам решётки периода p со сглаживанием t²(3 − 2t),
    // в масштабе OrganicOctaveScale. Дроби точные: S = r²(3p − 2r) над p³.
    private static uint ValueNoise(int worldX, int worldY, int period, uint seed)
    {
        int x0 = worldX / period;
        int y0 = worldY / period;
        uint rx = (uint)(worldX % period);
        uint ry = (uint)(worldY % period);
        uint p = (uint)period;
        uint d = p * p * p;
        uint sx = rx * rx * ((3u * p) - (2u * rx));
        uint sy = ry * ry * ((3u * p) - (2u * ry));
        uint h00 = Hash(x0, y0, seed) >> (24 - OrganicHashBits);
        uint h10 = Hash(x0 + 1, y0, seed) >> (24 - OrganicHashBits);
        uint h01 = Hash(x0, y0 + 1, seed) >> (24 - OrganicHashBits);
        uint h11 = Hash(x0 + 1, y0 + 1, seed) >> (24 - OrganicHashBits);
        uint bottom = unchecked((h00 * d) + ((h10 - h00) * sx));
        uint top = unchecked((h01 * d) + ((h11 - h01) * sx));
        uint value = unchecked((bottom * d) + ((top - bottom) * sy));
        return unchecked((value << 1) + ((d * d) / 2u)) / (d * d);
    }

    // 24-битный хэш решётки шума и органических рёбер.
    public static uint Hash(int x, int y, uint seed)
    {
        uint hash = unchecked(((uint)x * 0x9E3779B9u) ^ ((uint)y * 0x85EBCA6Bu) ^ seed);
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7FEB352Du);
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x846CA68Bu);
        hash ^= hash >> 16;
        return hash & 0x00FFFFFFu;
    }

    // Одинаковый ключ у двух клеток по обе стороны ребра: изгиб −2..2.
    public static int ComputeOrganicEdgeBend(int worldX, int unityY, bool vertical)
    {
        uint hash = Hash(
            worldX, unityY,
            vertical ? TerrainConfigHolder.OrganicEdgeVerticalSeed :
                TerrainConfigHolder.OrganicEdgeHorizontalSeed);
        return (int)System.Math.Min((hash * 5u) >> 24, 4u) - 2;
    }

    public static bool IsWavy(CachedCellData data)
    {
        return data.Outline == CellOutline.Wavy;
    }

    // Держит узлы всё, кроме гибкого и волнистого контура.
    public static bool Holds(CachedCellData data)
    {
        return data.Outline is not (CellOutline.Pliant or CellOutline.Wavy);
    }

    public static int RandXd(int x, int y)
    {
        int num = unchecked(((TerrainConfigHolder.ClassicXHashA * x) +
            (TerrainConfigHolder.ClassicXHashB * y)) *
            ((TerrainConfigHolder.ClassicXHashC * x) +
            (TerrainConfigHolder.ClassicXHashD * y))) %
            TerrainConfigHolder.ClassicXHashModulus;
        return (num * num) % TerrainConfigHolder.ClassicJitterRange;
    }

    public static int RandYd(int x, int y)
    {
        int num = unchecked(((TerrainConfigHolder.ClassicYHashA * x) +
            (TerrainConfigHolder.ClassicYHashB * y)) *
            ((TerrainConfigHolder.ClassicYHashC * x) +
            (TerrainConfigHolder.ClassicYHashD * y))) %
            TerrainConfigHolder.ClassicYHashModulus;
        return (num * num) % TerrainConfigHolder.ClassicJitterRange;
    }

    // rx, ry и центр — в единицах узла.
    private static TerrainVertexOffset ComputeOffsetFromJitter(
        CachedCellData tl,
        CachedCellData tr,
        CachedCellData bl,
        CachedCellData br,
        int worldY,
        int rx,
        int ry,
        int freeJitterCenter)
    {
        // Внутри массы узел колышется свободно в обе стороны:
        // здесь нет внешней стороны, к которой нужно привязывать знак.
        if (IsWavy(tl) && IsWavy(tr) && IsWavy(bl) && IsWavy(br))
        {
            return Units(rx - freeJitterCenter, -(ry - freeJitterCenter));
        }

        if (Holds(tl) || Holds(tr) || Holds(bl) || Holds(br))
        {
            return TerrainVertexOffset.Zero;
        }

        if (worldY == 0 || (IsWavy(tl) && IsWavy(br)) || (IsWavy(tr) && IsWavy(bl)))
        {
            return TerrainVertexOffset.Zero;
        }

        if (IsWavy(tl) && IsWavy(tr))
        {
            return Units(0, -ry);
        }

        if (IsWavy(tl) && IsWavy(bl))
        {
            return Units(-rx, 0);
        }

        if (IsWavy(tr) && IsWavy(br))
        {
            return Units(rx, 0);
        }

        if (IsWavy(bl) && IsWavy(br))
        {
            return Units(0, ry);
        }

        if (IsWavy(tl))
        {
            return Units(-rx, -ry);
        }

        if (IsWavy(tr))
        {
            return Units(rx, -ry);
        }

        if (IsWavy(bl))
        {
            return Units(-rx, ry);
        }

        if (IsWavy(br))
        {
            return Units(rx, ry);
        }

        return TerrainVertexOffset.Zero;
    }

    private static TerrainVertexOffset Units(int x, int y) =>
        new(x / (float)UnitsPerStep, y / (float)UnitsPerStep, 0f);
}
