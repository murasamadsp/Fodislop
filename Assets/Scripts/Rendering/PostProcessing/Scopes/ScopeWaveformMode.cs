#nullable enable

namespace Fodinae.Rendering.PostProcessing.Scopes;

internal enum ScopeWaveformMode
{
    /// <summary>RGB-каналы поверх друг друга.</summary>
    Overlay = 0,

    /// <summary>Три RGB-парада рядом.</summary>
    Parade = 1,

    /// <summary>Отдельный luma waveform.</summary>
    Luma = 2,
}
