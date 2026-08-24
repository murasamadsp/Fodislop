#nullable enable

using UnityEngine;

namespace Fodinae.World.Terrain
{
    /// <summary>
    /// Accumulates the regions of the terrain grid that need patching before
    /// the next mesh update, as a bounded set of separate rectangles.
    /// </summary>
    /// <remarks>
    /// This used to be four ints on <see cref="TerrainRenderer"/> holding one
    /// min/max rectangle. That is correct for a player mining a single cell and
    /// badly wrong for streamed chunks: they arrive scattered across the
    /// viewport, each one small, and unioning two of them at opposite corners
    /// produces a rectangle covering the whole screen. The renderer's size
    /// check then measured that union, concluded the patch was not worth it,
    /// and handed the frame to the full rebuild - which is how the debug
    /// overlay came to read 9 rebuilds and 9 full repopulations. Every single
    /// one.
    ///
    /// Keeping the rectangles apart makes <see cref="TotalArea"/> describe the
    /// cells a patch will actually visit, which is what that decision needs.
    ///
    /// It is a separate type from the renderer because the interesting part is
    /// arithmetic - clamping, unioning, overflow - and a MonoBehaviour cannot
    /// be exercised without a scene. Here it can be fuzzed directly.
    /// </remarks>
    public sealed class DirtyRectSet
    {
        public const int MaximumRects = 8;

        private readonly RectInt[] _rects = new RectInt[MaximumRects];
        private int _count;

        public int Count => _count;

        public bool IsEmpty => _count == 0;

        public RectInt this[int index] => _rects[index];

        public void Clear()
        {
            _count = 0;
        }

        /// <summary>
        /// The number of cells a patch would visit: the sum of the rectangles,
        /// not the area of their bounding box.
        /// </summary>
        public long TotalArea
        {
            get
            {
                long total = 0;
                for (int i = 0; i < _count; i++)
                {
                    total += Area(_rects[i]);
                }

                return total;
            }
        }

        /// <summary>
        /// Records a rectangle to patch, clipped to <paramref name="bounds"/>.
        /// </summary>
        /// <returns>
        /// False when the rectangle lies wholly outside the bounds or is empty,
        /// in which case nothing was recorded.
        /// </returns>
        public bool Add(RectInt candidate, RectInt bounds)
        {
            RectInt clipped = Intersect(candidate, bounds);
            if (clipped.width <= 0 || clipped.height <= 0)
            {
                return false;
            }

            for (int i = 0; i < _count; i++)
            {
                RectInt existing = _rects[i];
                if (Contains(existing, clipped))
                {
                    return true;
                }

                // Merge only where the union costs no more than keeping the two
                // rectangles apart - touching or overlapping ones. Merging
                // distant rectangles is what produced the screen-sized union.
                RectInt union = Union(existing, clipped);
                if (Area(union) <= Area(existing) + Area(clipped))
                {
                    _rects[i] = union;
                    return true;
                }
            }

            if (_count < MaximumRects)
            {
                _rects[_count++] = clipped;
                return true;
            }

            // Out of slots. Absorb into whichever rectangle grows least, so the
            // overflow costs the smallest amount of extra area rather than
            // whatever happens to sit at index zero.
            int bestIndex = 0;
            long bestGrowth = long.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                long growth = Area(Union(_rects[i], clipped)) - Area(_rects[i]);
                if (growth < bestGrowth)
                {
                    bestGrowth = growth;
                    bestIndex = i;
                }
            }

            _rects[bestIndex] = Union(_rects[bestIndex], clipped);
            return true;
        }

        public static long Area(RectInt rect)
        {
            return (long)rect.width * rect.height;
        }

        private static bool Contains(RectInt outer, RectInt inner)
        {
            return inner.xMin >= outer.xMin && inner.xMax <= outer.xMax &&
                inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
        }

        private static RectInt Union(RectInt a, RectInt b)
        {
            int minX = Mathf.Min(a.xMin, b.xMin);
            int minY = Mathf.Min(a.yMin, b.yMin);
            int maxX = Mathf.Max(a.xMax, b.xMax);
            int maxY = Mathf.Max(a.yMax, b.yMax);
            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        private static RectInt Intersect(RectInt a, RectInt b)
        {
            // Built from long arithmetic because the server supplies the
            // candidate: a hostile or simply buggy chunk rectangle can carry
            // int.MaxValue extents, and computing xMax as x + width in int
            // overflows to a negative number, which would turn a rejected
            // rectangle into an accepted one.
            long aMinX = a.xMin;
            long aMinY = a.yMin;
            long aMaxX = aMinX + a.width;
            long aMaxY = aMinY + a.height;

            long minX = System.Math.Max(aMinX, b.xMin);
            long minY = System.Math.Max(aMinY, b.yMin);
            long maxX = System.Math.Min(aMaxX, b.xMax);
            long maxY = System.Math.Min(aMaxY, b.yMax);

            if (maxX <= minX || maxY <= minY)
            {
                return new RectInt(0, 0, 0, 0);
            }

            return new RectInt((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }
    }
}
