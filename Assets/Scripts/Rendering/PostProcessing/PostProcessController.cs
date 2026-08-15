#nullable enable

using System;
using System.Reflection;
using Fodinae.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    internal static class PostProcessDefaults
    {
        public static ClampedFloatParameter BloomIntensity() => new(0f, 0f, 5f);

        public static ClampedFloatParameter BloomThreshold() => new(0.9f, 0f, 2f);

        public static ClampedFloatParameter BloomScatter() => new(0.7f, 0.1f, 1f);

        public static ColorParameter BloomTint() => new(Color.white);

        public static ClampedFloatParameter VignetteIntensity() => new(0f, 0f, 1f);

        public static ColorParameter VignetteColor() => new(Color.black);

        public static ClampedFloatParameter VignetteSmoothness() => new(0.2f, 0.01f, 1f);

        public static Vector2Parameter VignetteCenter() => new(new Vector2(0.5f, 0.5f));

        public static ClampedFloatParameter ChromaticAberrationIntensity() => new(0f, 0f, 1f);

        public static ClampedFloatParameter ColorGradingExposure() => new(0f, -4f, 4f);

        public static ColorParameter ColorGradingFilter() => new(Color.white);

        public static ClampedFloatParameter ColorGradingContrast() => new(0f, -1f, 1f);

        public static ClampedFloatParameter ColorGradingSaturation() => new(1f, 0f, 2f);

        public static BoolParameter ColorGradingToneMapping() => new(true);

        public static ClampedFloatParameter ColorGradingWhitePoint() => new(1f, 0.25f, 8f);

        public static ClampedFloatParameter EigengrauIntensity() => new(0.2f, 0f, 1f);

        public static ColorParameter EigengrauColor() =>
            new(new Color(0.018f, 0.02f, 0.028f, 1f));

        public static ClampedFloatParameter EigengrauDarknessThreshold() => new(0.18f, 0.02f, 0.75f);

        public static ClampedFloatParameter EigengrauNoiseScale() => new(1f, 0.75f, 2f);

        public static ClampedFloatParameter EigengrauAnimationSpeed() => new(18f, 1f, 60f);

        public static ClampedFloatParameter MotionBlurIntensity() => new(0f, 0f, 1f);

        public static ClampedIntParameter MotionBlurMaxSamples() => new(8, 2, 32);
    }

    [DisallowMultipleComponent]
    public class PostProcessController : MonoBehaviour
    {
        private static PostProcessController? _instance;
        public static PostProcessController Instance => _instance!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            _instance = null;
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
        private bool _ownsRuntimeVolume;
        private bool _ownsRuntimeProfile;
        private float _lastWorldUiOrthographicSize = float.NaN;
        private float _lastWorldUiFieldOfView = float.NaN;
        private float _lastWorldUiNearClipPlane = float.NaN;
        private float _lastWorldUiFarClipPlane = float.NaN;
        private Matrix4x4 _lastWorldUiProjection;
        private bool _hasWorldUiProjection;

        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ChromaticAberrationComponent? _chromaticAberration;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;

        public float BloomIntensity
        {
            get => _bloom != null ? _bloom.intensity.value : 0f;
            set
            {
                if (_bloom != null)
                {
                    _bloom.intensity.overrideState = true;
                    _bloom.intensity.value = Mathf.Clamp(value, 0f, 5f);
                    _bloom.active = _bloom.intensity.value > 0f;
                }
            }
        }

        public float VignetteIntensity
        {
            get => _vignette != null ? _vignette.intensity.value : 0f;
            set
            {
                if (_vignette != null)
                {
                    _vignette.intensity.overrideState = true;
                    _vignette.intensity.value = Mathf.Clamp01(value);
                    _vignette.active = _vignette.intensity.value > 0f;
                }
            }
        }

        public float ChromaticAberrationIntensity
        {
            get => _chromaticAberration != null ? _chromaticAberration.intensity.value : 0f;
            set
            {
                if (_chromaticAberration != null)
                {
                    _chromaticAberration.intensity.overrideState = true;
                    _chromaticAberration.intensity.value = Mathf.Clamp01(value);
                    _chromaticAberration.active = _chromaticAberration.intensity.value > 0f;
                }
            }
        }

        public float Contrast
        {
            get => _colorGrading != null ? _colorGrading.contrast.value : 0f;
            set
            {
                if (_colorGrading != null)
                {
                    _colorGrading.contrast.overrideState = true;
                    _colorGrading.contrast.value = Mathf.Clamp(value, -1f, 1f);
                    UpdateColorGradingActiveState();
                }
            }
        }

        public float Saturation
        {
            get => _colorGrading != null ? _colorGrading.saturation.value : 1f;
            set
            {
                if (_colorGrading != null)
                {
                    _colorGrading.saturation.overrideState = true;
                    _colorGrading.saturation.value = Mathf.Clamp(value, 0f, 2f);
                    UpdateColorGradingActiveState();
                }
            }
        }

        public float EigengrauIntensity
        {
            get => _eigengrau != null ? _eigengrau.intensity.value : 0f;
            set
            {
                if (_eigengrau != null)
                {
                    _eigengrau.intensity.overrideState = true;
                    _eigengrau.intensity.value = Mathf.Clamp01(value);
                    _eigengrau.active = _eigengrau.intensity.value > 0f;
                }
            }
        }

        public float MotionBlurIntensity
        {
            get => _motionBlur != null ? _motionBlur.intensity.value : 0f;
            set
            {
                if (_motionBlur != null)
                {
                    _motionBlur.intensity.overrideState = true;
                    _motionBlur.intensity.value = Mathf.Clamp01(value);
                    _motionBlur.active = _motionBlur.intensity.value > 0f;
                }
            }
        }

        private void UpdateColorGradingActiveState()
        {
            if (_colorGrading != null)
            {
                _colorGrading.active = _colorGrading.IsActive();
            }
        }

        private void Awake()
        {
            _instance = this;
            _mainCamera = Camera.main;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            Volume? ownedVolume = _ownsRuntimeVolume ? _volume : null;
            VolumeProfile? ownedProfile = _ownsRuntimeProfile ? _volume?.sharedProfile : null;

            if (ownedProfile != null)
            {
                Destroy(ownedProfile);
            }

            if (ownedVolume != null)
            {
                Destroy(ownedVolume.gameObject);
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                EnsureVolumeSetup();
            }
        }

        public void Start()
        {
            EnsureVolumeSetup();
        }

        public void EnsureVolumeSetup()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _mainCamera ??= Camera.main;
            var mainCam = _mainCamera;
            if (mainCam != null)
            {
                EnsureCameraSetup(mainCam);
            }

            if (_volume == null)
            {
                _volume = GetComponent<Volume>();
                if (_volume == null)
                {
                    _volume = FindAnyObjectByType<Volume>();
                }
            }

            if (_volume == null)
            {
                var volumeGO = new GameObject("GlobalPostProcessVolume");
                _volume = volumeGO.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 1f;
                _ownsRuntimeVolume = true;
            }

            var profile = _volume.sharedProfile;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "RuntimePostProcessVolumeProfile";
                _volume.sharedProfile = profile;
                _ownsRuntimeProfile = true;
            }

            // Profiles can be partially serialized (for example after an editor
            // domain reload). Do not treat a non-empty profile as complete: each
            // runtime component is an independent part of the post-process contract.
            GetOrAddComponent(ref _bloom, profile);
            GetOrAddComponent(ref _vignette, profile);
            GetOrAddComponent(ref _chromaticAberration, profile);
            GetOrAddComponent(ref _colorGrading, profile);
            GetOrAddComponent(ref _eigengrau, profile);
            GetOrAddComponent(ref _motionBlur, profile);
        }

        public void EnsureEditorVolume()
        {
            EnsureVolumeSetup();
        }

        private void LateUpdate()
        {
            _mainCamera ??= Camera.main;
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
                _worldUiCamera == null ||
                _worldUiCameraData == null ||
                (mainCamera.cullingMask & _worldUiLayerMask) != 0 ||
                _worldUiCamera.cullingMask != _worldUiLayerMask ||
                _worldUiCameraData.renderType != CameraRenderType.Overlay ||
                _worldUiCameraData.renderPostProcessing ||
                !_configuredMainCameraData.cameraStack.Contains(_worldUiCamera);

            if (cameraSeparationIsBroken)
            {
                EnsureCameraSetup(mainCamera);
            }

            if (_worldUiCamera == null)
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
            EnsureWorldUiCamera(mainCamera, cameraData);
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

        private void GetOrAddComponent<T>(
            ref T? target,
            VolumeProfile profile)
            where T : VolumeComponent
        {
            if (!profile.TryGet(out target) || target == null)
            {
                target = profile.Add<T>();
            }

            EnsureParameters(target);
            EnableOverrides(target);
        }

        private static void EnsureParameters(VolumeComponent component)
        {
            switch (component)
            {
                case BloomComponent bloom:
                    bloom.intensity ??= PostProcessDefaults.BloomIntensity();
                    bloom.threshold ??= PostProcessDefaults.BloomThreshold();
                    bloom.scatter ??= PostProcessDefaults.BloomScatter();
                    bloom.tint ??= PostProcessDefaults.BloomTint();
                    break;
                case VignetteComponent vignette:
                    vignette.intensity ??= PostProcessDefaults.VignetteIntensity();
                    vignette.color ??= PostProcessDefaults.VignetteColor();
                    vignette.smoothness ??= PostProcessDefaults.VignetteSmoothness();
                    vignette.center ??= PostProcessDefaults.VignetteCenter();
                    break;
                case ChromaticAberrationComponent chromaticAberration:
                    chromaticAberration.intensity ??= PostProcessDefaults.ChromaticAberrationIntensity();
                    break;
                case ColorGradingComponent colorGrading:
                    colorGrading.exposure ??= PostProcessDefaults.ColorGradingExposure();
                    colorGrading.colorFilter ??= PostProcessDefaults.ColorGradingFilter();
                    colorGrading.contrast ??= PostProcessDefaults.ColorGradingContrast();
                    colorGrading.saturation ??= PostProcessDefaults.ColorGradingSaturation();
                    colorGrading.toneMapping ??= PostProcessDefaults.ColorGradingToneMapping();
                    colorGrading.toneMappingWhitePoint ??= PostProcessDefaults.ColorGradingWhitePoint();
                    break;
                case EigengrauComponent eigengrau:
                    eigengrau.intensity ??= PostProcessDefaults.EigengrauIntensity();
                    eigengrau.color ??= PostProcessDefaults.EigengrauColor();
                    eigengrau.darknessThreshold ??= PostProcessDefaults.EigengrauDarknessThreshold();
                    eigengrau.noiseScale ??= PostProcessDefaults.EigengrauNoiseScale();
                    eigengrau.animationSpeed ??= PostProcessDefaults.EigengrauAnimationSpeed();
                    break;
                case MotionBlurComponent motionBlur:
                    motionBlur.intensity ??= PostProcessDefaults.MotionBlurIntensity();
                    motionBlur.maxSamples ??= PostProcessDefaults.MotionBlurMaxSamples();
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported post-process component type '{component.GetType().FullName}'.");
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
    }
}
