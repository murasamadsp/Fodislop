#nullable enable

using System;
using Kern.World.Terrain;
using UnityEngine;

namespace Kern.Core;

public enum TerrainDistortionStyle
{
    Classic = 0,
    Organic = 1,
}

[Serializable]
public sealed class TerrainSettings
{
    // Дефолти — авторський вигляд, єдиний дім у TerrainConfigHolder.
    [SettingRange(0.001f, 1024f)]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.FlowScale")]
    public Vector2 FlowScale = TerrainConfigHolder.FlowScale;

    [SettingRange(0f, 10f)]
    [SettingLabel("settings.world.shimmer_speed")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.ShimmerSpeedScale")]
    public float ShimmerSpeedScale = TerrainConfigHolder.ShimmerSpeedScale;

    [SettingRange(0f, 10f)]
    [SettingLabel("settings.world.blinking_speed")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.BlinkingSpeedScale")]
    public float BlinkingSpeedScale = TerrainConfigHolder.BlinkingSpeedScale;

    [SettingLabel("settings.world.shimmer_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.ShimmerColor")]
    public Color ShimmerColor = TerrainConfigHolder.ShimmerColor;

    // Смещение узлов сетки террейна. Название ключа говорит про кромку
    // блока — так было, пока смещались только границы массивов породы;
    // теперь колышется и их внутренность, как в оригинале Mines.
    //
    // По умолчанию включено: это и есть задуманный вид, а выключенное
    // состояние оставляет механическую решётку. Выключатель остаётся —
    // и в настройках графики, и в инструментах (F1).
    [SettingUnbounded("Тумблер искажения сетки террейна.")]
    [SettingLabel("settings.world.block_edge_distortion")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainBuildPipeline.EnableDistortion")]
    public bool EnableDistortion = true;

    [SettingUnbounded("Стиль смещения узлов; серверный тип Distortion не меняется.")]
    [SettingLabel("settings.world.distortion_style")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainBuildPipeline.DistortionStyle")]
    public TerrainDistortionStyle DistortionStyle = TerrainDistortionStyle.Organic;

    // Тумблер каймы: затемнения к границам чужой семьи каймы.
    // Выключенная кайма не убирает ни маску, ни транспорт — шейдер просто
    // перестаёт на неё умножать, поэтому переключение стоит кадра.
    [SettingUnbounded("Тумблер каймы на границах семей.")]
    [SettingLabel("settings.world.rim")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainRenderer.ApplyClientConfig")]
    public bool EnableRim = true;

    [SettingLabel("settings.world.surface_glow_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public Color TransitGlowColor = TerrainConfigHolder.TransitGlowColor;

    [SettingRange(0f, 8f)]
    [SettingLabel("settings.world.surface_glow")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float TransitGlowStrength = TerrainConfigHolder.TransitGlowStrength;

    [SettingLabel("settings.world.far_surface_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public Color PerspectiveGlowColor = TerrainConfigHolder.PerspectiveGlowColor;

    [SettingRange(0f, 8f)]
    [SettingLabel("settings.world.far_surface_glow")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float PerspectiveGlowStrength = TerrainConfigHolder.PerspectiveGlowStrength;

    [SettingRange(0f, 1f)]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float SurfaceOccupancy = TerrainConfigHolder.SurfaceOccupancy;
}
