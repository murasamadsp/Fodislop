#nullable enable

using MinesServer.Data;

namespace Kern.Core.Interfaces;
public interface IBuildingService
{
    void AddOrUpdateBuilding(ushort x, ushort y, BuildingType buildingType, byte variant, byte linkedClan);
    void RemoveBuilding(ushort x, ushort y);
    void ClearAllBuildings();
}
