#nullable enable

using System;

namespace Fodinae.Networking.Connection
{
    /// <summary>
    /// Экспоненциальный backoff реконнекта с капом:
    /// 1s → 2s → 4s → 8s → 16s → 30s → 30s ...
    /// </summary>
    public sealed class ReconnectBackoff
    {
        private static readonly float[] Steps = [1f, 2f, 4f, 8f, 16f, 30f];

        private int _attempt;

        /// <summary>Количество зафиксированных неудачных попыток.</summary>
        public int AttemptCount => _attempt;

        /// <summary>Задержка до следующей попытки на основе текущего числа неудач.</summary>
        public float CurrentDelay => Steps[Math.Min(_attempt, Steps.Length - 1)];

        public void RecordFailure()
        {
            _attempt++;
        }

        public void Reset()
        {
            _attempt = 0;
        }
    }
}
