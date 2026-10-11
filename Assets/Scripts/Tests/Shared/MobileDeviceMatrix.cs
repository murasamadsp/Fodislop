#nullable enable

using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.Tests;

// Матрица устройств для проверок формы экрана и раскладки. Пиксели — в
// альбомной ориентации, плотность — паспортная (ppi), безопасная область —
// в пикселях экрана с началом внизу слева, как отдаёт Screen.safeArea.
// Ожидаемая форма записана руками по паспортным данным, а не вычислена
// правилами, которые здесь проверяются.
public readonly struct MobileDevice
{
    public readonly string Name;
    public readonly Vector2Int Pixels;
    public readonly float Dpi;
    public readonly Rect SafeArea;
    public readonly ScreenForm ExpectedForm;

    public MobileDevice(string name, Vector2Int pixels, float dpi, Rect safeArea, ScreenForm expectedForm)
    {
        Name = name;
        Pixels = pixels;
        Dpi = dpi;
        SafeArea = safeArea;
        ExpectedForm = expectedForm;
    }

    public override string ToString() => Name;
}

public static class MobileDeviceMatrix
{
    public static readonly MobileDevice[] All =
    {
        // 654×368 dp: самый маленький живой телефон, кнопки «Домой» и выреза нет.
        new("iPhone SE 3", new Vector2Int(1334, 750), 326f, new Rect(0, 0, 1334, 750), ScreenForm.Phone),

        // 973×449 dp; Dynamic Island слева, полоса «Домой» снизу (59 pt / 21 pt × 3).
        new("iPhone 15 Pro Max", new Vector2Int(2796, 1290), 460f, new Rect(177, 63, 2796 - 354, 1290 - 63), ScreenForm.Phone),

        // 897×404 dp; вырез под камеру слева.
        new("Pixel 8", new Vector2Int(2400, 1080), 428f, new Rect(118, 0, 2400 - 118, 1080), ScreenForm.Phone),

        // Внешний экран Galaxy Z Flip6: 391×376 dp — почти квадрат.
        new("Galaxy Z Flip6 cover", new Vector2Int(748, 720), 306f, new Rect(0, 0, 748, 720), ScreenForm.Micro),

        // Телефон в сплите на половину высоты: 897×202 dp.
        new("Pixel 8 split", new Vector2Int(2400, 540), 428f, new Rect(0, 0, 2400, 540), ScreenForm.Micro),

        // Внешний экран Galaxy Z Fold6: узкий высокий телефон, 946×385 dp.
        new("Galaxy Z Fold6 cover", new Vector2Int(2376, 968), 402f, new Rect(0, 0, 2376, 968), ScreenForm.Phone),

        // Внутренний экран Galaxy Z Fold6: 924×794 dp, почти квадрат.
        new("Galaxy Z Fold6 inner", new Vector2Int(2160, 1856), 374f, new Rect(0, 0, 2160, 1856), ScreenForm.Tablet),

        // 1112×730 dp.
        new("iPad mini 6", new Vector2Int(2266, 1488), 326f, new Rect(0, 40, 2266, 1488 - 40), ScreenForm.Tablet),

        // 1668×1251 dp.
        new("iPad Pro 13", new Vector2Int(2752, 2064), 264f, new Rect(0, 40, 2752, 2064 - 40), ScreenForm.Tablet),
    };
}
