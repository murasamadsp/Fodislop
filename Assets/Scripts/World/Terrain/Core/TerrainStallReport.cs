#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Решение кадра и его цена — то, что надо знать про провис.</summary>
public readonly record struct TerrainStallFrame(
    bool Scrolled,
    Vector2Int ScrollDelta,
    Vector2Int Origin,
    Vector2Int Size,
    int DirtyRectCount,
    long DirtyArea,
    float RefreshTextureMs,
    float ProcessMs,
    float UploadMs,
    float ScrollMs,
    float IndexRemoveMs,
    float WarmupMs,
    float FillMs,
    int FilledCells,
    int UploadRectCount,
    long UploadTexels,
    float QuadMs,
    float PackMs,
    float StageMs,
    float StageCopyMs,
    float StageApplyMs,
    int UploadStrips,
    float PlanMs,
    float DimensionsMs);

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
    private float _worstFloodMs;
    private float _worstMeshMs;

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
            _worstFloodMs = telemetry.TerrainFloodFillTimeMs;
            _worstMeshMs = telemetry.TerrainMeshTimeMs;
        }

        float now = Time.unscaledTime;
        if (_worstMs < BudgetMs || now < _nextReportTime)
        {
            return;
        }

        _nextReportTime = now + IntervalSeconds;

        // «Прочее» — это разница между измеренным кадром и суммой стадий.
        // Крупное «прочее» означает, что провис не в перечисленных стадиях, и
        // искать надо снаружи: в приёме пакета, в хранилище, в освещении.
        float accounted = _worstCacheMs + _worstFloodMs + _worstMeshMs +
            _worstFrame.UploadMs + _worstFrame.RefreshTextureMs +
            _worstFrame.PlanMs + _worstFrame.DimensionsMs;
        Debug.LogWarning(
            $"[TerrainStall] {_worstMs:F1} мс · окно {_worstFrame.Size.x}×{_worstFrame.Size.y} " +
            $"в ({_worstFrame.Origin.x},{_worstFrame.Origin.y}) · " +
            $"{(_worstFrame.Scrolled ? $"сдвиг {_worstFrame.ScrollDelta.x},{_worstFrame.ScrollDelta.y}" : "полная сборка")} · " +
            $"кэш {_worstCacheMs:F1} · заливка {_worstFloodMs:F1} · тексели {_worstMeshMs:F1} · " +
            $"выгрузка {_worstFrame.UploadMs:F1} · текстуры типов {_worstFrame.RefreshTextureMs:F1} · " +
            $"процесс {_worstFrame.ProcessMs:F1} · план {_worstFrame.PlanMs:F1} · " +
            $"размеры {_worstFrame.DimensionsMs:F1} · прочее {_worstMs - accounted:F1} · " +
            $"[тексели: кольца {_worstFrame.ScrollMs:F1} · снятие с индекса {_worstFrame.IndexRemoveMs:F1} · " +
            $"прогрев {_worstFrame.WarmupMs:F1} · заливка {_worstFrame.FillMs:F1} на {_worstFrame.FilledCells} клеток " +
            $"(квады {_worstFrame.QuadMs:F1} · упаковка {_worstFrame.PackMs:F1})] · " +
            $"[выгрузка: {(_worstFrame.UploadRectCount == 0 ? "целиком" : _worstFrame.UploadRectCount + " прямоуг.")} " +
            $"{_worstFrame.UploadTexels} текселей · набивка {_worstFrame.StageMs:F1} " +
            $"(строки {_worstFrame.StageCopyMs:F1} · загрузка {_worstFrame.StageApplyMs:F1}) · " +
            $"полосок {_worstFrame.UploadStrips}] · " +
            $"заплаток {_worstFrame.DirtyRectCount} на {_worstFrame.DirtyArea} клеток · " +
            $"всего: полных {_worstFullPopulates}, заплаток {_worstPatches}, чанков {_worstChunkLoads}");

        _worstMs = 0f;
    }
}
