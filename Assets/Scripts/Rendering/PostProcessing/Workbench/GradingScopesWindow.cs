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
        PostProcessRuntimeState.CompareMode = CompareMode.Off;
        PostProcessRuntimeState.CompareBefore = false;
        ScopesRenderPass.SourceMode = ScopesSourceMode.After;
        ScopesRenderPass.WaveformMode = ScopeWaveformMode.Overlay;
        ScopesRenderPass.HistogramMode = 0;
        _scroll = default;
    }

    protected override void OnVisibilityChanged(bool visible)
    {
        if (!visible)
        {
            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
            PostProcessRuntimeState.CompareSplit = 0f;
            PostProcessRuntimeState.CompareMode = CompareMode.Off;
            PostProcessRuntimeState.CompareBefore = false;
            ScopesRenderPass.SourceMode = ScopesSourceMode.After;
            ScopesRenderPass.WaveformMode = ScopeWaveformMode.Overlay;
            ScopesRenderPass.HistogramMode = 0;
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
            DrawScopeSourceRow();
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
            if (available)
            {
                GUILayout.Label(
                    $"Clipped: shadows {ScopesRenderPass.ClippedBlackSamples:N0} / " +
                    $"highlights {ScopesRenderPass.ClippedHighlightSamples:N0} samples",
                    ToolTheme.MutedLabel);
            }

            float scopeWidth = Mathf.Max(120f, Mathf.Min(410f, Rect.width - 48f));
            DrawScope(
                "Гистограмма",
                available ? ScopesRenderPass.LiveHistogram : null,
                scopeWidth,
                128f);
            GUILayout.Space(6f);
            DrawScope(
                ScopesRenderPass.WaveformMode == ScopeWaveformMode.Parade
                    ? "Waveform RGB parade"
                    : ScopesRenderPass.WaveformMode == ScopeWaveformMode.Luma
                        ? "Waveform Luma"
                        : "Waveform RGB overlay",
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
            bool showSkinToneLine = GUILayout.Toggle(
                ScopesRenderPass.ShowSkinToneLine,
                "Skin-tone line",
                ToolTheme.SegmentedButton);
            ScopesRenderPass.ShowSkinToneLine = showSkinToneLine;
            GUILayout.Label(
                "Targets: R / Mg / B / Cy / G / Y · 75% / 100%",
                ToolTheme.MutedLabel);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("vectorscope zoom", ToolTheme.FieldLabel, GUILayout.Width(120f));
                ScopesRenderPass.VectorscopeScale = GUILayout.HorizontalSlider(
                    ScopesRenderPass.VectorscopeScale,
                    0.5f,
                    2f);
                GUILayout.Label(
                    $"{ScopesRenderPass.VectorscopeScale:0.00}×",
                    ToolTheme.FieldLabel,
                    GUILayout.Width(48f));
            }
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
            DebugViewButton("highlights", PostProcessDebugView.HighlightClipping);
            DebugViewButton("shadows", PostProcessDebugView.ShadowClipping);
            DebugViewButton("GAMUT WARNING", PostProcessDebugView.GamutWarning);
            DebugViewButton("luma", PostProcessDebugView.LumaOnly);
            DebugViewButton("sat", PostProcessDebugView.SaturationOnly);
            DebugViewButton("matte", PostProcessDebugView.QualifierMatte);
            DebugViewButton("R", PostProcessDebugView.SoloRed);
            DebugViewButton("G", PostProcessDebugView.SoloGreen);
            DebugViewButton("B", PostProcessDebugView.SoloBlue);
        }

        string explanation = PostProcessRuntimeState.DebugView switch
        {
            PostProcessDebugView.FalseColor =>
                "зелёное — ключевой тон, жёлтое и оранжевое — света, " +
                "красное — пересвет, синее — провал",
            PostProcessDebugView.Clipping =>
                "красное — упёрлось в потолок, синее — село в пол",
            PostProcessDebugView.HighlightClipping =>
                "красное — clipped highlights, исходное изображение сохранено",
            PostProcessDebugView.ShadowClipping =>
                "синее — clipped shadows, исходное изображение сохранено",
            PostProcessDebugView.GamutWarning =>
                "синий — ниже display gamut, магентовый — выше, белый — оба предупреждения",
            PostProcessDebugView.LumaOnly =>
                "монохромная яркость финального graded output",
            PostProcessDebugView.SaturationOnly =>
                "чёрный — нейтральный, белый — максимальная насыщенность",
            PostProcessDebugView.QualifierMatte =>
                "белое — выбранная qualifier-маска, чёрное — исключённые пиксели",
            PostProcessDebugView.SoloRed => "только красный канал",
            PostProcessDebugView.SoloGreen => "только зелёный канал",
            PostProcessDebugView.SoloBlue => "только синий канал",
            PostProcessDebugView.None => string.Empty,
            _ => "неизвестный вид кадра",
        };
        if (!string.IsNullOrEmpty(explanation))
        {
            GUILayout.Label(explanation, MutedLabelStyle);
        }
    }

    private void DrawCompareRow()
    {
        GUILayout.Label("СРАВНЕНИЕ ДО / ПОСЛЕ", SectionLabelStyle);

        using (new GUILayout.HorizontalScope())
        {
            CompareModeButton("выкл", CompareMode.Off);
            CompareModeButton("верт. wipe", CompareMode.VerticalWipe);
            CompareModeButton("гориз. wipe", CompareMode.HorizontalWipe);
            CompareModeButton("side-by-side", CompareMode.SideBySide);
            CompareModeButton("A/B", CompareMode.AbToggle);
        }

        CompareMode mode = PostProcessRuntimeState.CompareMode;
        if (mode == CompareMode.AbToggle)
        {
            bool before = GUILayout.Toggle(
                PostProcessRuntimeState.CompareBefore,
                "Показывать BEFORE",
                ToolTheme.SegmentedButton);
            PostProcessRuntimeState.CompareBefore = before;
        }

        float split = PostProcessRuntimeState.CompareSplit;
        if (mode is CompareMode.VerticalWipe or CompareMode.HorizontalWipe)
        {
            using (new GUILayout.HorizontalScope())
            {
                split = GUILayout.HorizontalSlider(
                    split, 0f, 1f);
                PostProcessRuntimeState.CompareSplit = split;
                GUILayout.Label($"{split:P0}", ToolTheme.FieldLabel, GUILayout.Width(48f));
            }
        }

            GUILayout.Label(
                mode switch
            {
                CompareMode.VerticalWipe => "Слева — BEFORE, справа — AFTER.",
                CompareMode.HorizontalWipe => "Снизу — BEFORE, сверху — AFTER.",
                CompareMode.SideBySide => "Левая половина — BEFORE, правая — AFTER.",
                CompareMode.AbToggle => PostProcessRuntimeState.CompareBefore
                    ? "A/B: показывается BEFORE."
                    : "A/B: показывается AFTER.",
                _ => "Сравнение выключено.",
            },
            ToolTheme.MutedLabel);
        GUILayout.Label(
            "Удерживайте \\ для временного bypass и быстрого A/B сравнения.",
            ToolTheme.MutedLabel);
    }

    private static void DrawScopeSourceRow()
    {
        GUILayout.Label("ИСТОЧНИК ПРИБОРОВ", SectionLabelStyle);
        using (new GUILayout.HorizontalScope())
        {
            ScopeSourceButton("после грейда", ScopesSourceMode.After);
            ScopeSourceButton("до грейда", ScopesSourceMode.Before);
        }

        GUILayout.Label(
            ScopesRenderPass.SourceMode == ScopesSourceMode.Before
                ? "Scopes читают исходный camera color до постпроцесса."
                : "Scopes читают финальный camera color после постпроцесса.",
            ToolTheme.MutedLabel);

        GUILayout.Label("WAVEFORM", SectionLabelStyle);
        using (new GUILayout.HorizontalScope())
        {
            WaveformModeButton("overlay", ScopeWaveformMode.Overlay);
            WaveformModeButton("RGB parade", ScopeWaveformMode.Parade);
            WaveformModeButton("Luma", ScopeWaveformMode.Luma);
        }

        GUILayout.Label("HISTOGRAM", SectionLabelStyle);
        using (new GUILayout.HorizontalScope())
        {
            HistogramModeButton("RGB + luma", 0);
            HistogramModeButton("luma", 1);
            HistogramModeButton("RGB", 2);
        }
    }

    private static void ScopeSourceButton(string label, ScopesSourceMode mode)
    {
        bool selected = ScopesRenderPass.SourceMode == mode;
        bool toggled = GUILayout.Toggle(selected, label, ToolTheme.SegmentedButton);
        if (toggled && !selected)
        {
            ScopesRenderPass.SourceMode = mode;
        }
    }

    private static void WaveformModeButton(string label, ScopeWaveformMode mode)
    {
        bool selected = ScopesRenderPass.WaveformMode == mode;
        bool toggled = GUILayout.Toggle(selected, label, ToolTheme.SegmentedButton);
        if (toggled && !selected)
        {
            ScopesRenderPass.WaveformMode = mode;
        }
    }

    private static void HistogramModeButton(string label, int mode)
    {
        bool selected = ScopesRenderPass.HistogramMode == mode;
        bool toggled = GUILayout.Toggle(selected, label, ToolTheme.SegmentedButton);
        if (toggled && !selected)
        {
            ScopesRenderPass.HistogramMode = mode;
        }
    }

    private static void CompareModeButton(string label, CompareMode mode)
    {
        bool selected = PostProcessRuntimeState.CompareMode == mode;
        bool toggled = GUILayout.Toggle(selected, label, ToolTheme.SegmentedButton);
        if (toggled && !selected)
        {
            PostProcessRuntimeState.CompareMode = mode;
            if (mode is CompareMode.VerticalWipe or CompareMode.HorizontalWipe)
            {
                PostProcessRuntimeState.CompareSplit = 0.5f;
            }
            else if (mode == CompareMode.Off)
            {
                PostProcessRuntimeState.CompareSplit = 0f;
                PostProcessRuntimeState.CompareBefore = false;
            }
        }
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
