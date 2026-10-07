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
            glows: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: true,
            glow: 0.6f,
            rimMask: 0,
            hasRim: false);

        Assert.That(data.PackedFlags, Is.EqualTo(53.15f).Within(0.0001f));
        // Старшая тетрада 0xA5 — бывшие диагонали; в контур они не попадают.
        Assert.That(data.PackedContour, Is.EqualTo(1f));
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    [TestCase(true, true, true)]
    public void PackRoundTripsIndependentLightingFlags(
        bool glows,
        bool hasRoundedPhysicalContour,
        bool isPhysicalMass)
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0xA5,
            glows,
            hasRoundedPhysicalContour,
            isPhysicalMass,
            glows ? 0.6f : 0f,
            rimMask: 0,
            hasRim: false);

        Assert.That(data.ForegroundSides, Is.EqualTo(0x05));
        Assert.That(data.Glows, Is.EqualTo(glows));
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
            glows: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: isPhysicalMass,
            glow: 1f,
            rimMask: 0,
            hasRim: false);

        Assert.That(data.ReceivesAmbientOcclusion, Is.EqualTo(expectedReceiver));
    }

    [TestCase(0f)]
    [TestCase(1f / 255f)]
    [TestCase(0.25f)]
    [TestCase(0.6f)]
    [TestCase(1f)]
    public void GlowRoundTripsWithoutCorruptingFlags(float glow)
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0x5A,
            glows: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: true,
            glow: glow,
            rimMask: 0,
            hasRim: false);

        Assert.That(data.Glow, Is.EqualTo(glow).Within(0.0001f));
        Assert.That(data.ForegroundSides, Is.EqualTo(0x0A));
        Assert.That(data.Glows, Is.True);
        Assert.That(data.IsPhysicalMass, Is.True);
    }

    [Test]
    public void NonGlowingDataDecodesZeroGlow()
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0,
            glows: false,
            hasRoundedPhysicalContour: false,
            isPhysicalMass: false,
            glow: 0.75f,
            rimMask: 0,
            hasRim: false);

        Assert.That(data.Glow, Is.Zero);
    }

    // Код каймы лежит над флагом roundable contour и не затрагивает его.
    [TestCase(0, 1)]
    [TestCase(0x05, 6)]
    [TestCase(0x0F, 16)]
    public void RimCodeRoundTripsBesideContourFields(int rimMask, int expectedCode)
    {
        TerrainLightingData data = TerrainLightingData.Pack(
            0xA5,
            glows: true,
            hasRoundedPhysicalContour: true,
            isPhysicalMass: true,
            glow: 0.6f,
            rimMask: (byte)rimMask,
            hasRim: true);

        Assert.That(data.RimCode, Is.EqualTo(expectedCode));
        Assert.That(data.IsRoundable, Is.True);
    }

    // Ноль означает «каймы нет», поэтому клетка без семьи и клетка, у
    // которой все четыре соседа чужие, обязаны различаться.
    [Test]
    public void NoRimIsDistinctFromAllNeighborsForeign()
    {
        TerrainLightingData without = TerrainLightingData.Pack(
            0, false, false, false, 0f, rimMask: 0, hasRim: false);
        TerrainLightingData surrounded = TerrainLightingData.Pack(
            0, false, false, false, 0f, rimMask: 0, hasRim: true);

        Assert.That(without.RimCode, Is.EqualTo(TerrainLightingData.NoRim));
        Assert.That(surrounded.RimCode, Is.EqualTo(1));
    }
}
