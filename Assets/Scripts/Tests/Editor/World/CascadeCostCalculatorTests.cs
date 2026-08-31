#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.World.Lighting;
using NUnit.Framework;

namespace Fodinae.Tests.World.Lighting
{
    /// <summary>
    /// CascadeCostCalculator is the pure telemetry half: it derives per-cascade
    /// ray budgets and merge-atlas taps from a CascadeLayout list, mirroring the
    /// arithmetic WorldLighting.compute actually runs per solve. The debug
    /// overlay shows these numbers, and a wrong stepCount here is a wrong cost
    /// reported with complete confidence. These tests pin the arithmetic against
    /// hand-computed expectations and the input-validation contract.
    /// </summary>
    [TestFixture]
    public class CascadeCostCalculatorTests
    {
        private static List<CascadeLayout> ThreeCascades()
        {
            return new List<CascadeLayout>
            {
                new(Offset: 0, EntryCount: 100, ProbeWidth: 10, ProbeHeight: 10, ProbeSpacing: 1, DirectionCount: 4, IntervalStart: 0f, IntervalEnd: 1f),
                new(Offset: 100, EntryCount: 200, ProbeWidth: 10, ProbeHeight: 5, ProbeSpacing: 2, DirectionCount: 16, IntervalStart: 1f, IntervalEnd: 4f),
                new(Offset: 300, EntryCount: 300, ProbeWidth: 20, ProbeHeight: 15, ProbeSpacing: 4, DirectionCount: 64, IntervalStart: 4f, IntervalEnd: 16f),
            };
        }

        [Test]
        public void StepCountIsIntervalLengthRoundedUpAndClampedToOne()
        {
            var destination = new List<CascadeCostSample>();
            CascadeCostCalculator.CollectCascadeCosts(ThreeCascades(), maximumSteps: 64, destination);

            // Interval [0,1]: length 1 -> one ray step.
            Assert.That(destination[0].StepCount, Is.EqualTo(1));

            // Interval [1,4]: length 3 -> three steps.
            Assert.That(destination[1].StepCount, Is.EqualTo(3));

            // Interval [4,16]: length 12 -> twelve steps.
            Assert.That(destination[2].StepCount, Is.EqualTo(12));
        }

        [Test]
        public void NegativeOrZeroIntervalLengthCoercesToOneStep()
        {
            // A cascade whose start == end (or inverted) still has to march at
            // least one interval; length = max(end-start, 1).
            var degenerate = new List<CascadeLayout>
            {
                new(0, 50, 5, 5, 1, 4, 10f, 10f),
            };

            var destination = new List<CascadeCostSample>();
            CascadeCostCalculator.CollectCascadeCosts(degenerate, maximumSteps: 64, destination);

            Assert.That(destination[0].StepCount, Is.EqualTo(1));
        }

        [Test]
        public void StepCountIsClampedToTheMaximumBudget()
        {
            var destination = new List<CascadeCostSample>();
            CascadeCostCalculator.CollectCascadeCosts(ThreeCascades(), maximumSteps: 2, destination);

            // Every interval longer than 2 must clamp to 2; the first (length 1)
            // stays at 1.
            Assert.That(destination[0].StepCount, Is.EqualTo(1));
            Assert.That(destination[1].StepCount, Is.EqualTo(2));
            Assert.That(destination[2].StepCount, Is.EqualTo(2));
        }

        [Test]
        public void RayCountAndRayStepCountFollowTheLayoutEntryCount()
        {
            var destination = new List<CascadeCostSample>();
            CascadeCostCalculator.CollectCascadeCosts(ThreeCascades(), maximumSteps: 64, destination);

            Assert.That(destination[0].RayCount, Is.EqualTo(100));
            Assert.That(destination[0].RayStepCount, Is.EqualTo(100)); // 100 * 1 step

            Assert.That(destination[1].RayCount, Is.EqualTo(200));
            Assert.That(destination[1].RayStepCount, Is.EqualTo(600)); // 200 * 3 steps

            Assert.That(destination[2].RayCount, Is.EqualTo(300));
            Assert.That(destination[2].RayStepCount, Is.EqualTo(3600)); // 300 * 12 steps
        }

        [Test]
        public void MergeTapsOnlyExistWhenACoarserCascadeFollows()
        {
            var destination = new List<CascadeCostSample>();
            CascadeCostCalculator.CollectCascadeCosts(ThreeCascades(), maximumSteps: 64, destination);

            // branch = clamp(next.Directions / cur.Directions, 1, 4) = 4
            // taps = EntryCount * branch * 4
            Assert.That(destination[0].MergeTapCount, Is.EqualTo(1600)); // 100 * 4 * 4
            Assert.That(destination[1].MergeTapCount, Is.EqualTo(3200)); // 200 * 4 * 4

            // Last cascade has nothing coarser above it to merge from.
            Assert.That(destination[2].MergeTapCount, Is.EqualTo(0));
        }

        [Test]
        public void MergeBranchIsFlatWhenTheNextCascadeHasFewerDirections()
        {
            // A coarser cascade keeps the merge fan from exploding; if the next
            // level has fewer directions than the current (should not normally
            // happen but must not divide-by-zero), the branch is clamped to 1.
            var list = new List<CascadeLayout>
            {
                new(0, 100, 10, 10, 1, 16, 0f, 4f),
                new(100, 50, 10, 5, 2, 4, 4f, 16f),
            };

            var destination = new List<CascadeCostSample>();
            CascadeCostCalculator.CollectCascadeCosts(list, maximumSteps: 64, destination);

            Assert.That(destination[0].MergeTapCount, Is.EqualTo(400)); // 100 * 1 * 4
        }

        [Test]
        public void ResultListIsClearedBeforeFilling()
        {
            var destination = new List<CascadeCostSample>
            {
                // Pre-seeded garbage that must be wiped by the collector.
                new(-1, 0, 0, 0, 0f, 0f, 0, 0, 0, 0),
            };

            CascadeCostCalculator.CollectCascadeCosts(ThreeCascades(), maximumSteps: 64, destination);

            Assert.That(destination.Count, Is.EqualTo(3));
            // The garbage sample is gone - every sample has a real index.
            CollectionAssert.AllItemsAreUnique(destination.ConvertAll(sample => sample.Index));
        }

        [Test]
        public void EmptyInputProducesEmptyOutput()
        {
            var destination = new List<CascadeCostSample> { default };
            CascadeCostCalculator.CollectCascadeCosts(
                new List<CascadeLayout>(),
                maximumSteps: 64,
                destination);

            Assert.That(destination, Is.Empty);
        }

        [Test]
        public void NullCascadesThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => CascadeCostCalculator.CollectCascadeCosts(
                    null!,
                    maximumSteps: 64,
                    new List<CascadeCostSample>()));
        }

        [Test]
        public void NullDestinationThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => CascadeCostCalculator.CollectCascadeCosts(
                    ThreeCascades(),
                    maximumSteps: 64,
                    null!));
        }
    }
}
