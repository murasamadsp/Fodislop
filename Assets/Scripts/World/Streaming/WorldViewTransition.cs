#nullable enable

using UnityEngine;

namespace Kern.World.Streaming;

/// <summary>
/// Переход вида при телепорте: камера стоит на старом месте, пока место
/// назначения не готово целиком, и переставляется одним кадром.
/// </summary>
///
/// Игрок не должен видеть прогрузку ни при каком телепорте. Сглаженная камера
/// летела бы к новой точке через всю карту, а окно террейна, чанки и свет
/// догоняли бы её на экране. Поэтому прыжок дальше кадра камеры не
/// догоняется: камера держит старый вид, террейн тем временем собирает окно
/// назначения и не публикует его, а когда окно готово и накрывает кадр
/// назначения — камера переставляется, и в этом же кадре окно публикуется.
///
/// Камера (CameraFollow) пишет сюда, куда хочет, и снимает удержание;
/// террейн (TerrainRenderer) читает точку назначения и пишет готовность.
/// Камера обновляется раньше террейна, поэтому готовность, записанная
/// террейном в кадре N, снимает удержание в кадре N + 1, и публикация идёт в
/// том же кадре N + 1.
public sealed class WorldViewTransition
{
    /// <summary>
    /// Предел удержания. Если место назначения так и не приехало (сервер не
    /// прислал чанки), вечно стоящая камера хуже прогрузки: игрок не видел
    /// бы, где он. Предел — страховка от зависания, а не штатный путь.
    /// </summary>
    public const float MaximumHoldSeconds = 5f;

    private RectInt _readyCells;
    private bool _hasReadyCells;
    private float _holdStartTime;

    /// <summary>Камера стоит, пока место назначения готовится.</summary>
    public bool IsHolding { get; private set; }

    /// <summary>Куда камера встанет после перехода, в мировых координатах.</summary>
    public Vector3 Destination { get; private set; }

    /// <summary>
    /// Есть ли что удерживать: опубликованный вид мира. До первой публикации
    /// экран закрыт загрузкой мира, и камера переставляется сразу.
    /// </summary>
    public bool CanHold { get; set; }

    public float HoldSeconds => IsHolding ? Time.unscaledTime - _holdStartTime : 0f;

    public void Hold(Vector3 destination)
    {
        if (!IsHolding)
        {
            IsHolding = true;
            _holdStartTime = Time.unscaledTime;
            _hasReadyCells = false;
        }

        Destination = destination;
    }

    /// <summary>
    /// Террейн: клетки мира, которые будут на экране сразу после перехода,
    /// собраны, текстуры их типов на месте, и шаг ждёт публикации.
    /// </summary>
    public void MarkReady(RectInt readyCells)
    {
        _readyCells = readyCells;
        _hasReadyCells = true;
    }

    public void ClearReady() => _hasReadyCells = false;

    /// <summary>
    /// Готово ли место назначения для кадра камеры с центром в
    /// <paramref name="position"/> и полуразмерами кадра в клетках.
    /// </summary>
    public bool IsReadyFor(Vector3 position, float halfWidthCells, float halfHeightCells, float cellSize)
    {
        if (!_hasReadyCells)
        {
            return false;
        }

        int minX = Mathf.FloorToInt((position.x / cellSize) - halfWidthCells);
        int minY = Mathf.FloorToInt((position.y / cellSize) - halfHeightCells);
        int maxX = Mathf.CeilToInt((position.x / cellSize) + halfWidthCells);
        int maxY = Mathf.CeilToInt((position.y / cellSize) + halfHeightCells);
        return minX >= _readyCells.xMin && minY >= _readyCells.yMin &&
            maxX <= _readyCells.xMax && maxY <= _readyCells.yMax;
    }

    public void Release()
    {
        IsHolding = false;
        _hasReadyCells = false;
    }
}
