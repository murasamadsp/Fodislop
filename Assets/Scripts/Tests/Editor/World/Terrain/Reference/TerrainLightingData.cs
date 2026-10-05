#nullable enable

namespace Kern.World.Terrain;

using System;

[Flags]
internal enum TerrainLightingFlags : byte
{
    None = 0,
    SolidTop = 1 << 0,
    SolidLeft = 1 << 1,
    SolidBottom = 1 << 2,
    SolidRight = 1 << 3,
    Emissive = 1 << 4,
    PhysicalMass = 1 << 5,
}

// Wire format written to TerrainVertex.UV6 and decoded by
// Assets/Shaders/Terrain/TerrainLightingData.hlsl.
internal readonly struct TerrainLightingData
{
    public const byte SolidBoundaryMask = 0x0F;

    private const float EmissionFractionScale = 0.25f;
    private const int ReliefCodeShift = 5;
    private const int ReliefCodeRange = 1 << ReliefCodeShift;

    // Бит 0 — roundable contour, биты 1-4 свободны,
    // биты 5-9 — код сторон рельефа, биты 10-13 — вогнутые углы.
    // Код сторон, а не маска:
    // ноль означает «клетка без рельефа, каймы нет», а маска рельефа
    // хранится как mask + 1. Иначе клетка без рельефа и клетка, у которой
    // все четыре соседа чужие, выглядели бы одинаково.
    public const int NoRelief = 0;
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

    public int SolidBoundary => (int)Flags & SolidBoundaryMask;

    public int ReliefCode => ((int)MathF.Round(PackedContour) >> ReliefCodeShift) & 0x1F;

    public int ReliefCornerMask => ((int)MathF.Round(PackedContour) >> 10) & SolidBoundaryMask;

    public bool IsEmissive => (Flags & TerrainLightingFlags.Emissive) != 0;

    public bool IsPhysicalMass => (Flags & TerrainLightingFlags.PhysicalMass) != 0;

    public bool ReceivesAmbientOcclusion => !IsPhysicalMass;

    public bool IsRoundable =>
        ((int)MathF.Round(PackedContour) & RoundableContourFlag) != 0;

    public float EmissionStrength => IsEmissive
        ? Math.Clamp((PackedFlags - MathF.Floor(PackedFlags)) / EmissionFractionScale, 0f, 1f)
        : 0f;

    // Дробная часть PackedFlags. Отдельной функцией, потому что строка типа
    // хранит её готовой, и шейдер складывает её с целыми флагами так же.
    public static float EmissionFraction(float emissionStrength) =>
        TerrainCellData.EmissionFraction(emissionStrength);

    public static TerrainLightingData Pack(
        byte solidConnectivityMask,
        bool isGlowing,
        bool hasRoundedPhysicalContour,
        bool isPhysicalMass,
        float emissionStrength,
        byte reliefMask,
        bool hasRelief,
        byte reliefCornerMask = 0)
    {
        var flags = (TerrainLightingFlags)(solidConnectivityMask & SolidBoundaryMask);
        if (isGlowing)
        {
            flags |= TerrainLightingFlags.Emissive;
        }

        if (isPhysicalMass)
        {
            flags |= TerrainLightingFlags.PhysicalMass;
        }

        int contourFlags = hasRoundedPhysicalContour ? RoundableContourFlag : 0;
        int reliefCode = hasRelief ? (reliefMask & SolidBoundaryMask) + 1 : NoRelief;
        int packedReliefCorners = hasRelief ? (reliefCornerMask & SolidBoundaryMask) << 10 : 0;
        return new TerrainLightingData(
            (byte)flags + EmissionFraction(emissionStrength),
            contourFlags + (reliefCode * ReliefCodeRange) + packedReliefCorners);
    }
}
