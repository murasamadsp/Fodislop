using System.Runtime.InteropServices;
using UnityEngine;

namespace Fodinae.World.Terrain
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TerrainVertex
    {
        public Vector3 Position; // 12 bytes (offset 0)
        public Color32 Color;    // 4 bytes  (offset 12 -> 16 bytes total, aligning UV1/Vector4 to 16-byte boundary)

        public Vector2 UV0;      // 8 bytes  (offset 16)
        public Vector4 UV1;      // 16 bytes (subAtlasRects)
        public Vector4 UV2;      // 16 bytes (tileSizeUVs)
        public Vector4 UV3;      // 16 bytes (worldPositions)
        public Vector4 UV4;      // 16 bytes (animationData)
        public Vector4 UV5;      // 16 bytes (packedReliefShadowLocalUV)
        public Vector4 UV6;      // 16 bytes (glowData)
    }
}
