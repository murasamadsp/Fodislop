#nullable enable

using System;
using System.Runtime.InteropServices;
using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

// ═══ Данные клетки террейна на GPU ════════════════════════════════════════
//
// Вся раскладка живёт здесь и в Assets/Shaders/Terrain/TerrainCellData.hlsl.
// Больше нигде не решается, какой бит что значит.
//
// КЛЕТКА — один ushort, 2 байта; на GPU две клетки в uint (младшая — клетка
// с чётным индексом кольца):
//
//    0- 7  тип переднего плана; 0 (CellType.Unloaded) — клетка не загружена,
//          у неё нет ни одного слоя
//    8-15  тип фона до решения слоя: сам пол, дорога под проходимой частью
//          пака или земля (TerrainCellLayers.ResolveBackground)
//
// Всё остальное шейдер выводит из клетки, её восьми соседей и их строк
// типов — тем же правилом, что прежняя CPU-сборка квада (эталон в тестах,
// Tests/Editor/World/Terrain/Reference/TerrainQuadBuilder.cs):
//   - узлы сетки — четыре угла клетки — по типам четырёх клеток вокруг узла
//     и его мировой координате (TerrainVertexDistortionCalculator), в
//     единицах 1/256 клетки;
//   - рисуется ли фон (тот же тип под клеткой, закрывающей её целиком, не
//     рисуется) и закрыт ли он передним планом полностью;
//   - дескриптор автотайла обоих слоёв, вариант стены пака по углам;
//   - маска твёрдых соседей, рельеф и его вогнутые углы;
//   - органические рёбра, фаза анимации, декаль, мировая клетка.
// Кольцо буфера на клетку шире окна с каждой стороны: в кайме лежит тип
// переднего плана, фона там нет (автотайл фона за окном не ищет).
//
// ТИП КЛЕТКИ — строка таблицы на 256 типов, два uint4, 32 байта. Строка 0
// всегда нулевая: так незагруженный сосед ничего не значит.
//
//   a.x  прямоугольник атласа x, y       half, half
//   a.y  прямоугольник атласа z, w       half, half
//   a.z  размер тайла, число кадров      half, half
//   a.w  высота кадра, скорость анимации half, half
//   b.x  цвет света RGB 0-23, слот атласа 24-31
//   b.y  доля свечения, float
//   b.z  тип анимации 0-7, профиль 8-15, флаги 16-23: светится 16, твёрдый
//        17 (непроходим: отбрасывает тень и задерживает свет), 18 свободен,
//        скругление 19, искажает (Cause) 20, не искажается (Block) 21,
//        пустота (Empty) 22, есть прямоугольник атласа 23; палитра 24-31
//   b.w  семья декали переднего плана 0-1, есть тайлгруппа 2, стена пака 3,
//        угол пака 4, непрозрачен в своём атласе 5, непрозрачен хоть в
//        одном атласе 6; текстура полотном 8; группа каймы 16-23;
//        тайлгруппа 24-31
//
// АВТОТАЙЛ — таблица TileBitmaskConverter (маска восьми соседей →
// дескриптор), 256 байт по четыре в uint; шейдер читает её, а не свою копию.
//
// ДЕКАЛИ — два глобальных правила (земля, камень): процент 0-6, зерно хэша
// 7-14, атлас камня 15. Фон всегда получает землю.
//
// ИСКАЖЕНИЕ — восемь глобальных float4 из TerrainConfigHolder (PackDistortion),
// все значения — целые меньше 2^24, во float точные:
//   v0  классика: шаг в единицах узла, диапазон джиттера, центр, —
//   v1  хэш x: A, B, C, D        v2  хэш y: A, B, C, D
//   v3  модули x, y; органика: наибольшее смещение и центр в единицах узла
//   v4  периоды: крупный, средний, мелкий; контраст
//   v5  веса в процентах: крупный, средний, мелкий; центр в процентах
//   v6  зёрна x: крупное, среднее, мелкое; биты хэша
//   v7  зёрна y: крупное, среднее, мелкое; —

/// <summary>Клетка на GPU: тип переднего плана и тип фона.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct TerrainCell(ushort Bits);

/// <summary>Строка таблицы типов на GPU.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct TerrainTypeRow(
    uint AX,
    uint AY,
    uint AZ,
    uint AW,
    uint BX,
    uint BY,
    uint BZ,
    uint BW);

/// <summary>Всё, что у типа одинаково во всех клетках, в развёрнутом виде.</summary>
///
/// Одно значение кормит строку таблицы и эталон вида в тестах: разойтись им
/// негде, потому что считаются одной функцией
/// (TerrainCellPacker.ResolveTypeSurface).
internal readonly record struct TerrainTypeSurface(
    int AtlasSlot,
    Vector4 AtlasRect,
    float TileSize,
    int FrameCount,
    float FrameHeightTiles,
    CellAnimationType Animation,
    TerrainAnimationSettings AnimationSettings,
    bool HasTileGroup,
    int TileGroupId,
    bool ContinuousSheet,
    byte ReliefGroup,
    Color32 LightColor,
    bool IsGlowing,
    float EmissionPower,
    bool Solid,
    bool ForegroundRoundable,
    TerrainDecalFamily ForegroundDecal,
    bool IsBuildingWall,
    bool IsBuildingCorner,
    bool OpaqueInOwnAtlas,
    bool OpaqueInAnyAtlas,
    CellDistortionType Distortion = CellDistortionType.Neutral,
    bool IsEmpty = false)
{
    public bool HasAtlasRect => AtlasRect.z >= 0.0001f;

}

public static class TerrainCellData
{
    // Квадов на клетку: фон и передний план (меш идентификаторов).
    public const int LayersPerCell = 2;
    public const int BackgroundLayer = 0;
    public const int ForegroundLayer = 1;
    public const int TypeCount = 256;

    // Кайма кольца вокруг окна: соседи краевых клеток.
    public const int RingMargin = 1;

    // Единица смещения узла: 1/256 клетки. Узел считается в ней целыми
    // числами на CPU и GPU одинаково (TerrainVertexDistortionCalculator).
    public const int GeometryUnitsPerCell = 256;

    public const int DistortionVectorCount = 8;

    private const int BackgroundTypeShift = 8;

    // b.z
    private const int ProfileShift = 8;
    private const uint GlowingFlag = 1u << 16;
    private const uint SolidFlag = 1u << 17;
    private const uint RoundableFlag = 1u << 19;
    private const uint CauseFlag = 1u << 20;
    private const uint BlockFlag = 1u << 21;
    private const uint EmptyFlag = 1u << 22;
    private const uint AtlasRectFlag = 1u << 23;
    private const int PaletteShift = 24;

    // b.w
    private const uint TileGroupFlag = 1u << 2;
    private const uint BuildingWallFlag = 1u << 3;
    private const uint BuildingCornerFlag = 1u << 4;
    private const uint OpaqueOwnFlag = 1u << 5;
    private const uint OpaqueAnyFlag = 1u << 6;
    private const uint ContinuousSheetFlag = 1u << 8;
    private const int ReliefGroupShift = 16;
    private const int TileGroupShift = 24;

    // Правило декали
    private const int DecalSeedShift = 7;
    private const uint DecalRockFlag = 1u << 15;

    public static TerrainCell PackCell(CellType foregroundType, CellType backgroundType) =>
        new((ushort)((byte)foregroundType | ((byte)backgroundType << BackgroundTypeShift)));

    // Клетка каймы кольца: её читают только соседи — тип переднего плана.
    public static TerrainCell PackMargin(CellType foregroundType) =>
        PackCell(foregroundType, CellType.Unloaded);

    public static CellType ForegroundTypeOf(TerrainCell cell) => (CellType)(byte)cell.Bits;

    public static CellType BackgroundTypeOf(TerrainCell cell) => (CellType)(byte)(cell.Bits >> BackgroundTypeShift);

    internal static TerrainTypeRow PackType(in TerrainTypeSurface surface)
    {
        float palette = surface.AnimationSettings.PaletteIndex;
        if ((uint)surface.AtlasSlot > byte.MaxValue ||
            (uint)surface.TileGroupId > byte.MaxValue ||
            palette < 0f || palette > byte.MaxValue || palette != MathF.Floor(palette))
        {
            throw new ArgumentOutOfRangeException(nameof(surface));
        }

        Vector4 rect = surface.AtlasRect;
        Color32 light = surface.LightColor;
        uint flags =
            (surface.IsGlowing ? GlowingFlag : 0u) |
            (surface.Solid ? SolidFlag : 0u) |
            (surface.ForegroundRoundable ? RoundableFlag : 0u) |
            (surface.Distortion == CellDistortionType.Cause ? CauseFlag : 0u) |
            (surface.Distortion == CellDistortionType.Block ? BlockFlag : 0u) |
            (surface.IsEmpty ? EmptyFlag : 0u) |
            (surface.HasAtlasRect ? AtlasRectFlag : 0u);
        uint neighbourhood =
            (uint)surface.ForegroundDecal |
            (surface.HasTileGroup ? TileGroupFlag : 0u) |
            (surface.IsBuildingWall ? BuildingWallFlag : 0u) |
            (surface.IsBuildingCorner ? BuildingCornerFlag : 0u) |
            (surface.OpaqueInOwnAtlas ? OpaqueOwnFlag : 0u) |
            (surface.OpaqueInAnyAtlas ? OpaqueAnyFlag : 0u) |
            (surface.ContinuousSheet ? ContinuousSheetFlag : 0u) |
            ((uint)surface.ReliefGroup << ReliefGroupShift) |
            ((uint)(surface.HasTileGroup ? surface.TileGroupId : 0) << TileGroupShift);
        return new TerrainTypeRow(
            Halves(rect.x, rect.y),
            Halves(rect.z, rect.w),
            Halves(surface.TileSize, surface.FrameCount),
            Halves(surface.FrameHeightTiles, surface.AnimationSettings.Speed),
            light.r | ((uint)light.g << 8) | ((uint)light.b << 16) | ((uint)surface.AtlasSlot << 24),
            unchecked((uint)BitConverter.SingleToInt32Bits(EmissionFraction(surface.EmissionPower))),
            (byte)surface.Animation |
                ((uint)(byte)surface.AnimationSettings.Profile << ProfileShift) |
                flags |
                ((uint)palette << PaletteShift),
            neighbourhood);
    }

    public const int TileDescriptorWords = 64;

    public static uint[] PackTileDescriptors()
    {
        var words = new uint[TileDescriptorWords];
        for (int mask = 0; mask < 256; mask++)
        {
            words[mask >> 2] |= (uint)TileBitmaskConverter.GetDescriptor((byte)mask) << ((mask & 3) * 8);
        }

        return words;
    }

    // Режим искажения для шейдера: 0 — выключено, 1 — классика, 2 — органика.
    public static int DistortionMode(TerrainDistortionSettings settings) =>
        !settings.EnableDistortion ? 0 :
        settings.DistortionStyle == TerrainDistortionStyle.Organic ? 2 : 1;

    public static Vector4[] PackDistortion()
    {
        const int units = TerrainVertexDistortionCalculator.UnitsPerStep;
        const int classicStep = TerrainConfigHolder.ClassicDistortionStrengthSteps * units;
        const int organicMaximum = TerrainConfigHolder.OrganicMaximumOffsetSteps * units;
        return
        [
            new(classicStep, TerrainConfigHolder.ClassicJitterRange,
                ((TerrainConfigHolder.ClassicJitterRange - 1) / 2) * classicStep, 0f),
            new(TerrainConfigHolder.ClassicXHashA, TerrainConfigHolder.ClassicXHashB,
                TerrainConfigHolder.ClassicXHashC, TerrainConfigHolder.ClassicXHashD),
            new(TerrainConfigHolder.ClassicYHashA, TerrainConfigHolder.ClassicYHashB,
                TerrainConfigHolder.ClassicYHashC, TerrainConfigHolder.ClassicYHashD),
            new(TerrainConfigHolder.ClassicXHashModulus, TerrainConfigHolder.ClassicYHashModulus,
                organicMaximum, organicMaximum / 2),
            new(TerrainConfigHolder.OrganicNoiseBroadPeriodCells, TerrainConfigHolder.OrganicNoiseMediumPeriodCells,
                TerrainConfigHolder.OrganicNoiseFinePeriodCells, TerrainConfigHolder.OrganicNoiseContrast),
            new(TerrainConfigHolder.OrganicNoiseBroadWeightPercent, TerrainConfigHolder.OrganicNoiseMediumWeightPercent,
                TerrainConfigHolder.OrganicNoiseFineWeightPercent, TerrainConfigHolder.OrganicNoiseCenterPercent),
            new(TerrainConfigHolder.OrganicNoiseBroadXSeed, TerrainConfigHolder.OrganicNoiseMediumXSeed,
                TerrainConfigHolder.OrganicNoiseFineXSeed, TerrainVertexDistortionCalculator.OrganicHashBits),
            new(TerrainConfigHolder.OrganicNoiseBroadYSeed, TerrainConfigHolder.OrganicNoiseMediumYSeed,
                TerrainConfigHolder.OrganicNoiseFineYSeed, 0f),
        ];
    }

    public static uint PackDecal(TerrainDecalRule rule)
    {
        if (rule.Percent > 100u || rule.Seed > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rule));
        }

        return rule.Percent |
            (rule.Seed << DecalSeedShift) |
            (rule.RockAtlas ? DecalRockFlag : 0u);
    }

    // Доля свечения — дробная часть флагов света: шейдер складывает её с
    // целыми флагами (раскладка TerrainLightingData.hlsl).
    internal static float EmissionFraction(float emissionStrength) =>
        emissionStrength * EmissionFractionScale;

    private const float EmissionFractionScale = 0.25f;

    private static uint Halves(float low, float high) =>
        Half(low) | ((uint)Half(high) << 16);

    // float → half обрезкой мантиссы; слишком малое — ±0, слишком большое — ±Inf.
    internal static ushort Half(float value)
    {
        int bits = BitConverter.SingleToInt32Bits(value);
        int sign = (bits >> 16) & 0x8000;
        int exponent = ((bits >> 23) & 0xFF) - 127 + 15;
        if (exponent <= 0)
        {
            return (ushort)sign;
        }

        if (exponent >= 31)
        {
            return (ushort)(sign | 0x7C00);
        }

        return (ushort)(sign | (exponent << 10) | ((bits & 0x7FFFFF) >> 13));
    }
}
