#nullable enable

using Fodinae.Rendering.PostProcessing.Scopes;
using Fodinae.Tools.Imgui;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing.Workbench;

/// <summary>
/// Приборы разбора и отладочные виды кадра.
/// </summary>
/// <remarks>
/// Приборы можно выключить, и это не экономия ради экономии: разбор читает
/// прореженный кадр и пишет в три буфера десять раз в секунду. Когда крутят
/// кривую, он нужен; когда смотрят на картинку — мешает мерить сам себя.
/// </remarks>
internal sealed class GradingScopesWindow : ToolWindow
{
    private bool _scopesEnabled;
    private bool? _scopesEnabledRequested;
    private PostProcessDebugView? _debugViewRequested;
    private Vector2 _scroll;

    public GradingScopesWindow()
        : base("Приборы изображения", new Rect(738f, 16f, 446f, 720f))
    {
    }

    /// <summary>
    /// Нужно ли считать приборы. Читает <see cref="GradingWorkbench"/>, чтобы
    /// включить или выключить проход целиком.
    /// </summary>
    public bool ScopesRequested => Visible && _scopesEnabled;

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(380f, 420f);

    protected override void OnPlaySessionReset()
    {
        _scopesEnabled = false;
        _scopesEnabledRequested = null;
        _debugViewRequested = null;
        PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
        PostProcessRuntimeState.CompareSplit = 0f;
        _scroll = default;
    }

    protected override void OnVisibilityChanged(bool visible)
    {
        if (!visible)
        {
            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
            PostProcessRuntimeState.CompareSplit = 0f;
            _debugViewRequested = null;
        }
    }

    protected override void DrawContent()
    {
        ApplyPendingChanges();
        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            GUILayout.Label("ИЗМЕРЕНИЕ СИГНАЛА", SectionLabelStyle);
            using (new GUILayout.HorizontalScope())
            {
                string marker = _scopesEnabled ? "●" : "○";
                bool scopesEnabled = GUILayout.Toggle(
                    _scopesEnabled,
                    $"{marker}  Считать приборы",
                    SegmentedButtonStyle);
                if (scopesEnabled != _scopesEnabled)
                {
                    _scopesEnabledRequested = scopesEnabled;
                }

                GUILayout.FlexibleSpace();
            }

            DrawDebugViewRow();
            DrawCompareRow();
            ToolTheme.Separator();

            bool available = _scopesEnabled && ScopesRenderPass.Available;
            string? failure = ScopesRenderPass.FailureMessage;
            string message = !_scopesEnabled
                ? "Приборы выключены — проход не запускается."
                : !available
                    ? failure == null
                        ? "Приборы недоступны: renderer feature ещё не создал " +
                          "ScopesRenderPass или не нашёл Scopes.compute."
                        : "Приборы остановлены: " + failure +
                          ". Выключите и снова включите «считать приборы» для повтора."
                    : "Обновление 5 раз/с; около 65 тыс. выборок на снимок.";
            GUILayout.Label(
                message,
                available ? ToolTheme.SuccessLabel : MutedLabelStyle);

            float scopeWidth = Mathf.Max(120f, Mathf.Min(410f, Rect.width - 48f));
            DrawScope(
                "Гистограмма",
                available ? ScopesRenderPass.LiveHistogram : null,
                scopeWidth,
                128f);
            GUILayout.Space(6f);
            DrawScope(
                "Waveform RGB",
                available ? ScopesRenderPass.LiveWaveform : null,
                scopeWidth,
                220f);
            GUILayout.Space(6f);
            DrawScope(
                "Вектороскоп",
                available ? ScopesRenderPass.LiveVectorscope : null,
                scopeWidth,
                220f,
                ScaleMode.ScaleToFit);
        }
    }

    private void DrawDebugViewRow()
    {
        GUILayout.Label("ВИД КАДРА", SectionLabelStyle);
        using (new GUILayout.HorizontalScope())
        {
            DebugViewButton("обычный", PostProcessDebugView.None);
            DebugViewButton("ложный цвет", PostProcessDebugView.FalseColor);
            DebugViewButton("отсечка", PostProcessDebugView.Clipping);
        }

        string explanation = PostProcessRuntimeState.DebugView switch
        {
            PostProcessDebugView.FalseColor =>
                "зелёное — ключевой тон, жёлтое и оранжевое — света, " +
                "красное — пересвет, синее — провал",
            PostProcessDebugView.Clipping =>
                "красное — упёрлось в потолок, синее — село в пол",
            PostProcessDebugView.None => string.Empty,
            _ => "неизвестный вид кадра",
        };
        if (!string.IsNullOrEmpty(explanation))
        {
            GUILayout.Label(explanation, MutedLabelStyle);
        }
    }

    private static void DrawCompareRow()
    {
        GUILayout.Label("СРАВНЕНИЕ ДО / ПОСЛЕ", SectionLabelStyle);
        float split;
        using (new GUILayout.HorizontalScope())
        {
            split = GUILayout.HorizontalSlider(
                PostProcessRuntimeState.CompareSplit, 0f, 1f);
            if (split < 0.01f)
            {
                split = 0f;
            }
            else if (split > 0.99f)
            {
                split = 1f;
            }

            PostProcessRuntimeState.CompareSplit = split;
            GUILayout.Label($"{split:P0}", ToolTheme.FieldLabel, GUILayout.Width(48f));
        }

        GUILayout.Label(
            split > 0f
                ? "Слева — исходный кадр, справа — результат тонкоррекции."
                : "Сравнение выключено.",
            ToolTheme.MutedLabel);
    }

    private void DebugViewButton(string label, PostProcessDebugView view)
    {
        bool selected = PostProcessRuntimeState.DebugView == view;
        bool toggled = GUILayout.Toggle(selected, label, ToolTheme.SegmentedButton);
        if (toggled != selected)
        {
            _debugViewRequested = toggled ? view : PostProcessDebugView.None;
        }
    }

    private void ApplyPendingChanges()
    {
        if (Event.current.type != EventType.Layout)
        {
            return;
        }

        if (_scopesEnabledRequested.HasValue)
        {
            _scopesEnabled = _scopesEnabledRequested.Value;
            _scopesEnabledRequested = null;
        }

        if (_debugViewRequested.HasValue)
        {
            PostProcessRuntimeState.DebugView = _debugViewRequested.Value;
            _debugViewRequested = null;
        }
    }

    private static void DrawScope(
        string title,
        RenderTexture? texture,
        float width,
        float height,
        ScaleMode scaleMode = ScaleMode.StretchToFill)
    {
        using (new GUILayout.VerticalScope(ToolTheme.Scope, GUILayout.Width(width)))
        {
            GUILayout.Label(title, ToolTheme.SectionLabel);

            Rect rect = GUILayoutUtility.GetRect(width, height);
            if (Event.current.type == EventType.Repaint && texture != null)
            {
                // Для прямоугольных приборов (гистограмма, waveform) — StretchToFill.
                // Для вектороскопа — ScaleToFit, чтобы круговая диаграмма цветности
                // не превращалась в сплюснутый эллипс.
                GUI.DrawTexture(rect, texture, scaleMode, false);
            }
            else if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(rect, "Нет сигнала", ToolTheme.MutedLabel);
            }
        }
    }
}
