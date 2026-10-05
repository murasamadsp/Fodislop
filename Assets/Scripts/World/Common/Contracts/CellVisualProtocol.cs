#nullable enable

using MinesServer.Data;

namespace Kern.World;

// Часть постройки: стена и угол пака, дверь и дорога внутри пака, строительный
// блок. Стена, угол и блок — глухие (бурением не убираются).
public enum CellStructurePart : byte
{
    None = 0,
    Wall = 1,
    Corner = 2,
    Door = 3,
    Road = 4,
    Block = 5,
}

// Декаль переднего плана: земля (атлас земли, доля из TerrainConfigHolder)
// или камень (атлас красно-чёрного камня, 30%). Фон всегда получает землю.
public enum TerrainDecalFamily : byte
{
    None = 0,
    Ground = 1,
    Stone = 2,
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
//             но клетку целиком не закрывает.
public enum CellShape : byte
{
    Flat = 0,
    Organic = 1,
    Square = 2,
    Round = 3,
}

public readonly record struct CellVisualProperties(
    CellShape Shape,
    TerrainDecalFamily DecalFamily = TerrainDecalFamily.None,
    TerrainAnimationProfile SurfaceProfile = TerrainAnimationProfile.Default,
    byte PaletteIndex = 0,
    CellStructurePart StructurePart = CellStructurePart.None)
{
    public bool IsRound => Shape == CellShape.Round;
    public bool IsBuildingWall => StructurePart == CellStructurePart.Wall;
    public bool IsBuildingCorner => StructurePart == CellStructurePart.Corner;
    public bool IsBuildingDoor => StructurePart == CellStructurePart.Door;
    public bool IsBuilding => StructurePart is CellStructurePart.Wall or CellStructurePart.Corner or CellStructurePart.Door;
}

public interface ICellVisualProtocol
{
    CellVisualProperties Get(CellType type);
}
