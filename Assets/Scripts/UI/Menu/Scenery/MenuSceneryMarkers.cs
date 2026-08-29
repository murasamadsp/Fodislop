#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Updates beacon reticles, ping pulses and station badge positioning over the planet scenery.
/// </summary>
public sealed class MenuSceneryMarkers
{
    private static readonly Vector3 LandingSiteDirection = new(-0.48f, 0.10f, -0.87f);

    public static void Animate(
        float time,
        VisualElement? beacon,
        VisualElement? beaconPing,
        VisualElement? stationBadge,
        VisualElement? sidebar,
        VisualElement? targetReticle,
        Image? planetBodyImage,
        MenuSceneryController? scenery)
    {
        UpdateStationMarker(beacon, beaconPing, stationBadge, sidebar, planetBodyImage, scenery);
        UpdateLandingSectorMarker(targetReticle, planetBodyImage, scenery);

        if (targetReticle != null)
        {
            float targetScale = 1.0f + (Mathf.Sin(time * 2.2f) * 0.04f);
            targetReticle.style.scale = new Scale(new Vector3(targetScale, targetScale, 1f));
        }
    }

    private static void UpdateStationMarker(
        VisualElement? beacon,
        VisualElement? beaconPing,
        VisualElement? stationBadge,
        VisualElement? sidebar,
        Image? planetBodyImage,
        MenuSceneryController? scenery)
    {
        if (beacon == null)
        {
            return;
        }

        IPanel? hostPanel = beacon.panel;

        if (hostPanel == null ||
            !TryGetPlanetFrame(planetBodyImage, scenery, out Rect rect, out Rect image) ||
            scenery == null ||
            !scenery.TryGetStationViewportPosition(out Vector2 viewport))
        {
            beacon.style.display = DisplayStyle.None;
            return;
        }

        Rect panel = hostPanel.visualTree.worldBound;
        float panelX = image.x + (viewport.x * image.width);
        float panelY = image.y + ((1f - viewport.y) * image.height);

        const float badgeWidth = 260f;
        const float badgeHeight = 46f;
        const float footerSafe = 56f;

        if (stationBadge != null)
        {
            const float edgeGap = 24f;
            const float markerGap = 28f;
            const float headerSafe = 84f;

            float safeRight = panel.width - edgeGap;
            if (sidebar != null)
            {
                Rect rail = sidebar.worldBound;
                if (rail.width > 0f && panelY + badgeHeight > rail.yMin && panelY < rail.yMax)
                {
                    safeRight = Mathf.Min(safeRight, rail.xMin - edgeGap);
                }
            }

            float preferred = panelX + markerGap + badgeWidth <= safeRight
                ? panelX + markerGap
                : panelX - markerGap - badgeWidth;

            float left = Mathf.Clamp(preferred, edgeGap, Mathf.Max(edgeGap, safeRight - badgeWidth));
            float top = Mathf.Clamp(
                panelY - (badgeHeight * 0.5f),
                headerSafe,
                Mathf.Max(headerSafe, panel.height - footerSafe - badgeHeight));

            stationBadge.style.left = left - panelX;
            stationBadge.style.top = top - panelY;
            stationBadge.style.right = StyleKeyword.Auto;
            stationBadge.style.bottom = StyleKeyword.Auto;
        }

        beacon.style.display = DisplayStyle.Flex;

        if (beaconPing != null)
        {
            float pingPhase = (Time.time * 0.4f) % 1.0f;
            float pingScale = Mathf.Lerp(1.0f, 2.5f, pingPhase);
            float pingAlpha = Mathf.Sin(pingPhase * Mathf.PI) * 0.8f;
            beaconPing.style.scale = new Scale(new Vector2(pingScale, pingScale));
            beaconPing.style.opacity = pingAlpha;
        }

        float x = rect.x + (viewport.x * rect.width);
        float y = rect.y + ((1f - viewport.y) * rect.height);

        const float markerHalfSize = 11f;
        beacon.style.left = x - markerHalfSize;
        beacon.style.top = y - markerHalfSize;
    }

    private static void UpdateLandingSectorMarker(
        VisualElement? targetReticle,
        Image? planetBodyImage,
        MenuSceneryController? scenery)
    {
        if (targetReticle == null)
        {
            return;
        }

        if (!TryGetPlanetFrame(planetBodyImage, scenery, out Rect rect, out _) ||
            scenery == null ||
            !scenery.TryGetPlanetSurfaceViewportPosition(LandingSiteDirection, out Vector2 viewport))
        {
            targetReticle.style.display = DisplayStyle.None;
            return;
        }

        float x = rect.x + (viewport.x * rect.width);
        float y = rect.y + ((1f - viewport.y) * rect.height);

        const float markerHalfSize = 11f;
        targetReticle.style.left = x - markerHalfSize;
        targetReticle.style.top = y - markerHalfSize;
        targetReticle.style.display = DisplayStyle.Flex;
    }

    public static bool TryGetPlanetFrame(
        Image? planetBodyImage,
        MenuSceneryController? scenery,
        out Rect localFrame,
        out Rect worldFrame)
    {
        localFrame = default;
        worldFrame = default;

        if (planetBodyImage == null || scenery == null || scenery.OutputTexture == null)
        {
            return false;
        }

        if (!ReferenceEquals(planetBodyImage.image, scenery.OutputTexture))
        {
            return false;
        }

        Rect rect = planetBodyImage.layout;
        if (rect.width <= 1f || rect.height <= 1f ||
            float.IsNaN(rect.width) || float.IsNaN(rect.height))
        {
            return false;
        }

        localFrame = rect;
        worldFrame = planetBodyImage.worldBound;
        return true;
    }
}
