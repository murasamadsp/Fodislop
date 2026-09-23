#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Решение кадра и его цена — то, что надо знать про провис.</summary>
///
/// Интервалы кадра делятся на независимые (план, размеры, процесс, выгрузка —
/// из них и считается «прочее») и вложенные в процесс (кэш, атласы). Фоновый
/// шаг не входит в кадр вовсе: его цифры — цена последнего опубликованного
/// шага на рабочем потоке, и в «прочее» они не вычитаются.
public readonly record struct TerrainStallFrame(
    bool Scrolled,
    Vector2Int ScrollDelta,
    Vector2Int Origin,
    Vector2Int Size,
    int DirtyRectCount,
    long DirtyArea,
    float ProcessMs,
    float UploadMs,
    int UploadRectCount,
    long UploadTexels,
    float StageMs,
    float StageCopyMs,
    float StageApplyMs,
    int UploadStrips,
    float PlanMs,
    float DimensionsMs,
    TerrainStallBuildState State,
    TerrainWorkerCost Worker);

/// <summary>Состояние фоновой сборки в кадре отчёта.</summary>
public readonly record struct TerrainStallBuildState(TerrainBuildState Build, bool InFlight);

/// <summary>Чем был шаг фоновой сборки.</summary>
public enum TerrainBuildStepKind
{
    None,
    Full,
    Scroll,
    Patch,
    Textures,
}

/// <summary>Цена последнего опубликованного шага на рабочем потоке.</summary>
public readonly record struct TerrainWorkerCost(
    TerrainBuildStepKind Kind,
    float CacheMs,
    float PrecalculateMs,
    float FloodFillMs,
    float MeshMs,
    float ScrollMs,
    float WarmupMs,
    float FillMs,
    int FilledCells,
    float QuadMs,
    float PackMs,
    float ElapsedMs,
    float LatencyMs);

/// <summary>
/// Печатает разбор кадра, в котором террейн съел больше бюджета.
/// </summary>
///
/// Зачем это в игре, а не в бенчмарке. Бенчмарк меряет процессорные стадии на
/// синтетических пещерах и показывает доли миллисекунды. Провис при догрузке
/// чанка живёт там, где бенчмарка нет: чтение настоящего хранилища,
/// разрешение метаданных с дозаказом текстур, выгрузка девяти каналов на GPU.
/// Без разбора по стадиям прямо в кадре причина назначается догадкой, а
/// догадка уже один раз стоила шестикратного падения fps.
///
/// Отчёт идёт в лог целиком одной строкой и не чаще раза в интервал: провис
/// обычно повторяется, и сто одинаковых строк ничего не добавляют. Зато самый
/// дорогой кадр за интервал сохраняется — печатается он, а не первый попавшийся.
public sealed class TerrainStallReport
{
    // Бюджет с запасом. Шаг конвейера на окне 256×160 стоит ~0.2 мс, полная
    // пересборка — единицы миллисекунд. Всё, что выше, — уже заметно глазу.
    private const float BudgetMs = 8f;
    private const float IntervalSeconds = 2f;

    private float _nextReportTime;
    private float _worstMs;
    private TerrainStallFrame _worstFrame;
    private int _worstFullPopulates;
    private int _worstPatches;
    private int _worstChunkLoads;
    private float _worstCacheMs;
    private float _worstAtlasMs;

    public static long Begin() => System.Diagnostics.Stopwatch.GetTimestamp();

    public static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

    public void Record(long startTimestamp, IFrameTelemetry telemetry, in TerrainStallFrame frame)
    {
        float totalMs = ElapsedMs(startTimestamp);
        if (totalMs > _worstMs)
        {
            _worstMs = totalMs;
            _worstFrame = frame;
            _worstFullPopulates = telemetry.TerrainFullPopulateCount;
            _worstPatches = telemetry.TerrainDirtyPatchCount;
            _worstChunkLoads = telemetry.TerrainChunkLoadCount;
            _worstCacheMs = telemetry.TerrainCacheTimeMs;
            _worstAtlasMs = telemetry.TerrainAtlasUploadTimeMs;
        }

        float now = Time.unscaledTime;
        if (_worstMs < BudgetMs || now < _nextReportTime)
        {
            return;
        }

        _nextReportTime = now + IntervalSeconds;

        // «Прочее» — это разница между измеренным кадром и суммой
        // независимых интервалов. Кэш и атласы идут внутри процесса и второй
        // раз не вычитаются. Крупное «прочее» означает, что провис не в
        // перечисленных стадиях, и искать надо снаружи: в приёме пакета, в
        // хранилище, в освещении.
        TerrainWorkerCost worker = _worstFrame.Worker;
        float accounted = _worstFrame.PlanMs + _worstFrame.DimensionsMs +
            _worstFrame.ProcessMs + _worstFrame.UploadMs;
        Debug.LogWarning(
            $"[TerrainStall] {_worstMs:F1} мс · окно {_worstFrame.Size.x}×{_worstFrame.Size.y} " +
            $"в ({_worstFrame.Origin.x},{_worstFrame.Origin.y}) · " +
            $"сборка {_worstFrame.State.Build}{(_worstFrame.State.InFlight ? " (идёт)" : string.Empty)} · " +
            $"план {_worstFrame.PlanMs:F1} · размеры {_worstFrame.DimensionsMs:F1} · " +
            $"процесс {_worstFrame.ProcessMs:F1} (кэш {_worstCacheMs:F1} · атласы {_worstAtlasMs:F1}) · " +
            $"выгрузка {_worstFrame.UploadMs:F1} · прочее {_worstMs - accounted:F1} · " +
            $"[выгрузка: {(_worstFrame.UploadRectCount == 0 ? "целиком" : _worstFrame.UploadRectCount + " прямоуг.")} " +
            $"{_worstFrame.UploadTexels} текселей · набивка {_worstFrame.StageMs:F1} " +
            $"(строки {_worstFrame.StageCopyMs:F1} · загрузка {_worstFrame.StageApplyMs:F1}) · " +
            $"полосок {_worstFrame.UploadStrips}] · " +
            $"заплаток {_worstFrame.DirtyRectCount} на {_worstFrame.DirtyArea} клеток · " +
            $"[фон, вне кадра: последний шаг " +
            $"{StepLabel(worker.Kind, _worstFrame.ScrollDelta)} · " +
            $"{worker.ElapsedMs:F1} мс на потоке, до показа {worker.LatencyMs:F1} мс · " +
            $"кэш {worker.CacheMs:F1} · предрасчёт {worker.PrecalculateMs:F1} · заливка фона {worker.FloodFillMs:F1} · тексели {worker.MeshMs:F1} " +
            $"(кольца {worker.ScrollMs:F1} · прогрев {worker.WarmupMs:F1} · заливка {worker.FillMs:F1} на {worker.FilledCells} клеток; " +
            $"сумма по потокам: квады {worker.QuadMs:F1} · упаковка {worker.PackMs:F1})] · " +
            $"всего: полных {_worstFullPopulates}, заплаток {_worstPatches}, чанков {_worstChunkLoads}");

        _worstMs = 0f;
    }

    private static string StepLabel(TerrainBuildStepKind kind, Vector2Int delta) => kind switch
    {
        TerrainBuildStepKind.Full => "полная сборка",
        TerrainBuildStepKind.Scroll => $"сдвиг {delta.x},{delta.y}",
        TerrainBuildStepKind.Patch => "заплатки",
        TerrainBuildStepKind.Textures => "перечитывание текстур",
        _ => "нет",
    };
}
