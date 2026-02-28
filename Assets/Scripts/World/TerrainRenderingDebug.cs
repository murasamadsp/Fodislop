using System;
using System.Collections.Generic;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.World;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Debug tool to verify terrain rendering and torus wrapping is working correctly
    /// </summary>
    public class TerrainRenderingDebug : MonoBehaviour
    {
        [Header("Debug Configuration")]
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool _enableDebugLogging = true;
        [Tooltip("Log frequency in seconds")]
        [SerializeField] private float _logInterval = 5f;
        [Tooltip("Test specific global positions")]
        [SerializeField] private Vector2Int[] _testPositions = new Vector2Int[]
        {
            new Vector2Int(0, 0),
            new Vector2Int(15, 0),
            new Vector2Int(16, 0),
            new Vector2Int(31, 0),
            new Vector2Int(32, 0),
            new Vector2Int(0, 15),
            new Vector2Int(0, 16),
            new Vector2Int(15, 15),
            new Vector2Int(16, 16),
            new Vector2Int(100, 100),
            new Vector2Int(-1, -1),
            new Vector2Int(-16, -16)
        };

        private float _lastLogTime = 0f;
        private TerrainRenderer _terrainRenderer;
        private WorldTextureManager _textureManager;

        private void Start()
        {
            _terrainRenderer = FindObjectOfType<TerrainRenderer>();
            _textureManager = WorldTextureManager.Instance;
        }

        private void Update()
        {
            if (!_enableDebugLogging) return;

            if (Time.time - _lastLogTime > _logInterval)
            {
                _lastLogTime = Time.time;
                LogDebugInfo();
            }
        }

        private void LogDebugInfo()
        {
            Debug.Log("=== Terrain Rendering Debug Info ===");
            Debug.Log($"TerrainRenderer found: {_terrainRenderer != null}");
            Debug.Log($"WorldTextureManager found: {_textureManager != null}");
            Debug.Log($"Visible chunks: {_terrainRenderer?._visibleChunks.Count ?? 0}");
            Debug.Log($"Chunk meshes: {_terrainRenderer?._chunkMeshes.Count ?? 0}");
            Debug.Log($"Atlas size: {_textureManager?._currentAtlas.Size ?? 0}");
            Debug.Log($"Cell size: {_textureManager?._currentAtlas.CellSize ?? 0}");
            Debug.Log("");

            if (_textureManager != null && _terrainRenderer != null)
            {
                TestTorusWrapping();
            }
        }

        private void TestTorusWrapping()
        {
            Debug.Log("=== Torus Wrapping Test ===");
            var atlas = _textureManager._currentAtlas;
            int terrainTileSize = 16; // Fixed terrain tile size
            int tilesPerRow = atlas.Size / terrainTileSize;

            Debug.Log($"Atlas Size: {atlas.Size}x{atlas.Size}");
            Debug.Log($"Terrain Tile Size: {terrainTileSize}x{terrainTileSize}");
            Debug.Log($"Tiles per row/column: {tilesPerRow}");
            Debug.Log("");

            foreach (var pos in _testPositions)
            {
                var wrappedCoord = atlas.GetWrappedCoordinate(CellType.Road, pos.x, pos.y);
                var regularCoord = atlas.GetCoordinate(CellType.Road);

                Debug.Log($"Global({pos.x}, {pos.y}) -> Wrapped({wrappedCoord.AtlasX}, {wrappedCoord.AtlasY})");

                // Verify coordinates are within bounds and aligned to grid
                bool inBounds = wrappedCoord.AtlasX >= 0 && wrappedCoord.AtlasX < atlas.Size &&
                               wrappedCoord.AtlasY >= 0 && wrappedCoord.AtlasY < atlas.Size;
                bool aligned = wrappedCoord.AtlasX % terrainTileSize == 0 && wrappedCoord.AtlasY % terrainTileSize == 0;
                bool correctSize = wrappedCoord.Width == terrainTileSize && wrappedCoord.Height == terrainTileSize;

                if (!inBounds) Debug.LogError($"  ERROR: Out of bounds!");
                if (!aligned) Debug.LogError($"  ERROR: Not aligned to grid!");
                if (!correctSize) Debug.LogError($"  ERROR: Wrong tile size! Expected {terrainTileSize}x{terrainTileSize}, got {wrappedCoord.Width}x{wrappedCoord.Height}");

                if (inBounds && aligned && correctSize)
                {
                    Debug.Log($"  ✓ Valid coordinates");
                }
            }
            Debug.Log("");
        }

        /// <summary>
        /// Manually test a specific position
        /// </summary>
        public void TestPosition(int globalX, int globalY)
        {
            if (_textureManager == null) return;

            var atlas = _textureManager._currentAtlas;
            var wrappedCoord = atlas.GetWrappedCoordinate(CellType.Road, globalX, globalY);

            Debug.Log($"Test Position: Global({globalX}, {globalY}) -> Wrapped({wrappedCoord.AtlasX}, {wrappedCoord.AtlasY})");
            Debug.Log($"Tile Size: {wrappedCoord.Width}x{wrappedCoord.Height}");
            Debug.Log($"Atlas Size: {wrappedCoord.AtlasWidth}x{wrappedCoord.AtlasHeight}");
            Debug.Log($"UV Range: U({wrappedCoord.U1:F4} to {wrappedCoord.U2:F4}), V({wrappedCoord.V1:F4} to {wrappedCoord.V2:F4})");
        }

        /// <summary>
        /// Test if wrapping is consistent for equivalent positions
        /// </summary>
        public void TestWrappingConsistency()
        {
            if (_textureManager == null) return;

            var atlas = _textureManager._currentAtlas;
            int tilesPerRow = atlas.Size / atlas.CellSize;

            Debug.Log("=== Wrapping Consistency Test ===");

            for (int baseX = 0; baseX < 4; baseX++)
            {
                for (int baseY = 0; baseY < 4; baseY++)
                {
                    var baseCoord = atlas.GetWrappedCoordinate(CellType.Road, baseX, baseY);
                    var wrappedCoord = atlas.GetWrappedCoordinate(CellType.Road, baseX + tilesPerRow, baseY + tilesPerRow);

                    bool consistent = baseCoord.AtlasX == wrappedCoord.AtlasX && baseCoord.AtlasY == wrappedCoord.AtlasY;

                    Debug.Log($"({baseX}, {baseY}) vs ({baseX + tilesPerRow}, {baseY + tilesPerRow}): {(consistent ? "✓ Consistent" : "✗ Inconsistent")}");

                    if (!consistent)
                    {
                        Debug.LogError($"  Base: ({baseCoord.AtlasX}, {baseCoord.AtlasY})");
                        Debug.LogError($"  Wrapped: ({wrappedCoord.AtlasX}, {wrappedCoord.AtlasY})");
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (!_enableDebugLogging) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("Terrain Rendering Debug");
            GUILayout.Label($"Visible Chunks: {_terrainRenderer?._visibleChunks.Count ?? 0}");
            GUILayout.Label($"Chunk Meshes: {_terrainRenderer?._chunkMeshes.Count ?? 0}");
            GUILayout.Label($"Atlas Size: {_textureManager?._currentAtlas.Size ?? 0}");
            GUILayout.Label($"Cell Size: {_textureManager?._currentAtlas.CellSize ?? 0}");

            if (GUILayout.Button("Test Wrapping Consistency"))
            {
                TestWrappingConsistency();
            }

            if (GUILayout.Button("Test Specific Position"))
            {
                TestPosition(16, 16);
            }

            GUILayout.EndArea();
        }
    }
}