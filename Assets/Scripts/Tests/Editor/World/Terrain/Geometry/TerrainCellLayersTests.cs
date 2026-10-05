#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainCellLayersTests
{
    // Пол лежит сам на себе, проходимая часть пака — на дороге, всё
    // остальное — на обычной земле.
    [TestCase(CellType.BuildingDoor, CellConfigProperties.Passable, CellType.Road)]
    [TestCase(CellType.BuildingWall, CellConfigProperties.Passable, CellType.Road)]
    [TestCase(CellType.BuildingWall, (CellConfigProperties)0, CellType.Empty)]
    [TestCase(CellType.Empty, CellConfigProperties.Passable, CellType.Empty)]
    [TestCase(CellType.Road, CellConfigProperties.Passable, CellType.Road)]
    [TestCase(CellType.Gate, CellConfigProperties.Passable, CellType.Gate)]
    [TestCase(CellType.VolcanoBackground, CellConfigProperties.Passable, CellType.VolcanoBackground)]
    [TestCase(CellType.Rock, (CellConfigProperties)0, CellType.Empty)]
    [TestCase(CellType.Unloaded, CellConfigProperties.Passable, CellType.Empty)]
    public void BackgroundIsTheFloorItselfOrGround(
        CellType foreground, CellConfigProperties properties, CellType expected)
    {
        Assert.That(TerrainCellLayers.ResolveBackground(foreground, properties), Is.EqualTo(expected));
    }

    [TestCase(CellType.Empty)]
    [TestCase(CellType.Unloaded)]
    [TestCase(CellType.Rock)]
    public void ExposedGroundHasOneBackgroundQuadAndNoDistortedForeground(CellType propagatedType)
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Empty, propagatedType, true, true, out CellType background), Is.True);
        Assert.That(background, Is.EqualTo(CellType.Empty));
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Empty, propagatedType, false, true, out _), Is.False);
    }

    [Test]
    public void SolidBlockKeepsItsForegroundAndUnderlyingGround()
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, CellType.Empty, false, true, out CellType foreground), Is.True);
        Assert.That(foreground, Is.EqualTo(CellType.Rock));
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, CellType.Empty, true, true, out CellType background), Is.True);
        Assert.That(background, Is.EqualTo(CellType.Empty));
    }

    [TestCase(CellType.Unloaded)]
    [TestCase(CellType.Rock)]
    public void MissingOrIdenticalSolidBackgroundIsNotDuplicated(CellType propagatedType)
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, propagatedType, true, true, out _), Is.False);
    }

    // Силуэт меньше клетки — подложка обязана остаться, иначе на
    // освободившемся месте дыра. Ровно это рисовало чёрные ореолы вокруг
    // круглых капель лавы.
    [Test]
    public void PartialSilhouetteKeepsIdenticalBackgroundUnderneath()
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, CellType.Rock, true, false, out CellType background), Is.True);
        Assert.That(background, Is.EqualTo(CellType.Rock));
    }

    // Незагруженная подложка остаётся отброшенной при любом силуэте: рисовать
    // под клеткой нечего, данных попросту нет.
    [Test]
    public void PartialSilhouetteStillDropsUnloadedBackground()
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, CellType.Unloaded, true, false, out _), Is.False);
    }
}
