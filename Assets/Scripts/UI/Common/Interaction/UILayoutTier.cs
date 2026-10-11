#nullable enable

using Kern.Core.Interfaces;
using UnityEngine.UIElements;

namespace Kern.UI;

// Классы раскладки на корне панели (panel.visualTree), чтобы их видели все
// деревья документа — и HUD, и инвентарь, и Программатор, и окна сервера.
// @media в USS нет: токены и правила компонентов переключаются классом.
//
//   tier--compact|standard|wide   плотность раскладки (токены ThemeTokens.uss)
//   form--desktop|micro|phone|tablet   форма экрана (ScreenShapeRules)
//   form--narrow|short             у Micro: не хватает ширины или высоты
//   pointer--touch                 ввод пальцем: цели касания не меньше 44 dp
public static class UILayoutTier
{
    public const string CompactClass = "tier--compact";
    public const string StandardClass = "tier--standard";
    public const string WideClass = "tier--wide";

    public const string FormDesktopClass = "form--desktop";
    public const string FormMicroClass = "form--micro";
    public const string FormPhoneClass = "form--phone";
    public const string FormTabletClass = "form--tablet";
    public const string FormNarrowClass = "form--narrow";
    public const string FormShortClass = "form--short";
    public const string TouchClass = "pointer--touch";

    private const float CompactMaxWidth = 900f;
    private const float WideMinWidth = 1600f;

    // Десктопный тир зависит от ширины окна, поэтому корень слушает свою
    // геометрию. Форма экрана приходит из UIScreen.
    public static void Attach(VisualElement visualTree)
    {
        visualTree.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    public static void Detach(VisualElement visualTree) =>
        visualTree.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

    private static void OnGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.currentTarget is VisualElement visualTree)
        {
            Apply(visualTree, UIScreen.Current);
        }
    }

    public static void Apply(VisualElement visualTree, in ScreenShape shape)
    {
        string tier = ResolveTier(shape, visualTree.resolvedStyle.width);

        // Смена класса меняет токены, токены — раскладку, раскладка снова шлёт
        // GeometryChangedEvent: без проверки на границе тира возможна осцилляция.
        if (!visualTree.ClassListContains(tier))
        {
            visualTree.EnableInClassList(CompactClass, tier == CompactClass);
            visualTree.EnableInClassList(StandardClass, tier == StandardClass);
            visualTree.EnableInClassList(WideClass, tier == WideClass);
        }

        visualTree.EnableInClassList(FormDesktopClass, shape.Form == ScreenForm.Desktop);
        visualTree.EnableInClassList(FormMicroClass, shape.Form == ScreenForm.Micro);
        visualTree.EnableInClassList(FormPhoneClass, shape.Form == ScreenForm.Phone);
        visualTree.EnableInClassList(FormTabletClass, shape.Form == ScreenForm.Tablet);
        visualTree.EnableInClassList(FormNarrowClass, ScreenShapeRules.IsNarrow(shape));
        visualTree.EnableInClassList(FormShortClass, ScreenShapeRules.IsShort(shape));
        visualTree.EnableInClassList(TouchClass, shape.IsTouch);
    }

    public static string ResolveTier(in ScreenShape shape, float panelWidth)
    {
        // Телефон всегда компактен, планшет — стандартен, сколько бы
        // логических пикселей ни было: на шести дюймах десктопная плотность
        // нечитаема, а на iPad компактная выглядит игрушечной.
        switch (shape.Form)
        {
            case ScreenForm.Micro:
            case ScreenForm.Phone:
                return CompactClass;
            case ScreenForm.Tablet:
                return StandardClass;
            case ScreenForm.Desktop:
                break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(shape), shape.Form, "Unknown screen form.");
        }

        // До первой раскладки ширина равна NaN — тогда остаётся стандартный.
        if (float.IsNaN(panelWidth) || panelWidth <= 0f)
        {
            return StandardClass;
        }

        if (panelWidth < CompactMaxWidth)
        {
            return CompactClass;
        }

        return panelWidth >= WideMinWidth ? WideClass : StandardClass;
    }
}
