#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

/// <summary>Тип клетки из cells.json: каждое поле — один ключ.</summary>
///
/// Имена и порядок совпадают с ключами cells.json; порядок — ход кадра:
/// слой, геометрия, текстура, цвет, декаль, кайма, свет, карта. Виртуальное
/// (тень и масса — от переднего плана, цвет блика — из альбедо) считает шейдер.
public readonly record struct BlockDefinition(
    // Слой отрисовки: передний план, фон или подложка под передним планом.
    CellDrawLayer DrawLayer,
    // Контур: гибкий, волнистый, жёсткий квадрат, капля, стена, угол, дверь.
    CellOutline Outline,
    // Откуда координаты текстуры: своя на клетку или общая по миру.
    CellTextureAnchor TextureAnchor,
    // Вид анимации текстуры: мигание, мерцание, радуга.
    CellAnimationType AnimationType,
    // Скорость анимации: кадров ленты в секунду, темп анимации и поверхности.
    float AnimationSpeed,
    // Эффект поверхности: расплав, грани, радужный кристалл.
    CellSurfaceEffect SurfaceEffect,
    // Палитра радужного эффекта поверхности.
    byte SurfaceEffectPalette,
    // Атлас декалей поверх клетки: без декалей, земля, камень.
    CellDecalAtlas DecalAtlas,
    // Масса для тёмной каймы по краю: 0 — без каймы; соседи одной ненулевой
    // массы — одно тело, между ними каймы нет.
    byte RimMass,
    // Сила свечения 0..1; 0 — не светится.
    float Glow,
    // Цвет клетки на карте.
    Color32 MapColor)
{
    // Передний план непроходим, фон проходим.
    public bool IsPassable => DrawLayer != CellDrawLayer.Foreground;
}
