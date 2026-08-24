#nullable enable

using Fodinae.Rendering;

namespace Fodinae.World.Lighting.Quality
{
    /// <summary>
    /// Resolves the lighting tier a <see cref="GraphicsQualitySettings"/> value
    /// actually runs at: <see cref="GraphicsPreset.Ultra"/> solves per-pixel
    /// rather than per-block, but never against an explicit
    /// <see cref="LightingQualityMode.Off"/>.
    /// </summary>
    public static class LightingQualityResolver
    {
        public static LightingQualityMode Resolve(
            GraphicsPreset preset,
            LightingQualityMode requested)
        {
            // Off is the player switching the subsystem off, and no preset
            // outranks that. Ultra used to, and the result was a trap: the
            // radiance-cascade solve is far and away the most expensive thing
            // in the frame - measured on this project at 46.9M ray-steps and
            // 69.8M atlas taps per solve - and on Ultra the one control that
            // turns it off silently did nothing. Somebody trying to make the
            // game playable would set lighting to Off, see no change, and have
            // no way to find out why.
            //
            // The lock's real purpose is to keep Ultra from quietly running the
            // cheaper per-block path, which is the enum's zero value and so the
            // one any older serialized settings deserialize to. That is about
            // PerBlock, not about Off.
            if (requested == LightingQualityMode.Off)
            {
                return LightingQualityMode.Off;
            }

            return preset == GraphicsPreset.Ultra
                ? LightingQualityMode.PerPixel
                : requested;
        }
    }
}
