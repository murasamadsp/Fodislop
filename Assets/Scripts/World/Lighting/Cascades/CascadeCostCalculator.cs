#nullable enable

using System;
using System.Collections.Generic;

namespace Kern.World.Lighting;
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

public static class CascadeCostCalculator
{
    public static long EstimateRayWorkUnits(IReadOnlyList<CascadeLayout> cascades)
    {
        if (cascades == null)
        {
            throw new ArgumentNullException(nameof(cascades));
        }

        var samples = new List<CascadeCostSample>(cascades.Count);
        CollectCascadeCosts(cascades, int.MaxValue, samples);

        long total = 0;
        foreach (CascadeCostSample sample in samples)
        {
            total = checked(total + sample.RayStepCount);
        }

        return total;
    }

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

            // Conservative DDA texel visits, including each connected merge
            // path. Early opacity termination and field clipping reduce this.
            // maximumSteps is retained for caller compatibility; it no longer
            // permits the transport solver to skip geometry.
            double traceLength = Math.Max((double)cascade.IntervalEnd - cascade.IntervalStart, 0d);
            int traceCount = 1;
            long mergeTaps = 0;
            if (index + 1 < cascades.Count)
            {
                CascadeLayout far = cascades[index + 1];
                int branchCount = Math.Clamp(
                    far.DirectionCount / Math.Max(1, cascade.DirectionCount),
                    1,
                    4);
                traceCount = branchCount * 4;
                mergeTaps = (long)cascade.EntryCount * traceCount;
                traceLength = Math.Abs((double)cascade.IntervalStart) + Math.Abs((double)far.IntervalStart) +
                    Math.Sqrt(2d) * far.ProbeSpacing;
            }

            double estimatedSteps = Math.Ceiling(Math.Sqrt(2d) * traceLength) + 2d;
            int stepsPerTrace = estimatedSteps >= int.MaxValue ? int.MaxValue : (int)estimatedSteps;
            long totalSteps = (long)stepsPerTrace * traceCount;
            int stepCount = (int)Math.Min(totalSteps, int.MaxValue);

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
