#nullable enable

using MinesServer.Data;

namespace Kern.World;

// Декаль переднего плана: земля (атлас земли, доля из TerrainConfigHolder)
// или камень (атлас красно-чёрного камня, 30%). Фон всегда получает землю.
public enum TerrainDecalFamily : byte
{
    None = 0,
    Ground = 1,
    Rock = 2,
}

public enum TerrainAnimationProfile : byte
{
    Default = 0,
    PrismaticCrystal = 1,
    MoltenSurface = 2,
    FacetedCrystal = 3,
}

// Поверхность клетки — одна анимация на тип.
//   Plain     — неподвижная текстура;
//   Blinking  — мигание (скорость — SurfaceSpeed);
//   Shimmer   — мерцание (скорость — SurfaceSpeed);
//   Molten    — расплав (лава);
//   Faceted   — грани кристалла;
//   Prismatic — радужный кристалл (палитра — SurfacePalette).
public enum CellSurface : byte
{
    Plain = 0,
    Blinking = 1,
    Shimmer = 2,
    Molten = 3,
    Faceted = 4,
    Prismatic = 5,
}

// Форма клетки — одна на все её геометрические решения.
//   Flat    — плоский пол: не искажается, лежит под остальным;
//   Organic — органическая порода: рваный край, двигает узлы сетки;
//   Square  — жёсткий квадрат: не искажается и держит соседние узлы;
//   Round   — круглая капля (пески, лава, кислоты): жёсткая, как Square,
//             но клетку целиком не закрывает;
//   Wall, Corner, Door — стена, угол и дверь пака: жёсткие, как Square;
//             стена выбирает картинку по соседним углам, дверь рисуется
//             отдельным слоем поверх.
public enum CellShape : byte
{
    Flat = 0,
    Organic = 1,
    Square = 2,
    Round = 3,
    Wall = 4,
    Corner = 5,
    Door = 6,
}

public readonly record struct CellVisualProperties(
    CellShape Shape,
    TerrainDecalFamily DecalFamily = TerrainDecalFamily.None,
    TerrainAnimationProfile SurfaceProfile = TerrainAnimationProfile.Default,
    byte PaletteIndex = 0)
{
    public bool IsRound => Shape == CellShape.Round;
    public bool IsBuildingWall => Shape == CellShape.Wall;
    public bool IsBuildingCorner => Shape == CellShape.Corner;
    public bool IsBuildingDoor => Shape == CellShape.Door;
    public bool IsBuilding => Shape is CellShape.Wall or CellShape.Corner or CellShape.Door;
}

public interface ICellVisualProtocol
{
    CellVisualProperties Get(CellType type);
}
