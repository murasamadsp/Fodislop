#nullable enable

using System;

namespace Kern.Core.Interfaces.Diagnostics;

/// <summary>Бюджет кадра и единственное определение провиса.</summary>
///
/// Провис определялся в пяти местах по-разному: фоновый отчёт сравнивал со
/// сглаженным средним, тест — с медианой, «Всплески» — в полтора раза и на
/// 4 мс, окна держали по своей копии 16.7 мс. Один и тот же кадр был провисом
/// в тесте и не был в отчёте. Теперь правило одно: кадр не короче
/// <see cref="StallMinimumMilliseconds"/> и в <see cref="StallOverBaseline"/>
/// раза дольше обычного (медианы).
public static class FrameBudget
{
    /// <summary>Целевой кадр — 60 Гц.</summary>
    public const double TargetFrameMilliseconds = 1000.0 / 60.0;

    /// <summary>Короче этого кадр глазу не заметен, как бы он ни вырос.</summary>
    public const double StallMinimumMilliseconds = 25.0;

    /// <summary>Во сколько раз провис дольше обычного кадра.</summary>
    public const double StallOverBaseline = 1.6;

    public static double StallThreshold(double baselineMilliseconds) =>
        Math.Max(StallMinimumMilliseconds, baselineMilliseconds * StallOverBaseline);

    public static bool IsStall(double frameMilliseconds, double baselineMilliseconds) =>
        frameMilliseconds >= StallThreshold(baselineMilliseconds);
}
