#if FMOD
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// FMOD Studio аудио-бэкенд с загрузкой банков с сервера.
    ///
    /// Для MMO: банки .bank скачиваются через ClientAssetLoader как обычные ассеты,
    /// кешируются на диске (PersistentAssetCache), и загружаются в FMOD из памяти.
    ///
    /// Структура каталогов:
    /// <code>
    /// Fodislop_Audio/             ← FMOD проект (отдельный репозиторий, не в Unity)
    ///   Fodislop_Audio.fspro
    ///   Assets/                   ← исходники .wav/.ogg
    ///   Banks/                    ← FMOD билдит сюда
    ///     Desktop/                ← копируется на сервер как banks/
    ///       Master.bank
    ///       Master.strings.bank
    ///       SFX.bank
    ///       Music.bank
    ///       ...
    ///
    /// Fodislop/                   ← Unity проект
    ///   StreamingAssets/Audio/    ← банки для локальной разработки (без сервера)
    /// </code>
    ///
    /// Как это работает в MMO:
    /// 1. Звукорежиссёр билдит банки в Fodinae_Audio/Banks/Desktop/
    /// 2. Банки заливаются на CDN-сервер игры
    /// 3. Клиент при логине получает список банков и скачивает их через ClientAssetLoader
    /// 4. Банки кешируются локально (PersistentAssetCache с ETag)
    /// 5. FmodAudioBackend получает byte[] из кеша/сети и загружает в FMOD через loadBankMemory
    /// </summary>
    public sealed class FmodAudioBackend : IAudioBackend
    {
        private AudioSystem _system;
        private readonly Dictionary<int, FMOD.Studio.EventInstance> _voices = new();
        private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _fmodBuses = new();
        private int _nextHandleId;

        private const string BANK_PATH = "banks";

        /// <summary>
        /// Список банков которые нужны клиенту. Сервер при логине присылает актуальный список
        /// (например WorldInitPacket может содержать массив имён банков).
        /// </summary>
        private static readonly string[] _requiredBanks =
        {
            "Master",
            "Master.strings",
            "SFX",
            "Music",
            "Ambience",
            "Voice",
        };

        public void Initialize(AudioSystem system)
        {
            _system = system;

            // Асинхронно грузим все банки с сервера/кеша
            LoadAllBanks().Forget();
        }

        /// <summary>
        /// Загружает все банки через ClientAssetLoader — сначала проверяет кеш, потом сервер.
        /// </summary>
        private async UniTaskVoid LoadAllBanks()
        {
            var loader = ClientAssetLoader.Instance;
            if (loader == null)
            {
                Debug.LogError("[FmodAudioBackend] ClientAssetLoader не доступен — банки FMOD не загружены");
                return;
            }

            foreach (var bankName in _requiredBanks)
            {
                var bankFile = $"{BANK_PATH}/{bankName}.bank";

                try
                {
                    var bankBytes = await loader.GetAssetBytesAsync(bankFile, timeoutSeconds: 30);

                    if (bankBytes == null || bankBytes.Length == 0)
                    {
                        // Пробуем локальный StreamingAssets для разработки без сервера
                        var localPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Audio", $"{bankName}.bank");
                        if (System.IO.File.Exists(localPath))
                        {
                            bankBytes = System.IO.File.ReadAllBytes(localPath);
                        }
                    }

                    if (bankBytes != null && bankBytes.Length > 0)
                    {
                        LoadBankFromMemory(bankName, bankBytes);
                    }
                    else
                    {
                        Debug.LogWarning($"[FmodAudioBackend] Банк '{bankName}' не найден ни на сервере ни локально");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FmodAudioBackend] Ошибка загрузки банка '{bankName}': {ex.Message}");
                }
            }

            // После загрузки банков — подключаем шины FMOD
            MapBuses();
        }

        /// <summary>
        /// Загружает банк из сырых байт в рантайме (серверный режим).
        /// </summary>
        private static void LoadBankFromMemory(string bankName, byte[] bankData)
        {
            // Пытаемся сначала загрузить .strings если это мастер-банк
            // (FMOD требует чтобы strings банк был загружен ДО основного)
            FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.loadBankMemory(
                bankData,
                FMOD.Studio.LOAD_BANK_FLAGS.NORMAL,
                out var bank);

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"[FmodAudioBackend] Не удалось загрузить банк '{bankName}' из памяти: {result}");
            }
        }

        /// <summary>
        /// Достаёт FMOD-шины после загрузки всех банков.
        /// Пути шин: bus:/SFX, bus:/Music... — настраиваются в FMOD Studio микшере.
        /// </summary>
        private void MapBuses()
        {
            var busPaths = new Dictionary<AudioBusType, string>
            {
                { AudioBusType.Master,    "bus:/" },
                { AudioBusType.Sfx,       "bus:/SFX" },
                { AudioBusType.Music,     "bus:/Music" },
                { AudioBusType.Voice,     "bus:/Voice" },
                { AudioBusType.Ambience,  "bus:/Ambience" },
                { AudioBusType.Ui,        "bus:/UI" },
                { AudioBusType.Narrative, "bus:/Narrative" },
            };

            foreach (var kvp in busPaths)
            {
                if (FMODUnity.RuntimeManager.StudioSystem.getBus(kvp.Value, out var bus) == FMOD.RESULT.OK)
                {
                    _fmodBuses[kvp.Key] = bus;
                }
                else
                {
                    Debug.LogWarning($"[FmodAudioBackend] Шина FMOD '{kvp.Value}' не найдена");
                }
            }
        }

        public AudioPlaybackHandle CreateVoice(AudioEvent evt, AudioLayer layer, string assetPath, Vector3? worldPosition)
        {
            string fmodPath = $"event:/{evt.Name}";

            if (FMODUnity.RuntimeManager.CreateInstance(fmodPath, out var instance) != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"[FmodAudioBackend] Событие FMOD '{fmodPath}' не найдено");
                return null;
            }

            if (layer.IsSpatial && worldPosition.HasValue)
            {
                var pos = worldPosition.Value;
                instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                {
                    position = new FMOD.VECTOR { x = pos.x, y = pos.y, z = 0f },
                    forward  = new FMOD.VECTOR { x = 0f, y = 0f, z = 1f },
                    up       = new FMOD.VECTOR { x = 0f, y = 1f, z = 0f },
                });
            }

            float effectiveVolume = layer.Volume * _system.GetBus(layer.Bus).EffectiveVolume;
            instance.setVolume(effectiveVolume);
            instance.setPitch(layer.Pitch * _system.GetBus(layer.Bus).Pitch);
            instance.start();

            var handle = new AudioPlaybackHandle(_nextHandleId, _system.GetBus(layer.Bus))
            {
                Priority = layer.Priority,
                StartTime = Time.unscaledTime,
                _isPlayingFunc  = h => _voices.TryGetValue(h.HandleId, out var inst) && IsInstancePlaying(inst),
                _stopAction     = (h, fade) => StopVoice(h, fade),
                _positionAction = (h, pos) => SetVoicePosition(h, pos),
                _volumeAction   = (h, vol) => SetVoiceVolume(h, vol),
                _pitchAction    = (h, pit) => SetVoicePitch(h, pit),
            };

            _voices[_nextHandleId] = instance;
            _system.GetBus(layer.Bus)._activeVoices++;
            _nextHandleId++;

            return handle;
        }

        public void StopVoice(AudioPlaybackHandle handle, float fadeOut)
        {
            if (!_voices.TryGetValue(handle.HandleId, out var instance))
                return;

            var mode = fadeOut > 0f ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE;
            instance.stop(mode);
            instance.release();
            _voices.Remove(handle.HandleId);
            _system.GetBus(handle.Bus.BusType)._activeVoices--;
        }

        public void SetVoicePosition(AudioPlaybackHandle handle, Vector3 worldPosition)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
            {
                instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                {
                    position = new FMOD.VECTOR { x = worldPosition.x, y = worldPosition.y, z = 0f },
                });
            }
        }

        public void SetVoiceVolume(AudioPlaybackHandle handle, float volume)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
                instance.setVolume(volume * _system.GetBus(handle.Bus.BusType).EffectiveVolume);
        }

        public void SetVoicePitch(AudioPlaybackHandle handle, float pitch)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
                instance.setPitch(pitch);
        }

        public bool IsPlaying(AudioPlaybackHandle handle)
        {
            if (!_voices.TryGetValue(handle.HandleId, out var instance))
                return false;

            instance.getPlaybackState(out var state);
            return state == FMOD.Studio.PLAYBACK_STATE.PLAYING || state == FMOD.Studio.PLAYBACK_STATE.STARTING;
        }

        public void Update(float deltaTime)
        {
            foreach (var (busType, fmodBus) in _fmodBuses)
            {
                var audioBus = _system.GetBus(busType);
                fmodBus.setVolume(audioBus.EffectiveVolume);
            }

            // Дакинг, лимиты голосов и приоритеты — нативно в FMOD Studio.
        }

        public void Shutdown()
        {
            foreach (var instance in _voices.Values)
            {
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }

            _voices.Clear();
        }

        private static bool IsInstancePlaying(FMOD.Studio.EventInstance instance)
        {
            instance.getPlaybackState(out var state);
            return state != FMOD.Studio.PLAYBACK_STATE.STOPPED;
        }
    }
}
#endif
