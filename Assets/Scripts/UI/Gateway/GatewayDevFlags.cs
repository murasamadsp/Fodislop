#nullable enable

namespace Fodinae.UI
{
    /// <summary>
    /// Переключатели ворот для разработки.
    ///
    /// Ворота устроены так, что при нормальной игре их не видно: токен сервер
    /// выдаёт при первом же подключении, онбординг помечается пройденным — и
    /// оба экрана дальше проскакивают. Для игрока это правильно, для работы над
    /// этими экранами — нет: посмотреть на них становится нельзя, не вычистив
    /// PlayerPrefs вручную.
    ///
    /// Поэтому в редакторе ворота по умолчанию показываются ВСЕГДА, а
    /// сохранённое состояние игнорируется. Выключается через
    /// «Fodinae/Ворота/Показывать вход и онбординг всегда».
    ///
    /// В сборке весь этот код вырезается препроцессором: ForceGates становится
    /// константой false, и никакой ветки в рантайме не остаётся.
    /// </summary>
    public static class GatewayDevFlags
    {
        /// <summary>Ключ EditorPrefs. Публичный — им пользуется пункт меню.</summary>
        public const string ForceGatesPrefsKey = "Fodinae.Gateway.ForceGates";

        /// <summary>
        /// Показывать вход и онбординг независимо от сохранённого состояния.
        /// В редакторе по умолчанию включено, в сборке всегда false.
        /// </summary>
        public static bool ForceGates
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(ForceGatesPrefsKey, true);
#else
                return false;
#endif
            }
        }
    }
}
