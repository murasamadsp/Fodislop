#nullable enable

namespace Kern.World.Terrain;

using System;

[Flags]
internal enum TerrainLightingFlags : byte
{
    None = 0,
    ForegroundTop = 1 << 0,
    ForegroundLeft = 1 << 1,
    ForegroundBottom = 1 << 2,
    ForegroundRight = 1 << 3,
    Glow = 1 << 4,
    PhysicalMass = 1 << 5,
}

// Wire format written to TerrainVertex.UV6 and decoded by
// Assets/Shaders/Terrain/TerrainLightingData.hlsl.
internal readonly struct TerrainLightingData
{
    public const byte ForegroundSidesMask = 0x0F;

    private const float GlowFractionScale = 0.25f;
    private const int RimCodeShift = 5;
    private const int RimCodeRange = 1 << RimCodeShift;

    // Бит 0 — roundable contour, биты 1-4 свободны,
    // биты 5-9 — код сторон каймы, биты 10-13 — вогнутые углы.
    // Код сторон, а не маска:
    // ноль означает «клетка без каймы, каймы нет», а маска каймы
    // хранится как mask + 1. Иначе клетка без каймы и клетка, у которой
    // все четыре соседа чужие, выглядели бы одинаково.
    public const int NoRim = 0;
    private const int RoundableContourFlag = 1 << 0;

    public TerrainLightingData(float packedFlags, float packedContour)
    {
        PackedFlags = packedFlags;
        PackedContour = packedContour;
    }

    public float PackedFlags { get; }

    public float PackedContour { get; }

    public TerrainLightingFlags Flags =>
        (TerrainLightingFlags)(byte)MathF.Floor(PackedFlags + 0.0001f);

    public int ForegroundSides => (int)Flags & ForegroundSidesMask;

    public int RimCode => ((int)MathF.Round(PackedContour) >> RimCodeShift) & 0x1F;

    public int RimCornerMask => ((int)MathF.Round(PackedContour) >> 10) & ForegroundSidesMask;

    public bool Glows => (Flags & TerrainLightingFlags.Glow) != 0;

    public bool IsPhysicalMass => (Flags & TerrainLightingFlags.PhysicalMass) != 0;

    public bool ReceivesAmbientOcclusion => !IsPhysicalMass;

    public bool IsRoundable =>
        ((int)MathF.Round(PackedContour) & RoundableContourFlag) != 0;

    public float Glow => Glows
        ? Math.Clamp((PackedFlags - MathF.Floor(PackedFlags)) / GlowFractionScale, 0f, 1f)
        : 0f;

    // Дробная часть PackedFlags. Отдельной функцией, потому что строка типа
    // хранит её готовой, и шейдер складывает её с целыми флагами так же.
    public static float GlowFraction(float glow) =>
        TerrainCellData.GlowFraction(glow);

    public static TerrainLightingData Pack(
        byte foregroundSides,
        bool glows,
        bool hasRoundedPhysicalContour,
        bool isPhysicalMass,
        float glow,
        byte rimMask,
        bool hasRim,
        byte rimCornerMask = 0)
    {
        var flags = (TerrainLightingFlags)(foregroundSides & ForegroundSidesMask);
        if (glows)
        {
            flags |= TerrainLightingFlags.Glow;
        }

        if (isPhysicalMass)
        {
            flags |= TerrainLightingFlags.PhysicalMass;
        }

        int contourFlags = hasRoundedPhysicalContour ? RoundableContourFlag : 0;
        int rimCode = hasRim ? (rimMask & ForegroundSidesMask) + 1 : NoRim;
        int packedRimCorners = hasRim ? (rimCornerMask & ForegroundSidesMask) << 10 : 0;
        return new TerrainLightingData(
            (byte)flags + GlowFraction(glow),
            contourFlags + (rimCode * RimCodeRange) + packedRimCorners);
    }
}
