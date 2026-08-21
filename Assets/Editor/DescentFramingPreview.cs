#nullable enable

using Fodinae.UI;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Предпросмотр облёта камеры при спуске.
    ///
    /// Само движение видно только после нажатия «НАЧАТЬ СПУСК В ШАХТУ» и длится
    /// ровно столько, сколько грузится мир, — то есть подобрать дугу, глядя на
    /// него, нельзя. Эти пункты ставят кадрирование в заданную долю и
    /// оставляют его там.
    ///
    /// MenuSceneryController помечен [ExecuteAlways], поэтому работает и без
    /// Play mode: достаточно открыть сцену меню.
    /// </summary>
    internal static class DescentFramingPreview
    {
        // Совпадает с MainMenu.LandingSiteDirection. Продублировано намеренно:
        // это редакторский инструмент, и он должен ломаться заметно, если
        // рантайм переедет на другую точку высадки.
        private static readonly Vector3 LandingSiteDirection = new(-0.48f, 0.10f, -0.87f);

        [MenuItem("Fodinae/Art/Спуск: обзор (0%)")]
        private static void Rest() => Apply(0f);

        [MenuItem("Fodinae/Art/Спуск: середина (50%)")]
        private static void Midway() => Apply(0.5f);

        [MenuItem("Fodinae/Art/Спуск: точка высадки (100%)")]
        private static void Arrived() => Apply(1f);

        private static void Apply(float progress)
        {
            var scenery = Object.FindAnyObjectByType<MenuSceneryController>(FindObjectsInactive.Include);
            if (scenery == null)
            {
                Debug.LogError(
                    "[Спуск] MenuSceneryController не найден. Открой сцену MainMenu — риг задника живёт в ней.");
                return;
            }

            scenery.SetDescentFraming(progress, LandingSiteDirection);
            scenery.ResolveOutput();
            Debug.Log($"[Спуск] Кадрирование выставлено на {progress:P0}.");
        }
    }
}
