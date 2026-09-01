#nullable enable

using UnityEngine;

namespace Fodinae.UI;

internal static class MenuSceneryProjection
{
    private const float OrbitRadius = 1.72f;
    private static readonly Vector3 _orbitTilt = new(72f, 0f, -19f);

    public static bool TryGetStationViewportPosition(
        Camera? camera,
        OrbitalStationMotion? station,
        Transform? occluder,
        out Vector2 viewportPosition)
    {
        viewportPosition = default;
        if (camera == null || station == null)
        {
            return false;
        }

        Vector3 stationPosition = station.transform.position;
        if (IsOccluded(camera, stationPosition, occluder))
        {
            return false;
        }

        return TryProject(camera, stationPosition, out viewportPosition);
    }

    public static bool TryGetOrbitPointViewportPosition(
        Camera? camera,
        Transform center,
        float angleDegrees,
        out Vector2 viewportPosition)
    {
        var localOffset = new Vector3(
            Mathf.Cos(angleDegrees * Mathf.Deg2Rad),
            0f,
            Mathf.Sin(angleDegrees * Mathf.Deg2Rad)) * OrbitRadius;
        Vector3 point = center.position + (Quaternion.Euler(_orbitTilt) * localOffset);
        return TryProject(camera, point, out viewportPosition);
    }

    public static bool TryGetSurfaceViewportPosition(
        Camera? camera,
        Transform? planet,
        Vector3 localSurfaceDirection,
        out Vector2 viewportPosition)
    {
        viewportPosition = default;
        if (planet == null)
        {
            return false;
        }

        float radius = 0.5f * planet.lossyScale.x;
        Vector3 point = planet.position + (localSurfaceDirection.normalized * radius);
        return TryProject(camera, point, out viewportPosition);
    }

    private static bool TryProject(
        Camera? camera,
        Vector3 worldPosition,
        out Vector2 viewportPosition)
    {
        viewportPosition = default;
        if (camera == null)
        {
            return false;
        }

        Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
        if (viewport.z <= 0f)
        {
            return false;
        }

        viewportPosition = new Vector2(viewport.x, viewport.y);
        return true;
    }

    private static bool IsOccluded(Camera camera, Vector3 point, Transform? occluder)
    {
        if (occluder == null)
        {
            return false;
        }

        Vector3 cameraPosition = camera.transform.position;
        Vector3 toOccluder = occluder.position - cameraPosition;
        Vector3 toPoint = point - cameraPosition;
        if (toPoint.magnitude <= toOccluder.magnitude)
        {
            return false;
        }

        float radius = occluder.lossyScale.x * 0.5f;
        float offAxis = Vector3.ProjectOnPlane(toPoint, toOccluder.normalized).magnitude;
        return offAxis < radius;
    }
}
