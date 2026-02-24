using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
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

        private CellConfigurationPacket[] cellConfigurations;
        private string worldCodeName;
        private string worldDisplayName;
        private ushort width;
        private ushort height;

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
            MapStorage.Instance.InitWorld(packet.CodeName, width, height);
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
