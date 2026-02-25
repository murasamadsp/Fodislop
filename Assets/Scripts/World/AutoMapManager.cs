using UnityEngine;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Auto-spawns a MapManager GameObject if one doesn't exist in the scene.
    /// This ensures the WorldBackgroundRenderer can always find a MapManager to subscribe to.
    /// </summary>
    [ExecuteAlways]
    public class AutoMapManager : MonoBehaviour
    {
        [Header("Auto MapManager Settings")]
        [Tooltip("Enable automatic MapManager creation if not found")]
        [SerializeField] private bool _autoCreateMapManager = true;
        [Tooltip("Check interval in seconds")]
        [SerializeField] private float _checkInterval = 2.0f;

        private float _lastCheckTime = 0f;
        private bool _mapManagerCreated = false;

        void Update()
        {
            if (!_autoCreateMapManager) return;
            
            // Check periodically if MapManager exists
            if (Time.time - _lastCheckTime >= _checkInterval)
            {
                CheckAndCreateMapManager();
                _lastCheckTime = Time.time;
            }
        }

        private void CheckAndCreateMapManager()
        {
            // Check if MapManager exists
            if (MapManager.Instance != null)
            {
                if (_mapManagerCreated)
                {
                    Debug.Log("AutoMapManager: MapManager found, removing auto-created instance");
                    _mapManagerCreated = false;
                }
                return;
            }

            // Create MapManager if it doesn't exist
            if (!_mapManagerCreated)
            {
                CreateMapManager();
                _mapManagerCreated = true;
            }
        }

        private void CreateMapManager()
        {
            // Create a new GameObject for MapManager
            var mapManagerGO = new GameObject("MapManager");
            var mapManager = mapManagerGO.AddComponent<MapManager>();
            
            // Make it persistent across scenes
            DontDestroyOnLoad(mapManagerGO);
            
            Debug.Log("AutoMapManager: Created MapManager GameObject in scene");
        }

        private void OnValidate()
        {
            if (_autoCreateMapManager)
            {
                Debug.Log("AutoMapManager: Auto-creation enabled");
            }
        }
    }
}