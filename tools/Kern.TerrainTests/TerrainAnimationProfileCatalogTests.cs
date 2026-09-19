#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.TerrainTests;

[TestFixture]
public sealed class TerrainAnimationProfileCatalogTests
{
    [TestCase(CellType.XGreen)]
    [TestCase(CellType.XBlue)]
    [TestCase(CellType.XRed)]
    [TestCase(CellType.XCyan)]
    [TestCase(CellType.XViolet)]
    public void Get_XCrystal_UsesPrismaticProfile(CellType cellType)
    {
        TerrainAnimationSettings settings =
            TerrainAnimationProfileCatalog.Get(cellType, 7f);

        Assert.That(settings.Profile, Is.EqualTo(TerrainAnimationProfile.PrismaticCrystal));
        Assert.That(settings.Speed, Is.EqualTo(50f));
    }

    [TestCase(CellType.Green)]
    [TestCase(CellType.Red)]
    [TestCase(CellType.Blue)]
    [TestCase(CellType.Violet)]
    [TestCase(CellType.White)]
    [TestCase(CellType.Cyan)]
    [TestCase(CellType.Lava)]
    [TestCase(CellType.PurpleAcid)]
    public void Get_OtherCell_PreservesConfiguredAnimation(CellType cellType)
    {
        TerrainAnimationSettings settings =
            TerrainAnimationProfileCatalog.Get(cellType, 7f);

        Assert.That(settings.Profile, Is.EqualTo(TerrainAnimationProfile.Default));
        Assert.That(settings.Speed, Is.EqualTo(7f));
    }
}
