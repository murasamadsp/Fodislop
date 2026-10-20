#nullable enable

using Kern.World;
using UnityEngine;

namespace Kern.Game;

public sealed class RobotMovement
{
    // Original RobotController constructor (0x1A926D8), Update (0x1A8F6A4).
    // These are visual response coefficients in 1/seconds, not step speed
    // or an angular velocity limit. Logical movement remains server-owned.
    private const float PositionResponsePerSecond = 5f;
    private const float RotationResponsePerSecond = 10f;

    private Vector3 _targetPosition;
    private Vector3 _serverPosition;
    private Vector3 _smoothPosition;
    private Vector3 _currentVelocity;
    private float _targetAngle;
    private Quaternion _smoothRotation = Quaternion.identity;
    private bool _hasReceivedInitialPosition;

    public Vector3 TargetPosition
    {
        get => _targetPosition;
        set => _targetPosition = value;
    }

    public Vector3 ServerPosition => _serverPosition;
    public Vector3 SmoothPosition => _smoothPosition;
    public float TargetAngle
    {
        get => _targetAngle;
        set => _targetAngle = value;
    }
    public bool HasReceivedInitialPosition => _hasReceivedInitialPosition;

    public void SnapTo(Vector3 position, float angle)
    {
        _hasReceivedInitialPosition = true;
        InitializePose(position, angle);
    }

    // A scene/factory pose is not a position received from the server.
    public void InitializePose(Vector3 position, float angle)
    {
        _targetPosition = position;
        _serverPosition = position;
        _smoothPosition = position;
        _targetAngle = angle;
        _smoothRotation = Quaternion.Euler(0f, 0f, angle);
        _currentVelocity = Vector3.zero;
    }

    public bool ApplyServerPosition(ushort x, ushort y, int worldHeight, bool isLocalPlayer, out bool isInitial)
    {
        _serverPosition = CoordinateUtils.ServerToUnityPos(x, y, worldHeight);

        if (!_hasReceivedInitialPosition)
        {
            _hasReceivedInitialPosition = true;
            _smoothPosition = _serverPosition;
            _targetPosition = _serverPosition;
            _currentVelocity = Vector3.zero;
            isInitial = true;
            return true;
        }

        isInitial = false;
        if (!isLocalPlayer || Vector3.Distance(_targetPosition, _serverPosition) > 2.0f)
        {
            _targetPosition = _serverPosition;
            return true;
        }

        return false;
    }

    public bool IsSettled(bool tentaclesSettled)
    {
        return (_smoothPosition - _targetPosition).sqrMagnitude <= 1e-8f &&
               _currentVelocity.sqrMagnitude <= 1e-8f &&
               Quaternion.Angle(_smoothRotation, Quaternion.Euler(0f, 0f, _targetAngle)) <= 0.001f &&
               tentaclesSettled;
    }

    public void TeleportToTarget()
    {
        _smoothPosition = _targetPosition;
        _smoothRotation = Quaternion.Euler(0f, 0f, _targetAngle);
        _currentVelocity = Vector3.zero;
    }

    public (Vector3 position, float angle, float movementFactor) Step(float deltaTime)
    {
        Vector3 previousPosition = _smoothPosition;
        // Match the reference's per-frame clamped Lerp, including its response
        // to a long frame. There is no spring velocity or distance-based gain.
        _smoothPosition = Vector3.Lerp(
            _smoothPosition, _targetPosition, PositionResponsePerSecond * deltaTime);
        _smoothRotation = Quaternion.Slerp(
            _smoothRotation,
            Quaternion.Euler(0f, 0f, _targetAngle),
            RotationResponsePerSecond * deltaTime);

        // The existing tail simulation consumes actual visual root velocity.
        // Never feed logical step cadence into the body's visual response.
        _currentVelocity = deltaTime > 0f
            ? (_smoothPosition - previousPosition) / deltaTime
            : Vector3.zero;
        float movementFactor = Mathf.Clamp01(_currentVelocity.magnitude / 5f);
        return (_smoothPosition, _smoothRotation.eulerAngles.z, movementFactor);
    }
}
