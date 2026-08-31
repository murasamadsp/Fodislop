#nullable enable

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    // Диагностический захват фактического рендера планеты главного меню в PNG,
    // чтобы агент мог видеть ту же картинку, что и пользователь, а не угадывать
    // по сырым equirect-текстурам.
    public static class PlanetCapture
    {
        [MenuItem("Fodinae/Diagnostics/Capture Planet PNG")]
        public static void Capture()
        {
            var scenery = Object.FindAnyObjectByType<UI.MenuSceneryController>();
            if (scenery == null)
            {
                Debug.LogError("[PlanetCapture] MenuSceneryController not found in active scene.");
                return;
            }

            scenery.SetDisplaySize(1024, 1024);
            scenery.RenderNow();

            var rt = scenery.OutputTexture;
            if (rt == null)
            {
                Debug.LogError("[PlanetCapture] OutputTexture is null after resolve.");
                return;
            }

            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture? previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }

            string path = Path.Combine(Application.dataPath, "..", "planet_capture.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            Debug.Log($"[PlanetCapture] Saved {path} ({rt.width}x{rt.height})");
        }
    }
}
