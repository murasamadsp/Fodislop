#nullable enable

using System.Collections.Generic;
using Unity.Profiling;

namespace Fodinae.Tools.Imgui.Profiling;

/// <summary>
/// Перечень участков кадра, за которыми имеет смысл смотреть.
/// </summary>
/// <remarks>
/// Имена здесь — не выдуманные ярлыки, а те самые строки, которыми размечен
/// код: <c>CommandBuffer.BeginSample</c> в конвейере света и постпроцесса и
/// <see cref="ProfilerMarker"/> в подсистемах на стороне CPU. Список и код
/// обязаны совпадать буквально; участок, которого нет, окно покажет как
/// отсутствующий, и это единственный способ заметить, что разметку
/// переименовали.
///
/// Порядок внутри групп — от общего к частному: сперва обёртка всего этапа,
/// потом его части. По сумме частей против обёртки видно, есть ли в этапе
/// работа, которую никто не разметил.
/// </remarks>
public static class FrameProbeCatalog
{
    /// <summary>Участки, записанные в командный буфер: работа видеокарты.</summary>
    public static List<FrameProbe> CreateGpuProbes() =>
    [
        new("Свет — весь блок", "Fodinae.RadianceCascades"),
        new("· поле материалов", "Fodinae.Lighting.MaterialField", isDetail: true),
        new("· сборка эмиссии", "Fodinae.Lighting.ComposeEmission", isDetail: true),
        new("· нормали", "Fodinae.Lighting.AutomaticNormals", isDetail: true),
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
    ];

    /// <summary>Участки на стороне процессора: подготовка кадра.</summary>
    public static List<FrameProbe> CreateCpuProbes() =>
    [
        new("Террейн — весь этап", "Fodinae.Terrain.LateUpdate.CPU"),
        new("· кеш клеток", "Fodinae.Terrain.Cache", isDetail: true),
        new("· предрасчёт", "Fodinae.Terrain.Precalculate", isDetail: true),
        new("· заливка фона", "Fodinae.Terrain.BackgroundFloodFill", isDetail: true),
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

    /// <summary>
    /// Встроенные счётчики Unity.
    /// </summary>
    /// <remarks>
    /// Часть из них есть не на каждой платформе и не в каждой сборке. Набор
    /// подобран так, чтобы отвечать на конкретные вопросы этого проекта:
    /// сколько стоит один сабмеш на атлас у террейна, во что обходится
    /// десяток render target'ов освещения и растёт ли число вызовов при
    /// движении.
    /// </remarks>
    public static List<FrameCounter> CreateCounters() =>
    [
        new("Вызовов отрисовки", "Draw Calls Count", ProfilerCategory.Render),
        new("Смен материала", "SetPass Calls Count", ProfilerCategory.Render),
        new("Пакетов", "Batches Count", ProfilerCategory.Render),
        new("Треугольников", "Triangles Count", ProfilerCategory.Render),
        new("Вершин", "Vertices Count", ProfilerCategory.Render),
        new("Render target'ов", "Render Textures Count", ProfilerCategory.Memory),
        new("Память render target'ов", "Render Textures Bytes", ProfilerCategory.Memory, isBytes: true),
        new("Память текстур", "Texture Memory", ProfilerCategory.Memory, isBytes: true),
    ];
}
