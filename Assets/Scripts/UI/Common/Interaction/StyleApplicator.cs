#nullable enable

using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    /// <summary>
    /// Стиль серверного окна: не приказ, а намерение.
    ///
    /// БЫЛО. Значения из GUIStylePacket клались прямо в element.style.*:
    /// backgroundColor, borderTopColor, marginTop и так далее. Инлайн в UI
    /// Toolkit выигрывает у любого правила USS, а значит серверные окна
    /// оставались вне дизайн-системы целиком: ни тема, ни тир, ни рампа высот
    /// на них не действовали. Тот же дефект, что 98 инлайнов в макете, только
    /// приходящий по проводу и побеждающий всё.
    ///
    /// СТАЛО. Протокол несёт ARGB и пиксели (изменить его отсюда нельзя — он
    /// живёт в MinesServer.Networking), поэтому клиент их ИНТЕРПРЕТИРУЕТ:
    /// ищет ближайший токен палитры и вешает класс утилиты вместо инлайна.
    /// Класс подчиняется теме и тиру, инлайн — нет.
    ///
    /// ЦЕНА, названная вслух. Клиент перестаёт слушаться сервера буквально:
    /// цвет #1a2b3c станет ближайшим токеном палитры, а не собой. Это выбор в
    /// пользу связности интерфейса. Промахи не глотаются: если ничего не попало
    /// в порог, значение применяется как раньше и увеличивает счётчик Misses —
    /// «сервер попросил цвет, которого нет в палитре» должно быть видно.
    /// </summary>
    public static class StyleApplicator
    {
        /// <summary>
        /// Порог примагничивания цвета: квадрат евклидова расстояния в RGB
        /// (0..1 на канал). 0.02 — примерно 8% по каждому каналу; палитра
        /// достаточно редкая, чтобы соседи не спорили. Значение осознанно
        /// консервативное: лучше промах со счётчиком, чем тихая подмена
        /// цвета на непохожий.
        /// </summary>
        private const float ColorThreshold = 0.02f;

        /// <summary>Порог по альфе отдельно: 0.12 разницы в прозрачности видно глазом.</summary>
        private const float AlphaThreshold = 0.12f;

        /// <summary>Порог по отступу: половина минимальной ступени шкалы.</summary>
        private const int SpaceThreshold = 1;

        /// <summary>Сколько раз сервер прислал значение, которого нет в палитре.</summary>
        public static int Misses { get; private set; }

        /// <summary>Последнее непримагниченное значение — для отладочного оверлея.</summary>
        public static string? LastMiss { get; private set; }

        public static void ResetMisses()
        {
            Misses = 0;
            LastMiss = null;
        }

        public static void ApplyStyles(VisualElement element, IGUIComponentPacket packet)
        {
            if (packet.Style is null)
            {
                return;
            }

            var s = packet.Style.Value;

            if (s.Background.A > 0)
            {
                ApplyColor(element, s.Background, "bg", c => element.style.backgroundColor = c);
            }

            if (s.BorderWidth > 0)
            {
                ApplyColor(element, s.Border, "bd", c =>
                {
                    element.style.borderTopColor = c;
                    element.style.borderBottomColor = c;
                    element.style.borderLeftColor = c;
                    element.style.borderRightColor = c;
                });

                // Толщина рамки — раскладка, а не тема: у неё нет ни токена,
                // ни зависимости от темы. Остаётся числом.
                element.style.borderTopWidth = s.BorderWidth;
                element.style.borderBottomWidth = s.BorderWidth;
                element.style.borderLeftWidth = s.BorderWidth;
                element.style.borderRightWidth = s.BorderWidth;
            }

            ApplySpace(element, "mar", s.Margin,
                (l) => element.style.marginLeft = l, (t) => element.style.marginTop = t,
                (r) => element.style.marginRight = r, (b) => element.style.marginBottom = b);

            ApplySpace(element, "pad", s.Padding,
                (l) => element.style.paddingLeft = l, (t) => element.style.paddingTop = t,
                (r) => element.style.paddingRight = r, (b) => element.style.paddingBottom = b);
        }

        private static void ApplyColor(
            VisualElement element,
            System.Drawing.Color color,
            string prefix,
            System.Action<Color> fallback)
        {
            var unity = ConvertColor(color);
            var swatch = NearestSwatch(unity, prefix);
            if (swatch != null)
            {
                element.AddToClassList(swatch);
                return;
            }

            Misses++;
            LastMiss = $"{prefix}: rgba({color.R}, {color.G}, {color.B}, {color.A / 255f:0.##})";
            fallback(unity);
        }

        private static string? NearestSwatch(Color color, string prefix)
        {
            string? best = null;
            float bestDistance = float.MaxValue;

            foreach (var swatch in DesignTokens.Colors)
            {
                if (swatch.UtilityClass == null || !swatch.UtilityClass.StartsWith(prefix))
                {
                    continue;
                }

                // Альфа проверяется отдельно от цвета: тон и прозрачность —
                // разные величины, и смешивать их в одном расстоянии значит
                // подменять полупрозрачную заливку непрозрачной того же тона.
                if (Mathf.Abs(swatch.A - color.a) > AlphaThreshold)
                {
                    continue;
                }

                float dr = swatch.R - color.r;
                float dg = swatch.G - color.g;
                float db = swatch.B - color.b;
                float distance = (dr * dr) + (dg * dg) + (db * db);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = swatch.UtilityClass;
                }
            }

            return bestDistance <= ColorThreshold ? best : null;
        }

        private static void ApplySpace(
            VisualElement element,
            string prefix,
            Margins margins,
            System.Action<int> left,
            System.Action<int> top,
            System.Action<int> right,
            System.Action<int> bottom)
        {
            ApplySide(element, prefix, "l", margins.Left, left);
            ApplySide(element, prefix, "t", margins.Top, top);
            ApplySide(element, prefix, "r", margins.Right, right);
            ApplySide(element, prefix, "b", margins.Bottom, bottom);
        }

        private static void ApplySide(
            VisualElement element,
            string prefix,
            string side,
            int value,
            System.Action<int> fallback)
        {
            if (value == 0)
            {
                return;
            }

            string? token = NearestSpace(value);
            if (token != null)
            {
                element.AddToClassList($"{prefix}-{side}-{token.Substring(2)}");
                return;
            }

            Misses++;
            LastMiss = $"{prefix}-{side}: {value}px";
            fallback(value);
        }

        private static string? NearestSpace(int px)
        {
            string? best = null;
            int bestDistance = int.MaxValue;

            foreach (var (token, step) in DesignTokens.Space)
            {
                int distance = Mathf.Abs(step - px);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = token;
                }
            }

            return bestDistance <= SpaceThreshold ? best : null;
        }

        public static Color ConvertColor(System.Drawing.Color c)
        {
            return new Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
        }
    }
}
