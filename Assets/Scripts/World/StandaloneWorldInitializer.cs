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

        protected void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _initializationAttempted = false;
            }
        }

        protected void Update()
        {
            if (!Application.isPlaying)
            {
                if (_mapManager == null || !_mapManager.IsWorldInitialized)
                {
                    _initializationAttempted = false;
                    AttemptStandaloneInitialization();
                }
            }
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

            _mapManager ??= GetComponent<MapManager>() ?? UnityEngine.Object.FindAnyObjectByType<MapManager>(FindObjectsInactive.Include);
            if (_mapManager == null)
            {
                _initializationAttempted = false;
                return;
            }

            int width = _testWorldWidth > 0 ? _testWorldWidth : 128;
            int height = _testWorldHeight > 0 ? _testWorldHeight : 128;
            string worldName = !string.IsNullOrWhiteSpace(_testWorldName) ? _testWorldName : "EditorPreview";

            if (_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (_enableDebugLogging)
            {
                Debug.Log($"[StandaloneWorldInitializer] Attempting standalone world: {worldName}");
            }

            try
            {
                _previewStorage = new MapStorage();
                _mapManager.InitializeEditorPreview(_previewStorage);
                var cellConfigurations = CreateTestCellConfigurations();
                var worldInitPacket = new WorldInitPacket
                {
                    CodeName = worldName,
                    DisplayName = worldName,
                    Width = (ushort)width,
                    Height = (ushort)height,
                    Cells = cellConfigurations,
                };

                _mapManager.LoadWorldInit(worldInitPacket);

                var cellLayer = _previewStorage.CellLayer;
                if (cellLayer != null)
                {
                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            CellType type;
                            if (x >= 62 && x <= 66 && y >= 62 && y <= 66)
                            {
                                type = CellType.Empty;
                            }
                            else if (x >= 60 && x <= 68 && y >= 60 && y <= 68 && ((x + y) % 4 == 0))
                            {
                                type = CellType.Lava;
                            }
                            else if (y < 4)
                            {
                                type = CellType.WhiteSand;
                            }
                            else if (y >= height - 10)
                            {
                                type = CellType.BlackBoulder1;
                            }
                            else
                            {
                                type = (x % 3 == 0) ? CellType.Boulder1 : CellType.WhiteSand;
                            }

                            cellLayer.SetCell(x, y, type);
                        }
                    }
                }

                var textureManager = UnityEngine.Object.FindAnyObjectByType<WorldTextureManager>(FindObjectsInactive.Include);
                var terrainRenderer = UnityEngine.Object.FindAnyObjectByType<Terrain.TerrainRenderer>(FindObjectsInactive.Include);
                if (terrainRenderer != null && textureManager != null)
                {
                    terrainRenderer.InitializeEditorPreview(_previewStorage, _mapManager, textureManager);
                }

                var player = UnityEngine.Object.FindAnyObjectByType<Player.Logic.PlayerMovementController>(FindObjectsInactive.Include);
                if (player != null)
                {
                    player.InitializeEditorPreview(_previewStorage, _mapManager);
                    player.UpdateServerPosition(new Vector2Int(64, 64));
                }

                var lightingEngine = UnityEngine.Object.FindAnyObjectByType<Lighting.TerrariaLightingEngine>(FindObjectsInactive.Include);
                if (lightingEngine != null)
                {
                    lightingEngine.SetDynamicLight(0, new Vector2(64.5f, 63.5f), Color.white, 2.5f);
                }
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
            SetConfig(configurations, CellType.Lava, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, CellDistortionType.Cause, unchecked((int)0xFFFF5500));

            return configurations;
        }

        private static void SetConfig(CellConfigurationPacket[] configs, CellType type, CellConfigProperties props, byte reliefGroup,
            CellDistortionType distortion = CellDistortionType.Neutral, int color = 0)
        {
            configs[(int)type] = new CellConfigurationPacket
            {
                Animation = 0,
                AnimationSpeed = 0,
                Color = color,
                FrameOffset = 0,
                Properties = props,
                ReliefGroup = reliefGroup,
                Distortion = distortion,
            };
        }
    }
}
