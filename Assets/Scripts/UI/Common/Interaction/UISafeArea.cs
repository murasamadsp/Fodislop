#nullable enable

using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

// Вырез экрана и скруглённые углы: рендер идёт во весь экран
// (Player Settings → Render outside safe area), поэтому край панели уезжает
// под вырез камеры. Корень документа сжимается до безопасного прямоугольника,
// и всё, что в документ положено, не оказывается под вырезом — включая
// инвентарь, миникарту и чат, которые вешаются на корень документа напрямую,
// а не внутрь дерева одного экрана.
public static class UISafeArea
{
    // Меньше половины пикселя — не изменение, а дребезг округления: запись
    // стиля помечает панель грязной и стоит пересчёта раскладки.
    private const float WriteEpsilon = 0.5f;

    // Вызывается один раз на корень — это обеспечивает UIScreen.Bind.
    // Смену одного только безопасного прямоугольника (поворот на 180°, вырез
    // переехал на другую сторону) геометрия не покажет: её доносит опрос
    // экрана в UIScreen.
    internal static void Attach(VisualElement documentRoot)
    {
        Apply(documentRoot);
        documentRoot.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    internal static void Detach(VisualElement documentRoot) =>
        documentRoot.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

    private static void OnGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.currentTarget is VisualElement documentRoot)
        {
            Apply(documentRoot);
        }
    }

    public static void Apply(VisualElement documentRoot) => Apply(documentRoot, UIScreen.Current);

    public static void Apply(VisualElement documentRoot, in ScreenShape shape)
    {
        if (documentRoot.panel == null)
        {
            return;
        }

        // Размер панели — по её корню, а не по самому документу: документ
        // уже сжат прошлыми инсетами, и правый с нижним отступами, посчитанные
        // от его ширины, на втором проходе обнулились бы.
        VisualElement visualTree = documentRoot.panel.visualTree;
        float panelWidth = visualTree.resolvedStyle.width;
        float panelHeight = visualTree.resolvedStyle.height;

        // До первой раскладки размеры равны NaN — считать инсеты не от чего.
        if (float.IsNaN(panelWidth) || float.IsNaN(panelHeight) ||
            panelWidth <= 0f || panelHeight <= 0f)
        {
            return;
        }

        Insets insets = Compute(shape, new Vector2(panelWidth, panelHeight));

        // Абсолютное позиционирование задаётся явно: инсеты в стиле корня
        // имеют смысл только у позиционированного элемента, а раскладка
        // документа полагается на остальные четыре края.
        if (documentRoot.style.position.value != Position.Absolute)
        {
            documentRoot.style.position = Position.Absolute;
        }

        // Сравнение — с записанным стилем, а не с resolvedStyle: у
        // абсолютного корня resolvedStyle.bottom/right отдаёт позицию из
        // раскладки, а не заданный отступ, и при смене экрана запись нового
        // нуля пропускалась — отступ прошлого устройства оставался.
        WriteIfChanged(documentRoot.style.left, insets.Left, value => documentRoot.style.left = value);
        WriteIfChanged(documentRoot.style.top, insets.Top, value => documentRoot.style.top = value);
        WriteIfChanged(documentRoot.style.right, insets.Right, value => documentRoot.style.right = value);
        WriteIfChanged(documentRoot.style.bottom, insets.Bottom, value => documentRoot.style.bottom = value);
    }

    private static void WriteIfChanged(StyleLength current, float target, System.Action<float> write)
    {
        bool set = current.keyword == StyleKeyword.Undefined && current.value.unit == LengthUnit.Pixel;
        if (!set || Mathf.Abs(current.value.value - target) >= WriteEpsilon)
        {
            write(target);
        }
    }

    // Безопасная область в пикселях экрана (начало внизу слева) → отступы
    // в пикселях панели (начало вверху слева). Панель целиком покрывает
    // экран, поэтому перевод — одно отношение размеров, без знания режима
    // масштаба PanelSettings.
    public static Insets Compute(in ScreenShape shape, Vector2 panelSize)
    {
        float toPanelX = panelSize.x / Mathf.Max(1, shape.PixelSize.x);
        float toPanelY = panelSize.y / Mathf.Max(1, shape.PixelSize.y);
        Rect safe = shape.SafeAreaPixels;

        return new Insets(
            Mathf.Max(0f, safe.xMin * toPanelX),
            Mathf.Max(0f, (shape.PixelSize.y - safe.yMax) * toPanelY),
            Mathf.Max(0f, (shape.PixelSize.x - safe.xMax) * toPanelX),
            Mathf.Max(0f, safe.yMin * toPanelY));
    }

    public readonly struct Insets
    {
        public readonly float Left;
        public readonly float Top;
        public readonly float Right;
        public readonly float Bottom;

        public Insets(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }
}
