#nullable enable

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    // Captures the actual composited Game View frame (UI Toolkit overlay,
    // header, sidebar, background, everything) during Play Mode, so a visual
    // bug that only shows up in the full composite - not in an isolated
    // camera/RT capture - can be self-inspected via Read without needing a
    // manually-annotated screenshot from the user.
    internal static class CaptureGameView
    {
        private const string OutputFileName = "game_view_capture.png";

        [MenuItem("Fodinae/Art/Enter Play Mode")]
        public static void EnterPlay()
        {
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Fodinae/Art/Exit Play Mode")]
        public static void ExitPlay()
        {
            EditorApplication.isPlaying = false;
        }

        [MenuItem("Fodinae/Art/Capture Game View")]
        public static void Capture()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[CaptureGameView] Not in Play Mode - ScreenCapture only captures the live rendered frame during Play.");
                return;
            }

            string absolutePath = ArtCapturePaths.Resolve(OutputFileName);
            ScreenCapture.CaptureScreenshot(absolutePath);
            Debug.Log($"[CaptureGameView] Requested screenshot to {absolutePath} (written asynchronously, ~1 frame later).");
        }
    }
}
