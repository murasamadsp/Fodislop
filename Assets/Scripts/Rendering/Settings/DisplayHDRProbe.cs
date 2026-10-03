#nullable enable

using System;
using System.Runtime.InteropServices;

namespace Kern.Rendering;

/// <summary>Умеет ли сам дисплей окна HDR — независимо от режима, в котором стартовал Unity.</summary>
///
/// Редактор связывает HDROutputSettings.available со стартовым режимом
/// (useHDRDisplay): в SDR-старте недоступным выглядит и HDR-дисплей. Отличить
/// «дисплей не умеет» от «редактор стартовал в SDR» можно только спросив
/// систему. На macOS это NSScreen: потенциальный запас EDR больше 1.0 есть
/// только у дисплея с HDR, у обычного он ровно 1.0. Где системного ответа нет,
/// дисплей считается неспособным: переключатель без доказательства не
/// включается.
///
/// Тот же запас даёт пиковую яркость панели. Значение EDR 1.0 — опорный
/// белый macOS, 100 нит; потенциальный запас — во сколько раз ярче него
/// панель может светить: 16 у XDR-экрана — это его 1600 нит.
internal static class DisplayHDRProbe
{
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
    private const float ReferenceWhiteNits = 100f;
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly IntPtr s_applicationClass = GetClass("NSApplication");
    private static readonly IntPtr s_screenClass = GetClass("NSScreen");
    private static readonly IntPtr s_sharedApplication = RegisterSelector("sharedApplication");
    private static readonly IntPtr s_mainWindow = RegisterSelector("mainWindow");
    private static readonly IntPtr s_screen = RegisterSelector("screen");
    private static readonly IntPtr s_mainScreen = RegisterSelector("mainScreen");
    private static readonly IntPtr s_respondsToSelector = RegisterSelector("respondsToSelector:");
    private static readonly IntPtr s_maximumPotentialEdr =
        RegisterSelector("maximumPotentialExtendedDynamicRangeColorComponentValue");

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterSelector(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendObject(IntPtr receiver, IntPtr selector);

    // BOOL — один байт (signed char на x86_64, bool на arm64), не Win32 BOOL.
    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr argument);

    // CGFloat — double; на arm64 и x86_64 он возвращается обычным objc_msgSend.
    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    /// AppKit — только с главного потока; вызывающие (меню, сверка вывода) на нём.
    public static bool CurrentDisplaySupportsHDR() => ReadPotentialHeadroom() > 1.0;

    public static bool TryReadPeakBrightnessNits(out float nits)
    {
        double headroom = ReadPotentialHeadroom();
        nits = headroom > 1.0 ? (float)(headroom * ReferenceWhiteNits) : 0f;
        return headroom > 1.0;
    }

    private static double ReadPotentialHeadroom()
    {
        IntPtr application = SendObject(s_applicationClass, s_sharedApplication);
        IntPtr window = application == IntPtr.Zero ? IntPtr.Zero : SendObject(application, s_mainWindow);
        IntPtr screen = window == IntPtr.Zero ? IntPtr.Zero : SendObject(window, s_screen);
        if (screen == IntPtr.Zero)
        {
            screen = SendObject(s_screenClass, s_mainScreen);
        }

        return screen != IntPtr.Zero && SendBool(screen, s_respondsToSelector, s_maximumPotentialEdr)
            ? SendDouble(screen, s_maximumPotentialEdr)
            : 0.0;
    }
#else
    public static bool CurrentDisplaySupportsHDR() => false;

    public static bool TryReadPeakBrightnessNits(out float nits)
    {
        nits = 0f;
        return false;
    }
#endif
}
