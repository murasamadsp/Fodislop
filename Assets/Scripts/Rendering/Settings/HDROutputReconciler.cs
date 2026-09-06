#nullable enable

using System;
using Fodinae.Rendering.PostProcessing;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer.Unity;

namespace Fodinae.Rendering;
/// <summary>
/// Keeps the operating system's HDR surface in step with the saved
/// preference for the whole life of the application.
/// </summary>
/// <remarks>
/// Reconciliation cannot be a one-shot call at startup: a display reports
/// its HDR capability late, the user can swap monitors, and the OS can drop
/// HDR mode on its own. It also cannot live in <see cref="DisplayManager"/>,
/// which exists only inside the MainGame scene — the menu and the loading
/// screens are on the persistent scope and need the same surface.
///
/// It used to sit in BootstrapLifetimeScope's own Update. That put a
/// per-frame rendering concern on the composition root, whose job is
/// building the container and nothing else.
/// </remarks>
public sealed class HDROutputReconciler : IStartable, ITickable, IDisposable
{
    // Probing every frame is pointless: HDR availability changes on the
    // scale of plugging in a monitor, not of a frame.
    private const float ProbeIntervalSeconds = 1f;

    // Через именованный контракт, а не через сырую Camera: тот же объект,
    // но бутстрап заводил IGameplayCamera именно для потребителей DI.
    private readonly IGameplayCamera _camera;
    private float _nextProbeTime;
    private Volume? _volume;
    private VolumeProfile? _profile;
    private Tonemapping? _tonemapping;

    public HDROutputReconciler(IGameplayCamera camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    public void Start()
    {
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "Display output calibration (runtime)";
        _tonemapping = _profile.Add<Tonemapping>(true);
        _tonemapping.mode.Override(TonemappingMode.Neutral);
        _tonemapping.neutralHDRRangeReductionMode.Override(NeutralRangeReductionMode.BT2390);
        _tonemapping.detectPaperWhite.Override(false);
        _tonemapping.detectBrightnessLimits.Override(false);
        _tonemapping.minNits.Override(0f);
        _tonemapping.hueShiftAmount.Override(0f);
        _volume = _camera.Camera.gameObject.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = float.MaxValue;
        _volume.weight = 1f;
        _volume.sharedProfile = _profile;
        UpdateCalibration();
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    public void Tick()
    {
        UpdateCalibration();
        if (Time.unscaledTime < _nextProbeTime)
        {
            return;
        }

        _nextProbeTime = Time.unscaledTime + ProbeIntervalSeconds;
        Apply();
    }

    public void Dispose()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_volume != null)
        {
            _volume.enabled = false;
            UnityEngine.Object.Destroy(_volume);
        }

        if (_profile != null)
        {
            foreach (VolumeComponent component in _profile.components)
            {
                UnityEngine.Object.Destroy(component);
            }

            UnityEngine.Object.Destroy(_profile);
        }

        _volume = null;
        _profile = null;
        _tonemapping = null;
    }

    private void UpdateCalibration()
    {
        if (_tonemapping == null)
        {
            return;
        }

        _tonemapping.paperWhite.value = PostProcessRuntimeState.DisplayPaperWhiteNits;
        _tonemapping.maxNits.value = PostProcessRuntimeState.DisplayPeakBrightnessNits;
        if (_camera.Camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            cameraData.volumeLayerMask |= 1 << _camera.Camera.gameObject.layer;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    private void Apply()
    {
        Camera camera = _camera.Camera;
        if (camera == null)
        {
            return;
        }

        HDROutput.Reconcile();
        HDROutput.ConfigureCamera(camera);
    }
}
