#nullable enable

using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// A disposable, editor-only visual smoke test for the compact world layout.
    /// It intentionally does not touch MainGame.unity or runtime services.
    /// </summary>
    public sealed class PreviewShowcaseWindow : EditorWindow
    {
        private const int WorldWidth = 48;
        private const int WorldHeight = 28;
        private bool _showLabels = true;

        [MenuItem("Fodinae/Preview/Showcase")]
        private static void Open()
        {
            PreviewShowcaseWindow window = GetWindow<PreviewShowcaseWindow>();
            window.titleContent = new GUIContent("Fodinae Preview Showcase");
            window.minSize = new Vector2(720f, 460f);
            window.Show();
        }

        protected void OnGUI()
        {
            EditorGUILayout.LabelField("Compact World Showcase", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Editor-only visual smoke test. Runtime scenes, assets and service state are not modified.",
                MessageType.Info);

            _showLabels = EditorGUILayout.ToggleLeft("Show test labels", _showLabels);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild", GUILayout.Width(120f)))
                {
                    Repaint();
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    $"World {WorldWidth}x{WorldHeight} · terrain · buildings · robots · HUD",
                    EditorStyles.miniLabel);
            }

            Rect previewRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            DrawShowcase(previewRect);
        }

        private void DrawShowcase(Rect rect)
        {
            if (rect.width <= 2f || rect.height <= 2f)
            {
                return;
            }

            EditorGUI.DrawRect(rect, new Color(0.035f, 0.045f, 0.065f, 1f));
            float scale = Mathf.Min(
                (rect.width - 32f) / WorldWidth,
                (rect.height - 72f) / WorldHeight);
            Rect worldRect = new Rect(
                rect.x + ((rect.width - (WorldWidth * scale)) * 0.5f),
                rect.y + 42f,
                WorldWidth * scale,
                WorldHeight * scale);

            DrawTerrain(worldRect, scale);
            DrawEntities(worldRect, scale);
            DrawHud(rect);
        }

        private static void DrawTerrain(Rect worldRect, float scale)
        {
            for (int y = 0; y < WorldHeight; y++)
            {
                for (int x = 0; x < WorldWidth; x++)
                {
                    Color color = ResolveCellColor(x, y);
                    Rect cellRect = new Rect(
                        worldRect.x + (x * scale),
                        worldRect.y + (y * scale),
                        Mathf.Ceil(scale),
                        Mathf.Ceil(scale));
                    EditorGUI.DrawRect(cellRect, color);
                }
            }
        }

        private static Color ResolveCellColor(int x, int y)
        {
            bool border = x == 0 || y == 0 || x == WorldWidth - 1 || y == WorldHeight - 1;
            if (border)
            {
                return new Color(0.28f, 0.30f, 0.34f, 1f);
            }

            bool road = y == WorldHeight / 2 || x == WorldWidth / 2 || (x > 5 && x < 17 && y == 7);
            if (road)
            {
                return new Color(0.48f, 0.50f, 0.55f, 1f);
            }

            bool lava = x > 34 && y > 14;
            if (lava)
            {
                return new Color(0.72f, 0.16f, 0.06f, 1f);
            }

            bool crystal = (x + (y * 3)) % 17 == 0;
            if (crystal)
            {
                return new Color(0.20f, 0.75f, 0.95f, 1f);
            }

            return new Color(0.10f, 0.20f, 0.13f, 1f);
        }

        private void DrawEntities(Rect worldRect, float scale)
        {
            DrawBlock(worldRect, scale, 8, 4, 7, 4, new Color(0.55f, 0.33f, 0.16f, 1f), "BUILDING");
            DrawBlock(worldRect, scale, 29, 5, 8, 5, new Color(0.38f, 0.40f, 0.46f, 1f), "BASE");
            DrawMarker(worldRect, scale, 24, 14, new Color(1f, 0.95f, 0.25f, 1f), "PLAYER");
            DrawMarker(worldRect, scale, 15, 13, new Color(0.95f, 0.25f, 0.75f, 1f), "ROBOT");
            DrawMarker(worldRect, scale, 35, 11, new Color(0.25f, 0.85f, 0.95f, 1f), "ROBOT");
        }

        private void DrawBlock(
            Rect worldRect,
            float scale,
            int x,
            int y,
            int width,
            int height,
            Color color,
            string label)
        {
            Rect block = new Rect(
                worldRect.x + (x * scale),
                worldRect.y + (y * scale),
                width * scale,
                height * scale);
            EditorGUI.DrawRect(block, color);
            if (_showLabels && scale >= 8f)
            {
                GUI.Label(block, label, EditorStyles.whiteMiniLabel);
            }
        }

        private void DrawMarker(Rect worldRect, float scale, int x, int y, Color color, string label)
        {
            float size = Mathf.Max(5f, scale * 0.7f);
            Rect marker = new Rect(
                worldRect.x + (x * scale) + ((scale - size) * 0.5f),
                worldRect.y + (y * scale) + ((scale - size) * 0.5f),
                size,
                size);
            EditorGUI.DrawRect(marker, color);
            if (_showLabels && scale >= 8f)
            {
                GUI.Label(new Rect(marker.x - 8f, marker.y - 18f, 90f, 18f), label, EditorStyles.whiteMiniLabel);
            }
        }

        private static void DrawHud(Rect rect)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, 300f, 22f), "FPS: 60   Ping: 40ms   Online: 3", EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 12f, rect.yMax - 24f, 300f, 18f), "Preview Showcase · no network", EditorStyles.miniLabel);
        }
    }
}
