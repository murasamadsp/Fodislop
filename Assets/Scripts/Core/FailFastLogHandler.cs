#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Fodinae.Core
{
    /// <summary>
    /// Fail-fast сторож: первая же ошибка (LogType.Error / Exception / Assert)
    /// останавливает приложение вместо того, чтобы дать битому состоянию жить
    /// дальше.
    ///
    /// Работает ТОЛЬКО в редакторе (пауза + Debug.Break). В билде — никогда:
    /// игрок не должен терять сессию из-за ошибки, а диагностика идёт через
    /// логи. Убирать UNITY_EDITOR из условия нельзя.
    ///
    /// Регистрируется один раз в BootstrapLifetimeScope.Awake — раньше любых
    /// других систем, чтобы поймать и ошибки старта тоже.
    /// </summary>
    public static class FailFastLogHandler
    {
        private static bool _registered;
        private static bool _failing;
        private static readonly HashSet<string> ReportedFailures = new(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSubsystemReload()
        {
            _registered = false;
            _failing = false;
            ReportedFailures.Clear();
        }

        /// <summary>Подписывает обработчик, если это редактор и подписки ещё нет.</summary>
        public static void EnsureRegistered()
        {
#if UNITY_EDITOR
            if (_registered)
            {
                return;
            }

            Application.logMessageReceived += OnLogMessage;
            _registered = true;
#endif
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (_failing)
            {
                return;
            }

            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }

            // Unity can invoke logMessageReceived once per frame when a scene
            // MonoBehaviour retries initialization from Update. Fail-fast is a
            // diagnostic breakpoint, not a second error pipeline: report each
            // distinct message/stack pair once per play session.
            string failureKey = string.Concat(type, "\n", message, "\n", stackTrace);
            if (!ReportedFailures.Add(failureKey))
            {
                return;
            }

            _failing = true;
            Application.logMessageReceived -= OnLogMessage;

            Debug.LogError($"[FailFast] {BuildReport(message, stackTrace, type)}");

            // В редакторе пауза нагляднее выхода: видно сцену и стек.
            Debug.Break();
            _failing = false;
            Application.logMessageReceived += OnLogMessage;
        }

        private static string BuildReport(string message, string stackTrace, LogType type)
        {
            var sb = new StringBuilder();
            sb.Append("Fail-fast остановил приложение: ");
            sb.AppendLine(type.ToString());
            sb.AppendLine(message);
            if (!string.IsNullOrEmpty(stackTrace))
            {
                sb.AppendLine(stackTrace);
            }

            return sb.ToString();
        }
    }
}
