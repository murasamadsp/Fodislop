#nullable enable

using System;
using Kern.Rendering;

namespace Kern.Core;
[Serializable]
public class ClientConfig
{
    // 32: TerrainSettings.EnableReliefRim.
    // 33: TerrainSettings.DistortionStyle.
    // Схемы 31 и 32 мигрируются штатным загрузчиком с созданием backup.
    public const int CurrentSchemaVersion = 33;

    public int SchemaVersion;
    public AudioSettings Audio = new();
    public DisplaySettings Display = new();
    public InterfaceSettings Interface = new();
    public AccessibilitySettings Accessibility = new();
    public ConnectionSettings Connection = new();
    public PostProcessSettings PostProcess = new();
    public TerrainSettings Terrain = new();
    public EffectSettings Effects = new();

    public GraphicsPreset GraphicsPreset = GraphicsPreset.High;
    public GraphicsQualitySettings GraphicsQualitySettings;
}
