#nullable enable

using UnityEngine;

namespace Kern.Core.Interfaces;

// Правила формы экрана. Данные — в ScreenShape.cs.
public static class ScreenShapeRules
{
    // dp Android и опора физического масштаба: 1 dp = 1/160 дюйма.
    public const float ReferenceDpi = 160f;

    // Плотность, когда платформа её не сообщила (Screen.dpi == 0): xhdpi,
    // нижняя граница современных телефонов. Ошибка в эту сторону делает
    // интерфейс крупнее, а не меньше пальца.
    public const float FallbackMobileDpi = 320f;

    // Micro — экран, на котором не помещается телефонная раскладка: короткая
    // сторона меньше 340 dp (окно в сплите) или длинная меньше 600 dp
    // (внешний экран флипа: почти квадрат ~390×380 dp).
    public const float MicroMaxShortSideDp = 340f;
    public const float MicroMaxLongSideDp = 600f;
    public const float PhoneMaxShortSideDp = 600f;

    // Минимальная цель касания, dp (Material 48, Apple 44 pt). Берётся
    // нижняя: на внешнем экране флипа 48 dp не помещают HUD в высоту.
    public const float MinTouchTargetDp = 44f;

    public static float ResolveDpi(float reportedDpi) =>
        float.IsNaN(reportedDpi) || reportedDpi <= 0f ? FallbackMobileDpi : reportedDpi;

    // Две разные нехватки места на Micro: узкий экран (внешний экран флипа —
    // не хватает ширины) и низкий (окно в сплите — не хватает высоты).
    public static bool IsNarrow(in ScreenShape shape) =>
        shape.Form == ScreenForm.Micro && Mathf.Max(shape.SizeDp.x, shape.SizeDp.y) < MicroMaxLongSideDp;

    public static bool IsShort(in ScreenShape shape) =>
        shape.Form == ScreenForm.Micro && shape.ShortSideDp < MicroMaxShortSideDp;

    public static ScreenForm ClassifyTouch(float shortSideDp, float longSideDp)
    {
        if (shortSideDp < MicroMaxShortSideDp || longSideDp < MicroMaxLongSideDp)
        {
            return ScreenForm.Micro;
        }

        return shortSideDp < PhoneMaxShortSideDp ? ScreenForm.Phone : ScreenForm.Tablet;
    }

    // usesPhysicalScale — мобильная платформа: там интерфейс меряется в dp.
    // isTouch — ввод пальцем; бывает и на десктопе с сенсорным экраном,
    // тогда форма остаётся Desktop, но цели касания всё равно крупные.
    public static ScreenShape Resolve(
        Vector2Int pixelSize,
        float reportedDpi,
        Rect safeAreaPixels,
        bool usesPhysicalScale,
        bool isTouch)
    {
        float dpi = usesPhysicalScale ? ResolveDpi(reportedDpi) : ReferenceDpi;
        ScreenForm form = ScreenForm.Desktop;
        if (usesPhysicalScale)
        {
            float pixelsPerDp = dpi / ReferenceDpi;
            float shortSide = Mathf.Min(pixelSize.x, pixelSize.y) / pixelsPerDp;
            float longSide = Mathf.Max(pixelSize.x, pixelSize.y) / pixelsPerDp;
            form = ClassifyTouch(shortSide, longSide);
        }

        // Пустой safeArea бывает до первого кадра: тогда безопасен весь экран.
        Rect safe = safeAreaPixels.width > 0f && safeAreaPixels.height > 0f
            ? safeAreaPixels
            : new Rect(0f, 0f, pixelSize.x, pixelSize.y);
        return new ScreenShape(pixelSize, dpi, safe, form, isTouch || usesPhysicalScale, usesPhysicalScale);
    }

    // Множитель PanelSettings.scale при ConstantPixelSize: один логический
    // пиксель панели равен одному dp, умноженному на пользовательский масштаб.
    public static float PhysicalPanelScale(in ScreenShape shape, float userMultiplier) =>
        shape.PixelsPerDp * userMultiplier;
}
