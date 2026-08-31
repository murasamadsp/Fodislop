#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.World.Lighting;
using NUnit.Framework;

namespace Fodinae.Tests.World.Lighting
{
    /// <summary>
    /// Fuzzes <see cref="CascadeCostCalculator"/> against a reference
    /// implementation written from the documented contract, not by copying the
    /// code under test.
    /// </summary>
    /// <remarks>
    /// The debug overlay prints these numbers as fact. A stepCount that is off
    /// by one is not a cosmetic error: ray budgets decide whether a solve fits
    /// the frame budget, and the merge-tap count decides atlas traffic. The
    /// reference here recomputes every field from the documented rules
    /// (interval length clamped to at least one, steps clamped to the budget,
    /// merge taps only for cascades with a coarser neighbour) so a silent
    /// drift in the implementation shows up as a mismatch instead of two wrong
    /// numbers agreeing with each other.
    ///
    /// The seeds are fixed. A fuzz test that picks a new seed every run reports
    /// failures nobody can reproduce.
    /// </remarks>
    [TestFixture]
    public class CascadeCostCalculatorFuzzTests
    {
        private static readonly int[] Seeds = [1, 7, 42, 1337, 90210, 2147483, 8675309];

        private static readonly int[] HostileEntryCounts = [0, -1, 1, 7, 1000];
        private static readonly int[] HostileDirectionCounts = [0, 1, 4, 16, 64, 256, 512];
        private static readonly float[] HostileIntervalStarts = [0f, -1f, 1f, 4f, 100f];
        private static readonly float[] HostileIntervalEnds = [0f, -1f, 1f, 4f, 16f, 1000f];

        [Test]
        public void SamplesMatchTheReferenceForEveryRandomWorld([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);

            for (int iteration = 0; iteration < 2000; iteration++)
            {
                var cascades = new List<CascadeLayout>();
                CascadeLayoutBuilder.BuildCascadeLayouts(
                    random.Next(1, 4097),
                    random.Next(1, 4097),
                    random.Next(64, 8193),
                    cascades);
                int maximumSteps = random.Next(1, 257);

                // Pre-seed garbage: the collector must wipe it, every time.
                var destination = new List<CascadeCostSample> { default, default };
                CascadeCostCalculator.CollectCascadeCosts(cascades, maximumSteps, destination);

                Assert.That(destination.Count, Is.EqualTo(cascades.Count), $"seed {seed}, iteration {iteration}");
                for (int i = 0; i < cascades.Count; i++)
                {
                    AssertSampleMatchesReference(destination[i], cascades, i, maximumSteps, seed, iteration);
                }
            }
        }

        [Test]
        public void HostileStepBudgetsNeverThrowAndStillMatchTheReference([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);
            int[] budgets = [0, -1, int.MinValue, 1, 63, 64, 256, int.MaxValue];

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                var cascades = new List<CascadeLayout>();
                CascadeLayoutBuilder.BuildCascadeLayouts(
                    random.Next(1, 4097),
                    random.Next(1, 4097),
                    random.Next(64, 8193),
                    cascades);

                int maximumSteps = Pick(random, budgets);
                var destination = new List<CascadeCostSample>();
                Assert.DoesNotThrow(
                    () => CascadeCostCalculator.CollectCascadeCosts(cascades, maximumSteps, destination),
                    $"seed {seed}, iteration {iteration}, maximumSteps {maximumSteps}");
                Assert.That(destination.Count, Is.EqualTo(cascades.Count), $"seed {seed}, iteration {iteration}");
                for (int i = 0; i < cascades.Count; i++)
                {
                    AssertSampleMatchesReference(destination[i], cascades, i, maximumSteps, seed, iteration);
                }
            }
        }

        [Test]
        public void HostileLayoutsMatchTheReferenceWithoutThrowing([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);

            for (int iteration = 0; iteration < 1000; iteration++)
            {
                int cascadeCount = random.Next(1, 8);
                var cascades = new List<CascadeLayout>(cascadeCount);
                int offset = 0;
                for (int i = 0; i < cascadeCount; i++)
                {
                    int entryCount = Pick(random, HostileEntryCounts);
                    cascades.Add(new CascadeLayout(
                        offset,
                        entryCount,
                        random.Next(0, 64),
                        random.Next(0, 64),
                        random.Next(1, 9),
                        Pick(random, HostileDirectionCounts),
                        Pick(random, HostileIntervalStarts),
                        Pick(random, HostileIntervalEnds)));
                    offset += entryCount;
                }

                // Budgets go negative on purpose: the documented clamp rule
                // must still produce a defined answer, not an exception.
                int maximumSteps = random.Next(-5, 65);
                var destination = new List<CascadeCostSample>();
                Assert.DoesNotThrow(
                    () => CascadeCostCalculator.CollectCascadeCosts(cascades, maximumSteps, destination),
                    $"seed {seed}, iteration {iteration}");
                Assert.That(destination.Count, Is.EqualTo(cascades.Count), $"seed {seed}, iteration {iteration}");
                for (int i = 0; i < cascades.Count; i++)
                {
                    AssertSampleMatchesReference(destination[i], cascades, i, maximumSteps, seed, iteration);
                }
            }
        }

        [Test]
        public void AggregateCostsStayNonNegativeForRealisticWorlds([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);

            for (int iteration = 0; iteration < 2000; iteration++)
            {
                var cascades = new List<CascadeLayout>();
                CascadeLayoutBuilder.BuildCascadeLayouts(
                    random.Next(1, 4097),
                    random.Next(1, 4097),
                    random.Next(64, 8193),
                    cascades);

                var destination = new List<CascadeCostSample>();
                CascadeCostCalculator.CollectCascadeCosts(cascades, random.Next(1, 257), destination);

                long rays = 0;
                long raySteps = 0;
                long mergeTaps = 0;
                foreach (CascadeCostSample sample in destination)
                {
                    Assert.That(sample.RayCount, Is.GreaterThan(0), $"seed {seed}, iteration {iteration}");
                    Assert.That(sample.RayStepCount, Is.GreaterThan(0), $"seed {seed}, iteration {iteration}");
                    rays += sample.RayCount;
                    raySteps += sample.RayStepCount;
                    mergeTaps += sample.MergeTapCount;
                }

                Assert.That(rays, Is.GreaterThan(0), $"seed {seed}, iteration {iteration}");
                Assert.That(
                    raySteps,
                    Is.GreaterThanOrEqualTo(rays),
                    $"seed {seed}, iteration {iteration}: every ray marches at least one step.");
                Assert.That(mergeTaps, Is.GreaterThanOrEqualTo(0), $"seed {seed}, iteration {iteration}");
            }
        }

        /// <summary>
        /// Recomputes one sample from the documented contract and asserts the
        /// collector produced exactly that. Written against the rules in the
        /// doc comments (interval length min 1, ceil, clamp to budget, merge
        /// taps only with a coarser neighbour), not against the implementation.
        /// </summary>
        private static void AssertSampleMatchesReference(
            CascadeCostSample sample,
            IReadOnlyList<CascadeLayout> cascades,
            int index,
            int maximumSteps,
            int seed,
            int iteration)
        {
            CascadeLayout cascade = cascades[index];
            string context = $"seed {seed}, iteration {iteration}, cascade {index}";

            // intervalLength = max(end - start, 1); stepCount = clamp(ceil(intervalLength), 1, maximumSteps).
            float intervalLength = Math.Max(cascade.IntervalEnd - cascade.IntervalStart, 1f);
            int stepCount = Clamp((int)Math.Ceiling(intervalLength), 1, maximumSteps);

            long mergeTaps = 0;
            if (index + 1 < cascades.Count)
            {
                int branchCount = Clamp(
                    cascades[index + 1].DirectionCount / Math.Max(1, cascade.DirectionCount),
                    1,
                    4);
                mergeTaps = (long)cascade.EntryCount * branchCount * 4;
            }

            Assert.That(sample.Index, Is.EqualTo(index), context);
            Assert.That(sample.ProbeWidth, Is.EqualTo(cascade.ProbeWidth), context);
            Assert.That(sample.ProbeHeight, Is.EqualTo(cascade.ProbeHeight), context);
            Assert.That(sample.DirectionCount, Is.EqualTo(cascade.DirectionCount), context);
            Assert.That(sample.IntervalStart, Is.EqualTo(cascade.IntervalStart), context);
            Assert.That(sample.IntervalEnd, Is.EqualTo(cascade.IntervalEnd), context);
            Assert.That(sample.StepCount, Is.EqualTo(stepCount), context);
            Assert.That(sample.RayCount, Is.EqualTo(cascade.EntryCount), context);
            Assert.That(sample.RayStepCount, Is.EqualTo((long)cascade.EntryCount * stepCount), context);
            Assert.That(sample.MergeTapCount, Is.EqualTo(mergeTaps), context);
        }

        /// <summary>
        /// Mirrors <c>Mathf.Clamp(int, int, int)</c> exactly (including the
        /// degenerate max &lt; min case) so the reference is self-contained and
        /// the hostile-budget tests stay defined.
        /// </summary>
        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static T Pick<T>(System.Random random, params T[] options)
        {
            return options[random.Next(options.Length)];
        }
    }
}
