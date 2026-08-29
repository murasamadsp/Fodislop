#nullable enable

using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Simulated spring-chain tail segment. Owns only simulation state —
/// rendering is delegated to <see cref="WorldEntityBatchRenderer"/>, which
/// merges all tentacles of all robots into one atlas-backed mesh.
/// </summary>
public class Tentacle
{
    private const float MAX_SEGMENT_DIST = 0.24f;
    private const float START_WIDTH = 0.17f;
    private const float END_WIDTH = 0.02f;

    private readonly WorldEntityBatchRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly float _wiggleOffset;
    private readonly float _lengthScale;
    private readonly float _sliceOffsetV;
    private readonly float _sliceScaleV;
    private readonly Vector3[] _positions;
    private readonly Vector3[] _prevPositions;
    private readonly Vector3[] _renderPoints;
    private readonly float[] _segmentLengths;
    private bool _isActive = true;

    public Tentacle(
        WorldEntityBatchRenderer renderer,
        Texture2D texture,
        Vector3 startPosition,
        float wiggleOffset,
        int sliceIndex,
        int totalSlices,
        float lengthScale = 1f)
    {
        _renderer = renderer;
        _texture = texture;
        _wiggleOffset = wiggleOffset;
        _lengthScale = lengthScale;

        const int count = WorldEntityBatchRenderer.POINT_COUNT;
        _positions = new Vector3[count];
        _prevPositions = new Vector3[count];
        _renderPoints = new Vector3[count];
        _segmentLengths = new float[count];

        _sliceScaleV = 1.0f / totalSlices;
        _sliceOffsetV = sliceIndex * _sliceScaleV;

        for (int i = 0; i < count; i++)
        {
            _positions[i] = startPosition;
            _prevPositions[i] = startPosition;
            _renderPoints[i] = startPosition;
        }

        _renderer.Register(this, _texture);
    }

    public Vector3 GetRenderPoint(int index)
    {
        return _renderPoints[index];
    }

    public bool IsActive => _isActive;

    internal Texture2D Texture => _texture;

    public bool IsSettled
    {
        get
        {
            for (int i = 1; i < _positions.Length; i++)
            {
                if ((_positions[i] - _prevPositions[i]).sqrMagnitude > 1e-7f)
                {
                    return false;
                }
            }

            return true;
        }
    }

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
        for (int i = 0; i < _positions.Length; i++)
        {
            _positions[i] = position;
            _prevPositions[i] = position;
            _renderPoints[i] = position;
        }

        _renderer.MarkDirty(_texture);
    }

    public void Update(Vector3 rootPosition, float rotationAngle, float movementFactor, float deltaTime)
    {
        if (!_isActive)
        {
            return;
        }

        float angleRad = rotationAngle * Mathf.Deg2Rad;
        Vector3 backwardDir = new Vector3(-Mathf.Cos(angleRad), -Mathf.Sin(angleRad), 0f);
        float spreadAngle = (rotationAngle + _wiggleOffset) * Mathf.Deg2Rad;
        Vector3 spreadDir = new Vector3(Mathf.Cos(spreadAngle), Mathf.Sin(spreadAngle), 0f);

        // Curl: each strand bends toward its own spread side, so the fan
        // curves open instead of trailing as four straight spikes. The sign of
        // the offset picks the side; the force is constant, which gives the
        // tail a characterful resting pose even when the robot stands still.
        float curlSign = _wiggleOffset >= 0f ? 1f : -1f;
        Vector3 curlDir = new Vector3(-spreadDir.y, spreadDir.x, 0f) * curlSign;
        Vector3 driftBias = (backwardDir * (0.40f * movementFactor))
                          + (spreadDir * (0.08f * movementFactor))
                          + (curlDir * 0.055f);

        // 1. Pin root
        _positions[0] = rootPosition;
        _prevPositions[0] = rootPosition;
        _renderPoints[0] = rootPosition;
        _segmentLengths[0] = 0f;

        // 2. Verlet Step with inertia and damping
        float damping = Mathf.Clamp(1.0f - (deltaTime * 12f), 0.70f, 0.95f);
        for (int i = 1; i < _positions.Length; i++)
        {
            Vector3 velocity = (_positions[i] - _prevPositions[i]) * damping;
            _prevPositions[i] = _positions[i];
            _positions[i] += velocity + (driftBias * (deltaTime * 4f));
        }

        // 3. PBD Distance Constraints (Relaxation iterations)
        float targetSegmentDist = MAX_SEGMENT_DIST * _lengthScale * Mathf.Max(0.5f, movementFactor);
        for (int iter = 0; iter < 3; iter++)
        {
            _positions[0] = rootPosition;
            for (int i = 1; i < _positions.Length; i++)
            {
                Vector3 delta = _positions[i] - _positions[i - 1];
                float dist = delta.magnitude;
                if (dist > 1e-5f)
                {
                    float diff = (dist - targetSegmentDist) / dist;
                    _positions[i] -= delta * (diff * 0.8f);
                }
                else
                {
                    _positions[i] = _positions[i - 1] + (backwardDir * 0.05f);
                }
            }
        }

        // 4. Procedural traveling wave for secondary motion. The phase sweeps
        // from root to tip (a wave that runs along the tail), amplitude grows
        // toward the tip so the end whips while the base stays attached, and a
        // slow idle sway keeps the tail alive when the robot stands still.
        int lastIndex = _positions.Length - 1;
        for (int i = 1; i < _positions.Length; i++)
        {
            float t = (float)i / lastIndex;
            float phase = (Time.time * 8f) - (t * 4.6f) + _wiggleOffset;
            float amplitude = (0.035f + (0.16f * movementFactor))
                * (0.35f + (0.65f * t))
                + (t * t * 0.05f);
            float wiggle = Mathf.Sin(phase) * amplitude;
            Vector3 direction = _positions[i] - _positions[i - 1];
            if (direction.sqrMagnitude < 1e-6f)
            {
                direction = backwardDir;
            }
            else
            {
                direction.Normalize();
            }

            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f);
            _renderPoints[i] = _positions[i] + (perpendicular * wiggle);
            _segmentLengths[i] = Vector3.Distance(_renderPoints[i], _renderPoints[i - 1]);
        }

        _renderer.MarkDirty(_texture);
    }

    /// <summary>
    /// Emits a billboarded quad strip (2 verts per chain point) for the 2D
    /// orthographic camera, replacing what LineRenderer used to rebuild on
    /// the CPU every frame per tentacle.
    /// </summary>
    public void WriteGeometry(
        Vector3[] verts,
        Vector2[] uvs,
        int vertBase,
        Rect atlasRect)
    {
        const int count = WorldEntityBatchRenderer.POINT_COUNT;

        float totalLength = 0f;
        for (int i = 1; i < count; i++)
        {
            totalLength += _segmentLengths[i];
        }

        float accumLength = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 direction;
            if (i == 0)
            {
                direction = _renderPoints[1] - _renderPoints[0];
            }
            else if (i == count - 1)
            {
                direction = _renderPoints[count - 1] - _renderPoints[count - 2];
            }
            else
            {
                direction = _renderPoints[i + 1] - _renderPoints[i - 1];
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
                accumLength += _segmentLengths[i + 1];
            }

            int vi = vertBase + (i * 2);
            Vector3 p = _renderPoints[i];
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
