#nullable enable

using UnityEngine;

namespace Kern.Game;

public class Tentacle
{
    private const float START_WIDTH = 0.125f;
    private const float END_WIDTH = 0.02f;

    private readonly WorldEntityBatchRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly float _sliceOffsetV;
    private readonly float _sliceScaleV;
    private readonly TailChain _chain;
    private bool _isActive = true;

    public Tentacle(
        WorldEntityBatchRenderer renderer,
        Texture2D texture,
        Vector3 startPosition,
        int sliceIndex,
        int totalSlices)
    {
        _renderer = renderer;
        _texture = texture;
        _chain = new TailChain(sliceIndex, startPosition);

        _sliceScaleV = 1.0f / totalSlices;
        _sliceOffsetV = sliceIndex * _sliceScaleV;

        _renderer.Register(this, _texture);
    }

    public bool IsActive => _isActive;

    /// <summary>Used by <see cref="WorldEntityVisibility.IsTentacleInView" /> to cull off-screen tails.</summary>
    public Vector3 RootPosition => _chain[0];

    internal Texture2D Texture => _texture;

    public bool IsSettled => _chain.IsSettled;

    public void SetActive(bool active)
    {
        if (_isActive == active)
        {
            return;
        }

        _isActive = active;
        _renderer.MarkDirty(_texture);
    }

    public void Snap(Vector3 position)
    {
        _chain.Snap(position);
        _renderer.MarkDirty(_texture);
    }

    /// <summary>
    ///     No facing term: the chain hangs off the robot's rendered position and its direction
    ///     is a consequence of movement, exactly as in the previous client. The strands therefore
    ///     differ only in inertia, so a standing robot gathers them into a tuft instead of a fan.
    /// </summary>
    public void Update(Vector3 rootPosition, float movementFactor, float deltaTime)
    {
        if (!_isActive)
        {
            return;
        }

        _chain.Step(rootPosition, movementFactor, deltaTime);
        _renderer.MarkDirty(_texture);
    }

    public void WriteGeometry(
        Vector3[] verts,
        Vector2[] uvs,
        int vertBase,
        Rect atlasRect)
    {
        const int count = TailChain.PointCount;

        float totalLength = _chain.TotalLength;

        float accumLength = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 direction;
            if (i == 0)
            {
                direction = _chain[1] - _chain[0];
            }
            else if (i == count - 1)
            {
                direction = _chain[count - 1] - _chain[count - 2];
            }
            else
            {
                direction = _chain[i + 1] - _chain[i - 1];
            }

            if (direction.sqrMagnitude < 1e-10f)
            {
                direction = Vector3.down;
            }
            else
            {
                direction.Normalize();
            }

            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0);
            float t = (float)i / (count - 1);
            float halfWidth = Mathf.Lerp(START_WIDTH, END_WIDTH, t) * 0.5f;

            float u = totalLength > 1e-6f ? accumLength / totalLength : t;
            if (i + 1 < count)
            {
                accumLength += _chain.SegmentLength(i + 1);
            }

            int vi = vertBase + (i * 2);
            Vector3 p = _chain[i];
            verts[vi] = p - (perpendicular * halfWidth);
            verts[vi + 1] = p + (perpendicular * halfWidth);

            float atlasU = atlasRect.xMin + (u * atlasRect.width);
            uvs[vi] = new Vector2(
                atlasU,
                atlasRect.yMin + (_sliceOffsetV * atlasRect.height));
            uvs[vi + 1] = new Vector2(
                atlasU,
                atlasRect.yMin + ((_sliceOffsetV + _sliceScaleV) * atlasRect.height));
        }
    }

    public void Destroy()
    {
        if (_renderer != null)
        {
            _renderer.Unregister(this, _texture);
        }
    }
}
