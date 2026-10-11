#nullable enable

using UnityEngine;

namespace Kern.Core;

public static class UIScaleUtility
{
    public const float HighDpiThreshold = 180f;
    public const float RetinaDefaultScale = 1.35f;
    public const float StandardDefaultScale = 1.00f;

    // Порог диагонали карманного устройства — только для старых миграций
    // конфига. Раскладка решается формой экрана (ScreenShapeRules), а
    // масштаб на мобильной платформе — физический: логический пиксель
    // панели равен dp, и экраны сверстаны так, чтобы уместиться в 640×360.
    public const float HandheldMaxInches = 7f;

    public const float UIScaleMin = 0.50f;
    public const float UIScaleMax = 2.50f;

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

    /// Карманное устройство: мобильная платформа и диагональ меньше семи дюймов.
    ///
    /// Проверка ограничена мобильными платформами намеренно. В редакторе
    /// Screen.width/height — это размер Game View, а Screen.dpi — DPI
    /// настоящего монитора; их частное диагональю не является (на этой
    /// машине выходит 7.9" при 14-дюймовом экране).
    public static bool IsHandheld
    {
        get
        {
            if (!Application.isMobilePlatform)
            {
                return false;
            }

            float inches = ScreenDiagonalInches;
            return inches > 0f && inches < HandheldMaxInches;
        }
    }

    public static float ScreenDiagonalInches
    {
        get
        {
            float dpi = Screen.dpi;
            if (dpi <= 0f)
            {
                return 0f;
            }

            return Mathf.Sqrt((Screen.width * Screen.width) + (Screen.height * Screen.height)) / dpi;
        }
    }

    // Мобильная платформа меряет интерфейс в dp (ScreenShapeRules): там
    // UIScale — множитель поверх физического размера, и единица означает
    // «как задумано». Десктоп остаётся на опорных 1920×1080.
    public static bool UsesPhysicalScale => Application.isMobilePlatform;

    public static float RecommendedDefaultScale =>
        UsesPhysicalScale ? StandardDefaultScale
        : IsRetinaOrHighDpi ? RetinaDefaultScale : StandardDefaultScale;

    public static float Clamp(float scale) =>
        Mathf.Clamp(scale, UIScaleMin, UIScaleMax);

    public static float ResolveEffectiveScale(float configuredScale)
    {
        if (float.IsNaN(configuredScale) || float.IsInfinity(configuredScale) || configuredScale <= 0f)
        {
            return RecommendedDefaultScale;
        }

        return Clamp(configuredScale);
    }
}
