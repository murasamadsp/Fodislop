#nullable enable

using System;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>
/// Allocates the frame-wide angular ray budget among dynamic lights that need tracing.
/// </summary>
internal static class DynamicPolarWorkBudget
{
    // Polar tracing cost is angles * emitter points * ray length. One shared
    // frame budget prevents each visible light from multiplying the cap.
    private const long MaximumPolarRayWorkUnits = LightingConfigHolder.MaximumDynamicPolarRayWorkUnits;

    public static long EstimateWorstCaseRayWorkUnits(
        int lightWidth,
        int lightHeight,
        LightingQualityTuning quality,
        int maximumLightCount)
    {
        double diagonal = Math.Sqrt((double)lightWidth * lightWidth + (double)lightHeight * lightHeight);
        long maximumRayLength = checked((long)Math.Ceiling(diagonal) + quality.LightPixelsPerCell * 2L + 2L);
        return checked(maximumRayLength * quality.DynamicPolarDirectionCount *
            quality.DynamicEmitterPointsPerAxis * quality.DynamicEmitterPointsPerAxis * Math.Max(1, maximumLightCount));
    }

    public static void ValidateWorstCaseQuality(
        int lightWidth,
        int lightHeight,
        LightingQualityTuning quality,
        int maximumLightCount)
    {
        long requestedWork = EstimateWorstCaseRayWorkUnits(lightWidth, lightHeight, quality, maximumLightCount);
        long minimumWork = checked(requestedWork * 4 / quality.DynamicPolarDirectionCount);
        if (minimumWork > MaximumPolarRayWorkUnits)
        {
            throw new InvalidOperationException(
                $"Динамическому освещению нужно не менее {minimumWork:N0} отсчётов веера " +
                $"при минимальных 4 направлениях и {maximumLightCount} фонарях; бюджет — " +
                $"{MaximumPolarRayWorkUnits:N0}. Уменьши число фонарей или размер поля.");
        }
    }

    public static int RequiredRayLength(int count, Vector2Int[] raySizes)
    {
        int longestRay = 1;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            longestRay = Mathf.Max(longestRay, raySizes[lightIndex].y);
        }
        return longestRay;
    }

    public static int AllocateRayFans(
        int count,
        int[] requestedRayFans,
        bool[] needsTrace,
        Vector2Int[] raySizes,
        out int longestRay)
    {
        int maxTextureSize = SystemInfo.maxTextureSize;
        int maxRayLength = maxTextureSize;

        long rayLengthUnits = 0;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            if (!needsTrace[lightIndex])
            {
                continue;
            }

            Vector2Int raySize = raySizes[lightIndex];
            rayLengthUnits += (long)LightingComputeBinder.DynamicEmitterPointCount *
                Mathf.Max(1, raySize.y);
        }

        int authoredDirections = LightingQualityTuningController.DynamicPolarDirectionCount;
        int budgetedDirections = rayLengthUnits == 0
            ? authoredDirections
            : (int)Math.Min(authoredDirections, MaximumPolarRayWorkUnits / rayLengthUnits);
        if (rayLengthUnits > 0 && budgetedDirections < 4)
        {
            throw new InvalidOperationException($"Dynamic transport needs at least {checked(rayLengthUnits * 4)} ray work units " +
                $"for the visible lights; configured limit is {MaximumPolarRayWorkUnits}. Reduce light count or transport distance.");
        }

        int widestRayFan = 1;
        longestRay = 1;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            if (!needsTrace[lightIndex])
            {
                raySizes[lightIndex] = new Vector2Int(1, 1);
                continue;
            }

            int requested = Mathf.Min(authoredDirections, Mathf.Max(4, requestedRayFans[lightIndex]));
            requested = Mathf.Min(requested, budgetedDirections);
            // Each emitter has its own array layer. Never shorten transport
            // to fit stacked emitter rows or reuse edge depth beyond the ray.
            int rayLength = Mathf.Max(1, raySizes[lightIndex].y);
            if (rayLength > maxRayLength)
            {
                throw new InvalidOperationException(
                    $"Dynamic transport requires {rayLength} distance samples; texture limit is {maxRayLength}.");
            }
            int rayFan = requested;
            if (requested < 1 || requested + 2 > maxTextureSize)
            {
                throw new InvalidOperationException($"Dynamic angular quality {requested} cannot fit the texture limit {maxTextureSize}.");
            }

            // The filtered polar texture stores one wrap column at each edge.
            raySizes[lightIndex] = new Vector2Int(rayFan, rayLength);
            widestRayFan = Mathf.Max(widestRayFan, rayFan);
            longestRay = Mathf.Max(longestRay, rayLength);
        }

        return widestRayFan;
    }
}
