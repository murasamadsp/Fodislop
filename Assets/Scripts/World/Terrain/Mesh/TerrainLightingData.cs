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
    RoundedPhysicalContour = 1 << 5,
    PhysicalMass = 1 << 6,
}

// Wire format written to TerrainVertex.UV6 and decoded by
// Assets/Shaders/TerrainLightingData.hlsl.
internal readonly record struct TerrainLightingData(
    float PackedFlags,
    float PackedContour)
{
    public const byte SolidBoundaryMask = 0x0F;

    private const float EmissionFractionScale = 0.25f;
    private const byte SolidDiagonalShift = 4;
    private const float ContourFlagsRange = 4f;
    private const int GlowingContourFlag = 1 << 0;
    private const int RoundableContourFlag = 1 << 1;

    public TerrainLightingFlags Flags =>
        (TerrainLightingFlags)(byte)MathF.Floor(PackedFlags + 0.0001f);

    public int SolidBoundary => (int)Flags & SolidBoundaryMask;

    public int SolidDiagonal => (int)MathF.Round(PackedContour) >> 2 & SolidBoundaryMask;

    public bool IsEmissive => (Flags & TerrainLightingFlags.Emissive) != 0;

    public bool HasRoundedPhysicalContour =>
        (Flags & TerrainLightingFlags.RoundedPhysicalContour) != 0;

    public bool IsPhysicalMass => (Flags & TerrainLightingFlags.PhysicalMass) != 0;

    public bool ReceivesAmbientOcclusion => !IsPhysicalMass;

    public bool IsRoundable =>
        ((int)MathF.Round(PackedContour) & RoundableContourFlag) != 0;

    public float EmissionStrength => IsEmissive
        ? Math.Clamp((PackedFlags - MathF.Floor(PackedFlags)) / EmissionFractionScale, 0f, 1f)
        : 0f;

    public static TerrainLightingData Pack(
        byte solidConnectivityMask,
        bool isGlowing,
        bool hasRoundedPhysicalContour,
        bool isPhysicalMass,
        float emissionStrength)
    {
        var flags = (TerrainLightingFlags)(solidConnectivityMask & SolidBoundaryMask);
        if (isGlowing)
        {
            flags |= TerrainLightingFlags.Emissive;
        }

        if (hasRoundedPhysicalContour)
        {
            flags |= TerrainLightingFlags.RoundedPhysicalContour;
        }

        if (isPhysicalMass)
        {
            flags |= TerrainLightingFlags.PhysicalMass;
        }

        int contourFlags = (isGlowing ? GlowingContourFlag : 0) |
            (hasRoundedPhysicalContour ? RoundableContourFlag : 0);
        int solidDiagonal = solidConnectivityMask >> SolidDiagonalShift;
        return new TerrainLightingData(
            (byte)flags + (emissionStrength * EmissionFractionScale),
            contourFlags + (solidDiagonal * ContourFlagsRange));
    }
}
