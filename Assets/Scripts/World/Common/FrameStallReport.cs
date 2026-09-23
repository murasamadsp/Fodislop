#nullable enable

using System;
using System.Text;
using Kern.Core.Interfaces.Diagnostics;
using Unity.Profiling;
using UnityEngine;

namespace Kern.Core;

/// <summary>
/// Разбор кадра, который провис целиком, — не только террейн.
/// </summary>
///
/// TerrainStall видит лишь LateUpdate террейна. Когда тяжёлая работа уходит в
/// другую часть кадра (загрузка текстур в Update, сборка мусора, ожидание
/// потока рендера), фриз остаётся на экране, а из лога пропадает. Этот отчёт
/// меряет весь кадр маркерами профайлера и печатает самый дорогой кадр за
/// интервал с разбивкой по участкам, от дорогих к дешёвым.
///
/// Участки частично вложены (скрипты содержат LateUpdate, LateUpdate содержит
/// террейн и свет), поэтому они не складываются: это список подозреваемых, а
/// не бухгалтерия. Маркер, которого в сборке нет, просто не печатается.
public sealed class FrameStallReport : IDisposable
{
    private const double BudgetMilliseconds = 25.0;

    // Провис — это кадр заметно дольше обычного, а не любой кадр над
    // бюджетом: в редакторе обычный кадр сам бывает за 30 мс, и такие строки
    // топили бы настоящие фризы.
    private const double SpikeOverBaseline = 1.6;
    private const float IntervalSeconds = 2f;
    private const int ShownProbes = 14;

    private static readonly (string Label, ProfilerCategory Category, string Marker)[] _Probes =
    [
        ("главный поток", ProfilerCategory.Internal, "Main Thread"),
        ("цикл игры", ProfilerCategory.Internal, "PlayerLoop"),
        ("цикл редактора", ProfilerCategory.Internal, "EditorLoop"),

        // Верхние фазы цикла игры покрывают его целиком: провис вне скриптов
        // ложится ровно в одну из них.
        ("фаза Initialization", ProfilerCategory.Internal, "Initialization"),
        ("фаза EarlyUpdate", ProfilerCategory.Internal, "EarlyUpdate"),
        ("фаза FixedUpdate", ProfilerCategory.Internal, "FixedUpdate"),
        ("фаза PreUpdate", ProfilerCategory.Internal, "PreUpdate"),
        ("фаза Update", ProfilerCategory.Internal, "Update"),
        ("фаза PreLateUpdate", ProfilerCategory.Internal, "PreLateUpdate"),
        ("фаза PostLateUpdate", ProfilerCategory.Internal, "PostLateUpdate"),
        ("цикл рендера URP", ProfilerCategory.Render, "RenderPipelineManager.DoRenderLoop_Internal()|UniversalRenderPipeline.RenderCameraStack"),
        ("камера URP", ProfilerCategory.Render, "UniversalRenderPipeline.RenderSingleCameraInternal|Inl_UniversalRenderPipeline.RenderSingleCameraInternal|RenderSingleCamera"),
        ("запись RenderGraph", ProfilerCategory.Render, "RecordRenderGraph|RenderGraph.RecordRenderGraph"),
        ("исполнение RenderGraph", ProfilerCategory.Render, "ExecuteRenderGraph|RenderGraph.Execute"),
        ("физика 2D", ProfilerCategory.Physics, "Physics2D.Simulate|Physics2D.FixedUpdate"),
        ("анимация", ProfilerCategory.Animation, "Director.ProcessFrame|PreLateUpdate.DirectorUpdateAnimationBegin"),
        ("скрипты Update", ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate"),
        ("скрипты LateUpdate", ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
        ("корутины", ProfilerCategory.Scripts, "Update.ScriptRunDelayedDynamicFrameRate"),
        ("UniTask Update", ProfilerCategory.Scripts, "UniTaskLoopRunnerUpdate"),
        ("UniTask Yield", ProfilerCategory.Scripts, "UniTaskLoopRunnerYieldUpdate"),
        ("сборка мусора", ProfilerCategory.Memory, "GC.Collect"),
        ("рендер кадра", ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering|PostLateUpdate.PresentAfterDraw"),
        ("ожидание потока рендера", ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
        ("ожидание команд рендера", ProfilerCategory.Render, "Gfx.WaitForGfxCommandsFromMainThread"),
        ("вывод кадра", ProfilerCategory.Render, "Gfx.PresentFrame"),
        ("террейн", ProfilerCategory.Scripts, "Kern.Terrain.LateUpdate.CPU"),
        ("свет", ProfilerCategory.Scripts, "Kern.Lighting.UpdateLighting.CPU"),
        ("поверхность", ProfilerCategory.Scripts, "Kern.Surface.LateUpdate"),
        ("сущности мира", ProfilerCategory.Scripts, "Kern.WorldEntities.LateUpdate"),
        ("постпроцесс", ProfilerCategory.Scripts, "Kern.PostProcess.LateUpdate"),
        ("сеть — разбор очереди", ProfilerCategory.Scripts, "Kern.Net.DrainPacketQueue"),
        ("текстуры — декодирование", ProfilerCategory.Scripts, "Kern.Textures.Decode"),
        ("текстуры — в атлас", ProfilerCategory.Scripts, "Kern.Textures.AtlasAdd"),
        ("UI Toolkit — панели", ProfilerCategory.Gui, "UIElementsUpdateRuntimePanels"),

        // Поток рендера: рекордер без CollectOnlyOnCurrentThread суммирует
        // маркер по всем потокам. Компиляция шейдера при первом показе
        // варианта — частая причина ожидания потока рендера в редакторе.
        ("рендер: команды", ProfilerCategory.Render, "Gfx.ProcessCommands"),
        ("рендер: компиляция шейдера", ProfilerCategory.Render, "Shader.CreateGPUProgram"),
        ("рендер: разбор шейдера", ProfilerCategory.Render, "Shader.Parse"),
        ("загрузка текстуры на GPU", ProfilerCategory.Render, "Texture.AwakeFromLoad"),
    ];

    // Работа, отданная рендеру, всплывает ожиданием через кадр-два: события
    // печатаются за столько кадров до провисшего.
    private const int EventLookbackFrames = 3;

    private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[_Probes.Length];
    private readonly double[] _worstValues = new double[_Probes.Length];
    private readonly int[] _order = new int[_Probes.Length];
    private readonly StringBuilder _text = new(512);
    private ProfilerRecorder _allocated;
    private long _worstAllocated;
    private double _worstFrameMs;
    private int _worstFrame;
    private float _nextReportTime;
    private double _baselineFrameMs;

    public FrameStallReport()
    {
        for (int index = 0; index < _Probes.Length; index++)
        {
            _recorders[index] = StartFirstValid(_Probes[index].Category, _Probes[index].Marker);
        }

        _allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
    }

    /// <summary>
    /// Раз в кадр. Читает прошлый, уже закончившийся кадр: его длительность
    /// и последние значения маркеров.
    /// </summary>
    public void Tick()
    {
        double frameMs = Time.unscaledDeltaTime * 1000.0;
        double baseline = _baselineFrameMs;
        _baselineFrameMs = baseline <= 0.0
            ? frameMs
            : baseline + ((Math.Min(frameMs, baseline * 3.0) - baseline) * 0.05);
        bool spike = frameMs >= BudgetMilliseconds &&
            (baseline <= 0.0 || frameMs >= baseline * SpikeOverBaseline);
        if (spike && frameMs > _worstFrameMs)
        {
            _worstFrameMs = frameMs;
            for (int index = 0; index < _recorders.Length; index++)
            {
                _worstValues[index] = _recorders[index].Valid
                    ? _recorders[index].LastValue * 1e-6
                    : -1.0;
            }

            _worstAllocated = _allocated.Valid ? _allocated.LastValue : -1;
            _worstFrame = Time.frameCount - 1;
        }

        float now = Time.unscaledTime;
        if (_worstFrameMs < BudgetMilliseconds || now < _nextReportTime)
        {
            return;
        }

        _nextReportTime = now + IntervalSeconds;
        for (int index = 0; index < _order.Length; index++)
        {
            _order[index] = index;
        }

        Array.Sort(_order, (left, right) => _worstValues[right].CompareTo(_worstValues[left]));
        _text.Clear();
        _text.Append("[FrameStall] ").Append(_worstFrameMs.ToString("F1")).Append(" мс кадр (обычный ")
            .Append(_baselineFrameMs.ToString("F1")).Append(')');
        if (_worstAllocated >= 0)
        {
            _text.Append(" · аллокации ").Append(_worstAllocated / 1024).Append(" КБ");
        }

        int shown = 0;
        for (int rank = 0; rank < _order.Length && shown < ShownProbes; rank++)
        {
            int index = _order[rank];
            if (_worstValues[index] < 0.05)
            {
                continue;
            }

            _text.Append(" · ").Append(_Probes[index].Label).Append(' ')
                .Append(_worstValues[index].ToString("F1"));
            shown++;
        }

        _text.Append(" (участки вложены, не складываются)");
        if (FrameEventLog.AppendRange(_text, _worstFrame - EventLookbackFrames, _worstFrame) == 0)
        {
            _text.Append(" · тяжёлых событий за ").Append(EventLookbackFrames)
                .Append(" кадра до провиса не было");
        }

        Debug.LogWarning(_text.ToString());
        _worstFrameMs = 0.0;
    }

    // Имя маркера меняется между версиями движка: берётся первое из
    // перечисленных через '|', у которого рекордер действителен.
    private static ProfilerRecorder StartFirstValid(ProfilerCategory category, string markers)
    {
        string[] names = markers.Split('|');
        for (int index = 0; index < names.Length; index++)
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, names[index], 1);
            if (recorder.Valid || index == names.Length - 1)
            {
                return recorder;
            }

            recorder.Dispose();
        }

        return default;
    }

    public void Dispose()
    {
        for (int index = 0; index < _recorders.Length; index++)
        {
            _recorders[index].Dispose();
        }

        _allocated.Dispose();
    }
}
