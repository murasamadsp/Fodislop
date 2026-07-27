using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Spatial
{
    /// <summary>
    /// Контроллер звукового сопровождения локации и игрового мира.
    ///
    /// Отвечает за реакцию на смену сцен, готовность локаций и атмосферный эмбиент.
    /// Не зависит от низкоуровневой работы с картой (MapManager) и от инфраструктурных серверов.
    /// </summary>
    public sealed class WorldAudioController : MonoBehaviour
    {
    }
}
