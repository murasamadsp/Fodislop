#nullable enable

using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    internal static class AutoVerifyMenu
    {
        private const string OutputFile = "auto_verify_menu.png";

        [MenuItem("Fodinae/Art/Auto Verify Menu (temp)")]
        public static void Run()
        {
            EditorApplication.isPlaying = true;
            EditorApplication.update += Tick;
        }

        private static float _started;

        private static void Tick()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_started <= 0f)
            {
                _started = Time.realtimeSinceStartup;
            }

            if (Time.realtimeSinceStartup - _started < 6f)
            {
                return;
            }

            EditorApplication.update -= Tick;
            ScreenCapture.CaptureScreenshot(ArtCapturePaths.Resolve(OutputFile));
            Debug.Log($"[AutoVerifyMenu] Captured to {OutputFile}, exiting play in 1s.");
            EditorApplication.isPlaying = false;
        }
    }
}
