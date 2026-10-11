#nullable enable

using UnityEngine;

namespace Kern.Core.Interfaces;

// Класс формы экрана. Решается по короткой стороне в dp (1/160 дюйма), а не
// по пикселям и не по платформе: один и тот же Android бывает телефоном,
// внешним экраном флипа и раскрытой раскладушкой — в течение одной сессии.
//
//   Desktop  мышь и клавиатура, раскладка под опорные 1920×1080
//   Micro    короткая < 340 dp или длинная < 600 dp — внешний экран флипа,
//            окно в сплите
//   Phone    короткая сторона < 600 dp — телефоны от SE до Pro Max
//   Tablet   короткая сторона ≥ 600 dp — iPad, внутренний экран раскладушки
public enum ScreenForm
{
    Desktop = 0,
    Micro = 1,
    Phone = 2,
    Tablet = 3,
}

// Снимок экрана: всё, по чему интерфейс и камера решают раскладку.
// Пиксели — в системе Screen (начало внизу слева), dp — физическая единица.
public readonly struct ScreenShape
{
    public readonly Vector2Int PixelSize;

    // Плотность, по которой считается dp. Уже разрешена: при Screen.dpi == 0
    // здесь стоит запасное значение, а не ноль.
    public readonly float Dpi;

    // Безопасный прямоугольник в пикселях экрана (начало внизу слева).
    public readonly Rect SafeAreaPixels;

    public readonly ScreenForm Form;

    // Ввод касанием: интерфейс обязан быть попадаемым пальцем.
    public readonly bool IsTouch;

    // Масштаб от пикселей к dp выбран физическим (мобильная платформа), а не
    // опорным разрешением панели.
    public readonly bool UsesPhysicalScale;

    public ScreenShape(
        Vector2Int pixelSize,
        float dpi,
        Rect safeAreaPixels,
        ScreenForm form,
        bool isTouch,
        bool usesPhysicalScale)
    {
        PixelSize = pixelSize;
        Dpi = dpi;
        SafeAreaPixels = safeAreaPixels;
        Form = form;
        IsTouch = isTouch;
        UsesPhysicalScale = usesPhysicalScale;
    }

    public float PixelsPerDp => Dpi / ScreenShapeRules.ReferenceDpi;

    public Vector2 SizeDp => new(PixelSize.x / PixelsPerDp, PixelSize.y / PixelsPerDp);

    public float ShortSideDp => Mathf.Min(SizeDp.x, SizeDp.y);

    public bool SameAs(in ScreenShape other) =>
        PixelSize == other.PixelSize &&
        Mathf.Approximately(Dpi, other.Dpi) &&
        SafeAreaPixels == other.SafeAreaPixels &&
        Form == other.Form &&
        IsTouch == other.IsTouch &&
        UsesPhysicalScale == other.UsesPhysicalScale;
}
