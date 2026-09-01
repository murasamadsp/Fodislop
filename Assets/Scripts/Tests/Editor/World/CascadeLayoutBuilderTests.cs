#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.World.Lighting;
using NUnit.Framework;

namespace Fodinae.Tests.World.Lighting
{
    /// <summary>
    /// CascadeLayoutBuilder is the pure half of the radiance-cascade atlas math:
    /// it decides how many probe grids fit the atlas and where each cascade's
    /// pixel block starts. LightingEngine feeds the output straight into the
    /// compute buffer offsets, so a regression here is not a wrong picture -
    /// it is overlapping probes or an offset that lands in the wrong cascade.
    /// The properties asserted mirror the contract LightingResourceManager relies
    /// on and has no way to check for itself.
    /// </summary>
    [TestFixture]
    public class CascadeLayoutBuilderTests
    {
        [Test]
        public void CascadeCountIsThreeBelowAtlasThresholdAndFourAbove()
        {
            Assert.That(CascadeLayoutBuilder.GetMaximumCascadeCount(0), Is.EqualTo(3));
            Assert.That(CascadeLayoutBuilder.GetMaximumCascadeCount(256), Is.EqualTo(3));
            Assert.That(CascadeLayoutBuilder.GetMaximumCascadeCount(257), Is.EqualTo(4));
            Assert.That(CascadeLayoutBuilder.GetMaximumCascadeCount(4096), Is.EqualTo(4));
        }

        [Test]
        public void EntryCountThrowsOnZeroOrNegativeCascadeBudget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CascadeLayoutBuilder.CalculateCascadeEntryCount(100, 50, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CascadeLayoutBuilder.CalculateCascadeEntryCount(100, 50, -3));
        }

        [Test]
        public void EntryCountIsTheSumOverAllBuiltCascades()
        {
            foreach ((int width, int height) in new[] { (100, 50), (512, 320), (1280, 720), (16, 16) })
            {
                foreach (long atlas in new[] { 256L, 512L, 2048L })
                {
                    int maxCascades = CascadeLayoutBuilder.GetMaximumCascadeCount(atlas);
                    long counted = CascadeLayoutBuilder.CalculateCascadeEntryCount(width, height, maxCascades);

                    var cascades = new List<CascadeLayout>();
                    CascadeLayoutBuilder.BuildCascadeLayouts(width, height, atlas, cascades);

                    long lastOffset = cascades[^1].Offset;
                    long lastEntry = cascades[^1].EntryCount;
                    Assert.That(
                        lastOffset + lastEntry,
                        Is.EqualTo(counted),
                        $"world {width}x{height} atlas {atlas}: builder total must match independent counter.");
                }
            }
        }

        [Test]
        public void CascadeOffsetsNeverOverlapAndAlwaysStartAfterThePreviousEntryBlock()
        {
            foreach ((int width, int height, long atlas) in new[] { (100, 50, 512L), (1280, 720, 1024L) })
            {
                var cascades = new List<CascadeLayout>();
                CascadeLayoutBuilder.BuildCascadeLayouts(width, height, atlas, cascades);

                int runningOffset = 0;
                foreach (CascadeLayout cascade in cascades)
                {
                    Assert.That(cascade.Offset, Is.EqualTo(runningOffset), "Offset must be cumulative.");
                    Assert.That(cascade.EntryCount, Is.GreaterThan(0));
                    runningOffset += cascade.EntryCount;
                }
            }
        }

        [Test]
        public void EveryCascadeHasStrictlyPositiveProbeDimensions()
        {
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(640, 360, 1024, cascades);

            foreach (CascadeLayout cascade in cascades)
            {
                Assert.That(cascade.ProbeWidth, Is.GreaterThan(0), "Probe grid width must stay >= 1.");
                Assert.That(cascade.ProbeHeight, Is.GreaterThan(0), "Probe grid height must stay >= 1.");
            }
        }

        [Test]
        public void SpacingDoublesDirectionQuadratuplesStepIntervalAdvancesContiguously()
        {
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(100, 50, 512, cascades);

            Assert.That(cascades.Count, Is.EqualTo(4));

            // Cascade 0 starts at 0 and ends at 1.
            Assert.That(cascades[0].ProbeSpacing, Is.EqualTo(1));
            Assert.That(cascades[0].DirectionCount, Is.EqualTo(4));
            Assert.That(cascades[0].IntervalStart, Is.EqualTo(0f));
            Assert.That(cascades[0].IntervalEnd, Is.EqualTo(1f));

            for (int i = 1; i < cascades.Count; i++)
            {
                Assert.That(cascades[i].ProbeSpacing, Is.EqualTo(cascades[i - 1].ProbeSpacing * 2));
                Assert.That(cascades[i].DirectionCount, Is.EqualTo(cascades[i - 1].DirectionCount * 4));
                Assert.That(cascades[i].IntervalStart, Is.EqualTo(cascades[i - 1].IntervalEnd));
                Assert.That(cascades[i].IntervalEnd, Is.EqualTo(cascades[i - 1].IntervalEnd * 4f));
            }
        }

        [Test]
        public void DirectionCountIsCappedAtTwoHundredFiftySix()
        {
            // A large world forces enough cascades that the direction budget
            // would quadruple past 256; it must cap, not overflow the buffer.
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(4000, 4000, 512, cascades);

            foreach (CascadeLayout cascade in cascades)
            {
                Assert.That(cascade.DirectionCount, Is.LessThanOrEqualTo(256));
            }

            // At least one cascade should actually hit the cap on a world this big.
            Assert.That(cascades[^1].DirectionCount, Is.GreaterThanOrEqualTo(256));
        }

        [Test]
        public void TinyWorldStopsQuicklyBecauseProbesQuadratupleButDistanceIsShort()
        {
            // World (1x1): sqrt(w^2+h^2) ~= 1.41. Cascade 0 covers out to 1;
            // cascade 1 (16 directions, interval [1,4]) covers the rest, so two
            // cascades address it: 1*1*4 + 1*1*16 = 20 entries.
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(1, 1, 512, cascades);

            Assert.That(cascades.Count, Is.EqualTo(2));
            Assert.That(cascades[0].Offset, Is.EqualTo(0));
            Assert.That(cascades[0].EntryCount, Is.EqualTo(4));
            Assert.That(cascades[1].Offset, Is.EqualTo(4));
            Assert.That(cascades[1].EntryCount, Is.EqualTo(16));
        }

        [Test]
        public void TheEntryCountCounterIsNeverZeroForTheDegenerateWorld()
        {
            // Even the degenerate 1x1 world is addressable; the counter must
            // agree with the builder for the same budget.
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(1, 1, 512, cascades);
            Assert.That(
                CascadeLayoutBuilder.CalculateCascadeEntryCount(1, 1, CascadeLayoutBuilder.GetMaximumCascadeCount(512)),
                Is.EqualTo(cascades[^1].Offset + cascades[^1].EntryCount));
        }
    }
}
