#nullable enable

using System;
using Fodinae.Rendering;
using UnityEngine.Serialization;

namespace Fodinae.Core;
[Serializable]
public class ClientConfig
{
    public const int CurrentSchemaVersion = 28;

    public int SchemaVersion;
    public AudioSettings Audio = new();
    public DisplaySettings Display = new();
    public InterfaceSettings Interface = new();
    public AccessibilitySettings Accessibility = new();
    public ConnectionSettings Connection = new();
    public PostProcessSettings PostProcess = new();
    public WorldLightingSettings Lighting = new();
    public TerrainSettings Terrain = new();
    public EffectSettings Effects = new();

    [FormerlySerializedAs("GraphicsQuality")]
    public GraphicsPreset GraphicsPreset = GraphicsPreset.High;
    public GraphicsQualitySettings GraphicsQualitySettings;
}
