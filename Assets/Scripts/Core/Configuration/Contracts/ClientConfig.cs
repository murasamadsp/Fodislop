#nullable enable

using System;
using Kern.Rendering;

namespace Kern.Core;
[Serializable]
public class ClientConfig
{
    // 32: TerrainSettings.EnableReliefRim.
    // 33: TerrainSettings.DistortionStyle.
    // 34: две ступени качества (Стандарт/Overdrive) вместо шести, без Custom.
    // 35: EffectSettings.BloomVariant.
    // 36: перебиндиваемые клавиши InterfaceSettings.Key* (вкладка «Управление»).
    // 37: InterfaceSettings.KeyAggression (агрессия перебиндивается).
    // 38: DisplaySettings.HDRSwitchPending для подтверждения смены режима дисплея.
    // 39: DisplaySettings.PeakBrightnessFromDisplay — пик с дисплея до ручной настройки.
    // 40: TerrainSettings EnableReliefRim → EnableRim, *Emission* → *Glow*,
    //     PulseSpeedScale → BlinkingSpeedScale.
    // Схемы 31–39 мигрируются штатным загрузчиком с созданием backup.
    public const int CurrentSchemaVersion = 40;

    public int SchemaVersion;
    public AudioSettings Audio = new();
    public DisplaySettings Display = new();
    public InterfaceSettings Interface = new();
    public ConnectionSettings Connection = new();
    public TerrainSettings Terrain = new();
    public EffectSettings Effects = new();

    public GraphicsPreset GraphicsPreset = GraphicsPreset.Standard;
    public GraphicsQualitySettings GraphicsQualitySettings;
}
