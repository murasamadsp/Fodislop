#nullable enable

using UnityEngine;

namespace Kern.World;

/// <summary>Stable world-coordinate hashing. No repeating lookup table or global random state.</summary>
internal static class SurfaceSpatialHash
{
    public static float Sample(long worldCoordinate, int seed, int channel)
    {
        unchecked
        {
            ulong value = (ulong)worldCoordinate ^ ((ulong)(uint)seed << 32) ^ ((ulong)(uint)channel * 0x9E3779B97F4A7C15UL);
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (value >> 40) * (1f / 16777216f);
        }
    }

    public static float Noise(float worldXCells, float wavelengthCells, int seed, int channel)
    {
        double position = (double)worldXCells / wavelengthCells;
        long cell = (long)System.Math.Floor(position);
        float t = (float)(position - cell);
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(Sample(cell, seed, channel), Sample(cell + 1, seed, channel), t);
    }
}
