#nullable enable

using Fodinae.UI;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Переключатель между запечёнными и процедурными полями планеты.
    ///
    /// Запекание обязано быть неотличимым: считается тот же код из
    /// PlanetSurfaceFields.hlsl и PlanetCloudFields.hlsl, просто один раз.
    /// Проверить это можно только сравнением — снять «Fodinae/Art/Capture Menu
    /// Scenery» в обоих режимах и сличить кадры. Поэтому переключатель живёт в
    /// меню, а не в закомментированной строке кода: сравнение понадобится
    /// снова при каждой правке полей.
    /// </summary>
    internal static class PlanetFieldsMenu
    {
        private const string MenuPath = "Fodinae/Art/Поля планеты: процедурные (без запекания)";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            PlanetFieldBaker.ForceProcedural = !PlanetFieldBaker.ForceProcedural;
            Menu.SetChecked(MenuPath, PlanetFieldBaker.ForceProcedural);

            // Применяем немедленно: инструменты захвата рисуют камеру напрямую,
            // не дожидаясь перерисовки редактора.
            var scenery = Object.FindAnyObjectByType<MenuSceneryController>(FindObjectsInactive.Include);
            if (scenery != null)
            {
                scenery.RefreshFields();
            }

            Debug.Log(PlanetFieldBaker.ForceProcedural
                ? "[Планета] Поля считаются процедурно — режим сравнения."
                : "[Планета] Поля запекаются.");
        }

        [MenuItem(MenuPath, isValidateFunction: true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, PlanetFieldBaker.ForceProcedural);
            return true;
        }
    }
}
