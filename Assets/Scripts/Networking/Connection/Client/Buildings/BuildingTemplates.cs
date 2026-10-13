#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Buildings;
public static class BuildingTemplates
{
    private static readonly Dictionary<BuildingType, PackBuilding> s_templates = new()
    {
        [BuildingType.Teleport] = new Teleport(),
        [BuildingType.Resp] = new RespawnStation(),
        [BuildingType.Up] = new UpgradeStation(),
        [BuildingType.Market] = new Market(),
        [BuildingType.Clans] = new ClansPack(),
        [BuildingType.Craft] = new Crafter(),
        [BuildingType.BombShop] = new BuildingShop(),
        [BuildingType.Gun] = new Gun(),
        [BuildingType.Storage] = new Storage(),
        [BuildingType.Science] = new NC(),
    };

    public static bool TryGet(BuildingType type, out PackBuilding? building) =>
        s_templates.TryGetValue(type, out building);

    /// <summary>
    /// Шаблон пака для выбранного предмета инвентаря. Соответствие повторяет
    /// серверный Inventory.typeditems: каждый пак-предмет ставит свой класс
    /// здания (ScienceCentre ставит NC — на проводе он BuildingType.Science)..
    /// </summary>
    public static bool TryGetByItem(ItemType item, out PackBuilding? building)
    {
        BuildingType? type = item switch
        {
            ItemType.Teleport => BuildingType.Teleport,
            ItemType.Resp => BuildingType.Resp,
            ItemType.Up => BuildingType.Up,
            ItemType.Market => BuildingType.Market,
            ItemType.Clans => BuildingType.Clans,
            ItemType.Craft => BuildingType.Craft,
            ItemType.BombShop => BuildingType.BombShop,
            ItemType.Storage => BuildingType.Storage,
            ItemType.ScienceCentre => BuildingType.Science,
            _ => null,
        };

        building = type is { } resolved && TryGet(resolved, out PackBuilding? found)
            ? found
            : null;
        return building != null;
    }

    public static ushort GetAnchorDistance(BuildingType type) => type switch
    {
        BuildingType.Up or BuildingType.Clans => 3,
        BuildingType.Science => 6,
        _ => 2,
    };
}
