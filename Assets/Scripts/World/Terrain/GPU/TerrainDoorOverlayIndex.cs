#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Где в окне стоят двери.
/// </summary>
///
/// Дверей в окне единицы. Здесь кольцевой флаг двери и множество номеров
/// дверных квадов. Клетка сообщает о себе одним вызовом
/// <see cref="RecordCell"/>, сдвиг — одним <see cref="Scroll"/>, а меш
/// накладки строится из множества, а не из сетки. Вид двери накладка берёт из
/// буфера клеток, как и сам террейн, поэтому смена вида поводом пересобирать
/// её не является — только смена состава.
public sealed class TerrainDoorOverlayIndex
{
    private readonly TerrainRingGrid<bool> _flags = new();
    private readonly HashSet<int> _quads = [];
    private readonly object _quadLock = new();
    private int[] _quadScratch = [];
    private int _width;
    private int _height;
    private bool _trackQuads;

    /// <summary>В окне есть хотя бы одна дверь — накладку есть что строить.</summary>
    public bool HasDoors => _quads.Count > 0;

    public void EnsureSize(int width, int height)
    {
        _width = width;
        _height = height;
        _flags.EnsureSize(width, height);
        _quads.Clear();
    }

    /// <summary>
    /// Полная сборка: пока она идёт, множество квадов не ведётся — его
    /// собирают одним проходом в конце.
    /// </summary>
    public void BeginFullBuild()
    {
        _quads.Clear();
        _trackQuads = false;
    }

    public void CompleteFullBuild()
    {
        _trackQuads = true;
        RebuildQuadIndex();
    }

    /// <summary>
    /// Кольцевой сдвиг: флаги двигаются вместе с окном, а накладка встаёт на
    /// новые координаты. Возвращается «состав дверей изменился».
    /// </summary>
    public bool Scroll(int dx, int dy)
    {
        int previousDoorCount = _quads.Count;
        _flags.Scroll(dx, dy);
        ScrollQuads(dx, dy);
        return _quads.Count != previousDoorCount;
    }

    /// <summary>
    /// Кольцевой сдвиг не чистит вошедшую полосу: в её слотах лежат флаги
    /// уехавших клеток, и «дверь была» там врёт. Полоса обнуляется до заливки,
    /// и каждая её клетка приходит в <see cref="RecordCell"/> как новая.
    /// </summary>
    ///
    /// Обнуляется РОВНО вошедшее, без каймы соседства: клетки каймы остались
    /// на месте вместе со своими дверями, и стереть им флаг значило бы
    /// разойтись с множеством в другую сторону.
    public void ClearBand(RectInt band)
    {
        for (int x = band.xMin; x < band.xMax; x++)
        {
            for (int y = band.yMin; y < band.yMax; y++)
            {
                _flags[x, y] = false;
            }
        }
    }

    /// <summary>
    /// Записать клетку и вернуть признак «двери задеты»: дверь появилась или
    /// исчезла.
    /// </summary>
    ///
    /// Возвращается признак, а не пишется в общее поле: полная сборка зовёт
    /// это из Parallel.For, и такая запись была гонкой.
    public bool RecordCell(int x, int y, bool door)
    {
        int quad = (x * _height) + y;
        bool wasDoor = _flags[x, y];
        bool doorsChanged = door != wasDoor;
        _flags[x, y] = door;

        // Дверей в окне единицы, а заплатка перечитывает тысячи клеток.
        // Безусловный Remove на каждой не-двери был хешированием впустую.
        if (_trackQuads && door != wasDoor)
        {
            lock (_quadLock)
            {
                if (door)
                {
                    _quads.Add(quad);
                }
                else
                {
                    _quads.Remove(quad);
                }
            }
        }

        return doorsChanged;
    }

    /// <summary>Квады всех дверей окна по возрастанию.</summary>
    ///
    /// Порядок обхода множества зависит от истории вставок и удалений, а она у
    /// двух клиентов на одной клетке разная. Сортировка делает меш накладки
    /// функцией от состояния окна, а не от пути к нему.
    public void CopyQuads(List<int> target)
    {
        target.Clear();
        target.AddRange(_quads);
        target.Sort();
    }

    private void ScrollQuads(int dx, int dy)
    {
        if (_quads.Count == 0)
        {
            return;
        }

        if (_quadScratch.Length < _quads.Count)
        {
            _quadScratch = new int[_quads.Count];
        }

        _quads.CopyTo(_quadScratch);
        int previousCount = _quads.Count;
        _quads.Clear();

        for (int index = 0; index < previousCount; index++)
        {
            int quad = _quadScratch[index];
            int x = (quad / _height) - dx;
            int y = (quad % _height) - dy;
            if ((uint)x < (uint)_width && (uint)y < (uint)_height)
            {
                _quads.Add((x * _height) + y);
            }
        }
    }

    private void RebuildQuadIndex()
    {
        _quads.Clear();
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (_flags[x, y])
                {
                    _quads.Add((x * _height) + y);
                }
            }
        }
    }
}
