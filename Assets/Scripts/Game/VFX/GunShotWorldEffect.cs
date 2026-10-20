#nullable enable

using Kern.Core.Lifecycle;
using Kern.World;
using UnityEngine;

namespace Kern.Game;

// GunShot.Start/Update: 0x19B5B84 / 0x19B5DB4. Inputs are captured at fire time.
internal sealed class GunShotWorldEffect : IServerWorldEffect
{
    private const int LifetimeTicks = 20;
    private const float TicksPerSecond = 30f;
    private const int GlowSortingOrder = RenderingConstants.BUILDING_ROOF_SORTING_ORDER + 11;
    private const int LineSortingOrder = GlowSortingOrder + 6;
    private readonly GameObject _object;
    private readonly LineRenderer _line;
    private readonly LineRenderer _glow;
    private readonly LineRenderer[] _sparkLines = new LineRenderer[LifetimeTicks + 1];
    private readonly LineRenderer[] _sparkGlows = new LineRenderer[LifetimeTicks + 1];
    private readonly Vector3[] _points = new Vector3[LifetimeTicks + 1];
    private readonly Vector3 _target;
    private readonly double _createdAt;
    private uint _random;
    private int _tick;
    private int _count = 1;

    internal GunShotWorldEffect(Vector3 origin, Vector3 target,
        SpriteAnimationLibrary library, ISceneObjectFactory objects, uint seed)
    {
        _target = target;
        _points[0] = origin;
        _random = seed;
        _createdAt = Time.timeAsDouble;
        _object = objects.Create("SpriteAnimations/GunShot", RuntimeOwner.VFX);
        _line = CreateLine("Line", 0.14f, LineSortingOrder, library.GunLineMaterial, objects);
        _glow = CreateLine("Glow", 1.25f, GlowSortingOrder, library.GunGlowMaterial, objects);
        _glow.startColor = _glow.endColor = new Color(1f, Mathf.GammaToLinearSpace(68f / 255f), Mathf.GammaToLinearSpace(68f / 255f), 1f);
        for (int index = 0; index < _sparkLines.Length; index++)
        {
            _sparkLines[index] = CreateLine("Spark", 0.07f, LineSortingOrder, library.GunLineMaterial, objects);
            _sparkGlows[index] = CreateLine("SparkGlow", 0.2f, GlowSortingOrder, library.GunGlowMaterial, objects);
            _sparkLines[index].startColor = _sparkLines[index].endColor = _glow.startColor;
            _sparkGlows[index].startColor = _sparkGlows[index].endColor = _glow.startColor;
        }
    }

    public bool IsDisposed { get; private set; }

    public void Update()
    {
        if (IsDisposed)
        {
            return;
        }

        int targetTick = (int)((Time.timeAsDouble - _createdAt) * TicksPerSecond);
        if (targetTick > LifetimeTicks)
        {
            Dispose();
            return;
        }

        while (_tick < targetTick)
        {
            _tick++;
            _points[_count] = (_points[_count - 1] + _target) * 0.5f;
            _count++;
            for (int index = 1; index < _count; index++)
            {
                Vector3 noise = new((0.5f - NextRandom()) * 1.25f, (0.5f - NextRandom()) * 1.25f, 0f);
                _points[index] = (_points[index] + noise) * 0.8f + _points[index - 1] * 0.2f;
            }

            Color color = new(NextRandom(), NextRandom(), NextRandom(), 1f);
            _line.startColor = _line.endColor = color.linear;
            _line.positionCount = _glow.positionCount = _count;
            for (int index = 0; index < _count; index++)
            {
                _line.SetPosition(index, _points[index]);
                _glow.SetPosition(index, _points[index]);
                bool active = NextRandom() < (float)index / _count;
                LineRenderer spark = _sparkLines[index];
                LineRenderer glow = _sparkGlows[index];
                spark.enabled = glow.enabled = active;
                if (active)
                {
                    Vector3 end = _points[index] + new Vector3((0.5f - NextRandom()) * 1.875f, (0.5f - NextRandom()) * 1.875f, 0f);
                    spark.positionCount = glow.positionCount = 2;
                    spark.SetPosition(0, _points[index]);
                    spark.SetPosition(1, end);
                    glow.SetPosition(0, _points[index]);
                    glow.SetPosition(1, end);
                }
            }
        }
    }

    private LineRenderer CreateLine(string name, float width, int order, Material material, ISceneObjectFactory objects)
    {
        GameObject child = objects.Create(name, RuntimeOwner.VFX);
        child.transform.SetParent(_object.transform, worldPositionStays: false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.startWidth = line.endWidth = width;
        line.sortingOrder = order;
        line.positionCount = 0;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.textureMode = LineTextureMode.Stretch;
        return line;
    }

    private float NextRandom()
    {
        _random = unchecked(_random * 1664525u + 1013904223u);
        return (_random >> 8) * (1f / 16777216f);
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        _object.SetActive(false);
        Object.Destroy(_object);
    }
}
