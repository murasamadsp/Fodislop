#nullable enable

namespace Fodinae.World.Terrain;

internal static class TerrainCacheArrayScroller
{
    public static void Scroll<T>(T[,] buffer, int width, int height, int dx, int dy)
    {
        if (dx > 0)
        {
            for (int x = 0; x < width - dx; x++)
            {
                CopyColumn(buffer, x + dx, x, height, dy);
            }
        }
        else if (dx < 0)
        {
            for (int x = width - 1; x >= -dx; x--)
            {
                CopyColumn(buffer, x + dx, x, height, dy);
            }
        }
        else
        {
            ScrollRows(buffer, width, height, dy);
        }
    }

    private static void CopyColumn<T>(T[,] buffer, int sourceX, int targetX, int height, int dy)
    {
        if (dy > 0)
        {
            for (int y = 0; y < height - dy; y++)
            {
                buffer[targetX, y] = buffer[sourceX, y + dy];
            }
        }
        else if (dy < 0)
        {
            for (int y = height - 1; y >= -dy; y--)
            {
                buffer[targetX, y] = buffer[sourceX, y + dy];
            }
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                buffer[targetX, y] = buffer[sourceX, y];
            }
        }
    }

    private static void ScrollRows<T>(T[,] buffer, int width, int height, int dy)
    {
        if (dy > 0)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height - dy; y++)
                {
                    buffer[x, y] = buffer[x, y + dy];
                }
            }
        }
        else if (dy < 0)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = height - 1; y >= -dy; y--)
                {
                    buffer[x, y] = buffer[x, y + dy];
                }
            }
        }
    }
}
