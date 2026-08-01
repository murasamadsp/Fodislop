#nullable enable

using Fodinae.Audio.Backend;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Audio.Spatial
{
    /// <summary>
    /// Аудио-зона — триггерный регион, задействующий FMOD Snapshot при входе игрока.
    ///
    /// Нативно меняет акустику, дакинг и фильтры шин в FMOD Studio без принудительной C#-мутации
    /// громкостей микшера (что предотвращает сброс пользовательских настроек из PauseMenu).
    ///
    /// Примеры использования:
    /// <list type="bullet">
    ///   <item><b>Кристальная жила:</b> snapshot:/Crystal_Zone — усиливает высокочастотные резонансы SFX, снижает Ambience</item>
    ///   <item><b>Вулканическая зона:</b> snapshot:/Volcano_Zone — добавляет низкочастотный Reverb, нагнетает Ambience</item>
    ///   <item><b>Пак (здание):</b> snapshot:/Pack_Interior — приглушает внешний Ambience, поднимает акустику помещения</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class AudioZone : MonoBehaviour
    {
    }
}
