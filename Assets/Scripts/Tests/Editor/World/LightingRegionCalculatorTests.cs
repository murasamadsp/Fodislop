#nullable enable

using UnityEngine;
using NUnit.Framework;
using Fodinae.World.Lighting;

namespace Fodinae.Tests.World.Lighting
{
    /// <summary>
    /// LightingRegionCalculator decides where the lighting field lives and when
    /// it is allowed to stay put. The renderer runs it from the game camera
    /// every frame; a wrong anchor means the field re-snaps on camera motion
    /// even when nothing actually left the cached area (the exact framerate
    /// sink AGENTS.md warns about). These tests pin the arithmetic: anchors are
    /// multiples of 8, sizes are multiples of 32, padding is applied on every
    /// side, and an already-stable region is returned verbatim.
    /// </summary>
    [TestFixture]
    public class LightingRegionCalculatorTests
    {
        [Test]
        public void SnapAlwaysAnchorsToAMultipleOfEight()
        {
            Assert.That(LightingRegionCalculator.SnapLightingRegion(0), Is.EqualTo(0));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(7), Is.EqualTo(0));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(8), Is.EqualTo(8));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(9), Is.EqualTo(8));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(16), Is.EqualTo(16));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(23), Is.EqualTo(16));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(31), Is.EqualTo(24));
        }

        [Test]
        public void SnapFloorsNegativeCoordinatesTowardNegativeInfinity()
        {
            // Floor, not truncation: a coordinate just below zero must snap
            // down to a negative anchor, otherwise the padded west edge of the
            // world wraps and clips geometry on the far side.
            Assert.That(LightingRegionCalculator.SnapLightingRegion(-1), Is.EqualTo(-8));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(-7), Is.EqualTo(-8));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(-8), Is.EqualTo(-8));
            Assert.That(LightingRegionCalculator.SnapLightingRegion(-9), Is.EqualTo(-16));
        }

        [Test]
        public void FreshRegionIsPaddedAndQuantizedToThirtyTwo()
        {
            // No previous region (NaN) => compute from scratch. The visible area
            // is 100x100 at origin; plus 16px padding each side and quantization
            // up to a multiple of 32 yields a 160x160 region anchored at -16.
            Vector4 region = LightingRegionCalculator.GetStableLightingRegion(
                visibleMinX: 0,
                visibleMinY: 0,
                visibleWidth: 100,
                visibleHeight: 100,
                lastVisibleRegion: new Vector4(float.NaN, 0, 0, 0));

            Assert.That(region.x, Is.EqualTo(-16f), "West edge must be padded and anchored.");
            Assert.That(region.y, Is.EqualTo(-16f), "South edge must be padded and anchored.");
            Assert.That(region.z, Is.EqualTo(160f), "Width must be padded then quantized to 32.");
            Assert.That(region.w, Is.EqualTo(160f), "Height must be padded then quantized to 32.");
        }

        [Test]
        public void ContainedRegionIsReturnedUnchanged()
        {
            Vector4 previous = new(0, 0, 200, 200);

            // A viewport that stays well inside the previous region (beyond the
            // safe margin) must NOT trigger a recompute - that is the churn the
            // quantization cache exists to prevent.
            Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
                visibleMinX: 40,
                visibleMinY: 40,
                visibleWidth: 20,
                visibleHeight: 20,
                lastVisibleRegion: previous);

            Assert.That(result, Is.EqualTo(previous));
        }

        [Test]
        public void EscapingTheSafeMarginForcesAReanchoredRegion()
        {
            // A viewport that slides far enough east that the west edge crosses
            // inside the safe margin must re-anchor the whole field, not drift.
            Vector4 previous = new(0, 0, 200, 200);
            Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
                visibleMinX: 160,
                visibleMinY: 0,
                visibleWidth: 50,
                visibleHeight: 50,
                lastVisibleRegion: previous);

            Assert.That(result.x, Is.Not.EqualTo(previous.x), "Region must move with the camera.");
            Assert.That(result.x % 8, Is.EqualTo(0f), "Re-anchored west edge must stay on an 8-cell grid.");
            Assert.That(result.z % 32, Is.EqualTo(0f), "Re-anchored width must stay on a 32-cell quantum.");
        }

        [Test]
        public void RegionIsNeverSmallerThanTwoCells()
        {
            Vector4 region = LightingRegionCalculator.GetStableLightingRegion(
                visibleMinX: 0,
                visibleMinY: 0,
                visibleWidth: 0,
                visibleHeight: 0,
                lastVisibleRegion: new Vector4(float.NaN, 0, 0, 0));

            Assert.That(region.z, Is.GreaterThanOrEqualTo(2f));
            Assert.That(region.w, Is.GreaterThanOrEqualTo(2f));
        }
    }
}
