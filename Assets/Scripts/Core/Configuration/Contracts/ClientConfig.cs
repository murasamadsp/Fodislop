#nullable enable

using System;
using Kern.Rendering;

namespace Kern.Core;
[Serializable]
public class ClientConfig
{
    // 31: DisplaySettings.HDRSwitchPending — метка безопасного старта
    // режима вывода.
    public const int CurrentSchemaVersion = 31;

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
