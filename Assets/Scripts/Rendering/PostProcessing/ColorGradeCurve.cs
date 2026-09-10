#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

public enum ColorCurveInterpolation
{
    Linear = 0,
    Smooth = 1,
}

/// <summary>Одна ограниченная RGB/luma-кривая рабочего места колориста.</summary>
public sealed class ColorGradeCurve
{
    public const int MaxPoints = 16;

    private readonly Vector2[] _points = new Vector2[MaxPoints];

    public ColorGradeCurve()
    {
        Reset();
    }

    public int PointCount { get; private set; }

    public ColorCurveInterpolation Interpolation { get; set; }

    public IReadOnlyList<Vector2> Points => _points;

    public Vector4[] ToShaderPoints()
    {
        var result = new Vector4[MaxPoints];
        for (int index = 0; index < MaxPoints; index++)
        {
            result[index] = new Vector4(_points[index].x, _points[index].y, 0f, 0f);
        }

        return result;
    }

    public Vector2 GetPoint(int index)
    {
        if (index < 0 || index >= PointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _points[index];
    }

    public void Reset()
    {
        PointCount = 2;
        _points[0] = Vector2.zero;
        _points[1] = Vector2.one;
        Interpolation = ColorCurveInterpolation.Smooth;
        ClearUnusedPoints();
    }

    public int AddPoint(Vector2 point)
    {
        if (PointCount >= MaxPoints)
        {
            return -1;
        }

        point = SanitizePoint(point);
        int index = PointCount;
        while (index > 1 && _points[index - 1].x > point.x)
        {
            _points[index] = _points[index - 1];
            index--;
        }

        _points[index] = point;
        PointCount++;
        Sanitize();
        return index;
    }

    public bool RemovePoint(int index)
    {
        if (index <= 0 || index >= PointCount - 1)
        {
            return false;
        }

        for (int pointIndex = index; pointIndex < PointCount - 1; pointIndex++)
        {
            _points[pointIndex] = _points[pointIndex + 1];
        }

        PointCount--;
        ClearUnusedPoints();
        return true;
    }

    public void SetPoint(int index, Vector2 point)
    {
        if (index < 0 || index >= PointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _points[index] = SanitizePoint(point);
        Sanitize();
    }

    public void Load(Vector2[]? points, int interpolation)
    {
        Reset();
        if (points != null)
        {
            PointCount = Mathf.Clamp(points.Length, 2, MaxPoints);
            for (int index = 0; index < PointCount; index++)
            {
                _points[index] = points[index];
            }
        }

        Interpolation = (ColorCurveInterpolation)interpolation;
        Sanitize();
    }

    public float Evaluate(float x)
    {
        x = float.IsFinite(x) ? Mathf.Clamp01(x) : 0f;
        if (PointCount == 2 &&
            _points[0] == Vector2.zero &&
            _points[1] == Vector2.one)
        {
            return x;
        }

        if (x <= _points[0].x)
        {
            return _points[0].y;
        }

        for (int index = 1; index < PointCount; index++)
        {
            Vector2 right = _points[index];
            if (x <= right.x)
            {
                Vector2 left = _points[index - 1];
                float width = Mathf.Max(right.x - left.x, 1e-5f);
                float t = Mathf.Clamp01((x - left.x) / width);
                if (Interpolation == ColorCurveInterpolation.Smooth)
                {
                    t = t * t * (3f - 2f * t);
                }

                return Mathf.Lerp(left.y, right.y, t);
            }
        }

        return _points[PointCount - 1].y;
    }

    public ColorGradeCurve Clone()
    {
        var clone = new ColorGradeCurve
        {
            PointCount = PointCount,
            Interpolation = Interpolation,
        };
        Array.Copy(_points, clone._points, _points.Length);
        return clone;
    }

    public void Sanitize()
    {
        PointCount = Mathf.Clamp(PointCount, 2, MaxPoints);
        Vector2 first = SanitizePoint(_points[0]);
        _points[0] = new Vector2(0f, first.y);
        for (int index = 1; index < PointCount; index++)
        {
            Vector2 point = SanitizePoint(_points[index]);
            float minimumX = _points[index - 1].x + 1e-4f;
            float maximumX = 1f - (PointCount - 1 - index) * 1e-4f;
            _points[index] = new Vector2(
                Mathf.Clamp(point.x, minimumX, maximumX),
                point.y);
        }

        Vector2 last = SanitizePoint(_points[PointCount - 1]);
        _points[PointCount - 1] = new Vector2(1f, last.y);
        if (!Enum.IsDefined(typeof(ColorCurveInterpolation), Interpolation))
        {
            Interpolation = ColorCurveInterpolation.Smooth;
        }

        ClearUnusedPoints();
    }

    private static Vector2 SanitizePoint(Vector2 point) => new(
        float.IsFinite(point.x) ? Mathf.Clamp01(point.x) : 0.5f,
        float.IsFinite(point.y) ? Mathf.Clamp01(point.y) : 0.5f);

    private void ClearUnusedPoints()
    {
        for (int index = PointCount; index < _points.Length; index++)
        {
            _points[index] = Vector2.zero;
        }
    }
}
