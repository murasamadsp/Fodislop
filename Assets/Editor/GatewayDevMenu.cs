#nullable enable

using Fodinae.UI;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Пункты меню для работы над воротами (вход + онбординг).
    ///
    /// «Показывать всегда» — галочка в EditorPrefs, включена по умолчанию:
    /// пока идёт работа над этими экранами, они не должны проскакивать из-за
    /// того, что токен уже получен, а онбординг помечен пройденным.
    ///
    /// «Сбросить сохранённое состояние» нужен отдельно: он проверяет обратный
    /// путь — то, что видит игрок при по-настоящему первом запуске, и то, что
    /// галочка «Авто-вход» действительно запоминается.
    /// </summary>
    internal static class GatewayDevMenu
    {
        private const string ForceGatesMenuPath = "Fodinae/Ворота/Показывать вход и онбординг всегда";
        private const string ResetMenuPath = "Fodinae/Ворота/Сбросить сохранённое состояние";

        // Ключи продублированы, а не вынесены в общую константу, намеренно:
        // это редакторский инструмент, который лезет в чужое хранилище, и он
        // должен ломаться заметно, если рантайм переименует ключ.
        private static readonly string[] RuntimeKeys =
        {
            "Auth.AutoLogin",
            "OnboardingCompleted1",
        };

        [MenuItem(ForceGatesMenuPath)]
        private static void ToggleForceGates()
        {
            bool next = !GatewayDevFlags.ForceGates;
            EditorPrefs.SetBool(GatewayDevFlags.ForceGatesPrefsKey, next);
            Debug.Log($"[Ворота] Показывать всегда: {(next ? "да" : "нет")}.");
        }

        [MenuItem(ForceGatesMenuPath, isValidateFunction: true)]
        private static bool ValidateToggleForceGates()
        {
            Menu.SetChecked(ForceGatesMenuPath, GatewayDevFlags.ForceGates);
            return true;
        }

        [MenuItem(ResetMenuPath)]
        private static void ResetSavedState()
        {
            foreach (string key in RuntimeKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
            Debug.Log("[Ворота] Сохранённое состояние очищено: авто-вход и отметка о пройденном онбординге.");
        }
    }
}
