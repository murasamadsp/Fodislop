#nullable enable

using System;
using System.Diagnostics;
using UnityEngine;

namespace Fodinae.Core
{
    public static class FrameProfiler
    {
        public static float TerrainMeshTimeMs { get; set; }
        public static float TerrainCacheTimeMs { get; set; }
        public static float TerrainFloodFillTimeMs { get; set; }
        public static float TerrainGpuUploadTimeMs { get; set; }
        public static float LightingSolveTimeMs { get; set; }
        public static int ActiveDynamicLights { get; set; }
        public static long GcAllocPerFrameBytes { get; set; }

        // Cumulative terrain rebuild counters, deliberately not reset per frame.
        //
        // "The terrain rebuilds and looks different while walking" has two very
        // different causes and reading the code cannot tell them apart: either
        // rebuilds are frequent (a cost problem), or a rebuild produces a
        // different image from the one before it (a correctness problem). Rates
        // separate the two in one walk.
        public static int TerrainRebuildCount { get; set; }

        // Rebuilds that could not scroll the cache and repopulated from scratch.
        public static int TerrainFullPopulateCount { get; set; }

        // Rebuilds that had to drop and reallocate the mesh, which shows as a
        // frame with no terrain at all.
        public static int TerrainMeshClearCount { get; set; }

        private static long _lastThreadAllocatedBytes;

        public static void BeginFrame()
        {
            long currentAlloc = GC.GetAllocatedBytesForCurrentThread();
            GcAllocPerFrameBytes = Math.Max(0, currentAlloc - _lastThreadAllocatedBytes);
            _lastThreadAllocatedBytes = currentAlloc;
        }

        public static void ResetFrameTimers()
        {
            TerrainMeshTimeMs = 0f;
            TerrainCacheTimeMs = 0f;
            TerrainFloodFillTimeMs = 0f;
            TerrainGpuUploadTimeMs = 0f;
            LightingSolveTimeMs = 0f;
        }
    }
}
