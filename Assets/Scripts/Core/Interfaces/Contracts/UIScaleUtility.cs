#nullable enable

using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Адаптация масштаба интерфейса (UI Toolkit и IMGUI) под экраны с высокой
/// плотностью пикселей (Retina-дисплеи MacBook, 4K/5K мониторы).
/// </summary>
/// <remarks>
/// PanelSettings в режиме ScaleWithScreenSize держит фиксированную логическую высоту
/// 1080 пикселей независимо от физического размера и DPI дисплея.
/// На 13–16" MacBook с Retina-матрицей (DPI ≈ 220–254) физический размер 1080p
/// сжимается вдвое по сравнению с обычным 24" настольным монитором (DPI ≈ 96).
/// Текст и кнопки при масштабе 1.0 становятся микроскопическими (~2.2 мм).
///
/// Этот класс определяет Retina / High-DPI экраны, вычисляет комфортный
/// базовый масштаб (1.35x) и предоставляет единый интерфейс масштабирования.
/// </remarks>
public static class UIScaleUtility
{
    public const float HighDpiThreshold = 180f;
    public const float RetinaDefaultScale = 1.35f;
    public const float StandardDefaultScale = 1.00f;

    public const float UIScaleMin = 0.50f;
    public const float UIScaleMax = 2.50f;

    /// <summary>
    /// Является ли текущий экран дисплеем с высокой плотностью пикселей (Retina / High-DPI).
    /// </summary>
    public static bool IsRetinaOrHighDpi
    {
        get
        {
            float dpi = Screen.dpi;
            if (dpi >= HighDpiThreshold)
            {
                return true;
            }

            // На macOS в редакторе или standalone встроенные экраны MacBook
            // имеют Retina-матрицу (обычно 220..260 dpi, но Unity иногда
            // сообщает 160+ в зависимости от масштабирования дисплея в ОС).
            if ((Application.platform == RuntimePlatform.OSXPlayer ||
                 Application.platform == RuntimePlatform.OSXEditor) &&
                dpi >= 160f)
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>Рекомендуемый базовый масштаб для текущего дисплея.</summary>
    public static float RecommendedDefaultScale =>
        IsRetinaOrHighDpi ? RetinaDefaultScale : StandardDefaultScale;

    /// <summary>Ограничивает масштаб допустимым безопасным диапазоном.</summary>
    public static float Clamp(float scale) =>
        Mathf.Clamp(scale, UIScaleMin, UIScaleMax);

    /// <summary>
    /// Вычисляет эффективный масштаб с учётом пользовательской настройки и Retina.
    /// Если настройка не задана (<= 0 или NaN), возвращает рекомендуемый дефолт.
    /// </summary>
    public static float ResolveEffectiveScale(float configuredScale)
    {
        if (float.IsNaN(configuredScale) || float.IsInfinity(configuredScale) || configuredScale <= 0f)
        {
            return RecommendedDefaultScale;
        }

        return Clamp(configuredScale);
    }
}
