#nullable enable

using Kern.Rendering.PostProcessing.Scopes;

namespace Kern.Rendering.PostProcessing.Workbench;

// Option lists for the scopes workbench segmented rows. Data only: no Unity
// calls, no state. Lives apart so GradingScopesWindow stays under the file
// size limit.
internal static class GradingScopesOptions
{
    internal readonly record struct Option(string Label, int Value);

    internal static readonly Option[] DebugViewOptions =
    [
        new("обычный", (int)PostProcessDebugView.None),
        new("ложный цвет", (int)PostProcessDebugView.FalseColor),
        new("отсечка", (int)PostProcessDebugView.Clipping),
        new("highlights", (int)PostProcessDebugView.HighlightClipping),
        new("shadows", (int)PostProcessDebugView.ShadowClipping),
        new("gamut warning", (int)PostProcessDebugView.GamutWarning),
        new("luma", (int)PostProcessDebugView.LumaOnly),
        new("sat", (int)PostProcessDebugView.SaturationOnly),
        new("matte", (int)PostProcessDebugView.QualifierMatte),
        new("RGB", (int)PostProcessDebugView.RgbParade),
    ];

    internal static readonly Option[] CompareOptions =
    [
        new("выкл", (int)CompareMode.Off),
        new("верт. wipe", (int)CompareMode.VerticalWipe),
        new("гориз. wipe", (int)CompareMode.HorizontalWipe),
        new("сплит", (int)CompareMode.SideBySide),
        new("мигание", (int)CompareMode.Blink),
        new("разница", (int)CompareMode.Difference),
    ];

    internal static readonly Option[] ScopeKindOptions =
    [
        new("Waveform", (int)ScopeKind.Waveform),
        new("Vectorscope", (int)ScopeKind.Vectorscope),
        new("Histogram", (int)ScopeKind.Histogram),
    ];

    internal static readonly Option[] WaveformModeOptions =
    [
        new("RGB parade", (int)WaveformMode.RgbParade),
        new("Overlay", (int)WaveformMode.Overlay),
        new("Luma", (int)WaveformMode.Luma),
    ];

    internal static readonly Option[] HistogramModeOptions =
    [
        new("RGB", (int)HistogramMode.RgbOverlay),
        new("Luma", (int)HistogramMode.Luma),
    ];

    internal static readonly Option[] VectorscopeModeOptions =
    [
        new("Hue / Sat", (int)VectorscopeMode.HueSaturation),
    ];
}
