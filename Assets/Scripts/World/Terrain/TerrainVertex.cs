#nullable enable

using System.Runtime.InteropServices;
using UnityEngine;

namespace Fodinae.World.Terrain
{
    [StructLayout(LayoutKind.Explicit)]
    public struct TerrainVertex
    {
        [FieldOffset(0)]
        public Vector3 Position;

        [FieldOffset(12)]
        public Color Color;

        [FieldOffset(28)]
        public Vector2 UV0;

        [FieldOffset(36)]
        public Vector4 UV1;

        [FieldOffset(52)]
        public Vector4 UV2;

        [FieldOffset(68)]
        public Vector4 UV3;

        [FieldOffset(84)]
        public Vector4 UV4;

        [FieldOffset(100)]
        public Vector4 UV5;

        [FieldOffset(116)]
        public Vector4 UV6;
    }
}
