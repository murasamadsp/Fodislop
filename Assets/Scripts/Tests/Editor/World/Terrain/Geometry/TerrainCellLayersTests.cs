#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainCellLayersTests
{
    [TestCase(CellType.BuildingDoor, CellType.Rock, CellConfigProperties.Passable, CellType.Road)]
    [TestCase(CellType.BuildingWall, CellType.Unloaded, CellConfigProperties.Passable, CellType.Road)]
    [TestCase(CellType.BuildingCorner, CellType.Empty, CellConfigProperties.Passable, CellType.Road)]
    [TestCase(CellType.BuildingWall, CellType.Rock, (CellConfigProperties)0, CellType.Rock)]
    [TestCase(CellType.Empty, CellType.Rock, CellConfigProperties.Passable, CellType.Empty)]
    [TestCase(CellType.Rock, CellType.Empty, CellConfigProperties.Passable, CellType.Empty)]
    public void BackgroundTextureDependencyMatchesAuthoredLayer(
        CellType foreground, CellType propagated, CellConfigProperties properties, CellType expected)
    {
        Assert.That(TerrainCellLayers.ResolveBackground(foreground, propagated, properties), Is.EqualTo(expected));
    }

    [Test]
    public void RoadArrivalRefreshesDoorAndDeduplicatesBothLayers()
    {
        var index = new TerrainCellTextureIndex();
        index.EnsureWindow(4, 3);
        CellType background = TerrainCellLayers.ResolveBackground(
            CellType.BuildingDoor, CellType.Rock, CellConfigProperties.Passable);
        index.UpdateCell(-2, 11, background, CellType.BuildingDoor);
        index.UpdateCell(-1, 12, CellType.Road, CellType.Road);

        index.CollectRefreshQuads([CellType.Road, CellType.BuildingDoor], -3, 10, 4, 3);
        Assert.That(index.TextureRefreshQuads, Is.EqualTo(new[] { 4, 8 }));
    }

    [Test]
    public void TextureIndexScrollPreservesOverlapAndReplacesOutgoingSlot()
    {
        var index = new TerrainCellTextureIndex();
        index.EnsureWindow(4, 3);
        index.UpdateCell(-3, 10, CellType.Road, CellType.BuildingDoor);
        index.UpdateCell(-2, 11, CellType.Road, CellType.BuildingDoor);
        index.UpdateCell(1, 10, CellType.Empty, CellType.Rock);

        index.CollectRefreshQuads([CellType.Road], -2, 10, 4, 3);
        Assert.That(index.TextureRefreshQuads, Is.EqualTo(new[] { 1 }));
        index.CollectRefreshQuads([CellType.Rock], -2, 10, 4, 3);
        Assert.That(index.TextureRefreshQuads, Is.EqualTo(new[] { 9 }));

        index.Clear();
        index.CollectRefreshQuads([CellType.Road, CellType.Rock], -2, 10, 4, 3);
        Assert.That(index.TextureRefreshQuads, Is.Empty);
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
