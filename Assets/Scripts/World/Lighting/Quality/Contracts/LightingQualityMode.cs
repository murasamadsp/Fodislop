#nullable enable

namespace Fodinae.World.Lighting.Quality;
public enum LightingQualityMode
{
    [Fodinae.Core.SettingLabel("settings.lighting.per_block")]
    PerBlock = 0,
    [Fodinae.Core.SettingLabel("settings.lighting.off")]
    Off = 1,
    [Fodinae.Core.SettingLabel("settings.lighting.per_pixel")]
    PerPixel = 2,

    [Fodinae.Core.SettingLabel("settings.lighting.per_pixel_bilinear")]
    PerPixelBilinearFix = 3,
}
