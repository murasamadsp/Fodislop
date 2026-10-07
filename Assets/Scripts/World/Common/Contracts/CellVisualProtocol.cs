#nullable enable

namespace Kern.World;

// Группа декали типа: земля (атлас земли, доля из TerrainConfigHolder)
// или камень (атлас красно-чёрного камня, 30%); номера — KERN_TERRAIN_DECAL_ATLAS_*.
public enum CellDecalAtlas : byte
{
    None = 0,
    Ground = 1,
    Rock = 2,
}

// Слой, в котором рисуется клетка.
//   Foreground — передний план: непроходим, даёт тень, под ним подложка;
//   Background — фон: проходим, лежит сам на себе;
//   Underlay   — подложка: фон, который лежит под каждым передним планом.
//                Ровно один тип.
public enum CellDrawLayer : byte
{
    Foreground = 0,
    Background = 1,
    Underlay = 2,
}

// Откуда клетка берёт координаты текстуры; номера — KERN_TERRAIN_TEXTURE_ANCHOR_*.
//   Cell  — своя картинка на клетку (тайл);
//   World — кусок общей картинки по мировой позиции: соседние клетки
//           читаются одним камнем без швов.
public enum CellTextureAnchor : byte
{
    Cell = 0,
    World = 1,
}

// Цветовая поверхность клетки; номера — KERN_TERRAIN_SURFACE_EFFECT_*. Анимация
// текстуры (мигание, мерцание, радуга) — отдельное поле, CellAnimationType.
//   Plain     — цвет текстуры как есть;
//   Molten    — расплав (лава);
//   Faceted   — грани кристалла;
//   Prismatic — радужный кристалл (палитра — SurfaceEffectPalette).
public enum CellSurfaceEffect : byte
{
    Plain = 0,
    Molten = 1,
    Faceted = 2,
    Prismatic = 3,
}

// Контур клетки — её край и как он ведёт себя в сетке; номера —
// KERN_TERRAIN_OUTLINE_*. Узлы сетки гнутся, только если рядом есть Wavy.
//   Pliant — квадрат, края гнутся вслед за соседями;
//   Wavy   — квадрат, сам гнёт свои края и узлы вокруг;
//   Rigid  — квадрат с прямыми краями: держит узлы, соседи его не гнут;
//   Round  — капля (пески, лава, кислоты): клетку целиком не закрывает, держит;
//   Wall, Corner, Door — стена, угол и дверь пака, держат: стена выбирает
//            картинку по соседним углам, дверь рисуется отдельным слоем поверх.
public enum CellOutline : byte
{
    Pliant = 0,
    Wavy = 1,
    Rigid = 2,
    Round = 3,
    Wall = 4,
    Corner = 5,
    Door = 6,
}
