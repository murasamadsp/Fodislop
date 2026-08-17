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
