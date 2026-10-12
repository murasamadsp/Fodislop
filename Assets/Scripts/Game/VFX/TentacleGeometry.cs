#nullable enable

using UnityEngine;

namespace Kern.Game;

/// <summary>
///     Ribbon layout of one tentacle, plus the claw that caps it.
/// </summary>
/// <remarks>
///     Ported from the previous client's <c>RobotScript.UpdateTailVertices</c>; the vertex
///     sharing and the claw are new. Two legacy defects are deliberately not reproduced, and
///     docs/architecture/TAIL_RENDERING.md has the background and the measurements.
/// </remarks>
internal static class TentacleGeometry
{
    /// <summary>Point count of one strand: the robot's own point plus four chain nodes.</summary>
    public const int PointCount = TailChain.PointCount;

    public const int SectorCount = PointCount - 1;

    public const int VerticesPerPoint = 2;

    /// <summary>
    ///     Total ribbon width is <c>2 / HalfWidthDivisor</c>. The legacy divisor of 16 assumed a
    ///     body one world unit wide; the batched body is 0.32.
    /// </summary>
    public const float HalfWidthDivisor = 25f;

    public const float RibbonHalfWidth = 1f / HalfWidthDivisor;

    /// <summary>Points along the claw's semicircular stroke.</summary>
    public const int ClawPoints = 7;

    /// <summary>
    ///     Width of the claw stroke. Must stay at or above the ribbon width: covering the
    ///     ribbon's flat end relies on the stroke being the wider of the two, or the cut shows
    ///     past its edges.
    /// </summary>
    public const float ClawStrokeWidth = 0.09f;

    /// <summary>Radius of the claw's centre line, measured forward from the tip.</summary>
    public const float ClawRadius = 0.09f;

    /// <summary>Prong tip width as a fraction of the stroke, so the prongs end in points.</summary>
    public const float ClawTipTaper = 0.15f;

    /// <summary>Fraction of the arc, at each end, over which the stroke ramps to full width.</summary>
    public const float ClawTaperFraction = 0.25f;

    public const int ClawVertices = ClawPoints * VerticesPerPoint;

    public const int ClawIndices = (ClawPoints - 1) * 6;

    public const int RibbonVertexCount = PointCount * VerticesPerPoint;

    public const int RibbonIndexCount = SectorCount * 6;

    public const int VerticesPerTentacle = RibbonVertexCount + ClawVertices;

    public const int IndicesPerTentacle = RibbonIndexCount + ClawIndices;

    /// <summary>Cap on the mitre, so a joint that folds back cannot spike the silhouette.</summary>
    private const float MaxMiterScale = 2f;

    private const float DegenerateDirectionSquared = 1e-10f;

    /// <summary>Smallest cosine treated as a real angle; below it the mitre divides by ~zero.</summary>
    private const float MinCosHalfTurn = 1e-4f;

    /// <summary>Writes one strand's vertices and UVs as a ribbon plus a claw.</summary>
    /// <param name="points">The rendered strip points, root first; <see cref="PointCount" /> of them.</param>
    /// <param name="atlasRect">Region of the shared atlas holding this tentacle's texture.</param>
    /// <param name="verts">Destination vertex buffer.</param>
    /// <param name="uvs">Destination UV buffer.</param>
    /// <param name="vertBase">First vertex slot to write.</param>
    /// <param name="originOffset">
    ///     Translation the simulation ran in, subtracted back out here. Without it the strip
    ///     detaches from the body, because point zero is the chain's root.
    /// </param>
    public static void WriteVertices(
        Vector3[] points,
        Rect atlasRect,
        Vector3[] verts,
        Vector2[] uvs,
        int vertBase,
        Vector3 originOffset = default)
    {
        Vector3 previous = SegmentDirection(points, 0, originOffset);
        Vector3 next = previous;

        for (int point = 0; point < PointCount; point++)
        {
            if (point > 0)
            {
                previous = next;
                // The tip has no segment after it, so it keeps the last segment's direction on
                // both sides and its offset is the plain perpendicular of that segment.
                next = point < PointCount - 1
                    ? SegmentDirection(points, point, originOffset)
                    : previous;
            }

            Vector3 centre = points[point] - originOffset;
            Vector3 offset = MitredOffset(previous, next, RibbonHalfWidth);
            int vertex = vertBase + (point * VerticesPerPoint);

            verts[vertex] = centre + offset;
            verts[vertex + 1] = centre - offset;

            // U runs the length of the tail once, V across its width: the texture's vertical
            // gradient is what gives the ribbon its shading.
            float u = atlasRect.xMin + (atlasRect.width * ((float)point / (PointCount - 1)));
            uvs[vertex] = new Vector2(u, atlasRect.yMin);
            uvs[vertex + 1] = new Vector2(u, atlasRect.yMax);
        }

        WriteClaw(points, atlasRect, verts, uvs, vertBase + RibbonVertexCount, originOffset);
    }

    /// <summary>Writes the claw that caps the ribbon's flat end.</summary>
    /// <remarks>
    ///     The rear apex of the arc sits exactly on the tip with the tangent across the tail, so
    ///     the stroke crosses the cut perpendicular and covers it. The arc steps by a constant
    ///     angle, which lets the mitre come from the step rather than neighbour lookups and keeps
    ///     this writer allocation-free.
    /// </remarks>
    private static void WriteClaw(
        Vector3[] points,
        Rect atlasRect,
        Vector3[] verts,
        Vector2[] uvs,
        int vertBase,
        Vector3 originOffset)
    {
        Vector3 tip = points[PointCount - 1] - originOffset;
        Vector3 outward = TrailingDirection(points, originOffset);
        Vector3 across = PerpendicularUnit(outward);

        float halfStroke = ClawStrokeWidth * 0.5f;
        float stepAngle = Mathf.PI / (ClawPoints - 1);
        float interiorMitre = Mathf.Min(1f / Mathf.Cos(stepAngle * 0.5f), MaxMiterScale);
        float halfAcrossExtent = ClawRadius + halfStroke;
        float u = atlasRect.xMin + (atlasRect.width * 0.5f);

        for (int i = 0; i < ClawPoints; i++)
        {
            float t = (float)i / (ClawPoints - 1);
            // A half circle, walked from one prong, through the apex on the tip, to the other.
            float angle = Mathf.PI * 0.5f + (Mathf.PI * t);

            // Local frame: "along" points away from the robot, "across" is its left.
            float along = ClawRadius + (ClawRadius * Mathf.Cos(angle));
            float sideways = ClawRadius * Mathf.Sin(angle);
            Vector3 centre = tip + (outward * along) + (across * sideways);

            Vector3 tangent =
                (outward * -Mathf.Sin(angle)) + (across * Mathf.Cos(angle));

            bool isProng = i == 0 || i == ClawPoints - 1;
            float taper = isProng
                ? ClawTipTaper
                : Mathf.Lerp(ClawTipTaper, 1f, Mathf.Min(t, 1f - t) / ClawTaperFraction);
            float halfWidth = halfStroke * taper * (isProng ? 1f : interiorMitre);

            Vector3 offset = Perpendicular(tangent, halfWidth);
            int vertex = vertBase + (i * VerticesPerPoint);

            verts[vertex] = centre + offset;
            verts[vertex + 1] = centre - offset;

            // V follows the across axis across the claw's own extent, so the texture's vertical
            // gradient keeps running and the claw takes the tail's shading.
            float span = halfAcrossExtent * 2f;
            uvs[vertex] = new Vector2(u, atlasRect.yMin + (atlasRect.height * ((sideways + halfWidth + halfAcrossExtent) / span)));
            uvs[vertex + 1] = new Vector2(u, atlasRect.yMin + (atlasRect.height * ((sideways - halfWidth + halfAcrossExtent) / span)));
        }
    }

    /// <summary>
    ///     Writes one strand's indices: two triangles per ribbon segment, then two per claw
    ///     segment, so every quad is closed and no vertex is left unreferenced.
    /// </summary>
    /// <param name="tris">Destination index buffer.</param>
    /// <param name="indexBase">First index slot to write.</param>
    /// <param name="vertexBase">First vertex slot the strand occupies.</param>
    public static void WriteIndices(int[] tris, int indexBase, int vertexBase)
    {
        WriteStripIndices(tris, indexBase, vertexBase, SectorCount);
        WriteStripIndices(
            tris,
            indexBase + RibbonIndexCount,
            vertexBase + RibbonVertexCount,
            ClawPoints - 1);
    }

    /// <summary>Two triangles per segment of a two-vertices-per-point strip.</summary>
    private static void WriteStripIndices(int[] tris, int indexBase, int vertexBase, int segmentCount)
    {
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int vertex = vertexBase + (segment * VerticesPerPoint);
            int index = indexBase + (segment * 6);

            tris[index] = vertex;
            tris[index + 1] = vertex + 1;
            tris[index + 2] = vertex + 2;
            tris[index + 3] = vertex + 2;
            tris[index + 4] = vertex + 1;
            tris[index + 5] = vertex + 3;
        }
    }

    /// <summary>Unit direction of the segment that starts at <paramref name="point" />.</summary>
    private static Vector3 SegmentDirection(Vector3[] points, int point, Vector3 originOffset)
    {
        Vector3 delta =
            (points[point + 1] - originOffset) - (points[point] - originOffset);
        Vector2 flat = new(delta.x, delta.y);
        if (flat.sqrMagnitude < DegenerateDirectionSquared)
        {
            return Vector3.down;
        }

        return new Vector3(flat.x, flat.y, 0f).normalized;
    }

    /// <summary>Direction the claw opens along: the last segment that has one.</summary>
    /// <remarks>
    ///     Right after a snap the last segment is degenerate, and a fixed fallback would stand the
    ///     claw on end at a right angle to the tail.
    /// </remarks>
    private static Vector3 TrailingDirection(Vector3[] points, Vector3 originOffset)
    {
        for (int point = PointCount - 2; point >= 0; point--)
        {
            Vector3 delta =
                (points[point + 1] - originOffset) - (points[point] - originOffset);
            if ((delta.x * delta.x) + (delta.y * delta.y) >= DegenerateDirectionSquared)
            {
                return new Vector3(delta.x, delta.y, 0f).normalized;
            }
        }

        // Every segment is degenerate: there is no direction to recover, so keep the claw from
        // picking an arbitrary one.
        return Vector3.left;
    }

    /// <summary>Offset of one point from the centre line, sized to half the ribbon width.</summary>
    /// <remarks>
    ///     Averaging two adjacent directions bisects the turn, so the length has to be mitred or
    ///     the ribbon pinches at every bend.
    /// </remarks>
    private static Vector3 MitredOffset(
        Vector3 previousDirection,
        Vector3 nextDirection,
        float offsetLength)
    {
        Vector3 bisector = previousDirection + nextDirection;
        if (bisector.sqrMagnitude < DegenerateDirectionSquared)
        {
            // A hard fold: the directions cancel, so there is no bisector to speak of.
            return Perpendicular(nextDirection, offsetLength);
        }

        Vector3 direction = bisector.normalized;
        float cosHalfTurn = Mathf.Clamp(Vector3.Dot(direction, nextDirection), 0f, 1f);
        float mitre = cosHalfTurn > MinCosHalfTurn
            ? Mathf.Min(1f / cosHalfTurn, MaxMiterScale)
            : MaxMiterScale;

        return Perpendicular(direction, offsetLength) * mitre;
    }

    /// <summary>Unit left normal of a direction.</summary>
    private static Vector3 PerpendicularUnit(Vector3 direction) => new(-direction.y, direction.x, 0f);

    /// <summary>Left normal of a direction, scaled to the given offset length.</summary>
    private static Vector3 Perpendicular(Vector3 direction, float offsetLength) =>
        PerpendicularUnit(direction) * offsetLength;
}