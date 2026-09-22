#nullable enable

namespace Kern.Tests.World;

using Kern.Core;
using NUnit.Framework;

// Порядок слоёв мира. Слои рисуются с альфа-блендингом, поэтому номер решает,
// что чем накрыто; совпавший номер не означает «рядом», он означает «порядок
// не задан» — Unity доберёт его очередью материала и расстоянием до камеры, и
// он поедет от кадра к кадру.
//
// Числа сами по себе ничего не значат и менять их можно свободно. Значат
// только знаки разностей, и они здесь закреплены.
[TestFixture]
public class WorldLayerOrderTests
{
    private const int WorldBackground =
        ProjectRuntimeContracts.RequiredLayers.WorldBackgroundSortingOrder;
    private const int Terrain =
        ProjectRuntimeContracts.RequiredLayers.TerrainSortingOrder;

    [Test]
    public void WorldBackgroundStaysStrictlyBehindTerrain()
    {
        Assert.That(
            WorldBackground,
            Is.LessThan(Terrain),
            "Подложка мира обязана лежать под террейном, а не вровень с ним.");
    }
}
