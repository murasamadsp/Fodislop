#nullable enable

using Fodinae.World.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace Fodinae.Tests.World.Lighting
{
    /// <summary>
    /// Fuzzes <see cref="LightingRegionCalculator.GetStableLightingRegion"/> -
    /// the per-frame decision of where the lighting field lives and when it is
    /// allowed to stay put.
    /// </summary>
    /// <remarks>
    /// The renderer runs this from the game camera every frame. The two ways it
    /// can betray the player are churn (re-anchoring on camera motion that
    /// stayed inside the safe margin, the exact framerate sink the cache exists
    /// to prevent) and a region that does not cover the padded viewport (the
    /// field then has holes in it). Both are silent - there is no error to log.
    /// So the properties asserted here are the contract the renderer relies on:
    /// the NaN x-component is the "no previous region" sentinel and forces a
    /// fresh compute no matter what else is in the vector; a viewport inside
    /// the safe margin returns the previous region verbatim; and a fresh region
    /// is anchored on the 8-cell grid, sized on the 32-cell quantum, at least
    /// two cells, and covers the padded viewport.
    ///
    /// The seeds are fixed. A fuzz test that picks a new seed every run reports
    /// failures nobody can reproduce.
    ///
    /// NaN in the z/w components is deliberately excluded: RoundToInt on NaN is
    /// platform-dependent, and the x-sentinel is the documented protocol for
    /// "no previous region".
    /// </remarks>
    [TestFixture]
    public class LightingRegionCalculatorFuzzTests
    {
        private const int Padding = LightingRegionCalculator.LightingRegionPaddingCells;

        /// <summary>Anchor grid cell size (SnapLightingRegion's stride).</summary>
        private const int AnchorCells = 8;

        /// <summary>Fresh-region size quantum.</summary>
        private const int Quantum = 32;

        private const int MinCell = 2;

        private static readonly int[] Seeds = [1, 7, 42, 1337, 90210, 2147483, 8675309];

        /// <summary>
        /// Coordinates chosen to break the arithmetic rather than to look
        /// plausible: far negative (world space extends below the origin), the
        /// anchor boundary at -1/0/1, and magnitudes big enough to stress the
        /// ceil/quantize path.
        /// </summary>
        private static readonly int[] HostileCoords = [-10_000_000, -1000, -17, -8, -1, 0, 1, 7, 8, 9, 1000, 10_000_000];

        private static readonly int[] HostileExtents = [-1_000_000, -32, -1, 0, 1, 7, 32, 1000, 10_000_000];

        [Test]
        public void FreshRegionsAreAnchoredQuantizedAndCoverThePaddedViewport([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);

            for (int iteration = 0; iteration < 3000; iteration++)
            {
                (int minX, int minY, int width, int height) = RandomViewport(random);
                Vector4 region = LightingRegionCalculator.GetStableLightingRegion(
                    minX, minY, width, height,
                    new Vector4(float.NaN, 0, 0, 0));

                AssertFreshRegion(region, minX, minY, width, height, seed, iteration);
            }
        }

        [Test]
        public void TheNaNSentinelForcesAFreshRegionRegardlessOfOtherComponents([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                (int minX, int minY, int width, int height) = RandomViewport(random);
                Vector4 canonical = LightingRegionCalculator.GetStableLightingRegion(
                    minX, minY, width, height,
                    new Vector4(float.NaN, 0, 0, 0));

                // Garbage in y/z/w must be ignored: x alone is the validity
                // sentinel, so both calls have to compute the same fresh region.
                Vector4 withGarbage = LightingRegionCalculator.GetStableLightingRegion(
                    minX, minY, width, height,
                    new Vector4(float.NaN, -777f, 123_456f, 9_000_000f));

                Assert.That(
                    withGarbage,
                    Is.EqualTo(canonical),
                    $"seed {seed}, iteration {iteration}: sentinel must ignore y/z/w garbage.");
                Assert.That(float.IsNaN(withGarbage.x), Is.False, $"seed {seed}, iteration {iteration}");
            }
        }

        [Test]
        public void TheContainmentDecisionMatchesTheReferenceForEveryRandomInput([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);

            for (int iteration = 0; iteration < 3000; iteration++)
            {
                Vector4 previous = RandomValidRegion(random);
                (int minX, int minY, int width, int height) = RandomViewport(random);

                Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
                    minX, minY, width, height, previous);

                // The reference re-derives the safe-margin rule from the doc
                // contract: margin = min(16, max(2, min(w,h) / 4)), and the
                // viewport is kept put only when it clears the margin on all
                // four sides. Exactly that - no extra drift tolerance, no
                // hysteresis - is what the renderer depends on.
                int currentMinX = Mathf.RoundToInt(previous.x);
                int currentMinY = Mathf.RoundToInt(previous.y);
                int regionWidth = Mathf.RoundToInt(previous.z);
                int regionHeight = Mathf.RoundToInt(previous.w);
                int safeMargin = Mathf.Min(
                    Padding,
                    Mathf.Max(MinCell, Mathf.Min(regionWidth, regionHeight) / 4));

                bool inside =
                    minX >= currentMinX + safeMargin &&
                    minX + width <= currentMinX + regionWidth - safeMargin &&
                    minY >= currentMinY + safeMargin &&
                    minY + height <= currentMinY + regionHeight - safeMargin;

                Vector4 expected = inside
                    ? previous
                    : LightingRegionCalculator.GetStableLightingRegion(
                        minX, minY, width, height,
                        new Vector4(float.NaN, 0, 0, 0));

                Assert.That(
                    result,
                    Is.EqualTo(expected),
                    $"seed {seed}, iteration {iteration}: viewport {minX},{minY} {width}x{height} " +
                    $"against region {previous}, inside={inside}.");
            }
        }

        [Test]
        public void HostilePreviousRegionsNeverThrowAndNeverProduceNaN([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);
            Vector4[] hostileRegions =
            [
                new(float.NaN, 0, 0, 0),                       // the sentinel
                new(float.NaN, 1000, 2000, 3000),              // sentinel + plausible garbage
                new(0, 0, -100, 200),                          // negative width
                new(-16, -16, 100, -100),                      // negative height
                new(0, 0, 2, 2),                               // degenerate minimum
                new(-10_000_000, -10_000_000, 20_000_000, 20_000_000), // giant
            ];

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                Vector4 previous = hostileRegions[random.Next(hostileRegions.Length)];
                (int minX, int minY, int width, int height) = RandomViewport(random);

                Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
                    minX, minY, width, height, previous);

                Assert.That(
                    float.IsNaN(result.x) || float.IsNaN(result.y) || float.IsNaN(result.z) || float.IsNaN(result.w),
                    Is.False,
                    $"seed {seed}, iteration {iteration}: hostile previous {previous} produced NaN.");

                if (float.IsNaN(previous.x))
                {
                    // The sentinel always recomputes: the result is a fresh
                    // region and must satisfy the fresh-region contract.
                    AssertFreshRegion(result, minX, minY, width, height, seed, iteration);
                }
                else if (result != previous)
                {
                    // Either the region was returned verbatim (fine even when
                    // the previous region is degenerate - the caller asked for
                    // stability) or a fresh compute happened, which must be
                    // valid geometry.
                    AssertFreshRegion(result, minX, minY, width, height, seed, iteration);
                }
            }
        }

        private static (int MinX, int MinY, int Width, int Height) RandomViewport(System.Random random)
        {
            // A quarter of the draws come from the hostile pool, so negative
            // extents, zero extents and magnitudes near the stress range are
            // hit constantly rather than by luck. All values stay bounded so
            // min + extent never overflows int.
            if (random.Next(4) == 0)
            {
                return (
                    Pick(random, HostileCoords),
                    Pick(random, HostileCoords),
                    Pick(random, HostileExtents),
                    Pick(random, HostileExtents));
            }

            return (
                random.Next(-1_000_000, 1_000_001),
                random.Next(-1_000_000, 1_000_001),
                random.Next(0, 2001),
                random.Next(0, 2001));
        }

        /// <summary>
        /// A structurally valid previous region: anchored anywhere, sized on
        /// the 32-cell quantum, at least 32 cells per side so the safe-margin
        /// rule has room to matter. Stored values are exactly representable as
        /// floats, so RoundToInt inside the calculator is lossless.
        /// </summary>
        private static Vector4 RandomValidRegion(System.Random random)
        {
            int width = random.Next(1, 257) * Quantum;
            int height = random.Next(1, 257) * Quantum;
            return new Vector4(
                random.Next(-1_000_000, 1_000_001),
                random.Next(-1_000_000, 1_000_001),
                width,
                height);
        }

        /// <summary>
        /// The fresh-region contract: west/south edges are snapped anchors
        /// (multiples of 8), sizes are multiples of 32 unless clamped to the
        /// two-cell minimum, and the region covers the viewport plus padding on
        /// every side.
        /// </summary>
        private static void AssertFreshRegion(
            Vector4 region,
            int minX,
            int minY,
            int width,
            int height,
            int seed,
            int iteration)
        {
            string context = $"seed {seed}, iteration {iteration}: viewport {minX},{minY} {width}x{height}, region {region}.";

            Assert.That(region.x % AnchorCells, Is.EqualTo(0f), context + " west edge must sit on an 8-cell anchor.");
            Assert.That(region.y % AnchorCells, Is.EqualTo(0f), context + " south edge must sit on an 8-cell anchor.");

            Assert.That(region.z, Is.GreaterThanOrEqualTo(MinCell), context);
            Assert.That(region.w, Is.GreaterThanOrEqualTo(MinCell), context);

            // Sizes sit on the 32-cell quantum, except when the padded extent
            // is <= 0 (degenerate viewport) and the size clamps to the 2-cell
            // minimum. A negative-zero modulo still compares equal to zero.
            Assert.That(
                region.z % Quantum == 0f || region.z == MinCell,
                Is.True,
                context + " width must be a 32-cell quantum or the 2-cell minimum.");
            Assert.That(
                region.w % Quantum == 0f || region.w == MinCell,
                Is.True,
                context + " height must be a 32-cell quantum or the 2-cell minimum.");

            // Coverage, computed in long so the assertion itself can never wrap.
            // A viewport with a negative extent is an inverted interval; the
            // region still trivially covers it because it extends outward from
            // the snapped min edge.
            Assert.That((long)region.x, Is.LessThanOrEqualTo((long)minX - Padding), context + " west padding missing.");
            Assert.That((long)region.y, Is.LessThanOrEqualTo((long)minY - Padding), context + " south padding missing.");
            Assert.That(
                (long)region.x + (long)region.z,
                Is.GreaterThanOrEqualTo((long)minX + width + Padding),
                context + " east padding missing.");
            Assert.That(
                (long)region.y + (long)region.w,
                Is.GreaterThanOrEqualTo((long)minY + height + Padding),
                context + " north padding missing.");
        }

        private static T Pick<T>(System.Random random, params T[] options)
        {
            return options[random.Next(options.Length)];
        }
    }
}
