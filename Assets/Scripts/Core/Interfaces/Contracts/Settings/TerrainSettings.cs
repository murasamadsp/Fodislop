#nullable enable

using System;
using Kern.Rendering.PostProcessing;
using UnityEngine;

namespace Kern.Core;

[Serializable]
public sealed class TerrainSettings
{
    // Дефолти — авторський вигляд, єдиний дім у PostProcessLook.SurfaceLook.
    [SettingRange(0.001f, 1024f)]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.FlowScale")]
    public Vector2 FlowScale = PostProcessLook.SurfaceLook.FlowScale;

    [SettingRange(0f, 10f)]
    [SettingLabel("settings.world.shimmer_speed")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.ShimmerSpeedScale")]
    public float ShimmerSpeedScale = PostProcessLook.SurfaceLook.ShimmerSpeedScale;

    [SettingRange(0f, 10f)]
    [SettingLabel("settings.world.pulse_speed")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.PulseSpeedScale")]
    public float PulseSpeedScale = PostProcessLook.SurfaceLook.PulseSpeedScale;

    [SettingLabel("settings.world.shimmer_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.ShimmerColor")]
    public Color ShimmerColor = PostProcessLook.SurfaceLook.ShimmerColor;

    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.DebugColor")]
    public Color DebugColor = Color.magenta;

    [SettingUnbounded("Тумблер отладочной раскраски террейна.")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.DebugMode")]
    public bool DebugMode;

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

    // Тумблер каймы рельефа: затемнения к границам чужой рельефной семьи.
    // Выключенная кайма не убирает ни маску, ни транспорт — шейдер просто
    // перестаёт на неё умножать, поэтому переключение стоит кадра.
    [SettingUnbounded("Тумблер каймы рельефа на границах семей.")]
    [SettingLabel("settings.world.relief_rim")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainRenderer.ApplyClientConfig")]
    public bool EnableReliefRim = true;

    [SettingLabel("settings.world.surface_emission_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public Color TransitEmissionColor = PostProcessLook.SurfaceLook.TransitEmissionColor;

    [SettingRange(0f, 8f)]
    [SettingLabel("settings.world.surface_emission")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float TransitEmissionStrength = PostProcessLook.SurfaceLook.TransitEmissionStrength;

    [SettingLabel("settings.world.far_surface_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public Color PerspectiveEmissionColor = PostProcessLook.SurfaceLook.PerspectiveEmissionColor;

    [SettingRange(0f, 8f)]
    [SettingLabel("settings.world.far_surface_emission")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float PerspectiveEmissionStrength = PostProcessLook.SurfaceLook.PerspectiveEmissionStrength;

    [SettingRange(0f, 1f)]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float SurfaceOccupancy = PostProcessLook.SurfaceLook.SurfaceOccupancy;
}
