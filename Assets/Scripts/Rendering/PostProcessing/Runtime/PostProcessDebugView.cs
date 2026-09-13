#nullable enable

namespace Fodinae.Rendering.PostProcessing;

public enum PostProcessDebugView
{
    None = 0,

    FalseColor = 1,

    Clipping = 2,

    GamutWarning = 3,

    LumaOnly = 4,

    SaturationOnly = 5,

    QualifierMatte = 6,

    SoloRed = 7,

    SoloGreen = 8,

    SoloBlue = 9,

    HighlightClipping = 10,

    ShadowClipping = 11,
}
