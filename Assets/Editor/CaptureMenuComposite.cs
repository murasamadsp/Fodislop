#nullable enable

using System.IO;
using Fodinae.UI;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    // Self-inspection tool: lays out the menu's two procedural layers - the
    // starfield blit and the 3D scenery render - onto one canvas exactly the way
    // MainMenu.uxml + Theme.uss position them, and writes the result to a PNG.
    //
    // Neither layer is visible to a camera-based capture: the starfield is a
    // Graphics.Blit into a RenderTexture with no camera, and the scenery camera
    // renders into its own target that only the UI Image samples. The scenery
    // capture and the backdrop capture each judge one layer in isolation, which
    // cannot answer the actual composition question - does the planet sit where
    // it should, do the markers stay in frame - because that only exists once
    // the two are stacked. This tool stacks them in Edit Mode.
    //
    // The layout constants mirror Theme.uss (PanelSettings reference resolution
    // is 1200x800). The .mm-planet-system box is right:-60px, top:60px,
    // bottom:-260px, width:860px, and .mm-planet-body is an 860x860 Image
    // centred inside it, so the planet rect is derived from those three rules.
    internal static class CaptureMenuComposite
    {
        private const string OutputFileName = "menu_composite_capture.png";
        private const string CameraObjectName = "MenuSceneryCamera";

        private const int CanvasWidth = 1200;
        private const int CanvasHeight = 800;

        // .mm-planet-system { right: -60px; top: 60px; bottom: -260px; width: 860px; }
        private const int PlanetContainerRight = -60;
        private const int PlanetContainerTop = 60;
        private const int PlanetContainerBottom = -260;
        private const int PlanetSize = 860;

        // .mm-target-reticle { left: 250px; top: 40%; width: 22px; height: 22px; }
        private static readonly Vector2 LandingReticlePosition = new Vector2(250f, CanvasHeight * 0.4f);

        private static readonly Color MarkerGold = new Color(245f / 255f, 197f / 255f, 66f / 255f);

        [MenuItem("Fodinae/Art/Capture Menu Composite")]
        public static void Capture()
        {
            var starfield = Object.FindAnyObjectByType<MenuStarfield>(FindObjectsInactive.Include);
            if (starfield == null)
            {
                Debug.LogError("[CaptureMenuComposite] No MenuStarfield in the open scenes - run 'Fodinae/Art/Build Menu Scenery Rig' first.");
                return;
            }

            var controller = Object.FindAnyObjectByType<MenuSceneryController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogError("[CaptureMenuComposite] No MenuSceneryController in the open scenes - run 'Fodinae/Art/Build Menu Scenery Rig' first.");
                return;
            }

            var cameraObject = GameObject.Find(CameraObjectName);
            if (cameraObject == null)
            {
                Debug.LogError($"[CaptureMenuComposite] GameObject '{CameraObjectName}' not found in the open scenes.");
                return;
            }

            var cam = cameraObject.GetComponent<Camera>();
            if (cam == null)
            {
                Debug.LogError($"[CaptureMenuComposite] '{CameraObjectName}' has no Camera component.");
                return;
            }

            Selection.activeObject = null;

            // Drive one frame of each layer explicitly: LateUpdate does not run
            // on demand in Edit Mode, and both layers are invisible to cameras.
            starfield.RenderNow();
            cam.Render();
            controller.ResolveOutput();

            RenderTexture? starfieldRt = starfield.Texture;
            RenderTexture? sceneryRt = controller.OutputTexture;
            if (starfieldRt == null || sceneryRt == null)
            {
                Debug.LogError("[CaptureMenuComposite] A layer has no render texture - check that MenuStarfield's material and MenuSceneryController are wired up.");
                return;
            }

            Texture2D? starfieldPixels = ReadLinearTexture(starfieldRt);
            Texture2D? sceneryPixels = ReadLinearTexture(sceneryRt);
            if (starfieldPixels == null || sceneryPixels == null)
            {
                Debug.LogError("[CaptureMenuComposite] Failed to read back a render texture.");
                return;
            }

            // Both targets are linear HDR; the composite is assembled in display
            // (sRGB) space, exactly like the UI pipeline that samples them.
            var output = new Texture2D(CanvasWidth, CanvasHeight, TextureFormat.RGBA32, mipChain: false, linear: false);
            Color[] pixels = new Color[CanvasWidth * CanvasHeight];

            // .mm-space-bg fills the panel. ScaleAndCrop on the Image maps to
            // cover-with-crop here, so the starfield region is the canvas scaled
            // up to the texture and centre-cropped.
            float coverScale = Mathf.Max((float)CanvasWidth / starfieldRt.width, (float)CanvasHeight / starfieldRt.height);
            float regionW = CanvasWidth / coverScale;
            float regionH = CanvasHeight / coverScale;
            float offsetX = (starfieldRt.width - regionW) * 0.5f;
            float offsetY = (starfieldRt.height - regionH) * 0.5f;

            for (int y = 0; y < CanvasHeight; y++)
            {
                for (int x = 0; x < CanvasWidth; x++)
                {
                    float tx = offsetX + (((x + 0.5f) / CanvasWidth) * regionW);
                    float ty = offsetY + (((y + 0.5f) / CanvasHeight) * regionH);
                    Color c = SampleBilinear(starfieldPixels, tx, ty);
                    pixels[(y * CanvasWidth) + x] = ToSrgb(c);
                }
            }

            // .mm-planet-system centred 860x860 inside its box; the planet Image
            // samples the whole scenery output, so draw the RT into that rect.
            int containerX = CanvasWidth - PlanetSize - PlanetContainerRight;
            int containerY = PlanetContainerTop;
            int containerH = CanvasHeight - PlanetContainerBottom - PlanetContainerTop;
            int planetY = containerY + ((containerH - PlanetSize) / 2);

            for (int y = 0; y < PlanetSize; y++)
            {
                for (int x = 0; x < PlanetSize; x++)
                {
                    float tx = ((x + 0.5f) / PlanetSize) * sceneryRt.width;
                    float ty = ((y + 0.5f) / PlanetSize) * sceneryRt.height;
                    Color src = ToSrgb(SampleBilinear(sceneryPixels, tx, ty));

                    int px = containerX + x;
                    int py = planetY + y;
                    if (px < 0 || px >= CanvasWidth || py < 0 || py >= CanvasHeight)
                    {
                        continue;
                    }

                    // Straight alpha after the resolve blit: standard over.
                    int index = (py * CanvasWidth) + px;
                    Color dst = pixels[index];
                    pixels[index] = (src * src.a) + (dst * (1f - src.a));
                }
            }

            // The station marker tracks the 3D orbit, so its position has to be
            // asked of the controller rather than read from USS. The landing
            // reticle is a fixed USS position.
            if (controller.TryGetStationViewportPosition(out Vector2 viewport))
            {
                float sx = containerX + (viewport.x * PlanetSize);
                float sy = planetY + ((1f - viewport.y) * PlanetSize);
                DrawReticle(pixels, new Vector2(sx, sy), MarkerGold);
            }

            DrawReticle(pixels, LandingReticlePosition, MarkerGold);

            output.SetPixels(pixels);
            output.Apply();

            string absolutePath = ArtCapturePaths.Resolve(OutputFileName);
            File.WriteAllBytes(absolutePath, output.EncodeToPNG());

            Object.DestroyImmediate(starfieldPixels);
            Object.DestroyImmediate(sceneryPixels);
            Object.DestroyImmediate(output);

            Debug.Log($"[CaptureMenuComposite] Wrote {absolutePath} ({CanvasWidth}x{CanvasHeight}, planet at x={containerX} y={planetY})");
        }

        // Reads an (HDR, linear) render target into a float texture for sampling.
        private static Texture2D? ReadLinearTexture(RenderTexture rt)
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, mipChain: false, linear: true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previousActive;
            return tex;
        }

        // Bilinear sample with clamp-to-edge, in texel coordinates.
        private static Color SampleBilinear(Texture2D tex, float tx, float ty)
        {
            float cx = Mathf.Clamp(tx - 0.5f, 0f, tex.width - 1f);
            float cy = Mathf.Clamp(ty - 0.5f, 0f, tex.height - 1f);
            int x0 = Mathf.FloorToInt(cx);
            int y0 = Mathf.FloorToInt(cy);
            int x1 = Mathf.Min(x0 + 1, tex.width - 1);
            int y1 = Mathf.Min(y0 + 1, tex.height - 1);
            float fx = cx - x0;
            float fy = cy - y0;

            Color c00 = tex.GetPixel(x0, y0);
            Color c10 = tex.GetPixel(x1, y0);
            Color c01 = tex.GetPixel(x0, y1);
            Color c11 = tex.GetPixel(x1, y1);
            return Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fy);
        }

        // Linear HDR -> display sRGB, clamped to the PNG's 8-bit range.
        private static Color ToSrgb(Color c)
        {
            return new Color(
                Mathf.Clamp01(Mathf.LinearToGammaSpace(c.r)),
                Mathf.Clamp01(Mathf.LinearToGammaSpace(c.g)),
                Mathf.Clamp01(Mathf.LinearToGammaSpace(c.b)),
                Mathf.Clamp01(c.a));
        }

        // The .mm-target-reticle visual: a 22px gold ring with a cross through
        // it. Painted procedurally so the composite shows the same marker the
        // menu draws without needing a live UI panel.
        private static void DrawReticle(Color[] pixels, Vector2 centre, Color color)
        {
            const float halfSize = 11f;
            const float ringRadius = 10f;
            const float ringThickness = 1.5f;
            const float crossHalf = 10f;
            const float crossThickness = 1.5f;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(centre.x - halfSize));
            int x1 = Mathf.Min(CanvasWidth - 1, Mathf.CeilToInt(centre.x + halfSize));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(centre.y - halfSize));
            int y1 = Mathf.Min(CanvasHeight - 1, Mathf.CeilToInt(centre.y + halfSize));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                    bool onRing = Mathf.Abs(dist - ringRadius) <= ringThickness;
                    bool onCross = (Mathf.Abs(x + 0.5f - centre.x) <= crossThickness && Mathf.Abs(y + 0.5f - centre.y) <= crossHalf)
                        || (Mathf.Abs(y + 0.5f - centre.y) <= crossThickness && Mathf.Abs(x + 0.5f - centre.x) <= crossHalf);
                    if (!onRing && !onCross)
                    {
                        continue;
                    }

                    int index = (y * CanvasWidth) + x;
                    Color dst = pixels[index];
                    pixels[index] = Color.Lerp(dst, color, 0.9f);
                }
            }
        }
    }
}
