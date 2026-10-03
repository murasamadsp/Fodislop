#nullable enable

using UnityEngine;
using NUnit.Framework;
using Kern.World.Lighting;
using Kern.World.Streaming;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public class LightingRegionCalculatorTests
{
    [Test]
    public void FreshRegionUsesTransportPaddingAndQuantizesAllocation()
    {
        // No previous region (NaN) => compute from scratch. The visible area
        // is 100x100 at origin; transport padding is 16 cells and allocation
        // dimensions are quantized independently by the governor.
        Vector4 region = LightingRegionCalculator.GetStableLightingRegion(
            visibleMinX: 0,
            visibleMinY: 0,
            visibleWidth: 100,
            visibleHeight: 100,
            lastVisibleRegion: new Vector4(float.NaN, 0, 0, 0));

        Assert.That(region.x, Is.EqualTo(-32f), "West edge must include transport padding and policy alignment.");
        Assert.That(region.y, Is.EqualTo(-32f), "South edge must include transport padding and policy alignment.");
        Assert.That(region.z, Is.EqualTo(192f), "Width must include policy alignment slack.");
        Assert.That(region.w, Is.EqualTo(192f), "Height must include policy alignment slack.");
    }

    [Test]
    public void ContainedRegionIsReturnedUnchanged()
    {
        Vector4 previous = new(0, 0, 200, 200);

        // A viewport that stays inside the previous allocated region must NOT
        // trigger a recompute: this is the churn the governor prevents.
        Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
            visibleMinX: 40,
            visibleMinY: 40,
            visibleWidth: 20,
            visibleHeight: 20,
            lastVisibleRegion: previous);

        Assert.That(result, Is.EqualTo(previous));
    }

    [Test]
    public void EscapingTheAllocatedRegionForcesAReanchoredRegion()
    {
        // A viewport that slides beyond the allocated region must re-anchor
        // the whole field, not drift.
        Vector4 previous = new(0, 0, 200, 200);
        Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
            visibleMinX: 160,
            visibleMinY: 0,
            visibleWidth: 50,
            visibleHeight: 50,
            lastVisibleRegion: previous);

        Assert.That(result.x, Is.Not.EqualTo(previous.x), "Region must move with the camera.");
        Assert.That(result.x, Is.EqualTo(128f), "Re-anchored west edge must follow the policy-aligned viewport.");
        Assert.That(result.z % 32, Is.EqualTo(0f), "Re-anchored width must stay on a 32-cell quantum.");
    }

    // Регрессия: размер региона зависел от привязки угла к сетке, и кадр
    // одного размера давал разную высоту при движении — свет перестраивался.
    [Test]
    public void RegionSizeDoesNotDependOnPosition()
    {
        Vector4 first = LightingRegionCalculator.GetStableLightingRegion(
            0, 0, 111, 62, new Vector4(float.NaN, 0, 0, 0));
        for (int offset = 1; offset < 64; offset++)
        {
            Vector4 moved = LightingRegionCalculator.GetStableLightingRegion(
                offset, offset * 3, 111, 62, new Vector4(float.NaN, 0, 0, 0));
            Assert.That(moved.z, Is.EqualTo(first.z), $"width at offset {offset}");
            Assert.That(moved.w, Is.EqualTo(first.w), $"height at offset {offset}");
        }
    }

    // Регрессия: регион мерился текущим зумом и рос рекордами отдаления.
    // Каждый рост пересоздавал ресурсы света и считал всё заново — провис
    // около секунды на колесе мыши. Кадр 3420×1890, зум от середины до упора.
    [Test]
    public void ZoomingOutWithinCameraContractKeepsRegionSize()
    {
        const float aspect = 3420f / 1890f;
        const float maximumOrthographicSize = 30f;
        Vector4 previous = new(float.NaN, 0, 0, 0);
        Vector4 first = default;
        for (float orthographicSize = 16.5f; orthographicSize <= maximumOrthographicSize; orthographicSize += 0.5f)
        {
            int visibleWidth = Mathf.CeilToInt(orthographicSize * 2f * aspect);
            int visibleHeight = Mathf.CeilToInt(orthographicSize * 2f);
            Vector2Int sizing = LightingRegionCalculator.ResolveSizingViewport(
                orthographicSize, aspect, maximumOrthographicSize, cellSize: 1f);
            Vector4 next = LightingRegionCalculator.GetStableLightingRegion(
                -visibleWidth / 2,
                -visibleHeight / 2,
                visibleWidth,
                visibleHeight,
                previous,
                sizing);
            if (float.IsNaN(previous.x))
            {
                first = next;
            }

            Assert.That(next, Is.EqualTo(first), $"orthographic size {orthographicSize}");
            previous = next;
        }

        // Кадр на упоре: 109×60 клеток плюс по 16 клеток каймы с каждой
        // стороны и квант 32 — 192×128, а не 128×128 середины зума.
        Assert.That(first.z, Is.EqualTo(192f));
        Assert.That(first.w, Is.EqualTo(128f));
    }

    [Test]
    public void SizingViewportFollowsCameraBeyondMaximumZoom()
    {
        Vector2Int sizing = LightingRegionCalculator.ResolveSizingViewport(
            orthographicSize: 40f, aspect: 2f, maximumOrthographicSize: 30f, cellSize: 1f);

        Assert.That(sizing, Is.EqualTo(new Vector2Int(160, 80)));
    }

    [Test]
    public void RegionDoesNotShrinkWhenViewportNeedsLessSpace()
    {
        Vector4 previous = new(0, 0, 192, 160);
        Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
            visibleMinX: 300,
            visibleMinY: 300,
            visibleWidth: 40,
            visibleHeight: 40,
            lastVisibleRegion: previous);

        Assert.That(result.z, Is.EqualTo(192f));
        Assert.That(result.w, Is.EqualTo(160f));
    }

    [Test]
    public void WalkingViewportDoesNotReanchorEverySmallStep()
    {
        Vector4 previous = new(float.NaN, 0, 0, 0);
        int reanchors = 0;
        int lastReanchorPosition = int.MinValue;

        for (int position = 0; position <= 256; position++)
        {
            Vector4 next = LightingRegionCalculator.GetStableLightingRegion(
                visibleMinX: position,
                visibleMinY: 0,
                visibleWidth: 100,
                visibleHeight: 100,
                lastVisibleRegion: previous);
            if (float.IsNaN(previous.x) || next != previous)
            {
                reanchors++;
                if (lastReanchorPosition != int.MinValue)
                {
                    Assert.That(
                        position - lastReanchorPosition,
                        Is.GreaterThan(1),
                        "The streaming governor must not reanchor on consecutive cell steps.");
                }

                lastReanchorPosition = position;
            }

            previous = next;
        }

        Assert.That(reanchors, Is.GreaterThan(1),
            "The walking fixture must exercise more than the initial region.");
    }

    [Test]
    public void WalkingViewportReanchorsNoMoreOftenThanPolicyQuantum()
    {
        Vector4 previous = new(float.NaN, 0, 0, 0);
        int lastReanchorPosition = int.MinValue;
        int reanchorCount = 0;
        int minimumInterval = int.MaxValue;

        for (int position = 0; position <= 512; position++)
        {
            Vector4 next = LightingRegionCalculator.GetStableLightingRegion(
                visibleMinX: position,
                visibleMinY: 0,
                visibleWidth: 100,
                visibleHeight: 100,
                lastVisibleRegion: previous);
            if (float.IsNaN(previous.x) || next != previous)
            {
                if (lastReanchorPosition != int.MinValue)
                {
                    minimumInterval = Mathf.Min(
                        minimumInterval,
                        position - lastReanchorPosition);
                }

                lastReanchorPosition = position;
                reanchorCount++;
            }

            previous = next;
        }

        Assert.That(reanchorCount, Is.GreaterThan(1));
        Assert.That(
            minimumInterval,
            Is.GreaterThanOrEqualTo(StreamingPolicy.Default.AllocationQuantumCells),
            "Lighting region must advance at policy cadence, never at a hidden smaller step.");
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

    [Test]
    public void TouchesStableRegionIncludesExactlyOneCellMargin()
    {
        Vector4 stable = new(0, 0, 10, 10);

        Assert.That(
            LightingRegionCalculator.TouchesStableRegion(10, 0, 1, 1, stable),
            Is.True,
            "The first cell immediately outside the right edge must invalidate lighting.");
        Assert.That(
            LightingRegionCalculator.TouchesStableRegion(11, 0, 1, 1, stable),
            Is.False,
            "A second cell outside the right edge must not invalidate lighting.");
        Assert.That(
            LightingRegionCalculator.TouchesStableRegion(0, 10, 1, 1, stable),
            Is.True,
            "The first cell immediately below the region must invalidate lighting.");
        Assert.That(
            LightingRegionCalculator.TouchesStableRegion(0, 11, 1, 1, stable),
            Is.False,
            "A second cell below the region must not invalidate lighting.");
    }
}
