#nullable enable

using System.Collections.Generic;
using Unity.Profiling;

namespace Fodinae.Tools.Imgui.Profiling;

public static class FrameProbeCatalog
{
    public static List<FrameProbe> CreateGpuProbes() =>
    [
        new("Свет — весь блок", "Fodinae.RadianceCascades"),
        new("· поле материалов", "Fodinae.Lighting.MaterialField", isDetail: true),
        new("· сборка эмиссии", "Fodinae.Lighting.ComposeEmission", isDetail: true),
        new("· статическая половина", "Fodinae.Lighting.StaticRadiance", isDetail: true),
        new("· динамическая половина", "Fodinae.Lighting.DynamicRadiance", isDetail: true),
        new("· каскады", "Fodinae.Lighting.RadianceCascades", isDetail: true),
        new("· композит", "Fodinae.Lighting.Composite", isDetail: true),
        new("Террейн — поля", "Fodinae.Terrain.RenderMaterialFields"),
        new("Постпроцесс — композит", "Fodinae.PostProcess.Composite"),
        new("· блум, префильтр", "Fodinae.PostProcess.Bloom.Prefilter", isDetail: true),
        new("· блум, вниз", "Fodinae.PostProcess.Bloom.Downsample", isDetail: true),
        new("· блум, вверх", "Fodinae.PostProcess.Bloom.Upsample", isDetail: true),
        new("· возврат в кадр", "Fodinae.PostProcess.BlitBack", isDetail: true),
        new("· копия истории", "Fodinae.PostProcess.HistoryCopy", isDetail: true),
    ];

    public static List<FrameProbe> CreateCpuProbes() =>
    [
        new("Игровой цикл (PlayerLoop)", "PlayerLoop", category: ProfilerCategory.Internal),
        new("· Update скриптов", "Update.ScriptRunBehaviourUpdate", isDetail: true, category: ProfilerCategory.Scripts),
        new("· LateUpdate скриптов", "LateUpdate.ScriptRunBehaviourLateUpdate", isDetail: true, category: ProfilerCategory.Scripts),
        new("· отрисовка камер", "Camera.Render", isDetail: true, category: ProfilerCategory.Render),
        new("· интерфейс UI Toolkit", "RuntimePanel.Draw", isDetail: true, category: ProfilerCategory.Gui),
        new("· инструменты IMGUI", "GUI.Repaint", isDetail: true, category: ProfilerCategory.Gui),
        new("· ожидание видеокарты", "Gfx.WaitForCommands", isDetail: true, category: ProfilerCategory.Render),
        new("· ожидание вывода (Present)", "Gfx.WaitForPresentOnGfxThread", isDetail: true, category: ProfilerCategory.Render),
        new("· сборка мусора GC", "GarbageCollector.CollectIncremental", isDetail: true, category: ProfilerCategory.Memory),
        new("Террейн — весь этап", "Fodinae.Terrain.LateUpdate.CPU"),
        new("· кеш клеток", "Fodinae.Terrain.Cache", isDetail: true),
        new("· предрасчёт", "Fodinae.Terrain.Precalculate", isDetail: true),
        new("· заливка фона", "Fodinae.World.Terrain.BackgroundFloodFill", isDetail: true),
        new("· сборка меша", "Fodinae.Terrain.MeshBuild", isDetail: true),
        new("· заливка вершин", "Fodinae.Terrain.MeshUpload", isDetail: true),
        new("Свет — весь этап", "Fodinae.Lighting.UpdateLighting.CPU"),
        new("· запись команд", "Fodinae.Lighting.BuildCommands.CPU", isDetail: true),
        new("· выполнение команд", "Fodinae.Lighting.ExecuteCommands.CPU", isDetail: true),
        new("· загрузка источников", "Fodinae.Lighting.DynamicLights.Upload.CPU", isDetail: true),
        new("· запись каскадов", "Fodinae.Lighting.Cascades.Record.CPU", isDetail: true),
        new("· запись разрешения", "Fodinae.Lighting.Resolve.Record.CPU", isDetail: true),
        new("· запись композита", "Fodinae.Lighting.Composite.Record.CPU", isDetail: true),
        new("Поверхность", "Fodinae.Surface.LateUpdate"),
        new("Сущности мира", "Fodinae.WorldEntities.LateUpdate"),
        new("Постпроцесс", "Fodinae.PostProcess.LateUpdate"),
        new("Сеть — разбор очереди", "Fodinae.Net.DrainPacketQueue"),
    ];

    public static List<FrameCounter> CreateCounters() =>
    [
        new("Вызовов отрисовки", "Draw Calls Count", ProfilerCategory.Render, isBytes: false, "Draw Calls", "DrawCalls Count"),
        new("Смен материала", "SetPass Calls Count", ProfilerCategory.Render, isBytes: false, "SetPass Calls", "SetPasses Count"),
        new("Пакетов", "Batches Count", ProfilerCategory.Render, isBytes: false, "SRP Batches Count", "SRP Batches"),
        new("Треугольников", "Triangles Count", ProfilerCategory.Render, isBytes: false, "Triangles"),
        new("Вершин", "Vertices Count", ProfilerCategory.Render, isBytes: false, "Vertices"),
        new("Render target'ов", "Render Textures Count", ProfilerCategory.Memory, isBytes: false, "RenderTextures Count"),
        new("Память render target'ов", "Render Textures Bytes", ProfilerCategory.Memory, isBytes: true, "RenderTextures Bytes"),
        new("Память текстур", "Texture Memory", ProfilerCategory.Memory, isBytes: true),
    ];
}
