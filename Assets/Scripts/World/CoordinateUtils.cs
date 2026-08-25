#nullable enable

using System;
using UnityEngine;

namespace Fodinae.World
{
    public static class CoordinateUtils
    {
        private static int ResolveHeight(int worldHeight)
        {
            if (worldHeight > 0)
            {
                return worldHeight;
            }

            throw new InvalidOperationException(
                "[CoordinateUtils] World height is required for coordinate conversion, " +
                "and must be supplied by the world data owner.");
        }

        /// <summary>
        /// Converts Server Y to Unity World Y (Centered on cell).
        /// </summary>
        public static float ServerToUnityY(int serverY, int worldHeight)
        {
            int h = ResolveHeight(worldHeight);
            return (h - 1 - serverY) + 0.5f;
        }

        /// <summary>
        /// Converts Unity World Y to Server Y. Coordinates outside the loaded
        /// world are invalid and must never wrap into another row.
        /// </summary>
        public static int UnityToServerY(float unityY, int worldHeight)
        {
            int h = ResolveHeight(worldHeight);
            int y = Mathf.FloorToInt(unityY);
            if (y < 0 || y >= h)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unityY),
                    unityY,
                    $"Unity Y must map inside the world height [0, {h}).");
            }

            return h - 1 - y;
        }

        /// <summary>
        /// Converts Server position to Unity World position (Center of cell).
        /// </summary>
        public static Vector3 ServerToUnityPos(int x, int y, int worldHeight, float z = 0f)
        {
            return new Vector3(x + 0.5f, ServerToUnityY(y, worldHeight), z);
        }

        /// <summary>
        /// Converts Unity World position to Server Grid position.
        /// </summary>
        public static Vector2Int UnityToServerPos(Vector3 unityPos, int worldHeight)
        {
            return new Vector2Int(Mathf.FloorToInt(unityPos.x), UnityToServerY(unityPos.y, worldHeight));
        }
    }
}
