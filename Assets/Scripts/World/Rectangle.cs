#nullable enable

namespace Fodinae.World;

/// <summary>
/// Represents a rectangle in the texture atlas.
/// </summary>
public readonly record struct Rectangle(int X, int Y, int Width, int Height);
