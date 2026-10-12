#nullable enable

namespace Kern.World.Terrain;

// Зеркало Assets/Shaders/Terrain/TerrainCellFormat.hlsl — только данные.
// Источник правды — шейдер: имя здесь — имя оттуда без KERN_TERRAIN_ в
// PascalCase, значение то же. TerrainCellFormatMirrorTests читает HLSL и
// роняет сборку, если хоть одна константа разошлась или пропала.
public static class TerrainCellFormat
{
    // Клетка.
    public const uint CellBits = 8u;
    public const uint CellsPerWord = 4u;
    public const uint CellTypeMask = 0xFFu;

    // Строка типа.
    public const float CellTexels = 32f;
    public const uint TypePixelMask = 0xFFFu;
    public const int TypePixelHighShift = 12;
    public const int TypeByteShift = 24;

    public const uint TypeSlotMask = 7u;
    public const uint TypeBackground = 1u << 3;
    public const uint TypeOpaqueOwn = 1u << 4;
    public const uint TypeOpaqueAny = 1u << 5;
    public const int TypeTextureAnchorShift = 6;
    public const uint TypeTextureAnchorMask = 1u;
    public const int TypeOutlineShift = 7;
    public const uint TypeOutlineMask = 7u;
    public const int TypeAnimationTypeShift = 10;
    public const uint TypeAnimationTypeMask = 3u;
    public const int TypeSurfaceEffectShift = 12;
    public const uint TypeSurfaceEffectMask = 3u;
    public const int TypeSurfaceEffectPaletteShift = 14;
    public const uint TypeSurfaceEffectPaletteMask = 7u;
    public const int TypeDecalAtlasShift = 17;
    public const uint TypeDecalAtlasMask = 3u;

    public const uint TypeAnimationSpeedMask = 0xFFFFu;
    public const int TypeGlowShift = 16;
    public const uint TypeGlowMask = 0xFFu;

    // Правило декали.
    public const uint DecalRulePercentMask = 0x7Fu;
    public const int DecalRuleSeedShift = 7;
    public const uint DecalRuleSeedMask = 0xFFu;
    public const uint DecalRuleRock = 1u << 15;

    // Код декали клетки.
    public const uint DecalVariants = 16u;
    public const int DecalRotationShift = 4;
    public const int DecalMirrorShift = 6;
    public const int DecalOffsetXShift = 7;
    public const int DecalOffsetYShift = 9;
    public const uint DecalRockAtlas = 1u << 12;

    // Стиль искажения.
    public const int DistortionStyleOff = 0;
    public const int DistortionStyleClassic = 1;
    public const int DistortionStyleOrganic = 2;

    // Флаги света.
    public const float GlowFractionScale = 0.25f;
}
