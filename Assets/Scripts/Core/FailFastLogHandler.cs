#nullable enable

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
