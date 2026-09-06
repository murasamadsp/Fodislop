#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Networking.Diagnostics;

/// <summary>
/// Учёт пакетов: что пришло от сервера, что ушло к нему и что осталось без обработчика.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Поток пакетов — единственная часть клиента, о которой до сих пор
/// нельзя было сказать вообще ничего. Пакет, у которого нет подписчика,
/// <see cref="NetworkService"/> молча отбрасывает: это правильное поведение
/// на бою и худшее из возможных при разборе, потому что «сервер не прислал» и
/// «прислал, но никто не слушает» выглядят с экрана одинаково. Здесь эти два
/// случая наконец различаются.
///
/// ПОЧЕМУ БЕЗ БЛОКИРОВОК. Приём складывается в очередь из сетевого потока, но
/// разбирается на главном: <c>ConnectionManager</c> сливает очередь в
/// <c>Update</c>, и вся раздача идёт оттуда. Отправка тоже с главного. Значит
/// запись однопоточная, и замок здесь был бы платой без покупки. Если разбор
/// когда-нибудь уедет в поток, это условие сломается тихо — потому оно и
/// записано.
///
/// ПОЧЕМУ ВЫКЛЮЧЕНО ПО УМОЛЧАНИЮ. Учёт стоит словаря на каждый пакет, а их в
/// секунду бывает много. Выключенный учёт — это одна проверка булева поля на
/// вызов; включает его окно, когда на него смотрят.
/// </remarks>
public static class PacketTelemetry
{
    /// <summary>Глубина ленты последних событий.</summary>
    public const int HistoryCapacity = 200;

    private static readonly Dictionary<string, PacketStat> _Incoming = [];
    private static readonly Dictionary<string, PacketStat> _Outgoing = [];
    private static readonly PacketEvent[] _History = new PacketEvent[HistoryCapacity];
    private static int _historyCursor;
    private static int _historyCount;
    private static double _windowStart;
    private static long _windowIncoming;
    private static long _windowOutgoing;

    /// <summary>Ведётся ли учёт. Выключенный стоит одной проверки.</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Сбрасывает состояние при запуске игры.
    /// </summary>
    /// <remarks>
    /// Статика переживает остановку и запуск, когда включён вход в игру без
    /// перезагрузки домена, — а он включён почти всегда, ради скорости. Без
    /// этого сброса учёт остался бы включённым с закрытым окном, лента
    /// продолжила бы наполняться, а счётчики показали бы смесь двух прогонов:
    /// числа выглядели бы настоящими и были бы неверны.
    /// </remarks>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        Enabled = false;
        Reset();
    }

    /// <summary>Сколько пакетов пришло всего, включая необработанные.</summary>
    public static long TotalIncoming { get; private set; }

    public static long TotalOutgoing { get; private set; }

    /// <summary>Пакеты, пришедшие без единого подписчика.</summary>
    public static long TotalUnhandled { get; private set; }

    /// <summary>Пачек HB и пакетов в них: сервер шлёт их слитно.</summary>
    public static long BatchCount { get; private set; }

    public static long BatchedPacketCount { get; private set; }

    /// <summary>Сжатых обёрток, снятых при разборе.</summary>
    public static long CompressedCount { get; private set; }

    /// <summary>Пакетов в секунду по последнему полному окну.</summary>
    public static double IncomingPerSecond { get; private set; }

    public static double OutgoingPerSecond { get; private set; }

    /// <summary>Сколько пакетов осталось в очереди после последнего разбора.</summary>
    public static int QueueDepth { get; private set; }

    /// <summary>Наибольшая замеченная глубина очереди.</summary>
    public static int PeakQueueDepth { get; private set; }

    /// <summary>Разборов, оборванных бюджетом кадра, и разборов, оборванных потолком партии.</summary>
    public static long BudgetStopCount { get; private set; }

    public static long BatchCapStopCount { get; private set; }

    public static void RecordIncoming(Type packetType, bool handled, double timeSeconds)
    {
        if (!Enabled)
        {
            return;
        }

        TotalIncoming++;
        _windowIncoming++;
        if (!handled)
        {
            TotalUnhandled++;
        }

        Accumulate(_Incoming, packetType.Name, handled, timeSeconds);
        PushHistory(new PacketEvent(packetType.Name, Incoming: true, handled, timeSeconds));
    }

    public static void RecordOutgoing(Type packetType, double timeSeconds)
    {
        if (!Enabled)
        {
            return;
        }

        TotalOutgoing++;
        _windowOutgoing++;
        Accumulate(_Outgoing, packetType.Name, handled: true, timeSeconds);
        PushHistory(new PacketEvent(packetType.Name, Incoming: false, Handled: true, timeSeconds));
    }

    /// <summary>
    /// Состояние очереди после разбора за кадр.
    /// </summary>
    /// <remarks>
    /// Разбор идёт по бюджету: доля времени кадра и потолок числа пакетов за
    /// раз, чтобы всплеск не вешал кадр. Значит очередь может не разобраться до
    /// конца, и тогда пакет, отправленный сервером, доедет до обработчика
    /// кадром позже или ещё дальше. Снаружи это выглядит как «сервер тормозит»,
    /// хотя сервер тут ни при чём, — потому обрыв разбора и считается отдельно
    /// от глубины очереди.
    /// </remarks>
    public static void RecordQueueState(int depth, bool stoppedByBudget, bool stoppedByCap)
    {
        if (!Enabled)
        {
            return;
        }

        QueueDepth = depth;
        PeakQueueDepth = Math.Max(PeakQueueDepth, depth);
        if (stoppedByBudget)
        {
            BudgetStopCount++;
        }

        if (stoppedByCap)
        {
            BatchCapStopCount++;
        }
    }

    /// <summary>
    /// Закрывает секундное окно и пересчитывает частоты.
    /// </summary>
    /// <remarks>
    /// Зовётся окном, а не записью: без этого частота замерла бы на последнем
    /// значении, как только поток прекратился, и «ничего не идёт» выглядело бы
    /// как «идёт ровно столько же». Считать надо и тогда, когда не случилось
    /// ничего.
    /// </remarks>
    public static void Poll(double nowSeconds)
    {
        if (_windowStart <= 0d)
        {
            _windowStart = nowSeconds;
            return;
        }

        double elapsed = nowSeconds - _windowStart;
        if (elapsed < 1d)
        {
            return;
        }

        IncomingPerSecond = _windowIncoming / elapsed;
        OutgoingPerSecond = _windowOutgoing / elapsed;
        _windowIncoming = 0;
        _windowOutgoing = 0;
        _windowStart = nowSeconds;
    }

    public static void RecordBatch(int packetCount)
    {
        if (!Enabled)
        {
            return;
        }

        BatchCount++;
        BatchedPacketCount += packetCount;
    }

    public static void RecordCompressed()
    {
        if (Enabled)
        {
            CompressedCount++;
        }
    }

    public static void Reset()
    {
        _Incoming.Clear();
        _Outgoing.Clear();
        Array.Clear(_History, 0, _History.Length);
        _historyCursor = 0;
        _historyCount = 0;
        TotalIncoming = 0;
        TotalOutgoing = 0;
        TotalUnhandled = 0;
        BatchCount = 0;
        BatchedPacketCount = 0;
        CompressedCount = 0;
        IncomingPerSecond = 0d;
        OutgoingPerSecond = 0d;
        QueueDepth = 0;
        PeakQueueDepth = 0;
        BudgetStopCount = 0;
        BatchCapStopCount = 0;
        _windowStart = 0d;
        _windowIncoming = 0;
        _windowOutgoing = 0;
    }

    /// <summary>Копирует сводку по типам в список вызывающего.</summary>
    public static void CollectIncoming(List<PacketStat> destination) => Copy(_Incoming, destination);

    public static void CollectOutgoing(List<PacketStat> destination) => Copy(_Outgoing, destination);

    /// <summary>
    /// Копирует ленту от свежих к старым.
    /// </summary>
    /// <remarks>
    /// Порядок обратный намеренно: смотрят всегда в конец, а не в начало, и
    /// прокручивать двести строк ради последней — не разбор, а работа руками.
    /// </remarks>
    public static void CollectHistory(List<PacketEvent> destination)
    {
        destination.Clear();
        for (int i = 0; i < _historyCount; i++)
        {
            int index = (_historyCursor - 1 - i + HistoryCapacity * 2) % HistoryCapacity;
            destination.Add(_History[index]);
        }
    }

    private static void Accumulate(
        Dictionary<string, PacketStat> target,
        string name,
        bool handled,
        double timeSeconds)
    {
        target.TryGetValue(name, out PacketStat stat);
        target[name] = new PacketStat(
            name,
            stat.Count + 1,
            handled ? stat.UnhandledCount : stat.UnhandledCount + 1,
            timeSeconds);
    }

    private static void PushHistory(PacketEvent entry)
    {
        _History[_historyCursor] = entry;
        _historyCursor = (_historyCursor + 1) % HistoryCapacity;
        _historyCount = Math.Min(_historyCount + 1, HistoryCapacity);
    }

    private static void Copy(Dictionary<string, PacketStat> source, List<PacketStat> destination)
    {
        destination.Clear();
        foreach (PacketStat stat in source.Values)
        {
            destination.Add(stat);
        }
    }
}

/// <summary>Сводка по одному типу пакета.</summary>
public readonly record struct PacketStat(
    string Name,
    long Count,
    long UnhandledCount,
    double LastSeenSeconds);

/// <summary>Одно событие ленты.</summary>
public readonly record struct PacketEvent(
    string Name,
    bool Incoming,
    bool Handled,
    double TimeSeconds);
