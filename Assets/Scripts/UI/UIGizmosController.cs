#nullable enable

#if UNITY_EDITOR
using Fodinae.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Editor-only debug helper. In Play Mode walks the UI Toolkit hierarchy and
    /// draws the bounds of every named container (windows, HUD panels, grids,
    /// hotbar/inventory cells) as world-space Gizmos projected through the camera.
    /// Also draws bounds of world-anchored UI elements (FloatingChatBubble).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIGizmosController : MonoBehaviour
    {
        public bool drawContainers = true;
        public bool drawCells = true;
        public bool drawWorldUI = true;
        public float maxDepth = 64f;
        public Color containerColor = new Color(0.2f, 0.8f, 1f, 0.9f);
        public Color cellColor = new Color(1f, 0.6f, 0.2f, 0.9f);
        public Color worldUIColor = new Color(0.8f, 0.2f, 1f, 0.9f);

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (drawContainers)
            {
                DrawUIDocumentContainers();
            }

            if (drawWorldUI)
            {
                DrawWorldUIElements();
            }
        }

        private void DrawUIDocumentContainers()
        {
            var docs = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            foreach (var doc in docs)
            {
                if (doc.rootVisualElement == null)
                {
                    continue;
                }

                WalkElement(doc.rootVisualElement, 0);
            }
        }

        private void WalkElement(VisualElement element, float depth)
        {
            if (element == null || depth > maxDepth)
            {
                return;
            }

            bool isCell = element.ClassListContains("inv-cell")
                          || element.ClassListContains("inventory-slot")
                          || element.ClassListContains("hotbar-slot");

            if (isCell)
            {
                if (drawCells)
                {
                    DrawElementBound(element, cellColor);
                }
            }
            else if (element.childCount > 0 || !string.IsNullOrEmpty(element.name))
            {
                if (drawContainers)
                {
                    DrawElementBound(element, containerColor);
                }
            }

            foreach (var child in element.Children())
            {
                WalkElement(child, depth + 1);
            }
        }

        private void DrawElementBound(VisualElement element, Color color)
        {
            Rect bound = element.worldBound;
            if (bound.width <= 0f || bound.height <= 0f)
            {
                return;
            }

            Vector3 worldMin = ScreenToWorld(bound.min);
            Vector3 worldMax = ScreenToWorld(bound.max);
            Vector3 center = (worldMin + worldMax) * 0.5f;
            Vector2 size = new Vector2(worldMax.x - worldMin.x, worldMax.y - worldMin.y);

            FodinaeGizmos.DrawBounds(center, size, color);

            if (!string.IsNullOrEmpty(element.name))
            {
                FodinaeGizmos.DrawLabel(worldMax + (Vector3.up * 0.35f), element.name, color);
            }
        }

        private static Vector3 ScreenToWorld(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return Vector3.zero;
            }

            // UI Toolkit worldBound lives in panel space (top-left origin, pixels).
            // Flip Y and push to the camera's XY plane.
            Vector3 flipped = new Vector3(screenPos.x, Screen.height - screenPos.y, 0f);
            Vector3 world = cam.ScreenToWorldPoint(flipped);
            world.z = 0f;
            return world;
        }

        private void DrawWorldUIElements()
        {
            var bubbles = FindObjectsByType<FloatingChatBubble>(FindObjectsInactive.Include);
            foreach (var bubble in bubbles)
            {
                DrawWorldObject(bubble.transform, worldUIColor);
            }
        }

        private static void DrawWorldObject(Transform target, Color color)
        {
            if (target == null)
            {
                return;
            }

            var renderer = target.GetComponentInChildren<Renderer>();
            if (renderer != null && renderer.bounds.size.sqrMagnitude > 0f)
            {
                Bounds b = renderer.bounds;
                FodinaeGizmos.DrawBounds(b.center, new Vector2(b.size.x, b.size.y), color);
                FodinaeGizmos.DrawLabel(b.center + (Vector3.up * (b.extents.y + 0.4f)), target.gameObject.name, color);
            }
        }
    }
}
#endif
