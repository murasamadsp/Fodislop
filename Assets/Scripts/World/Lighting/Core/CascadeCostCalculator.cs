#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.World.Lighting
{
    /// <summary>
    /// Per-cascade cost of one full radiance solve, in the units that
    /// actually decide how long the GPU spends on it.
    /// </summary>
    public readonly record struct CascadeCostSample(
        int Index,
        int ProbeWidth,
        int ProbeHeight,
        int DirectionCount,
        float IntervalStart,
        float IntervalEnd,
        int StepCount,
        long RayCount,
        long RayStepCount,
        long MergeTapCount);

    /// <summary>
    /// Pure calculation helper for Radiance Cascades telemetry and ray budget analysis.
    /// </summary>
    public static class CascadeCostCalculator
    {
        /// <summary>
        /// Rays, ray-march steps and far-cascade atlas taps one full solve
        /// issues. Mirrors the arithmetic in <c>WorldLighting.compute</c>.
        /// </summary>
        public static void CollectCascadeCosts(
            IReadOnlyList<CascadeLayout> cascades,
            int maximumSteps,
            List<CascadeCostSample> destination)
        {
            if (cascades == null)
            {
                throw new ArgumentNullException(nameof(cascades));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            for (int index = 0; index < cascades.Count; index++)
            {
                CascadeLayout cascade = cascades[index];

                // SolveCascade: intervalLength = max(end - start, 1);
                // stepCount = clamp(ceil(intervalLength), 1, min(_MaximumIntervalSteps, 64)).
                float intervalLength = Mathf.Max(cascade.IntervalEnd - cascade.IntervalStart, 1f);
                int stepCount = Mathf.Clamp(
                    Mathf.CeilToInt(intervalLength),
                    1,
                    maximumSteps);

                // The merge reads directionBranchCount * 4 atlas entries per ray,
                // at scattered indices, for every cascade that has a coarser one
                // above it.
                long mergeTaps = 0;
                if (index + 1 < cascades.Count)
                {
                    int branchCount = Mathf.Clamp(
                        cascades[index + 1].DirectionCount / Mathf.Max(1, cascade.DirectionCount),
                        1,
                        4);
                    mergeTaps = (long)cascade.EntryCount * branchCount * 4;
                }

                destination.Add(new CascadeCostSample(
                    index,
                    cascade.ProbeWidth,
                    cascade.ProbeHeight,
                    cascade.DirectionCount,
                    cascade.IntervalStart,
                    cascade.IntervalEnd,
                    stepCount,
                    cascade.EntryCount,
                    (long)cascade.EntryCount * stepCount,
                    mergeTaps));
            }
        }
    }
}
