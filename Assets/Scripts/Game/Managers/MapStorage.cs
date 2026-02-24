using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Assets.Scripts.Game.Managers
{
    public class MapStorage
    {
        private static MapStorage _instance;
        public static MapStorage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MapStorage();
                }
                return _instance;
            }
        }

        public WorldLayer<CellType> cellLayer;

        public void InitWorld(string worldCodeName, int width, int height)
        {
            var path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";
            Debug.Log($"Initializing cell storage at: {path}");
            // Assuming chunk size of 32 for now. These should probably be configurable.
            int widthChunks = (width + 31) / 32;
            int heightChunks = (height + 31) / 32;
            cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks);
        }

        private MapStorage() { }

        public void Dispose()
        {
            cellLayer?.Dispose();
            _instance = null;
        }
    }
}
