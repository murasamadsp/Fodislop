#nullable enable

using System;
using System.Reflection;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;
using Unity.Profiling;

namespace Fodinae.Rendering.PostProcessing
{
    public static class PostProcessDefaults
    {
        // These values only construct valid VolumeParameter instances for Unity
        // serialization. ProjectDefaults/ClientConfig is the sole visual source
        // of truth and overwrites every parameter before the first render.
        public static ClampedFloatParameter BloomIntensity() => new(0f, 0f, 5f);

        public static ClampedFloatParameter BloomThreshold() => new(0f, 0f, 2f);

        public static ClampedFloatParameter BloomSoftKnee() => new(0.5f, 0f, 1f);

        public static ClampedFloatParameter BloomRadius() => new(3f, 0.5f, 8f);

        public static ClampedFloatParameter BloomScatter() => new(0.1f, 0.1f, 1f);

        public static ColorParameter BloomTint() => new(Color.white);

        public static ClampedFloatParameter VignetteIntensity() => new(0f, 0f, 1f);

        public static ColorParameter VignetteColor() => new(Color.black);

        public static ClampedFloatParameter VignetteSmoothness() => new(0.01f, 0.01f, 1f);

        public static Vector2Parameter VignetteCenter() => new(new Vector2(0.5f, 0.5f));

        public static ClampedFloatParameter ChromaticAberrationIntensity() => new(0f, 0f, 1f);

        public static ClampedFloatParameter ColorGradingExposure() => new(0f, -4f, 4f);

        public static ColorParameter ColorGradingFilter() => new(Color.white);

        public static ClampedFloatParameter ColorGradingContrast() => new(0f, -1f, 1f);

        public static ClampedFloatParameter ColorGradingSaturation() => new(1f, 0f, 2f);

        public static BoolParameter ColorGradingToneMapping() => new(false);

        public static ClampedFloatParameter ColorGradingWhitePoint() => new(0.25f, 0.25f, 8f);

        public static ClampedFloatParameter EigengrauIntensity() => new(0f, 0f, 1f);

        public static ColorParameter EigengrauColor() => new(Color.black);

        public static ClampedFloatParameter EigengrauDarknessThreshold() => new(0.02f, 0.02f, 0.75f);

        public static ClampedFloatParameter EigengrauNoiseScale() => new(0.75f, 0.75f, 2f);

        public static ClampedFloatParameter EigengrauAnimationSpeed() => new(1f, 1f, 60f);

        public static ClampedFloatParameter MotionBlurIntensity() => new(0f, 0f, 1f);

    }

    [DisallowMultipleComponent]
    public class PostProcessController : MonoBehaviour
    {
        private static readonly ProfilerMarker PostProcessLateUpdateMarker =
            new("Fodinae.PostProcess.LateUpdate");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
        }

        [SerializeField]
        private Volume? _volume;

        private Camera? _configuredMainCamera;
        private UniversalAdditionalCameraData? _configuredMainCameraData;
        private Camera? _worldUiCamera;
        private UniversalAdditionalCameraData? _worldUiCameraData;
        private Camera? _mainCamera;
        private UniversalAdditionalCameraData? _cachedMainCameraData;
        private int _worldUiLayerMask;
        private float _lastWorldUiOrthographicSize = float.NaN;
        private float _lastWorldUiFieldOfView = float.NaN;
        private float _lastWorldUiNearClipPlane = float.NaN;
        private float _lastWorldUiFarClipPlane = float.NaN;
        private Matrix4x4 _lastWorldUiProjection;
        private bool _hasWorldUiProjection;
        private bool _worldUiSeparationRequired = true;

        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ChromaticAberrationComponent? _chromaticAberration;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;

        [Inject]
        private IClientConfigManager? _clientConfigManager;

        public float BloomIntensity
        {
            get => GetRequired(_bloom, nameof(_bloom)).intensity.value;
            set
            {
                BloomComponent bloom = GetRequired(_bloom, nameof(_bloom));
                bloom.intensity.overrideState = true;
                bloom.intensity.value = Mathf.Clamp(value, 0f, 5f);
                bloom.active = bloom.intensity.value > 0f;
            }
        }

        public float VignetteIntensity
        {
            get => GetRequired(_vignette, nameof(_vignette)).intensity.value;
            set
            {
                VignetteComponent vignette = GetRequired(_vignette, nameof(_vignette));
                vignette.intensity.overrideState = true;
                vignette.intensity.value = Mathf.Clamp01(value);
                vignette.active = vignette.intensity.value > 0f;
            }
        }

        public float ChromaticAberrationIntensity
        {
            get => GetRequired(_chromaticAberration, nameof(_chromaticAberration)).intensity.value;
            set
            {
                ChromaticAberrationComponent chromaticAberration =
                    GetRequired(_chromaticAberration, nameof(_chromaticAberration));
                chromaticAberration.intensity.overrideState = true;
                chromaticAberration.intensity.value = Mathf.Clamp01(value);
                chromaticAberration.active = chromaticAberration.intensity.value > 0f;
            }
        }

        public float Contrast
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).contrast.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.contrast.overrideState = true;
                colorGrading.contrast.value = Mathf.Clamp(value, -1f, 1f);
                UpdateColorGradingActiveState();
            }
        }

        public float Saturation
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).saturation.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.saturation.overrideState = true;
                colorGrading.saturation.value = Mathf.Clamp(value, 0f, 2f);
                UpdateColorGradingActiveState();
            }
        }

        public float EigengrauIntensity
        {
            get => GetRequired(_eigengrau, nameof(_eigengrau)).intensity.value;
            set
            {
                EigengrauComponent eigengrau = GetRequired(_eigengrau, nameof(_eigengrau));
                eigengrau.intensity.overrideState = true;
                eigengrau.intensity.value = Mathf.Clamp01(value);
                eigengrau.active = eigengrau.intensity.value > 0f;
            }
        }

        public float MotionBlurIntensity
        {
            get => GetRequired(_motionBlur, nameof(_motionBlur)).intensity.value;
            set
            {
                MotionBlurComponent motionBlur = GetRequired(_motionBlur, nameof(_motionBlur));
                motionBlur.intensity.overrideState = true;
                motionBlur.intensity.value = Mathf.Clamp01(value);
                motionBlur.active = motionBlur.intensity.value > 0f;
            }
        }

        private void UpdateColorGradingActiveState()
        {
            ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
            colorGrading.active = colorGrading.IsActive();
        }

        private void Awake()
        {
            _mainCamera = GameplayCamera.Resolve();
        }

        private void OnDestroy()
        {
        }

        private void OnEnable()
        {
            if (Application.isPlaying &&
                _clientConfigManager != null &&
                _clientConfigManager.Config != null)
            {
                EnsureVolumeSetup();
            }
        }

        public void Start()
        {
            if (!Application.isPlaying ||
                _clientConfigManager == null ||
                _clientConfigManager.Config == null)
            {
                return;
            }

            EnsureVolumeSetup();
        }

        public void EnsureVolumeSetup()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = GameplayCamera.Resolve();
            }

            var mainCam = _mainCamera;
            if (mainCam != null)
            {
                EnsureCameraSetup(mainCam);
            }

            if (_volume == null)
            {
                throw new InvalidOperationException(
                    "PostProcessController requires a serialized Volume component.");
            }

            VolumeProfile? profile = _volume.profile;
            if (profile == null)
            {
                throw new InvalidOperationException(
                    "PostProcessController requires a runtime VolumeProfile on its serialized Volume.");
            }

            ValidateProfileComponents(profile);

            RequireComponent(ref _bloom, profile);
            RequireComponent(ref _vignette, profile);
            RequireComponent(ref _chromaticAberration, profile);
            RequireComponent(ref _colorGrading, profile);
            RequireComponent(ref _eigengrau, profile);
            RequireComponent(ref _motionBlur, profile);
            ApplyClientConfig();
        }

        public void ApplyClientConfig()
        {
            if (_bloom == null || _vignette == null ||
                _chromaticAberration == null || _colorGrading == null ||
                _eigengrau == null || _motionBlur == null)
            {
                EnsureVolumeSetup();
            }

            IClientConfigManager clientConfigManager = _clientConfigManager ??
                throw new InvalidOperationException(
                    "PostProcessController requires IClientConfigManager injection.");
            ClientConfig config = clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "PostProcessController requires an initialized ClientConfig.");
            // The graphics preset used to stop at this class's doorstep: every
            // value below is an artistic one from ClientConfig, and nothing
            // here ever read GraphicsQualitySettings. That made the whole
            // post-processing stack cost the same on VeryLow as on Ultra -
            // bloom pyramid, motion blur and all - no matter which preset the
            // player picked, and it kept costing that with world lighting
            // switched off, because the two subsystems are unrelated.
            PostProcessRenderPass.SetAdvancedSettings(config.AdvancedPostProcess);

            BloomIntensity = config.BloomIntensity;
            BloomComponent bloom = GetRequired(_bloom, nameof(_bloom));
            bloom.threshold.overrideState = true;
            bloom.threshold.value = config.BloomThreshold;
            bloom.softKnee.overrideState = true;
            bloom.softKnee.value = config.BloomSoftKnee;
            bloom.radius.overrideState = true;
            bloom.radius.value = config.BloomRadius;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = config.BloomScatter;
            bloom.tint.overrideState = true;
            bloom.tint.value = config.BloomTint;

            VignetteIntensity = config.VignetteIntensity;
            VignetteComponent vignette = GetRequired(_vignette, nameof(_vignette));
            vignette.color.overrideState = true;
            vignette.color.value = config.VignetteColor;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = config.VignetteSmoothness;
            vignette.center.overrideState = true;
            vignette.center.value = config.VignetteCenter;

            ChromaticAberrationIntensity = config.ChromaticAberrationIntensity;
            Exposure = config.ColorGradingExposure;
            ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
            colorGrading.colorFilter.overrideState = true;
            colorGrading.colorFilter.value = config.ColorGradingFilter;
            colorGrading.toneMappingWhitePoint.overrideState = true;
            colorGrading.toneMappingWhitePoint.value = config.ColorGradingToneMappingWhitePoint;

            Contrast = config.ColorGradingContrast;
            Saturation = config.ColorGradingSaturation;
            ToneMapping = config.ColorGradingToneMapping;
            EigengrauIntensity = config.EigengrauIntensity;
            EigengrauComponent eigengrau = GetRequired(_eigengrau, nameof(_eigengrau));
            eigengrau.color.overrideState = true;
            eigengrau.color.value = config.EigengrauColor;
            eigengrau.darknessThreshold.overrideState = true;
            eigengrau.darknessThreshold.value = config.EigengrauDarknessThreshold;
            eigengrau.noiseScale.overrideState = true;
            eigengrau.noiseScale.value = config.EigengrauNoiseScale;
            eigengrau.animationSpeed.overrideState = true;
            eigengrau.animationSpeed.value = config.EigengrauAnimationSpeed;

            MotionBlurIntensity = config.MotionBlurIntensity;
            MotionBlurComponent motionBlur = GetRequired(_motionBlur, nameof(_motionBlur));
            motionBlur.maxSamples.overrideState = true;

            // Enable the renderer pass only after every Volume value and every
            // fused setting has been applied as one coherent configuration.
            PostProcessRenderPass.SetQuality(
                config.GraphicsQualitySettings.PostProcessQuality);

            _worldUiSeparationRequired =
                config.GraphicsQualitySettings.PostProcessQuality != PostProcessQualityMode.Off &&
                (bloom.active || vignette.active ||
                    GetRequired(_chromaticAberration, nameof(_chromaticAberration)).active ||
                    colorGrading.active || eigengrau.active || motionBlur.active ||
                    config.AdvancedPostProcess.HasAnyEffects());
            if (_configuredMainCamera != null && _configuredMainCameraData != null)
            {
                ConfigureWorldUiRendering(_configuredMainCamera, _configuredMainCameraData);
            }
        }

        public float Exposure
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).exposure.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.exposure.overrideState = true;
                colorGrading.exposure.value = Mathf.Clamp(value, -4f, 4f);
                UpdateColorGradingActiveState();
            }
        }

        public bool ToneMapping
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).toneMapping.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.toneMapping.overrideState = true;
                colorGrading.toneMapping.value = value;
                UpdateColorGradingActiveState();
            }
        }

        public void EnsureEditorVolume()
        {
            EnsureVolumeSetup();
        }

        private void LateUpdate()
        {
            using var marker = PostProcessLateUpdateMarker.Auto();
            if (_mainCamera == null)
            {
                _mainCamera = GameplayCamera.Resolve();
            }

            Camera? mainCamera = _configuredMainCamera;
            if (mainCamera == null)
            {
                mainCamera = _mainCamera;
            }

            if (mainCamera == null)
            {
                return;
            }

            bool cameraSeparationIsBroken = _worldUiSeparationRequired
                ? _configuredMainCamera != mainCamera ||
                    _configuredMainCameraData == null ||
                    _worldUiCamera == null ||
                    _worldUiCameraData == null ||
                    (mainCamera.cullingMask & _worldUiLayerMask) != 0 ||
                    !_worldUiCamera.enabled ||
                    _worldUiCamera.cullingMask != _worldUiLayerMask ||
                    _worldUiCameraData.renderType != CameraRenderType.Overlay ||
                    _worldUiCameraData.renderPostProcessing ||
                    !_configuredMainCameraData.cameraStack.Contains(_worldUiCamera)
                : _configuredMainCamera != mainCamera ||
                    _configuredMainCameraData == null ||
                    (mainCamera.cullingMask & _worldUiLayerMask) == 0 ||
                    (_worldUiCamera != null && _worldUiCamera.enabled) ||
                    (_worldUiCamera != null &&
                        _configuredMainCameraData.cameraStack.Contains(_worldUiCamera));

            if (cameraSeparationIsBroken)
            {
                EnsureCameraSetup(mainCamera);
            }

            if (!_worldUiSeparationRequired || _worldUiCamera == null)
            {
                return;
            }

            Matrix4x4 projection = mainCamera.projectionMatrix;
            bool projectionChanged =
                !_hasWorldUiProjection ||
                _worldUiCamera.orthographic != mainCamera.orthographic ||
                !Mathf.Approximately(_lastWorldUiOrthographicSize, mainCamera.orthographicSize) ||
                !Mathf.Approximately(_lastWorldUiFieldOfView, mainCamera.fieldOfView) ||
                !Mathf.Approximately(_lastWorldUiNearClipPlane, mainCamera.nearClipPlane) ||
                !Mathf.Approximately(_lastWorldUiFarClipPlane, mainCamera.farClipPlane) ||
                _lastWorldUiProjection != projection;
            if (!projectionChanged)
            {
                return;
            }

            _worldUiCamera.orthographic = mainCamera.orthographic;
            _worldUiCamera.orthographicSize = mainCamera.orthographicSize;
            _worldUiCamera.fieldOfView = mainCamera.fieldOfView;
            _worldUiCamera.nearClipPlane = mainCamera.nearClipPlane;
            _worldUiCamera.farClipPlane = mainCamera.farClipPlane;
            _worldUiCamera.projectionMatrix = projection;
            _lastWorldUiOrthographicSize = mainCamera.orthographicSize;
            _lastWorldUiFieldOfView = mainCamera.fieldOfView;
            _lastWorldUiNearClipPlane = mainCamera.nearClipPlane;
            _lastWorldUiFarClipPlane = mainCamera.farClipPlane;
            _lastWorldUiProjection = projection;
            _hasWorldUiProjection = true;
        }

        private void EnsureCameraSetup(Camera mainCamera)
        {
            UniversalAdditionalCameraData? cameraData = null;
            if (mainCamera == _configuredMainCamera && _cachedMainCameraData != null)
            {
                cameraData = _cachedMainCameraData;
            }
            else
            {
                cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData == null)
                {
                    cameraData = mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }

                _cachedMainCameraData = cameraData;
            }

            // This project uses its own renderer feature. Keeping URP's built-in
            // full-screen pass enabled would undermine the UI camera separation.
            cameraData.renderPostProcessing = false;
            cameraData.volumeLayerMask = -1;
            cameraData.volumeTrigger = mainCamera.transform;

            _configuredMainCamera = mainCamera;
            _configuredMainCameraData = cameraData;
            ConfigureWorldUiRendering(mainCamera, cameraData);
        }

        private void ConfigureWorldUiRendering(
            Camera mainCamera,
            UniversalAdditionalCameraData mainCameraData)
        {
            int uiLayer = UnityRenderLayerContracts.RequireWorldUIGameObjectLayer();
            UnityRenderLayerContracts.RequireWorldUISortingLayer();
            _worldUiLayerMask = 1 << uiLayer;

            if (_worldUiSeparationRequired)
            {
                EnsureWorldUiCamera(mainCamera, mainCameraData);
                return;
            }

            mainCamera.cullingMask |= _worldUiLayerMask;
            if (_worldUiCamera == null)
            {
                Transform? existingTransform = mainCamera.transform.Find("WorldUICamera");
                _worldUiCamera = existingTransform != null
                    ? existingTransform.GetComponent<Camera>()
                    : null;
            }

            if (_worldUiCamera != null)
            {
                mainCameraData.cameraStack.Remove(_worldUiCamera);
                _worldUiCamera.enabled = false;
            }
        }

        private void EnsureWorldUiCamera(Camera mainCamera, UniversalAdditionalCameraData mainCameraData)
        {
            int uiLayer = UnityRenderLayerContracts.RequireWorldUIGameObjectLayer();
            UnityRenderLayerContracts.RequireWorldUISortingLayer();

            _worldUiLayerMask = 1 << uiLayer;
            mainCamera.cullingMask &= ~_worldUiLayerMask;

            var existingTransform = mainCamera.transform.Find("WorldUICamera");
            _worldUiCamera = existingTransform != null ? existingTransform.GetComponent<Camera>() : null;
            if (_worldUiCamera == null)
            {
                var cameraObject = new GameObject("WorldUICamera");
                cameraObject.transform.SetParent(mainCamera.transform, false);
                _worldUiCamera = cameraObject.AddComponent<Camera>();
                _worldUiCamera.CopyFrom(mainCamera);
            }

            _worldUiCamera.cullingMask = _worldUiLayerMask;
            _worldUiCamera.clearFlags = CameraClearFlags.Nothing;
            _worldUiCamera.depth = mainCamera.depth + 1f;
            _worldUiCamera.enabled = true;

            _worldUiCameraData = _worldUiCamera.GetComponent<UniversalAdditionalCameraData>();
            if (_worldUiCameraData == null)
            {
                _worldUiCameraData = _worldUiCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            _worldUiCameraData.renderType = CameraRenderType.Overlay;
            _worldUiCameraData.renderPostProcessing = false;
            if (!mainCameraData.cameraStack.Contains(_worldUiCamera))
            {
                mainCameraData.cameraStack.Add(_worldUiCamera);
            }
        }

        private static void RequireComponent<T>(
            ref T? target,
            VolumeProfile profile)
            where T : VolumeComponent
        {
            if (!profile.TryGet(out target) || target == null)
            {
                target = profile.Add<T>(overrides: true);
                if (target == null)
                {
                    throw new InvalidOperationException(
                        $"Post-process VolumeProfile '{profile.name}' is missing " +
                        $"the required '{typeof(T).Name}' component and could not create it.");
                }
            }

            EnableOverrides(target);
        }


        private static void ValidateProfileComponents(VolumeProfile profile)
        {
            int removed = profile.components.RemoveAll(c => c == null);
            if (removed > 0)
            {
                Debug.LogWarning($"[PostProcessController] Cleaned up {removed} null/missing component(s) from VolumeProfile '{profile.name}'.");
            }
        }

        private static void EnableOverrides(VolumeComponent component)
        {
            FieldInfo[] fields = component.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
            {
                if (!typeof(VolumeParameter).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                object? value = field.GetValue(component);
                if (value is not VolumeParameter parameter)
                {
                    throw new InvalidOperationException(
                        $"Post-process component '{component.GetType().FullName}' has a null parameter field '{field.Name}'.");
                }

                parameter.overrideState = true;
            }
        }

        private static T GetRequired<T>(T? component, string fieldName)
            where T : UnityEngine.Object
        {
            return component ?? throw new InvalidOperationException(
                $"PostProcessController component '{fieldName}' is not initialized.");
        }
    }
}
