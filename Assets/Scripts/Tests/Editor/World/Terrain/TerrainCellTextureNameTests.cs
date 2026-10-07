#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainCellTextureNameTests
{
    [TestCase("Cells/Rock.png", CellType.Rock)]
    [TestCase("cells/redrock", CellType.RedRock)]
    [TestCase("Cells/117.png", CellType.RedRock)]
    public void TryParseCellType_RecognizesEnumTextureNames(
        string filename,
        CellType expectedCellType)
    {
        bool success = TerrainCellTextureName.TryParseCellType(filename, out CellType cellType);

        Assert.That(success, Is.True);
        Assert.That(cellType, Is.EqualTo(expectedCellType));
    }

    [TestCase("Cells/NotACell.png")]
    [TestCase("Other/Rock.png")]
    [TestCase("Cells/12345.png")]
    public void TryParseCellType_RejectsUnknownCellTextureNames(string filename)
    {
        bool success = TerrainCellTextureName.TryParseCellType(filename, out _);

        Assert.That(success, Is.False);
    }
}
