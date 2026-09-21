#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>Мировая координата клетки одним числом.</summary>
///
/// Ключ словаря, а не координата для счёта: складывать и сравнивать его
/// нельзя, он только адресует. Отрицательные координаты штатны — мир шире
/// окна в обе стороны, поэтому y кладётся без знака, а распаковывается обратно
/// в int.
public static class TerrainCoordinateKey
{
    public static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

    public static int UnpackX(long key) => (int)(key >> 32);

    public static int UnpackY(long key) => (int)key;
}

/// <summary>
/// Какие клетки окна имеют данный тип — и какой тип у данной клетки.
/// </summary>
///
/// Нужен ровно для одного вопроса: приехала текстура типа N, какие клетки
/// перечитать. Без обратного индекса на это отвечает только проход по всему
/// окну, а текстуры приезжают пачками по мере исследования мира.
///
/// Два словаря, а не один: прямой отвечает на вопрос выше, обратный нужен,
/// чтобы при смене типа клетки снять её со старого типа. Держать их врозь и
/// синхронизировать руками — то, чем занимались кэш клеток и индекс текстур,
/// каждый по-своему.
public sealed class CellTypeSpatialIndex
{
    private static readonly HashSet<long> _Empty = [];

    private readonly Dictionary<CellType, HashSet<long>> _keysByType = [];
    private readonly Dictionary<long, CellType> _typeByKey = [];

    public IReadOnlyCollection<long> KeysOf(CellType type) =>
        _keysByType.TryGetValue(type, out HashSet<long>? keys) ? keys : _Empty;

    public void Clear()
    {
        _keysByType.Clear();
        _typeByKey.Clear();
    }

    /// <summary>
    /// Запомнить тип клетки. <see cref="CellType.Unloaded"/> не индексируется:
    /// незагруженная клетка не принадлежит никакому типу.
    /// </summary>
    ///
    /// <param name="removePrevious">
    /// Снять клетку с прежнего типа. Ложь допустима только сразу после
    /// <see cref="Clear"/>, когда прежнего типа заведомо нет.
    /// </param>
    public void Set(long key, CellType type, bool removePrevious = true)
    {
        if (removePrevious)
        {
            Remove(key);
        }

        if (type == CellType.Unloaded)
        {
            return;
        }

        if (!_keysByType.TryGetValue(type, out HashSet<long>? keys))
        {
            keys = [];
            _keysByType.Add(type, keys);
        }

        keys.Add(key);
        _typeByKey[key] = type;
    }

    public void Remove(long key)
    {
        if (!_typeByKey.Remove(key, out CellType previousType) ||
            !_keysByType.TryGetValue(previousType, out HashSet<long>? keys))
        {
            return;
        }

        keys.Remove(key);
        if (keys.Count == 0)
        {
            _keysByType.Remove(previousType);
        }
    }

    public void RemoveRect(int startX, int endX, int startY, int endY)
    {
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                Remove(TerrainCoordinateKey.Pack(x, y));
            }
        }
    }
}
