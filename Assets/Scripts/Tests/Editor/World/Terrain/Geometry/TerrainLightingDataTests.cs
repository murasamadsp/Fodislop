#nullable enable

namespace Kern.Tests.World;

using Kern.World.Terrain;
using NUnit.Framework;

[TestFixture]
public class TerrainLightingDataTests
{
    [Test]
    public void PackedValuesKeepTheShaderWireLayout()
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0xA5,
            isGlowing: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: true,
            emissionStrength: 0.6f);

        Assert.That(data.PackedFlags, Is.EqualTo(117.15f).Within(0.0001f));
        Assert.That(data.PackedContour, Is.EqualTo(43f));
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    [TestCase(true, true, true)]
    public void PackRoundTripsIndependentLightingFlags(
        bool isGlowing,
        bool hasRoundedPhysicalContour,
        bool isPhysicalMass)
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0xA5,
            isGlowing,
            hasRoundedPhysicalContour,
            isPhysicalMass,
            isGlowing ? 0.6f : 0f);

        Assert.That(data.SolidBoundary, Is.EqualTo(0x05));
        Assert.That(data.SolidDiagonal, Is.EqualTo(0x0A));
        Assert.That(data.IsEmissive, Is.EqualTo(isGlowing));
        Assert.That(data.IsRoundable, Is.EqualTo(hasRoundedPhysicalContour));
        Assert.That(data.HasRoundedPhysicalContour, Is.EqualTo(hasRoundedPhysicalContour));
        Assert.That(data.IsPhysicalMass, Is.EqualTo(isPhysicalMass));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void AmbientOcclusionReceiverIsDerivedOnlyFromPhysicalMass(
        bool isPhysicalMass,
        bool expectedReceiver)
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0xFF,
            isGlowing: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: isPhysicalMass,
            emissionStrength: 1f);

        Assert.That(data.ReceivesAmbientOcclusion, Is.EqualTo(expectedReceiver));
    }

    [TestCase(0f)]
    [TestCase(1f / 255f)]
    [TestCase(0.25f)]
    [TestCase(0.6f)]
    [TestCase(1f)]
    public void EmissionStrengthRoundTripsWithoutCorruptingFlags(float emissionStrength)
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0x5A,
            isGlowing: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: true,
            emissionStrength: emissionStrength);

        Assert.That(data.EmissionStrength, Is.EqualTo(emissionStrength).Within(0.0001f));
        Assert.That(data.SolidBoundary, Is.EqualTo(0x0A));
        Assert.That(data.IsEmissive, Is.True);
        Assert.That(data.HasRoundedPhysicalContour, Is.True);
        Assert.That(data.IsPhysicalMass, Is.True);
    }

    [Test]
    public void NonEmissiveDataDecodesZeroEmission()
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0,
            isGlowing: false,
            hasRoundedPhysicalContour: false,
            isPhysicalMass: false,
            emissionStrength: 0.75f);

        Assert.That(data.EmissionStrength, Is.Zero);
    }

    [TestCase(MinesServer.Data.CellType.QuadBlock)]
    [TestCase(MinesServer.Data.CellType.Support)]
    [TestCase(MinesServer.Data.CellType.MilitaryBlock)]
    [TestCase(MinesServer.Data.CellType.BuildingWall)]
    [TestCase(MinesServer.Data.CellType.BuildingDoor)]
    [TestCase(MinesServer.Data.CellType.BuildingCorner)]
    [TestCase(MinesServer.Data.CellType.Gate)]
    [TestCase(MinesServer.Data.CellType.TeleportBlock)]
    [TestCase(MinesServer.Data.CellType.Box)]
    public void BuildingAndArtificialBlocksAreIdentifiedAsNonEmissive(MinesServer.Data.CellType cellType)
    {
        Assert.That(Kern.World.MapCellConfigCatalog.IsBuildingOrArtificialBlock(cellType), Is.True);
    }
}
