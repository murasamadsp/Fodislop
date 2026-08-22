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
        public Color32 Color;

        [FieldOffset(16)]
        public Vector2 UV0;

        [FieldOffset(24)]
        public Vector4 UV1;

        [FieldOffset(40)]
        public Vector4 UV2;

        [FieldOffset(56)]
        public Vector4 UV3;

        [FieldOffset(72)]
        public Vector4 UV4;

        [FieldOffset(88)]
        public Vector4 UV5;

        [FieldOffset(104)]
        public Vector4 UV6;
    }
}
