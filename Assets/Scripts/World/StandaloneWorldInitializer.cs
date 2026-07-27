using System;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    [RequireComponent(typeof(MapManager))]
    public class StandaloneWorldInitializer : MonoBehaviour
    {
        [Header("Standalone Configuration")]
        [SerializeField]
        private bool _enableStandaloneMode = true;

        [SerializeField]
        private int _testWorldWidth = 128;

        [SerializeField]
        private int _testWorldHeight = 128;

        [SerializeField]
        private string _testWorldName = "Standalone_Test_World";

        [SerializeField]
        private float _checkInterval = 2.0f;

        [Header("Debug Settings")]
        [SerializeField]
        private bool _enableDebugLogging = true;

        private MapManager _mapManager;
        private bool _initializationAttempted = false;
        private bool _isInitialized = false;
        private float _startTime;

        [ContextMenu("Force Standalone Initialization")]
        public void ForceStandaloneInitialization()
        {
            _initializationAttempted = false;
            _isInitialized = false;
            AttemptStandaloneInitialization();
        }

        protected void Awake()
        {
            _mapManager = GetComponent<MapManager>();
            if (_enableDebugLogging)
            {
                Debug.Log("[StandaloneWorldInitializer] AWAKE CALLED");
            }

            if (!enabled)
            {
                enabled = true;
            }
        }

        protected void OnEnable()
        {
            if (_enableDebugLogging)
            {
                Debug.Log("[StandaloneWorldInitializer] ONENABLE CALLED");
            }

            if (!_enableStandaloneMode)
            {
                return;
            }

            _startTime = Time.time;
            InvokeRepeating(nameof(CheckInitializationTimeout), _checkInterval, _checkInterval);
        }

        protected void Start()
        {
            if (_enableDebugLogging)
            {
                Debug.Log("[StandaloneWorldInitializer] START CALLED");
            }

            if (!_enableStandaloneMode)
            {
                return;
            }
        }

        protected void OnDisable()
        {
            CancelInvoke();
        }

        private void Update()
        {
            if (!_enableStandaloneMode || _isInitialized)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                return;
            }

            if (_mapManager.IsWorldInitialized)
            {
                _isInitialized = true;
                enabled = false;
                return;
            }

            var storage = ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            if (storage != null && storage.IsReady)
            {
                _isInitialized = true;
                _mapManager.OnWorldInitialized?.Invoke();
                _mapManager.OnWorldDataLoaded?.Invoke();
                enabled = false;
            }
        }

        private void CheckInitializationTimeout()
        {
            if (!Application.isPlaying || _initializationAttempted || _isInitialized)
            {
                return;
            }

            if (_mapManager.IsWorldInitialized)
            {
                if (_enableDebugLogging)
                {
                    Debug.Log("[StandaloneWorldInitializer] World already initialized, skipping standalone mode");
                }

                _isInitialized = true;
                enabled = false;
                return;
            }

            var cm = ServiceLocator.Resolve<IConnectionService>() as ConnectionManager;
            if (cm != null && cm.Connection != null &&
                cm.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                if (_enableDebugLogging)
                {
                    Debug.Log("[StandaloneWorldInitializer] Server connected, waiting for world data");
                }

                return;
            }

            if (Time.time - _startTime >= _checkInterval * 3)
            {
                AttemptStandaloneInitialization();
            }
        }

        private void AttemptStandaloneInitialization()
        {
            if (_initializationAttempted)
            {
                return;
            }

            _initializationAttempted = true;

            if (_mapManager.IsWorldInitialized)
            {
                if (_enableDebugLogging)
                {
                    Debug.Log("[StandaloneWorldInitializer] World already initialized, aborting standalone init");
                }

                return;
            }

            if (_enableDebugLogging)
            {
                Debug.Log($"[StandaloneWorldInitializer] Attempting standalone world: {_testWorldName}");
            }

            try
            {
                var cellConfigurations = CreateTestCellConfigurations();
                var worldInitPacket = new WorldInitPacket
                {
                    CodeName = _testWorldName,
                    DisplayName = _testWorldName,
                    Width = (ushort)_testWorldWidth,
                    Height = (ushort)_testWorldHeight,
                    Cells = cellConfigurations,
                };

                _mapManager.LoadWorldInit(worldInitPacket);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StandaloneWorldInitializer] Failed to create standalone world: {ex.Message}");
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
                };
            }

            return configurations;
        }
    }
}
