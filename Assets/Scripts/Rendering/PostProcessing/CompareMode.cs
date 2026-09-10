#nullable enable

namespace Fodinae.Rendering.PostProcessing;

/// <summary>Режим сравнения исходного и обработанного кадра.</summary>
public enum CompareMode
{
    Off = 0,
    VerticalWipe = 1,
    HorizontalWipe = 2,
    SideBySide = 3,
    AbToggle = 4,
}
