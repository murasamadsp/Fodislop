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
            emissionStrength: 0.6f,
            reliefMask: 0,
            hasRelief: false);

        Assert.That(data.PackedFlags, Is.EqualTo(53.15f).Within(0.0001f));
        Assert.That(data.PackedContour, Is.EqualTo(21f));
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
            isGlowing ? 0.6f : 0f,
            reliefMask: 0,
            hasRelief: false);

        Assert.That(data.SolidBoundary, Is.EqualTo(0x05));
        Assert.That(data.SolidDiagonal, Is.EqualTo(0x0A));
        Assert.That(data.IsEmissive, Is.EqualTo(isGlowing));
        Assert.That(data.IsRoundable, Is.EqualTo(hasRoundedPhysicalContour));
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
            emissionStrength: 1f,
            reliefMask: 0,
            hasRelief: false);

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
            emissionStrength: emissionStrength,
            reliefMask: 0,
            hasRelief: false);

        Assert.That(data.EmissionStrength, Is.EqualTo(emissionStrength).Within(0.0001f));
        Assert.That(data.SolidBoundary, Is.EqualTo(0x0A));
        Assert.That(data.IsEmissive, Is.True);
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
            emissionStrength: 0.75f,
            reliefMask: 0,
            hasRelief: false);

        Assert.That(data.EmissionStrength, Is.Zero);
    }

    // Код рельефа лежит над флагом roundable contour и не затрагивает его.
    [TestCase(0, 1)]
    [TestCase(0x05, 6)]
    [TestCase(0x0F, 16)]
    public void ReliefCodeRoundTripsBesideContourFields(int reliefMask, int expectedCode)
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0xA5,
            isGlowing: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: true,
            emissionStrength: 0.6f,
            reliefMask: (byte)reliefMask,
            hasRelief: true);

        Assert.That(data.ReliefCode, Is.EqualTo(expectedCode));
        Assert.That(data.IsRoundable, Is.True);
    }

    // Ноль означает «рельефа нет», поэтому клетка без семьи и клетка, у
    // которой все четыре соседа чужие, обязаны различаться.
    [Test]
    public void NoReliefIsDistinctFromAllNeighborsForeign()
    {
        TerrainLightingData without = TerrainLightingData.Pack(
            0, false, false, false, 0f, reliefMask: 0, hasRelief: false);
        TerrainLightingData surrounded = TerrainLightingData.Pack(
            0, false, false, false, 0f, reliefMask: 0, hasRelief: true);

        Assert.That(without.ReliefCode, Is.EqualTo(TerrainLightingData.NoRelief));
        Assert.That(surrounded.ReliefCode, Is.EqualTo(1));
    }
}
