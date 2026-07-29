#nullable enable

using UnityEngine;

namespace Fodinae.Scripts.World.Terrain
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    public struct TerrainVertex
    {
        [System.Runtime.InteropServices.FieldOffset(0)]
        public Vector3 Position;

        [System.Runtime.InteropServices.FieldOffset(12)]
        public Color Color;

        [System.Runtime.InteropServices.FieldOffset(28)]
        public Vector2 UV0;

        [System.Runtime.InteropServices.FieldOffset(36)]
        public Vector4 UV1;   // subAtlasRects

        [System.Runtime.InteropServices.FieldOffset(52)]
        public Vector4 UV2;   // tileSizeUVs

        [System.Runtime.InteropServices.FieldOffset(68)]
        public Vector4 UV3;   // worldPositions

        [System.Runtime.InteropServices.FieldOffset(84)]
        public Vector4 UV4;   // animationData

        [System.Runtime.InteropServices.FieldOffset(100)]
        public Vector4 UV5;   // packedReliefShadowLocalUV

        [System.Runtime.InteropServices.FieldOffset(116)]
        public Vector4 UV6;   // glowData (x = cellGlow)
    }
}
