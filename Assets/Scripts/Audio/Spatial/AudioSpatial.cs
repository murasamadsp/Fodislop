#nullable enable

using Fodinae.Audio.Backend;
using Fodinae.Audio.Core;
using Fodinae.Core.Interfaces;
using VContainer;
using UnityEngine;

namespace Fodinae.Audio.Spatial
{
    /// <summary>
    /// Вешается на любой GameObject чтобы он излучал пространственный звук.
    ///
    /// Использует нативную привязку FMOD (AttachInstanceToGameObject), позиция и поворот
    /// обновляются нативно в C++ FMOD Engine без C#-поллинга на каждом кадре.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public sealed class AudioSpatial : MonoBehaviour
    {
        [Tooltip("Имя аудио-события. Можно сменить на лету через SetEvent().")]
        [SerializeField]
        private string _eventName = string.Empty;

        [Tooltip("Слой.")]
        [SerializeField]
        private AudioLayer _layer = AudioLayer.SFXDefault();

        [Tooltip("Громкость.")]
        [SerializeField]
        [Range(0f, 2f)]
        private float _volume;
        private AudioPlaybackHandle? _handle;

        [Inject]
        private IAudioSystem _audioSystem = null!;

        /// <summary>Начать проигрывание текущего события с нативной привязкой FMOD к GameObject.</summary>
        public void PlayCurrent()
        {
            if (string.IsNullOrEmpty(_eventName) || _audioSystem == null)
            {
                return;
            }

            Stop();
            float? vol = _volume > 0f ? _volume : null;
            _handle = _audioSystem.PlayAttached(_eventName, gameObject, _layer, vol);
        }

        /// <summary>Сменить событие на лету (старое останавливается, новое стартует).</summary>
        public void SetEvent(string eventName, AudioLayer? layer = null, float? volume = null)
        {
            _eventName = eventName;
            if (layer.HasValue)
            {
                _layer = layer.Value;
            }

            if (volume.HasValue)
            {
                _volume = volume.Value;
            }

            PlayCurrent();
        }

        /// <summary>Остановить с фейд-аутом.</summary>
        public void Stop(float fadeOut = 0f)
        {
            _handle?.Stop(fadeOut);
            _handle = default;
        }

        /// <summary>Играет ли сейчас звук.</summary>
        public bool IsPlaying => _handle != null && _handle.IsPlaying;
    }
}
