#nullable enable

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Fodinae.World
{
    /// <summary>
    /// Utility class to draw consistent and pretty Gizmos in the Editor.
    /// </summary>
    public static class FodinaeGizmos
    {
        private static GUIStyle? _labelStyle;

        private static GUIStyle LabelStyle
        {
            get
            {
                if (_labelStyle == null)
                {
                    _labelStyle = new GUIStyle
                    {
                        fontSize = 12,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                    };
                }

                return _labelStyle;
            }
        }

        public static void DrawCircle(Vector3 center, float radius, Color color, float thickness = 2f)
        {
            Handles.color = color;
            Handles.DrawWireDisc(center, Vector3.forward, radius, thickness);
        }

        public static void DrawLabel(Vector3 position, string text, Color color)
        {
            LabelStyle.normal.textColor = color;
            Handles.Label(position, text, LabelStyle);
        }

        public static void DrawLine(Vector3 start, Vector3 end, Color color, float thickness = 1f)
        {
            Handles.color = color;
            Handles.DrawLine(start, end, thickness);
        }

        public static void DrawBounds(Vector3 center, Vector2 size, Color color)
        {
            Handles.color = color;
            Handles.DrawWireCube(center, new Vector3(size.x, size.y, 0.1f));
        }

        public static void DrawGrid(Vector3 origin, int width, int height, float cellSize, Color color)
        {
            Handles.color = color;
            for (int i = 0; i <= width; i++)
            {
                Handles.DrawLine(origin + new Vector3(i * cellSize, 0, 0), origin + new Vector3(i * cellSize, height * cellSize, 0));
            }

            for (int j = 0; j <= height; j++)
            {
                Handles.DrawLine(origin + new Vector3(0, j * cellSize, 0), origin + new Vector3(width * cellSize, j * cellSize, 0));
            }
        }

        public static void DrawArrow(Vector3 pos, Vector3 direction, Color color, float length = 1f, float arrowHeadLength = 0.25f, float arrowHeadAngle = 20f)
        {
            if (direction == Vector3.zero)
            {
                return;
            }

            Vector3 dir = direction.normalized;
            Vector3 end = pos + (dir * length);

            Handles.color = color;
            Handles.DrawLine(pos, end);

            // 2D-наконечник: поворачиваем «хвост» стрелки в ±angle вокруг оси Z (плоскость XY)
            Vector3 headBase = -dir * arrowHeadLength;
            Vector3 right = Quaternion.Euler(0, 0, arrowHeadAngle) * headBase;
            Vector3 left = Quaternion.Euler(0, 0, -arrowHeadAngle) * headBase;
            Handles.DrawLine(end, end + right);
            Handles.DrawLine(end, end + left);
        }

        public static void DrawDottedLine(Vector3 start, Vector3 end, Color color, float dashSize = 2f)
        {
            Handles.color = color;
            Handles.DrawDottedLine(start, end, dashSize);
        }

        public static void DrawSolidRect(Vector3 center, Vector2 size, Color fillColor, Color outlineColor)
        {
            Vector3[] verts = new Vector3[]
            {
                center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0),
                center + new Vector3(size.x * 0.5f, -size.y * 0.5f, 0),
                center + new Vector3(size.x * 0.5f, size.y * 0.5f, 0),
                center + new Vector3(-size.x * 0.5f, size.y * 0.5f, 0),
            };
            Handles.DrawSolidRectangleWithOutline(verts, fillColor, outlineColor);
        }
    }
}
#endif
