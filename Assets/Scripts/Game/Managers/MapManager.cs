using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using System;
using UnityEngine;

namespace Fodinae.Assets.Scripts.Game.Managers
{
    public class MapManager : MonoBehaviour
    {
        private static MapManager _instance;
        public static MapManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<MapManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[MapManager]");
                        _instance = go.AddComponent<MapManager>();
                    }
                }
                return _instance;
            }
        }

        public event Action OnWorldInitialized;
        public Action OnWorldDataLoaded;

        private CellConfigurationPacket[] cellConfigurations;
        private string worldCodeName;
        private string worldDisplayName;
        private ushort width;
        private ushort height;
        private bool _isWorldInitialized = false;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

    public void LoadWorldInit(WorldInitPacket packet)
    {
        worldCodeName = packet.CodeName;
        worldDisplayName = packet.DisplayName;
        width = packet.Width;
        height = packet.Height;
        cellConfigurations = packet.Cells;
        Debug.Log($"World initialized: {packet.DisplayName} ({packet.CodeName}) [{width}x{height}]");
        
        // Initialize MapStorage with the world data
        MapStorage.Instance.InitWorld(packet.CodeName, width, height);
        
        // Verify that MapStorage was properly initialized
        if (!MapStorage.Instance.IsReady)
        {
            Debug.LogError($"MapStorage failed to initialize cell layer for world {packet.CodeName}");
            Debug.LogError($"MapStorage state: IsInitialized={MapStorage.Instance.IsInitialized()}, cellLayer={(MapStorage.Instance.cellLayer != null ? "not null" : "null")}");
        }
        else
        {
            Debug.Log($"MapStorage initialized successfully for world '{packet.CodeName}' with layer size: {MapStorage.Instance.cellLayer.WidthChunks}x{MapStorage.Instance.cellLayer.HeightChunks}");
        }
        
        _isWorldInitialized = true;
        OnWorldInitialized?.Invoke();
        
        // Only trigger OnWorldDataLoaded if MapStorage is actually ready
        if (MapStorage.Instance.IsReady)
        {
            OnWorldDataLoaded?.Invoke();
            Debug.Log("MapManager: World data loaded event triggered successfully");
        }
        else
        {
            Debug.LogWarning("MapManager: World data loaded event skipped - MapStorage not ready");
        }
    }

        public CellConfigurationPacket GetCellConfig(CellType cellType)
        {
            if (cellConfigurations == null || (int)cellType >= cellConfigurations.Length)
            {
                Debug.LogError($"Cell configuration for type {cellType} not found.");
                return default;
            }
            return cellConfigurations[(int)cellType];
        }

        /// <summary>
        /// Get the minimap color for a cell type from server configuration
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Unity Color converted from ARGB int, or transparent if not found</returns>
        public Color GetCellMinimapColor(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            if (config.Color == 0)
            {
                // Return transparent color if no configuration found
                return new Color(0, 0, 0, 0);
            }

            // Convert ARGB int to Unity Color
            // ARGB format: AARRGGBB
            int argb = config.Color;
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);

            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        /// <summary>
        /// Get the animation frame height in pixels for a cell type
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Frame height in pixels (tile height * 16), or 0 if not animated</returns>
        public int GetAnimationFrameHeight(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            if (config.Animation == CellAnimationType.None)
            {
                return 0;
            }

            // Frame height is defined in tiles, each tile is 16x16 pixels
            // Properties field contains the frame height in tiles
            return (int)config.Properties * 16;
        }

        /// <summary>
        /// Get the animation speed for a cell type
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Animation speed in frames per second</returns>
        public byte GetAnimationSpeed(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.AnimationSpeed;
        }

        /// <summary>
        /// Get the frame offset for a cell type
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Frame offset</returns>
        public byte GetFrameOffset(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.FrameOffset;
        }

        /// <summary>
        /// Check if a cell type has animation
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>True if animated, false otherwise</returns>
        public bool HasAnimation(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.Animation != CellAnimationType.None;
        }

        /// <summary>
        /// Get world information
        /// </summary>
        public string WorldCodeName => worldCodeName;
        public string WorldDisplayName => worldDisplayName;
        public ushort WorldWidth => width;
        public ushort WorldHeight => height;
    }
}
