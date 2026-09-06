#nullable enable

using UnityEngine;

namespace Fodinae.Tools.Imgui;

/// <summary>
/// Обвязка окна: рамка, полосы развёртки, уголки, шкалы.
/// </summary>
/// <remarks>
/// Всё здесь рисуется поверх содержимого и ничего о нём не знает. Это
/// сознательная граница: окно описывает, что показать, обвязка — как выглядит
/// сам инструмент. Раньше обе задачи решались в одном методе, и любая правка
/// вида требовала лезть в код, который считает числа.
///
/// Каждый метод обязан сам проверить <see cref="EventType.Repaint"/>. Рисование
/// на других событиях не только бесполезно, но и ломает раскладку: GUILayout
/// считает контролы, а не пиксели, и лишний вызов в Layout-проходе разъезжается
/// с Repaint-проходом.
/// </remarks>
public static class ToolChrome
{
    private const float BracketLength = 14f;
    private const float BracketThickness = 2f;
    private const float MarkerWidth = 3f;

    /// <summary>Метка слева в полосе заголовка: у активного окна горит.</summary>
    public static void DrawHeaderMarker(float headerHeight, bool focused)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = focused
            ? ToolPalette.Accent
            : ToolPalette.Fade(ToolPalette.Accent, 0.32f);
        GUI.DrawTexture(new Rect(1f, 1f, MarkerWidth, headerHeight - 2f), ToolPalette.White);
        GUI.color = previous;
    }

    /// <summary>
    /// Линейка под заголовком с делениями.
    /// </summary>
    /// <remarks>
    /// Деления — не украшение: по ним видно, что окно тянут за край, потому что
    /// их число меняется вместе с шириной. Сплошная линия этого не показывала.
    /// </remarks>
    public static void DrawHeaderRule(float width, float headerHeight, bool focused)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previous = GUI.color;
        float y = headerHeight - 2f;
        float usable = Mathf.Max(0f, width - 2f);

        GUI.color = ToolPalette.Fade(ToolPalette.Accent, focused ? 0.55f : 0.22f);
        GUI.DrawTexture(new Rect(1f, y, usable, 1f), ToolPalette.White);

        GUI.color = ToolPalette.Fade(ToolPalette.Accent, focused ? 0.85f : 0.35f);
        for (float x = 6f; x < usable - 4f; x += 9f)
        {
            GUI.DrawTexture(new Rect(x, y - 2f, 1f, 2f), ToolPalette.White);
        }

        GUI.color = previous;
    }

    /// <summary>Уголки по четырём краям: рамка окна читается как прицел.</summary>
    public static void DrawCornerBrackets(Rect local, bool focused)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = focused
            ? ToolPalette.Accent
            : ToolPalette.Fade(ToolPalette.Data, 0.30f);

        float length = Mathf.Min(BracketLength, local.width * 0.25f);
        float t = BracketThickness;
        float right = local.width;
        float bottom = local.height;

        // Левый верхний.
        GUI.DrawTexture(new Rect(0f, 0f, length, t), ToolPalette.White);
        GUI.DrawTexture(new Rect(0f, 0f, t, length), ToolPalette.White);

        // Правый верхний срезан рамкой, поэтому там только нижняя половина.
        GUI.DrawTexture(new Rect(right - t, ToolPalette.NotchSize, t, length), ToolPalette.White);

        // Левый нижний.
        GUI.DrawTexture(new Rect(0f, bottom - t, length, t), ToolPalette.White);
        GUI.DrawTexture(new Rect(0f, bottom - length, t, length), ToolPalette.White);

        // Правый нижний.
        GUI.DrawTexture(new Rect(right - length, bottom - t, length, t), ToolPalette.White);
        GUI.DrawTexture(new Rect(right - t, bottom - length, t, length), ToolPalette.White);

        GUI.color = previous;
    }

    /// <summary>
    /// Полосы развёртки по телу окна.
    /// </summary>
    /// <remarks>
    /// Текстура повторяется по вертикали через координаты, а не растягивается:
    /// иначе шаг полос менялся бы с высотой окна и на высоком окне превращался
    /// в градиент.
    /// </remarks>
    public static void DrawScanlines(Rect local)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        GUI.DrawTextureWithTexCoords(
            local,
            ToolPalette.Scanlines,
            new Rect(0f, 0f, 1f, local.height / 4f));
    }

    /// <summary>Горизонтальная шкала заполнения.</summary>
    public static void DrawMeter(Rect area, float normalized, Color color)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = ToolPalette.Fade(color, 0.16f);
        GUI.DrawTexture(area, ToolPalette.White);

        float filled = Mathf.Clamp01(normalized) * area.width;
        GUI.color = color;
        GUI.DrawTexture(new Rect(area.x, area.y, filled, area.height), ToolPalette.White);

        // Головка шкалы ярче хвоста: край заполнения виден и на узкой полосе,
        // где сплошная заливка сливается с подложкой.
        if (filled > 2f)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(area.x + filled - 2f, area.y, 2f, area.height), ToolPalette.White);
        }

        GUI.color = previous;
    }

    /// <summary>Шкала строкой раскладки — для содержимого окон.</summary>
    public static void MeterLine(float normalized, Color color, float height = 4f)
    {
        Rect area = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
        DrawMeter(area, normalized, color);
    }

    /// <summary>Заголовок раздела с ведущей чертой и волосяной линией под ним.</summary>
    public static void SectionHeader(string title)
    {
        GUILayout.Space(3f);
        Rect line = GUILayoutUtility.GetRect(1f, 14f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            Color previous = GUI.color;
            GUI.color = ToolPalette.Accent;
            GUI.DrawTexture(new Rect(line.x, line.y + 3f, 2f, 9f), ToolPalette.White);

            GUI.color = ToolPalette.Hairline;
            GUI.DrawTexture(new Rect(line.x, line.yMax + 1f, line.width, 1f), ToolPalette.White);

            GUI.color = previous;
            var shifted = new Rect(line.x + 7f, line.y, line.width - 7f, line.height);
            GUI.Label(shifted, title, ToolTheme.SectionLabel);
        }

        GUILayout.Space(5f);
    }

    /// <summary>
    /// Полоса-предупреждение во всю ширину окна.
    /// </summary>
    /// <remarks>
    /// Нужна там, где состояние инструмента меняет сам кадр. Обход подсистемы
    /// легко забыть включённым, и дальше человек час объясняет себе цифры,
    /// которые получены не из игры. Полоса стоит поперёк содержимого именно
    /// затем, чтобы её нельзя было не заметить.
    /// </remarks>
    public static void Banner(string text, Color color)
    {
        Rect area = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = ToolPalette.Fade(color, 0.18f);
        GUI.DrawTexture(area, ToolPalette.White);
        GUI.color = color;
        GUI.DrawTexture(new Rect(area.x, area.y, 3f, area.height), ToolPalette.White);
        GUI.color = previous;

        DrawTinted(
            new Rect(area.x + 9f, area.y + 5f, area.width - 12f, area.height),
            text,
            ToolTheme.SectionLabel,
            color);
    }

    /// <summary>
    /// Рисует надпись общим стилем, временно перекрасив его.
    /// </summary>
    /// <remarks>
    /// Копия стиля была бы честнее, но это новая <see cref="GUIStyle"/> на
    /// каждое событие IMGUI, то есть мусор в куче ради одного цвета. Подмена в
    /// общем стиле дешевле, и <c>finally</c> здесь не формальность: без него
    /// исключение внутри отрисовки оставило бы чужой цвет во всех остальных
    /// окнах до перезапуска, и искать причину пришлось бы долго.
    /// </remarks>
    public static void DrawTinted(Rect area, string text, GUIStyle style, Color color)
    {
        Color previous = style.normal.textColor;
        style.normal.textColor = color;
        try
        {
            GUI.Label(area, text, style);
        }
        finally
        {
            style.normal.textColor = previous;
        }
    }

    /// <summary>Точка состояния перед строкой: цвет несёт смысл, текст — величину.</summary>
    public static void StatusPip(Color color, float size = 7f)
    {
        Rect area = GUILayoutUtility.GetRect(size + 4f, size + 4f, GUILayout.Width(size + 4f));
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = ToolPalette.Fade(color, 0.25f);
        GUI.DrawTexture(area, ToolPalette.White);
        GUI.color = color;
        const float inset = 2f;
        GUI.DrawTexture(
            new Rect(area.x + inset, area.y + inset, area.width - inset * 2f, area.height - inset * 2f),
            ToolPalette.White);
        GUI.color = previous;
    }
}
