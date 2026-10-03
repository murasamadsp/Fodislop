#nullable enable

using System;
using UnityEngine;

namespace Kern.Game;

/// <summary>
///     One strand of a robot's tail: a <see cref="TailChain" /> simulation plus the buffer
///     bookkeeping the shared batch renderer needs.
/// </summary>
/// <remarks>
///     No facing term, as in the previous client: the strands differ only in inertia, so a
///     standing robot gathers them into a tuft rather than a fan.
/// </remarks>
public sealed class Tentacle
{
    private readonly WorldEntityBatchRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly TailChain _chain;
    private readonly Vector3[] _points = new Vector3[TentacleGeometry.PointCount];
    private Vector3 _originOffset;
    private bool _isActive = true;

    /// <param name="strandIndex">
    ///     Index of this strand, which selects its inertia. Strand zero trails the furthest
    ///     behind and the last strand snaps back hardest.
    /// </param>
    public Tentacle(
        WorldEntityBatchRenderer renderer,
        Texture2D texture,
        Vector3 startPosition,
        int strandIndex,
        Func<float>? unitRandom = null)
    {
        _renderer = renderer;
        _texture = texture;
        _chain = new TailChain(strandIndex, startPosition, unitRandom);

        _renderer.Register(this, _texture);
    }

    public bool IsActive => _isActive;

    /// <summary>The real robot position, used to cull off-screen tails.</summary>
    /// <remarks>
    ///     Not the chain's root: the debug modes push that away from the robot, and culling
    ///     against it would drop a visible tail.
    /// </remarks>
    public Vector3 RootPosition => _chain[0] - _originOffset;

    /// <summary>Rendered tip, in robot space; the claw reaches past the last chain point.</summary>
    public Vector3 TipPosition => _chain[TentacleGeometry.PointCount - 1] - _originOffset;

    internal Texture2D Texture => _texture;

    public bool IsSettled => _chain.IsSettled;

    /// <inheritdoc cref="TailChain.TipDistanceTo" />
    public float TipDistanceTo(Vector3 root) => _chain.TipDistanceTo(root);

    public void SetActive(bool active)
    {
        if (_isActive == active)
        {
            return;
        }

        _isActive = active;
        if (!active)
        {
            // Nothing reads the frame translation while culled, but leaving the last one behind
            // would make RootPosition and TipPosition report a stale frame on the way back in.
            _originOffset = Vector3.zero;
        }

        _renderer.MarkDirty(_texture);
    }

    public void Snap(Vector3 position)
    {
        _originOffset = Vector3.zero;
        _chain.Snap(position);
        _renderer.MarkDirty(_texture);
    }

    /// <param name="rootPosition">The robot's real rendered position.</param>
    /// <param name="originOffset">
    ///     Translation to simulate in. The chain gets the root plus this and the geometry
    ///     subtracts it again, so the strip stays welded to the body.
    /// </param>
    /// <param name="stretch">Anti-stretch distance shared by every strand.</param>
    public void Update(
        Vector3 rootPosition,
        Vector3 originOffset,
        float movementFactor,
        float deltaTime,
        float stretch)
    {
        // Recorded before the early-out so the frame translation is never a frame behind what
        // the owner asked for.
        _originOffset = originOffset;
        if (!_isActive)
        {
            return;
        }

        _chain.Step(rootPosition + originOffset, movementFactor, deltaTime, stretch);
        _renderer.MarkDirty(_texture);
    }

    public void WriteGeometry(
        Vector3[] verts,
        Vector2[] uvs,
        int vertBase,
        Rect atlasRect)
    {
        for (int i = 0; i < TentacleGeometry.PointCount; i++)
        {
            _points[i] = _chain[i];
        }

        TentacleGeometry.WriteVertices(_points, atlasRect, verts, uvs, vertBase, _originOffset);
    }

    public void Destroy()
    {
        _renderer.Unregister(this, _texture);
    }
}