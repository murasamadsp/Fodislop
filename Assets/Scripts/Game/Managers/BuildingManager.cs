#nullable enable

using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World;
using UnityEngine;
// Протокол по-прежнему называет это Pack: PackType живёт во внешней сборке
// MinesServer.Data, исходников которой в проекте нет. Алиас держит границу —
// наш домен говорит Building, провод остаётся Pack.
using BuildingType = MinesServer.Data.PackType;

namespace Kern.Game.Managers;

// Чистый сервис контейнера (docs/architecture/SCENE_STANDARD.md §1): здания создаются фабрикой
// под Runtime/Buildings, сам сервис объекта на сцене не имеет.
public sealed class BuildingManager : IBuildingService
{
    private const string TAG = "[BuildingManager]";
    private readonly IMapDataProvider _mapDataProvider;
    private readonly ISceneObjectFactory _sceneObjects;
    private readonly Dictionary<Vector2Int, Building> _buildings = new();

    public BuildingManager(IMapDataProvider mapDataProvider, ISceneObjectFactory sceneObjects)
    {
        _mapDataProvider = mapDataProvider;
        _sceneObjects = sceneObjects;
    }

    public void AddOrUpdateBuilding(ushort x, ushort y, BuildingType buildingType, byte variant, byte linkedClan)
    {
        var pos = new Vector2Int(x, y);
        if (_buildings.TryGetValue(pos, out Building? building))
        {
            building.Initialize(buildingType, variant, linkedClan);
            return;
        }

        building = _sceneObjects.Create<Building>($"Building_{x}_{y}", RuntimeOwner.Buildings);
        building.transform.position = CoordinateUtils.ServerToUnityPos(x, y, _mapDataProvider.WorldHeight);
        building.Initialize(buildingType, variant, linkedClan);
        _buildings[pos] = building;
    }

    public void RemoveBuilding(ushort x, ushort y)
    {
        var pos = new Vector2Int(x, y);
        if (_buildings.TryGetValue(pos, out Building? building))
        {
            Object.Destroy(building.gameObject);
            _buildings.Remove(pos);
        }
        else
        {
            Debug.LogWarning($"{TAG} RemoveBuilding: no building at ({x},{y})");
        }
    }

    public void ClearAllBuildings()
    {
        int count = _buildings.Count;
        foreach (Building building in _buildings.Values)
        {
            if (building != null)
            {
                Object.Destroy(building.gameObject);
            }
        }

        _buildings.Clear();
        Debug.Log($"{TAG} Cleared {count} buildings");
    }
}
