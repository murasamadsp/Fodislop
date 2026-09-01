// ФАЙЛ МАШИННЫЙ. Правки будут затёрты.
// Источник истины: visual/main-menu-mirror/css/tokens.css
// Генератор:       visual/main-menu-mirror/tools/emit-uss-tokens.py

namespace Fodinae.UI.Builders
{
    /// <summary>Палитра дизайн-системы для примагничивания серверных значений.</summary>
    public static class DesignTokens
    {
        public readonly struct Swatch
        {
            public readonly string Token;
            public readonly string UtilityClass;
            public readonly float R, G, B, A;

            public Swatch(string token, string utilityClass, float r, float g, float b, float a)
            {
                Token = token; UtilityClass = utilityClass;
                R = r; G = g; B = b; A = a;
            }
        }

        public static readonly Swatch[] Colors =
        {
            new Swatch("--accent-cyan", "bg-accent-cyan", 0.3373f, 0.8667f, 0.8314f, 1.0000f),
            new Swatch("--accent-cyan-deep", "bg-accent-cyan-deep", 0.1176f, 0.4392f, 0.4157f, 1.0000f),
            new Swatch("--accent-cyan-dense", "bg-accent-cyan-dense", 0.3373f, 0.8667f, 0.8314f, 0.8500f),
            new Swatch("--accent-cyan-fill", "bg-accent-cyan-fill", 0.3373f, 0.8667f, 0.8314f, 0.5500f),
            new Swatch("--accent-cyan-glow", "bg-accent-cyan-glow", 0.3373f, 0.8667f, 0.8314f, 0.3000f),
            new Swatch("--accent-cyan-hover", "bg-accent-cyan-hover", 0.5255f, 0.9294f, 0.9020f, 1.0000f),
            new Swatch("--accent-cyan-solid", "bg-accent-cyan-solid", 0.3373f, 0.8667f, 0.8314f, 1.0000f),
            new Swatch("--accent-cyan-tint", "bg-accent-cyan-tint", 0.3373f, 0.8667f, 0.8314f, 0.0800f),
            new Swatch("--accent-cyan-wash", "bg-accent-cyan-wash", 0.3373f, 0.8667f, 0.8314f, 0.1200f),
            new Swatch("--accent-gold", "bg-accent-gold", 0.9608f, 0.7725f, 0.2588f, 1.0000f),
            new Swatch("--accent-gold-fill", "bg-accent-gold-fill", 0.9608f, 0.7725f, 0.2588f, 0.5500f),
            new Swatch("--accent-gold-glow", "bg-accent-gold-glow", 0.9608f, 0.7725f, 0.2588f, 0.3000f),
            new Swatch("--accent-gold-hover", "bg-accent-gold-hover", 1.0000f, 0.8118f, 0.3020f, 1.0000f),
            new Swatch("--accent-gold-tint", "bg-accent-gold-tint", 0.9608f, 0.7725f, 0.2588f, 0.0800f),
            new Swatch("--accent-gold-wash", "bg-accent-gold-wash", 0.9608f, 0.7725f, 0.2588f, 0.1200f),
            new Swatch("--border-cyan", "bd-border-cyan", 0.3373f, 0.8667f, 0.8314f, 0.5500f),
            new Swatch("--border-danger", "bd-border-danger", 1.0000f, 0.3333f, 0.3333f, 0.4000f),
            new Swatch("--border-gold", "bd-border-gold", 0.9608f, 0.7725f, 0.2588f, 0.5500f),
            new Swatch("--border-hairline", "bd-border-hairline", 0.5490f, 0.7255f, 0.8039f, 0.0800f),
            new Swatch("--border-line", "bd-border-line", 0.5490f, 0.7255f, 0.8039f, 0.1500f),
            new Swatch("--border-strong", "bd-border-strong", 0.5490f, 0.7255f, 0.8039f, 0.1500f),
            new Swatch("--border-subtle", "bd-border-subtle", 0.5490f, 0.7255f, 0.8039f, 0.0800f),
            new Swatch("--focus-ring-color", null, 0.5255f, 0.9294f, 0.9020f, 1.0000f),
            new Swatch("--light-edge", "bg-light-edge", 1.0000f, 1.0000f, 1.0000f, 0.4500f),
            new Swatch("--light-film", "bg-light-film", 1.0000f, 1.0000f, 1.0000f, 0.0600f),
            new Swatch("--light-gleam", "bg-light-gleam", 1.0000f, 1.0000f, 1.0000f, 0.7000f),
            new Swatch("--light-sheen", "bg-light-sheen", 1.0000f, 1.0000f, 1.0000f, 0.1200f),
            new Swatch("--light-solid", "bg-light-solid", 1.0000f, 1.0000f, 1.0000f, 1.0000f),
            new Swatch("--rarity-common", "fg-rarity-common", 0.5216f, 0.5961f, 0.6392f, 1.0000f),
            new Swatch("--rarity-epic", "fg-rarity-epic", 0.6588f, 0.3333f, 0.9686f, 1.0000f),
            new Swatch("--rarity-legendary", "fg-rarity-legendary", 0.9608f, 0.7725f, 0.2588f, 1.0000f),
            new Swatch("--rarity-rare", "fg-rarity-rare", 0.3373f, 0.8667f, 0.8314f, 1.0000f),
            new Swatch("--rarity-uncommon", "fg-rarity-uncommon", 0.2902f, 0.8706f, 0.5020f, 1.0000f),
            new Swatch("--shadow-solid", null, 0.0000f, 0.0000f, 0.0000f, 1.0000f),
            new Swatch("--state-anomaly", "bg-state-anomaly", 0.6588f, 0.3333f, 0.9686f, 1.0000f),
            new Swatch("--state-anomaly-glow", "bg-state-anomaly-glow", 0.6588f, 0.3333f, 0.9686f, 0.4000f),
            new Swatch("--state-anomaly-wash", "bg-state-anomaly-wash", 0.6588f, 0.3333f, 0.9686f, 0.1500f),
            new Swatch("--state-danger", "bg-state-danger", 1.0000f, 0.3333f, 0.3333f, 1.0000f),
            new Swatch("--state-danger-glow", "bg-state-danger-glow", 1.0000f, 0.3333f, 0.3333f, 0.4000f),
            new Swatch("--state-danger-hover", "bg-state-danger-hover", 1.0000f, 0.4824f, 0.4824f, 1.0000f),
            new Swatch("--state-danger-wash", "bg-state-danger-wash", 1.0000f, 0.3333f, 0.3333f, 0.1500f),
            new Swatch("--state-magma-glow", "bg-state-magma-glow", 1.0000f, 0.4784f, 0.2118f, 0.4000f),
            new Swatch("--state-magma-tint", "bg-state-magma-tint", 1.0000f, 0.4784f, 0.2118f, 0.0800f),
            new Swatch("--state-ok", "bg-state-ok", 0.2902f, 0.8706f, 0.5020f, 1.0000f),
            new Swatch("--state-ok-tint", "bg-state-ok-tint", 0.2902f, 0.8706f, 0.5020f, 0.0800f),
            new Swatch("--state-ok-wash", "bg-state-ok-wash", 0.2902f, 0.8706f, 0.5020f, 0.0800f),
            new Swatch("--state-warn", "bg-state-warn", 1.0000f, 0.4784f, 0.2118f, 1.0000f),
            new Swatch("--state-warn-wash", "bg-state-warn-wash", 1.0000f, 0.4784f, 0.2118f, 0.0800f),
            new Swatch("--surface-abyss-dense", "bg-surface-abyss-dense", 0.0275f, 0.0510f, 0.0784f, 0.9000f),
            new Swatch("--surface-abyss-solid", "bg-surface-abyss-solid", 0.0275f, 0.0510f, 0.0784f, 1.0000f),
            new Swatch("--surface-abyss-veil", "bg-surface-abyss-veil", 0.0275f, 0.0510f, 0.0784f, 0.6000f),
            new Swatch("--surface-crisis-dense", "bg-surface-crisis-dense", 0.0627f, 0.0118f, 0.0118f, 0.9000f),
            new Swatch("--surface-crisis-solid", "bg-surface-crisis-solid", 0.0627f, 0.0118f, 0.0118f, 1.0000f),
            new Swatch("--surface-ember-solid", "bg-surface-ember-solid", 0.1020f, 0.0706f, 0.0235f, 1.0000f),
            new Swatch("--surface-inset", "bg-surface-inset", 0.0118f, 0.0196f, 0.0353f, 0.5000f),
            new Swatch("--surface-on-accent", "bg-surface-on-accent", 0.0157f, 0.0314f, 0.0549f, 1.0000f),
            new Swatch("--surface-panel", "bg-surface-panel", 0.0275f, 0.0510f, 0.0784f, 0.9000f),
            new Swatch("--surface-raised", "bg-surface-raised", 0.0431f, 0.0784f, 0.1176f, 0.6000f),
            new Swatch("--surface-scrim", "bg-surface-scrim", 0.0118f, 0.0196f, 0.0353f, 0.9000f),
            new Swatch("--surface-shelf-dense", "bg-surface-shelf-dense", 0.0667f, 0.1176f, 0.1804f, 0.9000f),
            new Swatch("--surface-shelf-solid", "bg-surface-shelf-solid", 0.0667f, 0.1176f, 0.1804f, 1.0000f),
            new Swatch("--surface-slate-dense", "bg-surface-slate-dense", 0.0431f, 0.0784f, 0.1176f, 0.9000f),
            new Swatch("--surface-slate-solid", "bg-surface-slate-solid", 0.0431f, 0.0784f, 0.1176f, 1.0000f),
            new Swatch("--surface-slate-veil", "bg-surface-slate-veil", 0.0431f, 0.0784f, 0.1176f, 0.6000f),
            new Swatch("--surface-solid", "bg-surface-solid", 0.0314f, 0.0627f, 0.0941f, 1.0000f),
            new Swatch("--surface-sunken", "bg-surface-sunken", 0.0118f, 0.0196f, 0.0353f, 0.6000f),
            new Swatch("--surface-void", "bg-surface-void", 0.0118f, 0.0196f, 0.0353f, 1.0000f),
            new Swatch("--surface-void-dense", "bg-surface-void-dense", 0.0118f, 0.0196f, 0.0353f, 0.9000f),
            new Swatch("--surface-void-haze", "bg-surface-void-haze", 0.0118f, 0.0196f, 0.0353f, 0.2000f),
            new Swatch("--surface-void-shade", "bg-surface-void-shade", 0.0118f, 0.0196f, 0.0353f, 0.5000f),
            new Swatch("--surface-void-solid", "bg-surface-void-solid", 0.0118f, 0.0196f, 0.0353f, 1.0000f),
            new Swatch("--surface-void-veil", "bg-surface-void-veil", 0.0118f, 0.0196f, 0.0353f, 0.6000f),
            new Swatch("--text-disabled", "fg-text-disabled", 0.3020f, 0.3882f, 0.4392f, 1.0000f),
            new Swatch("--text-on-gold", "fg-text-on-gold", 0.0157f, 0.0314f, 0.0549f, 1.0000f),
            new Swatch("--text-primary", "fg-text-primary", 0.9412f, 0.9647f, 0.9725f, 1.0000f),
            new Swatch("--text-secondary", "fg-text-secondary", 0.5216f, 0.5961f, 0.6392f, 1.0000f),
            new Swatch("--text-tertiary", "fg-text-tertiary", 0.3843f, 0.4824f, 0.5373f, 1.0000f),
        };

        /// <summary>Ступени шкалы пространства: значение в пикселях и класс утилиты.</summary>
        public static readonly (string Token, int Px)[] Space =
        {
            ("--space-1", 2),
            ("--space-2", 4),
            ("--space-3", 6),
            ("--space-4", 8),
            ("--space-5", 10),
            ("--space-6", 12),
            ("--space-7", 14),
            ("--space-8", 16),
            ("--space-9", 20),
            ("--space-10", 24),
            ("--space-11", 28),
            ("--space-12", 32),
            ("--space-13", 40),
            ("--space-14", 48),
        };
    }
}
