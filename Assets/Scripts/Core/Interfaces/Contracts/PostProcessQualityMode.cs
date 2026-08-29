#nullable enable

namespace Fodinae.Rendering.PostProcessing
{
    /// <summary>
    /// How much of the post-processing stack a quality tier is allowed to run.
    /// </summary>
    /// <remarks>
    /// The tiers are split by cost, not by taste. Bloom is a nine-dispatch
    /// pyramid (five downsamples, four upsamples) and motion blur adds a
    /// velocity pass plus a multi-tap resolve; between them they are almost the
    /// whole cost of the stack. Vignette, chromatic aberration, colour grading
    /// and eigengrau are one full-screen pass each and stay on wherever
    /// post-processing runs at all.
    ///
    /// <see cref="Full"/> is the enum's zero value on purpose, for the same
    /// reason <c>LightingQualityMode.PerBlock</c> is: serialized
    /// <see cref="GraphicsQualitySettings"/> data predates this field, and
    /// deserializing it must reproduce what that data used to do - which was
    /// to run the entire stack - rather than silently switch effects off in
    /// somebody's saved config.
    /// </remarks>
    public enum PostProcessQualityMode
    {
        Full = 0,
        Off = 1,
        Essential = 2,
    }
}
