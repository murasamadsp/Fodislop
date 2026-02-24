using System;
using System.Collections.Generic;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.World;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Demo script for testing the terrain renderer with sample world data
    /// </summary>
    public class TerrainRendererDemo : MonoBehaviour
    {
        [Header("Demo Configuration")]
        [Tooltip("Reference to the terrain renderer to test")]
        [SerializeField] private EnhancedTerrainRenderer _terrainRenderer;
        
        [Tooltip("Reference to the world layer for testing")]
        [SerializeField] private WorldLayer<CellType> _worldLayer;
        
        [Tooltip("Sample world size for testing")]
        [SerializeField] private int _worldSize = 64;
        
        [Tooltip("Enable random world generation")]
        [SerializeField] private bool _generateRandomWorld = true;

        [Header("Test Patterns")]
        [Tooltip("Enable checkerboard pattern test")]
        [SerializeField] private bool _enableCheckerboard = false;
        
        [Tooltip("Enable gradient pattern test")]
        [SerializeField] private bool _enableGradient = false;

        private void Start()
        {
            if (_terrainRenderer == null)
            {
                Debug.LogError("TerrainRenderer reference not set in TerrainRendererDemo");
                return;
            }

            if (_worldLayer == null)
            {
                Debug.LogError("WorldLayer reference not set in TerrainRendererDemo");
                return;
            }

            // Initialize demo world
            InitializeDemoWorld();
            
            Debug.Log($"TerrainRendererDemo initialized with world size {_worldSize}x{_worldSize}");
        }

        private void InitializeDemoWorld()
        {
            // Generate sample world data
            if (_generateRandomWorld)
            {
                GenerateRandomWorld();
            }
            else if (_enableCheckerboard)
            {
                GenerateCheckerboardPattern();
            }
            else if (_enableGradient)
            {
                GenerateGradientPattern();
            }
            else
            {
                // Default pattern - simple terrain
                GenerateSimpleTerrain();
            }
        }

        private void GenerateRandomWorld()
        {
            var random = new System.Random();
            var cellTypes = Enum.GetValues(typeof(CellType)) as CellType[];
            
            // Remove special cell types that shouldn't be in random generation
            var validCellTypes = new List<CellType>();
            foreach (var cellType in cellTypes)
            {
                if (cellType != CellType.Unloaded && cellType != CellType.Pregener)
                {
                    validCellTypes.Add(cellType);
                }
            }

            for (int y = 0; y < _worldSize; y++)
            {
                for (int x = 0; x < _worldSize; x++)
                {
                    int randomIndex = random.Next(validCellTypes.Count);
                    _worldLayer[x, y] = validCellTypes[randomIndex];
                }
            }
        }

        private void GenerateCheckerboardPattern()
        {
            for (int y = 0; y < _worldSize; y++)
            {
                for (int x = 0; x < _worldSize; x++)
                {
                    if ((x + y) % 2 == 0)
                    {
                        _worldLayer[x, y] = CellType.Road;
                    }
                    else
                    {
                        _worldLayer[x, y] = CellType.Boulder1;
                    }
                }
            }
        }

        private void GenerateGradientPattern()
        {
            for (int y = 0; y < _worldSize; y++)
            {
                for (int x = 0; x < _worldSize; x++)
                {
                    float gradient = (x + y) / (float)(_worldSize * 2);
                    
                    if (gradient < 0.25f)
                    {
                        _worldLayer[x, y] = CellType.Empty;
                    }
                    else if (gradient < 0.5f)
                    {
                        _worldLayer[x, y] = CellType.Road;
                    }
                    else if (gradient < 0.75f)
                    {
                        _worldLayer[x, y] = CellType.Boulder1;
                    }
                    else
                    {
                        _worldLayer[x, y] = CellType.WhiteSand;
                    }
                }
            }
        }

        private void GenerateSimpleTerrain()
        {
            // Create a simple terrain with different cell types in quadrants
            int halfSize = _worldSize / 2;
            
            for (int y = 0; y < _worldSize; y++)
            {
                for (int x = 0; x < _worldSize; x++)
                {
                    if (x < halfSize && y < halfSize)
                    {
                        _worldLayer[x, y] = CellType.Empty;
                    }
                    else if (x >= halfSize && y < halfSize)
                    {
                        _worldLayer[x, y] = CellType.Road;
                    }
                    else if (x < halfSize && y >= halfSize)
                    {
                        _worldLayer[x, y] = CellType.Boulder1;
                    }
                    else
                    {
                        _worldLayer[x, y] = CellType.WhiteSand;
                    }
                }
            }
        }

        /// <summary>
        /// Update world data at runtime for testing
        /// </summary>
        public void UpdateWorldData()
        {
            InitializeDemoWorld();
        }

        /// <summary>
        /// Get demo statistics
        /// </summary>
        /// <returns>String with demo statistics</returns>
        public string GetDemoStats()
        {
            int totalCells = _worldSize * _worldSize;
            var cellCounts = new Dictionary<CellType, int>();
            
            for (int y = 0; y < _worldSize; y++)
            {
                for (int x = 0; x < _worldSize; x++)
                {
                    var cellType = _worldLayer[x, y];
                    if (cellCounts.ContainsKey(cellType))
                    {
                        cellCounts[cellType]++;
                    }
                    else
                    {
                        cellCounts[cellType] = 1;
                    }
                }
            }

            string stats = $"Demo World Stats ({_worldSize}x{_worldSize} = {totalCells} cells):\n";
            foreach (var kvp in cellCounts)
            {
                float percentage = (kvp.Value / (float)totalCells) * 100;
                stats += $"  {kvp.Key}: {kvp.Value} ({percentage:F1}%)\n";
            }

            return stats;
        }

        private void OnValidate()
        {
            // Ensure world size is a multiple of chunk size
            if (_worldLayer != null && _worldSize % _worldLayer.ChunkSize != 0)
            {
                Debug.LogWarning("World size should be a multiple of chunk size for optimal performance");
            }
        }
    }
}