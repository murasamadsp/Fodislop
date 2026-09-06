#nullable enable

using System;
using Unity.Profiling;

namespace Fodinae.Tools.Imgui.Profiling;

/// <summary>
/// Один именованный участок кадра, снятый через <see cref="ProfilerRecorder"/>.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Разметка по горячим местам стоит в коде давно: около полутора
/// десятков <see cref="ProfilerMarker"/> на стороне CPU и дюжина именованных
/// участков командного буфера на стороне GPU. До сих пор их не читал никто, и
/// вопрос «во что обходится кадр, когда освещение выключено» приходилось
/// решать чтением кода вместо чтения чисел.
///
/// ПОЧЕМУ КАТЕГОРИЯ ПОДБИРАЕТСЯ. У профилировщика имя участка живёт внутри
/// категории, и категория зависит от того, чем участок объявлен:
/// <c>new ProfilerMarker(name)</c> по умолчанию попадает в скрипты, а
/// <c>CommandBuffer.BeginSample</c> — в отрисовку. Держать это соответствие
/// списком руками значит гарантированно разойтись с кодом при первой же
/// перестановке. Дешевле попробовать несколько категорий и оставить ту, где
/// участок нашёлся.
///
/// ПОЧЕМУ БЫВАЕТ НЕДОСТУПНО. Счётчики существуют только когда в сборке
/// определён <c>ENABLE_PROFILER</c> — он есть в Instrumented, Checked и Debug,
/// но не в Release. Недоступный участок обязан честно сказать, что его нет:
/// ноль вместо отсутствующего числа — это ложь, по которой принимают решения.
/// </remarks>
public sealed class FrameProbe : IDisposable
{
    /// <summary>Глубина окна усреднения в кадрах.</summary>
    private const int SampleCapacity = 20;

    /// <summary>
    /// Категории в порядке проверки.
    /// </summary>
    /// <remarks>
    /// Ровно две, и обе точно соответствуют тому, чем размечен код: участки
    /// командного буфера попадают в отрисовку, <see cref="ProfilerMarker"/> без
    /// явной категории — в скрипты. Третьей «на всякий случай» здесь нет:
    /// проверить её в этом окружении нечем, а недоказанная строка в списке
    /// однажды притворится объяснением, почему участок не нашёлся.
    /// </remarks>
    private static readonly ProfilerCategory[] _Candidates =
    [
        ProfilerCategory.Render,
        ProfilerCategory.Scripts,
    ];

    private ProfilerRecorder _recorder;

    public FrameProbe(string title, string markerName, bool isDetail = false)
    {
        Title = title;
        MarkerName = markerName;
        IsDetail = isDetail;
    }

    public string Title { get; }

    /// <summary>
    /// Часть ли это участка, объявленного выше.
    /// </summary>
    /// <remarks>
    /// Признак объявлен полем, а не выводится из точки в начале подписи.
    /// Подпись — это текст для человека, и однажды её перепишут; сортировка,
    /// разбирающая текст, сломается молча и в тот же день.
    /// </remarks>
    public bool IsDetail { get; }

    public string MarkerName { get; }

    public bool Available => _recorder.Valid;

    /// <summary>Последнее значение в миллисекундах, или ноль, если участок не найден.</summary>
    public double LastMilliseconds { get; private set; }

    /// <summary>Среднее по окну в миллисекундах.</summary>
    public double AverageMilliseconds { get; private set; }

    public void Start()
    {
        if (_recorder.Valid)
        {
            return;
        }

        foreach (ProfilerCategory category in _Candidates)
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                category,
                MarkerName,
                SampleCapacity);
            if (recorder.Valid)
            {
                _recorder = recorder;
                return;
            }

            recorder.Dispose();
        }
    }

    public void Stop()
    {
        if (_recorder.Valid)
        {
            _recorder.Dispose();
        }

        _recorder = default;
        LastMilliseconds = 0d;
        AverageMilliseconds = 0d;
    }

    /// <summary>
    /// Пересчитывает показания по окну кадров.
    /// </summary>
    /// <remarks>
    /// Среднее берётся по окну, а не по последнему кадру, потому что участки
    /// кадра шумят сильнее самого кадра: один и тот же проход может пропустить
    /// кадр целиком (свет решается не каждый кадр) и дать ноль там, где работы
    /// просто не было. Последнее значение остаётся рядом — по паре видно, идёт
    /// работа ровно или всплесками.
    /// </remarks>
    public void Sample()
    {
        if (!_recorder.Valid)
        {
            return;
        }

        LastMilliseconds = _recorder.LastValue / 1_000_000.0;

        int count = _recorder.Count;
        if (count <= 0)
        {
            AverageMilliseconds = 0d;
            return;
        }

        double total = 0d;
        for (int i = 0; i < count; i++)
        {
            total += _recorder.GetSample(i).Value;
        }

        AverageMilliseconds = total / count / 1_000_000.0;
    }

    public void Dispose()
    {
        Stop();
    }
}
