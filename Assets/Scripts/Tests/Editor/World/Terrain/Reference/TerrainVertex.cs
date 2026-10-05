#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Kern.World.Terrain;
[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct TerrainVertex
{
    // ── Float32 ────────────────────────────────────── offset  bytes
    [FieldOffset(0)]  public Vector3 Position;    //  0    12

    // ── Float16 raw storage — UV0 ─────────────────
    [FieldOffset(12)] public ushort UV0x;          // 12     2
    [FieldOffset(14)] public ushort UV0y;          // 14     2

    // ── Float16 raw storage — UV1 ─────────────────
    [FieldOffset(16)] public ushort UV1x;          // 16     2
    [FieldOffset(18)] public ushort UV1y;          // 18     2
    [FieldOffset(20)] public ushort UV1z;          // 20     2
    [FieldOffset(22)] public ushort UV1w;          // 22     2

    // ── Float16 raw storage — UV2 ─────────────────
    [FieldOffset(24)] public ushort UV2x;          // 24     2
    [FieldOffset(26)] public ushort UV2y;          // 26     2
    [FieldOffset(28)] public ushort UV2z;          // 28     2
    [FieldOffset(30)] public ushort UV2w;          // 30     2

    // ── Float32 (world tile coords, can exceed 2048) ──
    [FieldOffset(32)] public Vector4 UV3;          // 32    16

    // ── Float16 raw storage — UV4 ─────────────────
    [FieldOffset(48)] public ushort UV4x;          // 48     2
    [FieldOffset(50)] public ushort UV4y;          // 50     2
    [FieldOffset(52)] public ushort UV4z;          // 52     2
    [FieldOffset(54)] public ushort UV4w;          // 54     2

    // ── Float16 raw storage — UV5 ─────────────────
    [FieldOffset(56)] public ushort UV5x;          // 56     2
    [FieldOffset(58)] public ushort UV5y;          // 58     2
    [FieldOffset(60)] public ushort UV5z;          // 60     2
    [FieldOffset(62)] public ushort UV5w;          // 62     2

    // ── Float32 (packed RGB color reaches 16 777 215) ──
    [FieldOffset(64)] public Vector4 UV6;          // 64    16
    //                                             ───────────
    //                                             total   80

    // ── Write-only properties: float → half ───────────────

    public void CopySurfaceFrom(in TerrainVertex source)
    {
        UV1x = source.UV1x;
        UV1y = source.UV1y;
        UV1z = source.UV1z;
        UV1w = source.UV1w;
        UV2x = source.UV2x;
        UV2y = source.UV2y;
        UV2z = source.UV2z;
        UV2w = source.UV2w;
        UV3 = source.UV3;
        UV4x = source.UV4x;
        UV4y = source.UV4y;
        UV4z = source.UV4z;
        UV4w = source.UV4w;
        UV6 = source.UV6;
    }

    public Vector2 UV0
    {
        set
        {
            UV0x = H(value.x);
            UV0y = H(value.y);
        }
    }

    public Vector4 UV1
    {
        set
        {
            UV1x = H(value.x);
            UV1y = H(value.y);
            UV1z = H(value.z);
            UV1w = H(value.w);
        }
    }

    public Vector4 UV2
    {
        set
        {
            UV2x = H(value.x);
            UV2y = H(value.y);
            UV2z = H(value.z);
            UV2w = H(value.w);
        }
    }

    public Vector4 UV4
    {
        set
        {
            UV4x = H(value.x);
            UV4y = H(value.y);
            UV4z = H(value.z);
            UV4w = H(value.w);
        }
    }

    public Vector4 UV5
    {
        set
        {
            UV5x = H(value.x);
            UV5y = H(value.y);
            UV5z = H(value.z);
            UV5w = H(value.w);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort H(float f)
    {
        int bits = BitConverter.SingleToInt32Bits(f);
        int sign = (bits >> 16) & 0x8000;
        int exp = ((bits >> 23) & 0xFF) - 127 + 15;
        int mantissa = bits & 0x7FFFFF;

        if (exp <= 0)
        {
            return (ushort)sign; // underflow → ±0
        }

        if (exp >= 31)
        {
            return (ushort)(sign | 0x7C00); // overflow → ±Inf
        }

        return (ushort)(sign | (exp << 10) | (mantissa >> 13));
    }

}
