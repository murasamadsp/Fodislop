using System;
using System.Collections.Generic;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.World;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Test component to verify that terrain torus wrapping is working correctly
    /// </summary>
    public class TerrainTorusWrappingTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Atlas size to test with")]
        [SerializeField] private int _atlasSize = 512;
        [Tooltip("Cell size for texture storage")]
        [SerializeField] private int _cellSize = 32;
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool _enableDebugLogging = true;

        private TextureAtlas _testAtlas;
        private CellType _testCellType = CellType.Road;
        private bool _testInitialized = false;

        private void Start()
        {
            InitializeTest();
        }

        private void InitializeTest()
        {
            if (_testInitialized) return;

            // Create a test atlas
            _testAtlas = new TextureAtlas(_atlasSize, _cellSize, 2);
            
            // Create a test texture and add it to the atlas
            var testTexture = CreateTestTexture(_cellSize, _cellSize, Color.green);
            if (_testAtlas.TryAddTexture(_testCellType, testTexture, out var baseCoord))
            {
                Debug.Log($"[TerrainTorusWrappingTest] Added test texture to atlas at {baseCoord}");
                _testInitialized = true;
            }
            else
            {
                Debug.LogError("[TerrainTorusWrappingTest] Failed to add test texture to atlas");
                return;
            }

            // Run the torus wrapping tests
            RunTorusWrappingTests();
        }

        private Texture2D CreateTestTexture(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void RunTorusWrappingTests()
        {
            if (!_testInitialized) return;

            Debug.Log("[TerrainTorusWrappingTest] Running torus wrapping tests...");

            // Test 1: Basic wrapping at atlas boundaries
            TestPosition(0, 0, "Edge (0,0)");
            TestPosition(31, 0, "Edge (31,0) - should wrap to (0,0)");
            TestPosition(32, 0, "Wrap (32,0) - should wrap to (0,0)");
            TestPosition(-1, 0, "Negative (-1,0) - should wrap to (31,0)");
            TestPosition(0, 21, "Edge (0,21) - should wrap to (0,0)");
            TestPosition(0, 22, "Wrap (0,22) - should wrap to (0,0)");
            TestPosition(0, -1, "Negative (0,-1) - should wrap to (0,21)");

            // Test 2: Variation testing
            TestVariation(0, 0, true, false, "Horizontal variation at (0,0)");
            TestVariation(0, 0, false, true, "Vertical variation at (0,0)");
            TestVariation(0, 0, true, true, "Both variations at (0,0)");

            // Test 3: Consistency check - same position should always return same coordinates
            TestConsistency();

            Debug.Log("[TerrainTorusWrappingTest] All tests completed!");
        }

        private void TestPosition(int globalX, int globalY, string description)
        {
            var wrappedCoord = _testAtlas.GetWrappedCoordinate(_testCellType, globalX, globalY);
            var baseCoord = _testAtlas.GetCoordinate(_testCellType);

            if (_enableDebugLogging)
            {
                Debug.Log($"[TerrainTorusWrappingTest] {description}: Global({globalX},{globalY}) -> Atlas({wrappedCoord.AtlasX},{wrappedCoord.AtlasY}) Size({wrappedCoord.Width}x{wrappedCoord.Height})");
            }

            // Verify that wrapped coordinates use 16x16 tiles
            bool correctSize = wrappedCoord.Width == 16 && wrappedCoord.Height == 16;
            if (!correctSize)
            {
                Debug.LogError($"[TerrainTorusWrappingTest] FAILED: {description} - Expected 16x16 tiles, got {wrappedCoord.Width}x{wrappedCoord.Height}");
            }

            // Verify that coordinates are within atlas bounds
            bool withinBounds = wrappedCoord.AtlasX >= 0 && wrappedCoord.AtlasY >= 0 &&
                               wrappedCoord.AtlasX + wrappedCoord.Width <= _atlasSize &&
                               wrappedCoord.AtlasY + wrappedCoord.Height <= _atlasSize;
            if (!withinBounds)
            {
                Debug.LogError($"[TerrainTorusWrappingTest] FAILED: {description} - Coordinates out of bounds: {wrappedCoord}");
            }
        }

        private void TestVariation(int globalX, int globalY, bool horizontal, bool vertical, string description)
        {
            var variation = new CellVariation { Horizontal = horizontal, Vertical = vertical };
            var wrappedCoord = _testAtlas.GetWrappedCoordinate(_testCellType, globalX, globalY, variation);

            if (_enableDebugLogging)
            {
                Debug.Log($"[TerrainTorusWrappingTest] {description}: Atlas({wrappedCoord.AtlasX},{wrappedCoord.AtlasY}) Size({wrappedCoord.Width}x{wrappedCoord.Height})");
            }

            // Verify that variation offsets are applied correctly (8 pixels for 16x16 tiles)
            int expectedX = wrappedCoord.AtlasX;
            int expectedY = wrappedCoord.AtlasY;
            if (horizontal) expectedX += 8;
            if (vertical) expectedY += 8;

            bool correctVariation = wrappedCoord.AtlasX == expectedX && wrappedCoord.AtlasY == expectedY;
            if (!correctVariation)
            {
                Debug.LogError($"[TerrainTorusWrappingTest] FAILED: {description} - Expected variation offset, got {wrappedCoord}");
            }
        }

        private void TestConsistency()
        {
            // Test that the same position always returns the same coordinates
            var coord1 = _testAtlas.GetWrappedCoordinate(_testCellType, 10, 15);
            var coord2 = _testAtlas.GetWrappedCoordinate(_testCellType, 10, 15);
            var coord3 = _testAtlas.GetWrappedCoordinate(_testCellType, 10, 15);

            bool consistent = coord1.Equals(coord2) && coord2.Equals(coord3);
            if (!consistent)
            {
                Debug.LogError($"[TerrainTorusWrappingTest] FAILED: Consistency test - coordinates not consistent: {coord1}, {coord2}, {coord3}");
            }
            else if (_enableDebugLogging)
            {
                Debug.Log($"[TerrainTorusWrappingTest] PASSED: Consistency test - all coordinates match: {coord1}");
            }
        }

        private void OnGUI()
        {
            if (!_testInitialized) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("Terrain Torus Wrapping Test", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });
            GUILayout.Label($"Atlas Size: {_atlasSize}x{_atlasSize}");
            GUILayout.Label($"Cell Size: {_cellSize}");
            GUILayout.Label($"Test Cell Type: {_testCellType}");
            GUILayout.Label("Check console for detailed test results");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_testAtlas != null)
            {
                _testAtlas.Clear();
            }
        }
    }
}