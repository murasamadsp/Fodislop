#nullable enable

using System.IO;
using Fodinae.UI;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    // Writes the menu's procedural starfield to a PNG, in Edit Mode.
    //
    // The starfield is not drawn by any camera - MenuStarfield blits it straight
    // into a RenderTexture that the UI shows as a background Image - so it is
    // invisible both to the scenery capture (which renders MenuSceneryCamera on
    // the MenuScenery layer) and to any camera-based capture. Without this the
    // only way to look at the sky was to enter Play Mode.
    internal static class CaptureBackdrop
    {
        private const string OutputFileName = "backdrop_capture.png";

        [MenuItem("Fodinae/Art/Capture Backdrop")]
        public static void Capture()
        {
            var starfield = Object.FindAnyObjectByType<MenuStarfield>(FindObjectsInactive.Include);
            if (starfield == null)
            {
                Debug.LogError("[CaptureBackdrop] No MenuStarfield in the open scenes - run 'Fodinae/Art/Build Menu Scenery Rig' first.");
                return;
            }

            // The component only blits from LateUpdate, which does not run on
            // demand in Edit Mode, so drive one frame explicitly.
            starfield.RenderNow();

            RenderTexture? rt = starfield.Texture;
            if (rt == null)
            {
                Debug.LogError("[CaptureBackdrop] MenuStarfield has no render texture - its material is probably unassigned.");
                return;
            }

            RenderTexture? previousActive = RenderTexture.active;
            var readback = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, mipChain: false, linear: true);
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readback.Apply();
            RenderTexture.active = previousActive;

            // The render target is linear; PNG is read back as sRGB by every
            // viewer. Skipping this conversion is what made an earlier version
            // of the scenery capture lie about its own colours.
            var output = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, mipChain: false, linear: false);
            Color[] pixels = readback.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                pixels[i] = new Color(
                    Mathf.LinearToGammaSpace(c.r),
                    Mathf.LinearToGammaSpace(c.g),
                    Mathf.LinearToGammaSpace(c.b),
                    1f);
            }

            output.SetPixels(pixels);
            output.Apply();

            string absolutePath = ArtCapturePaths.Resolve(OutputFileName);
            File.WriteAllBytes(absolutePath, output.EncodeToPNG());

            Object.DestroyImmediate(readback);
            Object.DestroyImmediate(output);

            Debug.Log($"[CaptureBackdrop] Wrote {absolutePath} ({rt.width}x{rt.height})");
        }
    }
}
