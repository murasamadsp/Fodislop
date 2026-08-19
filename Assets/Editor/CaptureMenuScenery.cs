#nullable enable

using System.IO;
using Fodinae.UI;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    // Self-inspection tool: renders the main menu's 3D scenery camera (star,
    // planet, station) to a PNG so composited output can be reviewed directly
    // (via Read) instead of guessing from source textures or asking for a
    // screenshot after every shader/layout change.
    internal static class CaptureMenuScenery
    {
        private const string OutputFileName = "menu_scenery_capture.png";
        private const string CameraObjectName = "MenuSceneryCamera";

        [MenuItem("Fodinae/Art/Capture Menu Scenery")]
        public static void Capture()
        {
            var cameraObject = GameObject.Find(CameraObjectName);
            if (cameraObject == null)
            {
                Debug.LogError($"[CaptureMenuScenery] GameObject '{CameraObjectName}' not found in the open scenes.");
                return;
            }

            var cam = cameraObject.GetComponent<Camera>();
            if (cam == null)
            {
                Debug.LogError($"[CaptureMenuScenery] '{CameraObjectName}' has no Camera component.");
                return;
            }

            Selection.activeObject = null;

            var controller = Object.FindAnyObjectByType<MenuSceneryController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogError("[CaptureMenuScenery] No MenuSceneryController in the open scenes.");
                return;
            }

            cam.Render();

            // Run the same premultiplied -> straight-alpha resolve the runtime
            // does, and sample its result rather than the camera target, so this
            // preview goes through the identical path as the menu.
            controller.ResolveOutput();

            RenderTexture? rt = controller.OutputTexture;
            if (rt == null)
            {
                Debug.LogError($"[CaptureMenuScenery] '{CameraObjectName}' has no output texture — MenuSceneryController must run its OnEnable first.");
                return;
            }

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            // The project renders in Linear color space and the scenery RT is a
            // linear HDR target, but ReadPixels does no conversion - so the raw
            // PNG holds linear values that any image viewer then interprets as
            // sRGB, showing the scene far darker and more contrasty than it
            // actually appears on screen. Encode display-referred sRGB instead,
            // so this capture is a faithful preview and art can be tuned against
            // it rather than against a mis-decoded one.
            // Composited over the menu's own backdrop rather than left
            // transparent. The scenery is mostly faint haze against alpha 0, and
            // a transparent PNG gets shown over white by most viewers - which
            // hides exactly the low-alpha atmosphere this capture exists to
            // judge, and makes the dark limb look like it has no glow at all.
            // Flattening onto the real background colour makes the preview match
            // what the menu actually shows.
            // Backdrop is the menu's generated space colour, already expressed in
            // sRGB (#03060c) - so the scene is converted to sRGB FIRST and the
            // composite happens in display space. Compositing in linear and then
            // gamma-encoding the backdrop too would lift it to a washed-out navy
            // that no longer matches the real background.
            var backdrop = new Color(3f / 255f, 6f / 255f, 12f / 255f);

            Color[] pixels = tex.GetPixels();
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color src = pixels[i];
                float alpha = src.a;

                Color display = linear ? src.gamma : src;

                // Straight alpha now (the resolve pass un-premultiplied it), so
                // this is the same src*a + dst*(1-a) that UI Toolkit applies.
                Color composited = (display * alpha) + (backdrop * (1f - alpha));
                composited.a = 1f;
                pixels[i] = composited;
            }

            tex.SetPixels(pixels);
            tex.Apply();

            Color32 corner = tex.GetPixel(4, 4);
            Color32 topEdge = tex.GetPixel(tex.width / 2, tex.height - 4);
            Debug.Log($"[CaptureMenuScenery] corner(4,4) rgba=({corner.r},{corner.g},{corner.b},{corner.a}) topEdge rgba=({topEdge.r},{topEdge.g},{topEdge.b},{topEdge.a})");

            byte[] png = tex.EncodeToPNG();
            string outputPath = ArtCapturePaths.Resolve(OutputFileName);
            File.WriteAllBytes(outputPath, png);
            Object.DestroyImmediate(tex);

            Debug.Log($"[CaptureMenuScenery] Saved capture to {outputPath}");
        }
    }
}
