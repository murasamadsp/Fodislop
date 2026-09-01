#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using VContainer;
// Протокол по-прежнему называет это Pack: PackType живёт во внешней сборке
// MinesServer.Data, исходников которой в проекте нет. Алиас держит границу —
// наш домен говорит Building, провод остаётся Pack.
using BuildingType = MinesServer.Data.PackType;

namespace Fodinae.Game.Managers
{
    public class BuildingManager : MonoBehaviour, IBuildingService
    {
        private const string TAG = "[BuildingManager]";
        private readonly Dictionary<Vector2Int, Building> _buildings = new();

        [Inject]
        private IMapDataProvider _mapDataProvider = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        private IMapDataProvider MapData => _mapDataProvider;

        public void AddOrUpdateBuilding(ushort x, ushort y, BuildingType buildingType, byte variant, byte linkedClan)
        {
            var pos = new Vector2Int(x, y);
            if (_buildings.TryGetValue(pos, out var building))
            {
                building.Initialize(buildingType, variant, linkedClan);
                return;
            }

            building = _sceneObjects.Create<Building>($"Building_{x}_{y}", RuntimeOwner.Buildings);
            building.transform.position = CoordinateUtils.ServerToUnityPos(x, y, MapData.WorldHeight);
            building.Initialize(buildingType, variant, linkedClan);
            _buildings[pos] = building;
        }

        public void RemoveBuilding(ushort x, ushort y)
        {
            var pos = new Vector2Int(x, y);
            if (_buildings.TryGetValue(pos, out var building))
            {
                Destroy(building.gameObject);
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
            foreach (var building in _buildings.Values)
            {
                if (building != null)
                {
                    Destroy(building.gameObject);
                }
            }

            _buildings.Clear();
            Debug.Log($"{TAG} Cleared {count} buildings");
        }
    }
}
