#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.Audio.Backend
{
    /// <summary>
    /// FMOD Studio аудио-бэкенд с диск-стримингом банков и селективной загрузкой сэмплов в ОЗУ.
    ///
    /// Оптимизация памяти:
    /// 1. Банки загружаются через loadBankFile с диска (persistentDataPath кэш или StreamingAssets).
    /// 2. Метаданные весят единицы КБ.
    /// 3. Сэмплы используются только если уже были заранее загружены FMOD.
    /// </summary>
    public sealed class FmodAudioBackend
    {
        private AudioSystem _system = null!;
        private IAssetLoader _assetLoader = null!;

        public void Initialize(AudioSystem system, IAssetLoader assetLoader)
        {
            _system = system;
            _assetLoader = assetLoader;
            LoadRequiredBanksAsync().Forget();
        }

        private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _fmodBuses = new();
        private readonly ConcurrentDictionary<string, FMOD.Studio.Bank> _loadedBanks = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unavailableBanks = new(StringComparer.OrdinalIgnoreCase);
        private bool _paused;

        private const string BANK_PATH = "banks";

        private static readonly string[] _requiredBanks =
        {
            "Master",
            "Master.strings",
        };

        private static readonly FMOD.VECTOR ForwardVector = new() { x = 0f, y = 0f, z = 1f };
        private static readonly FMOD.VECTOR UpVector = new() { x = 0f, y = 1f, z = 0f };



        public async UniTaskVoid LoadRequiredBanksAsync()
        {
            try
            {
                foreach (var bankName in _requiredBanks)
                {
                    try
                    {
                        await EnsureBankLoadedAsync(bankName);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            $"[FmodAudioBackend] FMOD bank '{bankName}' could not be loaded; " +
                            $"continuing with the remaining banks: {exception.Message}");
                    }
                }

                MapBuses();

                // Missing FMOD content is a supported no-audio state. Do not
                // surface it as a gameplay warning or make callers wait for it.
            }
            catch (OperationCanceledException)
            {
                // Audio initialization may be interrupted during domain reload
                // or teardown; no audio service needs to be kept alive then.
            }
            catch (Exception exception)
            {
                // FMOD is optional presentation. A failed bank must not become
                // an unobserved UniTaskVoid exception or block gameplay startup.
                Debug.LogWarning(
                    $"[FmodAudioBackend] Audio initialization skipped: {exception.Message}");
            }
        }

        /// <summary>
        /// Гарантирует наличие и загрузку .bank файла с диска (CDN cache -> StreamingAssets fallback).
        /// </summary>
        public async UniTask<bool> EnsureBankLoadedAsync(string bankName)
        {
            var cleanBankName = bankName.Replace(".bank", string.Empty);
            if (_loadedBanks.ContainsKey(cleanBankName))
            {
                return true;
            }

            if (_unavailableBanks.Contains(cleanBankName))
            {
                return false;
            }

            string? bankFilePath = null;
            var localPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Audio", $"{cleanBankName}.bank");

            if (System.IO.File.Exists(localPath))
            {
                bankFilePath = localPath;
            }
            else
            {
                if (_assetLoader is ClientAssetLoader assetLoader)
                {
                    var relativeRemotePath = $"{BANK_PATH}/{cleanBankName}.bank";
                    if (assetLoader.IsKnownMissing(relativeRemotePath))
                    {
                        _unavailableBanks.Add(cleanBankName);
                        return false;
                    }

                    try
                    {
                        bankFilePath = await assetLoader.GetAssetPathAsync(relativeRemotePath);
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(bankFilePath) || !System.IO.File.Exists(bankFilePath))
            {
                _unavailableBanks.Add(cleanBankName);
                return false;
            }

            if (!IsFmodBank(bankFilePath))
            {
                Debug.LogWarning(
                    $"[FmodAudioBackend] Ignoring invalid audio bank '{bankFilePath}'.");
                if (bankFilePath.Equals(
                    PersistentAssetCache.GetAssetPath($"banks/{cleanBankName}.bank"),
                    StringComparison.OrdinalIgnoreCase))
                {
                    PersistentAssetCache.RemoveAsset($"banks/{cleanBankName}.bank");
                }

                _unavailableBanks.Add(cleanBankName);
                return false;
            }

            // Bank discovery is optional presentation work. NORMAL may synchronously
            // load sample data on the main thread; a missing/late audio asset must
            // never freeze world simulation. NONBLOCKING leaves sample loading to
            // FMOD's streamer, while CreateVoice below rejects non-resident samples.
            FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.loadBankFile(
                bankFilePath,
                FMOD.Studio.LOAD_BANK_FLAGS.NONBLOCKING,
                out var bank);

            if (result == FMOD.RESULT.OK)
            {
                _loadedBanks[cleanBankName] = bank;
                Debug.Log($"[FmodAudioBackend] Успешно загружен банк '{cleanBankName}' из: {bankFilePath}");
                return true;
            }
            else if (result == FMOD.RESULT.ERR_EVENT_ALREADY_LOADED)
            {
                return true;
            }

            Debug.LogWarning(
                $"[FmodAudioBackend] FMOD loadBankFile failed for '{cleanBankName}': {result}.");
            _unavailableBanks.Add(cleanBankName);
            return false;
        }

        private static bool IsFmodBank(string path)
        {
            Span<byte> header = stackalloc byte[12];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read = stream.Read(header);
            return read == header.Length &&
                header[..4].SequenceEqual("RIFF"u8) &&
                header[8..12].SequenceEqual("FEV "u8);
        }

        public void UnloadBank(string bankName)
        {
            var cleanBankName = bankName.Replace(".bank", string.Empty);
            if (_loadedBanks.TryGetValue(cleanBankName, out var bank))
            {
                bank.unload();
                _loadedBanks.TryRemove(cleanBankName, out _);
                Debug.Log($"[FmodAudioBackend] Банк '{cleanBankName}' выгружен из памяти.");
            }
        }

        private void MapBuses()
        {
            var busPaths = new Dictionary<AudioBusType, string>
            {
                { AudioBusType.Master,   "bus:/" },
                { AudioBusType.SFX,      "bus:/sfx" },
                { AudioBusType.Music,    "bus:/music" },
                { AudioBusType.Voice,    "bus:/voice" },
                { AudioBusType.Ambience, "bus:/ambience" },
                { AudioBusType.UI,       "bus:/ui" },
            };

            foreach (var kvp in busPaths)
            {
                if (FMODUnity.RuntimeManager.StudioSystem.getBus(kvp.Value, out var bus) == FMOD.RESULT.OK)
                {
                    _fmodBuses[kvp.Key] = bus;
                }
            }

            _system?.ApplySavedBusVolumes();
            SetPaused(_paused);
        }

        public float GetBusVolume(AudioBusType type)
        {
            if (_fmodBuses.TryGetValue(type, out var bus))
            {
                bus.getVolume(out float volume);
                return volume;
            }

            return 1f;
        }

        public void SetBusVolume(AudioBusType type, float volume)
        {
            if (_fmodBuses.TryGetValue(type, out var bus))
            {
                bus.setVolume(Mathf.Clamp01(volume));
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_fmodBuses.TryGetValue(AudioBusType.Master, out FMOD.Studio.Bus masterBus))
            {
                masterBus.setPaused(paused);
            }
        }

        public AudioPlaybackHandle? CreateVoice(string eventName, AudioLayer layer, Vector3? worldPosition, GameObject? targetGameObject = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            string fmodPath = eventName.StartsWith("event:/", StringComparison.OrdinalIgnoreCase) || eventName.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase)
                ? eventName
                : $"event:/{eventName}";

            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(fmodPath, out var eventDescription) != FMOD.RESULT.OK)
            {
                // Audio is optional for offline/editor runs. A missing event is a
                // no-op; the world must not produce a warning or block on FMOD.
                return null;
            }

            // Never request sample loading from the game loop. FMOD can block
            // updateForStart while loading samples, which produces a visible
            // hitch. Events whose samples are not already resident are skipped.
            eventDescription.getSampleLoadingState(out FMOD.Studio.LOADING_STATE sampleState);
            if (sampleState != FMOD.Studio.LOADING_STATE.LOADED)
            {
                return null;
            }

            FMOD.RESULT instResult = eventDescription.createInstance(out var instance);
            if (instResult != FMOD.RESULT.OK || !instance.isValid())
            {
                Debug.LogWarning($"[FmodAudioBackend] Не удалось создать экземпляр события '{fmodPath}': {instResult}");
                return null;
            }

            if (layer.IsSpatial)
            {
                if (targetGameObject != null)
                {
                    FMODUnity.RuntimeManager.AttachInstanceToGameObject(instance, targetGameObject);
                }
                else if (worldPosition.HasValue)
                {
                    var pos = worldPosition.Value;
                    instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                    {
                        position = new FMOD.VECTOR { x = pos.x, y = pos.y, z = 0f },
                        forward = ForwardVector,
                        up = UpVector,
                    });
                }
            }

            instance.setVolume(layer.Volume);
            instance.setPitch(layer.Pitch);
            instance.start();

            return new AudioPlaybackHandle(instance, layer.Bus);
        }

        public AudioPlaybackHandle? PlaySnapshot(string snapshotPath)
        {
            string fullPath = snapshotPath.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase) || snapshotPath.StartsWith("event:/", StringComparison.OrdinalIgnoreCase)
                ? snapshotPath
                : $"snapshot:/{snapshotPath}";

            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(fullPath, out var eventDescription) != FMOD.RESULT.OK)
            {
                return null;
            }

            eventDescription.getSampleLoadingState(out FMOD.Studio.LOADING_STATE sampleState);
            if (sampleState != FMOD.Studio.LOADING_STATE.LOADED)
            {
                return null;
            }

            if (eventDescription.createInstance(out var instance) == FMOD.RESULT.OK && instance.isValid())
            {
                instance.start();
                return new AudioPlaybackHandle(instance, AudioBusType.Master);
            }

            return null;
        }

        public void SetGlobalParameter(string name, float value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            FMODUnity.RuntimeManager.StudioSystem.setParameterByName(name, value);
        }

        public void Update()
        {
            // FMOD Studio C++ engine обновляет внутренние состояния нативно — внешних вызовов не требуется.
        }

        public void Shutdown()
        {
            foreach (var bank in _loadedBanks.Values)
            {
                bank.unload();
            }

            _loadedBanks.Clear();
        }
    }
}
