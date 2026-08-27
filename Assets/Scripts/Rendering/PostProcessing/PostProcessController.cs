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
        private Camera? _worldUICamera;
        private UniversalAdditionalCameraData? _worldUICameraData;
        private Camera? _mainCamera;
        private UniversalAdditionalCameraData? _cachedMainCameraData;
        private int _worldUILayerMask;
        private float _lastWorldUIOrthographicSize = float.NaN;
        private float _lastWorldUIFieldOfView = float.NaN;
        private float _lastWorldUINearClipPlane = float.NaN;
        private float _lastWorldUIFarClipPlane = float.NaN;
        private Matrix4x4 _lastWorldUIProjection;
        private bool _hasWorldUIProjection;
        private bool _worldUISeparationRequired = true;

        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ChromaticAberrationComponent? _chromaticAberration;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;

        [Inject]
        private IClientConfigManager? _clientConfigManager;

        [Inject]
        private void Construct(Volume volume)
        {
            _volume = volume ?? throw new ArgumentNullException(nameof(volume));
        }

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
            motionBlur.intensity.overrideState = true;

            // Enable the renderer pass only after every Volume value and every
            // fused setting has been applied as one coherent configuration.
            PostProcessRenderPass.SetQuality(
                config.GraphicsQualitySettings.PostProcessQuality);

            _worldUISeparationRequired = false;
            if (_configuredMainCamera != null && _configuredMainCameraData != null)
            {
                ConfigureWorldUIRendering(_configuredMainCamera, _configuredMainCameraData);
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

            bool cameraSeparationIsBroken =
                _configuredMainCamera != mainCamera ||
                _configuredMainCameraData == null ||
                (mainCamera.cullingMask & _worldUILayerMask) == 0 ||
                (_worldUICamera != null && _worldUICamera.enabled) ||
                (_worldUICamera != null &&
                    _configuredMainCameraData.cameraStack.Contains(_worldUICamera));

            if (cameraSeparationIsBroken)
            {
                EnsureCameraSetup(mainCamera);
            }

            if (!_worldUISeparationRequired || _worldUICamera == null)
            {
                return;
            }

            Matrix4x4 projection = mainCamera.projectionMatrix;
            bool projectionChanged =
                !_hasWorldUIProjection ||
                _worldUICamera.orthographic != mainCamera.orthographic ||
                !Mathf.Approximately(_lastWorldUIOrthographicSize, mainCamera.orthographicSize) ||
                !Mathf.Approximately(_lastWorldUIFieldOfView, mainCamera.fieldOfView) ||
                !Mathf.Approximately(_lastWorldUINearClipPlane, mainCamera.nearClipPlane) ||
                !Mathf.Approximately(_lastWorldUIFarClipPlane, mainCamera.farClipPlane) ||
                _lastWorldUIProjection != projection;
            if (!projectionChanged)
            {
                return;
            }

            _worldUICamera.orthographic = mainCamera.orthographic;
            _worldUICamera.orthographicSize = mainCamera.orthographicSize;
            _worldUICamera.fieldOfView = mainCamera.fieldOfView;
            _worldUICamera.nearClipPlane = mainCamera.nearClipPlane;
            _worldUICamera.farClipPlane = mainCamera.farClipPlane;
            _worldUICamera.projectionMatrix = projection;
            _lastWorldUIOrthographicSize = mainCamera.orthographicSize;
            _lastWorldUIFieldOfView = mainCamera.fieldOfView;
            _lastWorldUINearClipPlane = mainCamera.nearClipPlane;
            _lastWorldUIFarClipPlane = mainCamera.farClipPlane;
            _lastWorldUIProjection = projection;
            _hasWorldUIProjection = true;
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
            ConfigureWorldUIRendering(mainCamera, cameraData);
        }

        private void ConfigureWorldUIRendering(
            Camera mainCamera,
            UniversalAdditionalCameraData mainCameraData)
        {
            int uiLayer = UnityRenderLayerContracts.RequireWorldUIGameObjectLayer();
            UnityRenderLayerContracts.RequireWorldUISortingLayer();
            _worldUILayerMask = 1 << uiLayer;

            mainCamera.cullingMask |= _worldUILayerMask;
            if (_worldUICamera == null)
            {
                Transform? existingTransform = mainCamera.transform.Find("WorldUICamera");
                _worldUICamera = existingTransform != null
                    ? existingTransform.GetComponent<Camera>()
                    : null;
            }

            if (_worldUICamera != null)
            {
                mainCameraData.cameraStack.Remove(_worldUICamera);
                _worldUICamera.enabled = false;
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
