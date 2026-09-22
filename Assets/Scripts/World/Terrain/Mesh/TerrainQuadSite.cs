#nullable enable

namespace Kern.World.Terrain;

/// <summary>Какой слой клетки собирается.</summary>
///
/// Фон — прямоугольная подложка под клеткой: её тип берётся из карты заливки,
/// геометрия не смещается, света и каймы она не несёт. Передний план — сама
/// клетка со всем этим.
public enum TerrainQuadLayer
{
    Background = 0,
    Foreground = 1,
}

/// <summary>
/// Место клетки: её адрес в окне и её адрес в мире.
/// </summary>
///
/// Локальные координаты индексируют кольцевые массивы окна, мировые идут в
/// тексель и в хэш искажения. Их нельзя путать, и держать их одной парой
/// int'ов в списке из восемнадцати параметров — способ однажды перепутать.
public readonly record struct TerrainQuadSite(
    int LocalX,
    int LocalY,
    int GridX,
    int UnityY,
    float CellSize);

/// <summary>Чем кончилась сборка квада.</summary>
///
/// Отрицательный атлас означает «квада нет»: клетка за миром, не загружена
/// или слой для неё пуст. Признак двери нужен накладке, которая рисуется
/// отдельным мешем поверх террейна.
public readonly record struct TerrainQuadResult(int AtlasIndex, bool IsDoor)
{
    public static TerrainQuadResult None => new(-1, false);

    public static TerrainQuadResult NoAtlas(bool isDoor) => new(-1, isDoor);

    public bool HasAtlas => AtlasIndex >= 0;
}
