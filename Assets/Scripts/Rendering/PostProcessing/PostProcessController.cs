#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{

[DisallowMultipleComponent]
public class PostProcessController : MonoBehaviour
{
    private static PostProcessController? _instance;
    public static PostProcessController Instance => _instance!;

    [SerializeField]
    private Volume? _volume;

    private Camera? _configuredMainCamera;
    private UniversalAdditionalCameraData? _configuredMainCameraData;
    private Camera? _worldUiCamera;
    private UniversalAdditionalCameraData? _worldUiCameraData;
    private int _worldUiLayerMask;

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
        var mainCam = Camera.main;
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
        }

        var profile = _volume.sharedProfile;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "RuntimePostProcessVolumeProfile";
            _volume.sharedProfile = profile;
        }

        if (profile.components.Count == 0)
        {
            GetOrAddComponent(ref _bloom, profile);
            GetOrAddComponent(ref _vignette, profile);
            GetOrAddComponent(ref _chromaticAberration, profile);
            GetOrAddComponent(ref _colorGrading, profile);
            GetOrAddComponent(ref _eigengrau, profile);
            GetOrAddComponent(ref _motionBlur, profile);
        }
        else
        {
            profile.TryGet(out _bloom);
            profile.TryGet(out _vignette);
            profile.TryGet(out _chromaticAberration);
            profile.TryGet(out _colorGrading);
            profile.TryGet(out _eigengrau);
            profile.TryGet(out _motionBlur);
        }
    }

    public void EnsureEditorVolume()
    {
        EnsureVolumeSetup();
    }

    private void LateUpdate()
    {
        var mainCamera = Camera.main;
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

        _worldUiCamera.orthographic = mainCamera.orthographic;
        _worldUiCamera.orthographicSize = mainCamera.orthographicSize;
        _worldUiCamera.fieldOfView = mainCamera.fieldOfView;
        _worldUiCamera.nearClipPlane = mainCamera.nearClipPlane;
        _worldUiCamera.farClipPlane = mainCamera.farClipPlane;
        _worldUiCamera.projectionMatrix = mainCamera.projectionMatrix;
    }

    private void EnsureCameraSetup(Camera mainCamera)
    {
        var cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null)
        {
            cameraData = mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
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
        int uiLayer = LayerMask.NameToLayer(PostProcessRendererFeature.WorldUiLayerName);
        if (uiLayer < 0)
        {
            Debug.LogError($"[PostProcess] Unity layer '{PostProcessRendererFeature.WorldUiLayerName}' is missing.");
            return;
        }

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

    private void GetOrAddComponent<T>(ref T? target, VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out target) || target == null)
        {
            target = profile.Add<T>();
        }
        target.SetAllOverridesTo(true);
    }
}
}
