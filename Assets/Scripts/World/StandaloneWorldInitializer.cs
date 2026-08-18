#nullable enable

using System;
using Fodinae.Game.Managers;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World
{
    [ExecuteAlways]
    [RequireComponent(typeof(MapManager))]
    public class StandaloneWorldInitializer : MonoBehaviour
    {
        [Header("Standalone Configuration")]
        [SerializeField]
        private bool _enableStandaloneMode = true;

        [SerializeField]
        private int _testWorldWidth;

        [SerializeField]
        private int _testWorldHeight;

        [SerializeField]
        private string _testWorldName = string.Empty;

        [Header("Debug Settings")]
        [SerializeField]
        private bool _enableDebugLogging = true;

        private MapManager? _mapManager;
        private bool _initializationAttempted = false;
        private MapStorage? _previewStorage;

        [ContextMenu("Force Standalone Initialization")]
        public void ForceStandaloneInitialization()
        {
            _initializationAttempted = false;
            AttemptStandaloneInitialization();
        }

        protected void Awake()
        {
            if (Application.isPlaying)
            {
                enabled = false;
                return;
            }

            _mapManager = GetComponent<MapManager>();
            if (_enableDebugLogging && !Application.isPlaying)
            {
                Debug.Log("[StandaloneWorldInitializer] Editor preview awake");
            }
        }

        protected void OnEnable()
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (!_enableStandaloneMode)
            {
                return;
            }

            AttemptStandaloneInitialization();
        }

        protected void OnDisable()
        {
            CancelInvoke();
            _previewStorage?.Dispose();
            _previewStorage = null;
        }

        private void AttemptStandaloneInitialization()
        {
            if (_initializationAttempted)
            {
                return;
            }

            _initializationAttempted = true;

            if (Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "[StandaloneWorldInitializer] Preview initialization is forbidden in Play Mode.");
            }

            if (_mapManager == null)
            {
                throw new InvalidOperationException(
                    "[StandaloneWorldInitializer] Preview requires MapManager on the same GameObject.");
            }

            if (_testWorldWidth <= 0 || _testWorldHeight <= 0 || string.IsNullOrWhiteSpace(_testWorldName))
            {
                throw new InvalidOperationException(
                    "[StandaloneWorldInitializer] Preview world name and positive dimensions must be configured explicitly.");
            }

            if (_testWorldWidth > ushort.MaxValue || _testWorldHeight > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"[StandaloneWorldInitializer] Preview dimensions {_testWorldWidth}x{_testWorldHeight} exceed packet limits.");
            }

            if (_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (_enableDebugLogging)
            {
                Debug.Log($"[StandaloneWorldInitializer] Attempting standalone world: {_testWorldName}");
            }

            try
            {
                _previewStorage = new MapStorage();
                _mapManager.InitializeEditorPreview(_previewStorage);
                var cellConfigurations = CreateTestCellConfigurations();
                var worldInitPacket = new WorldInitPacket
                {
                    CodeName = _testWorldName,
                    DisplayName = _testWorldName,
                    Width = (ushort)_testWorldWidth,
                    Height = (ushort)_testWorldHeight,
                    Cells = cellConfigurations,
                };

                _mapManager!.LoadWorldInit(worldInitPacket);
            }
            catch (Exception ex)
            {
                _initializationAttempted = false;
                Debug.LogError($"[StandaloneWorldInitializer] Preview initialization failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private static CellConfigurationPacket[] CreateTestCellConfigurations()
        {
            var configurations = new CellConfigurationPacket[256];

            for (int i = 0; i < configurations.Length; i++)
            {
                configurations[i] = new CellConfigurationPacket
                {
                    Animation = 0,
                    AnimationSpeed = 0,
                    Color = 0,
                    FrameOffset = 0,
                    Properties = 0,
                    ReliefGroup = 0,
                    Distortion = 0,
                };
            }

            const CellConfigProperties ROAD_PROPS = CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow;
            const CellConfigProperties SAND_BOULDER_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow;

            SetConfig(configurations, CellType.Empty, ROAD_PROPS, 0);
            SetConfig(configurations, CellType.Road, ROAD_PROPS, 0);
            SetConfig(configurations, CellType.BuildingRoad, ROAD_PROPS, 0);
            SetConfig(configurations, CellType.GoldenRoad, ROAD_PROPS, 0);
            SetConfig(configurations, CellType.PolymerRoad, ROAD_PROPS, 0);
            SetConfig(configurations, CellType.VolcanoBackground, ROAD_PROPS, 0);
            SetConfig(configurations, CellType.BlackBoulder1, SAND_BOULDER_PROPS, 1, CellDistortionType.Cause);
            SetConfig(configurations, CellType.Boulder1, SAND_BOULDER_PROPS, 1, CellDistortionType.Cause);
            SetConfig(configurations, CellType.WhiteSand, SAND_BOULDER_PROPS, 1, CellDistortionType.Cause);

            return configurations;
        }

        private static void SetConfig(CellConfigurationPacket[] configs, CellType type, CellConfigProperties props, byte reliefGroup,
            CellDistortionType distortion = CellDistortionType.Neutral)
        {
            configs[(int)type] = new CellConfigurationPacket
            {
                Animation = 0,
                AnimationSpeed = 0,
                Color = 0,
                FrameOffset = 0,
                Properties = props,
                ReliefGroup = reliefGroup,
                Distortion = distortion,
            };
        }
    }
}
