#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Fodinae.Networking.Buildings
{
    /// <summary>
    /// Registry of every placeable pack footprint known to the offline server
    /// imitation (DummyConnection). Templates mirror the authoritative
    /// MinesServer Game/Buildings sources one to one.
    /// </summary>
    public static class BuildingTemplates
    {
        private static readonly Dictionary<PackType, PackBuilding> _templates = new()
        {
            [PackType.Teleport] = new Teleport(),
            [PackType.Resp] = new Resp(),
            [PackType.Up] = new Up(),
            [PackType.Market] = new Market(),
            [PackType.Clans] = new ClansPack(),
            [PackType.Craft] = new Crafter(),
            [PackType.BombShop] = new Bshop(),
            [PackType.Gun] = new Gun(),
            [PackType.Storage] = new Storage(),
            [PackType.Science] = new NC(),
        };

        public static bool TryGet(PackType type, out PackBuilding? building)
        {
            if (_templates.TryGetValue(type, out PackBuilding? found))
            {
                building = found;
                return true;
            }

            building = null;
            return false;
        }

        /// <summary>
        /// Anchor distance in cells ahead of the player's facing direction,
        /// copied from the authoritative Inventory.TryPlacePack dispatch:
        /// Up/Jobs/ClansPack place at 3, NC places at 6, everything else at 2.
        /// </summary>
        public static ushort GetAnchorDistance(PackType type) => type switch
        {
            PackType.Up or PackType.Clans => 3,
            PackType.Science => 6,
            _ => 2,
        };
    }
}
