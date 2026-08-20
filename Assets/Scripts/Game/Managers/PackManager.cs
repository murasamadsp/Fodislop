#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using VContainer;

namespace Fodinae.Game.Managers
{
    public class PackManager : MonoBehaviour, IPackService
    {
        private const string TAG = "[PackManager]";
        private readonly Dictionary<Vector2Int, Pack> _packs = new();

        // Resolved on use, not injected as a field.
        //
        // MapManager.Construct takes PackManager, so a PackManager --> IMapDataProvider
        // field injection closes a construction-time cycle and VContainer refuses to build
        // the whole game scope: MapManager needs PackManager to be constructed, PackManager
        // needs MapManager. That is not a false positive — both really would have to exist
        // before the other. Nothing here needs the map at construction time; the only use is
        // one WorldHeight read while spawning a pack, long after both are alive.
        [Inject]
        private ISessionContainer _session = null!;

        private IMapDataProvider MapData =>
            _session.TryResolve<IMapDataProvider>() ??
            throw new System.InvalidOperationException(
                $"{TAG} IMapDataProvider is required for pack placement.");

        public void AddOrUpdatePack(ushort x, ushort y, PackType packType, byte variant, byte linkedClan)
        {
            var pos = new Vector2Int(x, y);
            if (_packs.TryGetValue(pos, out var pack))
            {
                pack.Initialize(packType, variant, linkedClan);
                return;
            }

            var go = new GameObject($"Pack_{x}_{y}");
            go.transform.SetParent(transform);
            go.transform.position = CoordinateUtils.ServerToUnityPos(x, y, MapData.WorldHeight);
            pack = go.AddComponent<Pack>();
            pack.Initialize(packType, variant, linkedClan);
            _packs[pos] = pack;
        }

        public void RemovePack(ushort x, ushort y)
        {
            var pos = new Vector2Int(x, y);
            if (_packs.TryGetValue(pos, out var pack))
            {
                Destroy(pack.gameObject);
                _packs.Remove(pos);
            }
            else
            {
                Debug.LogWarning($"{TAG} RemovePack: no pack at ({x},{y})");
            }
        }

        public void ClearAllPacks()
        {
            int count = _packs.Count;
            foreach (var pack in _packs.Values)
            {
                if (pack != null)
                {
                    Destroy(pack.gameObject);
                }
            }

            _packs.Clear();
            Debug.Log($"{TAG} Cleared {count} packs");
        }
    }
}
