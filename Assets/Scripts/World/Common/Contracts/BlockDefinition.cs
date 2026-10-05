#nullable enable

using MinesServer.Data;

namespace Kern.World;

public readonly record struct BlockDefinition
{
    // 1. Физика и базовые свойства
    public bool Passable { get; init; } = false;

    // 2. Светотень и освещение
    public bool EmitsLight { get; init; } = false;

    // 3. Поверхность и декаль
    public CellSurface Surface { get; init; } = CellSurface.Plain;

    // Скорость мигания и мерцания; у остальных поверхностей ноль.
    public byte SurfaceSpeed { get; init; } = 0;

    // Палитра радужного кристалла; у остальных поверхностей ноль.
    public byte SurfacePalette { get; init; } = 0;

    public TerrainDecalFamily DecalFamily { get; init; } = TerrainDecalFamily.None;

    // 4. Геометрия и кайма
    // Группа каймы: соседи одной ненулевой группы — одна масса без каймы.
    public byte RimGroup { get; init; } = 0;

    public CellShape Shape { get; init; } = CellShape.Flat;

    // 5. Постройки и интерактивные зоны
    public CellStructurePart StructurePart { get; init; } = CellStructurePart.None;

    // 6. Карта
    public string? MapColorHex { get; init; } = null;

    public BlockDefinition()
    {
    }
}
