#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Fodinae.Core;

namespace Fodinae.Rendering.PostProcessing;
public static class PostProcessRuntimeState
{
    internal static Camera? MainCamera { get; private set; }
    private static uint _cameraGeneration;
    private static uint _pipelineGeneration;

    // Выключить постпроцесс нельзя ничем: ни настройкой, ни отладочным
    // байпасом, ни ожиданием конфига — кривая вывода сжимает HDR каскадного
    // света, и без неё кадр не неверно окрашен, а просто неверен.
    private static AdvancedPostProcessSnapshot _advanced;

    private static float _displayGamma = DisplaySettings.DefaultGamma;
    private static float _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
    private static float _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
    private static ColorGradeSnapshot _colorGrade = ColorGradeSnapshot.FromLook();
    private static ColorGradeSnapshot _colorGradeSource;
    private static bool _hasColorGradeSource;
    private static PostProcessDebugView _debugView;
    private static float _compareSplit;
    private static CompareMode _compareMode;
    private static bool _compareBefore;
    private static bool _bypassPostProcessEffects = true;
    private static bool _temporaryBypass;

    internal static uint CameraGeneration => _cameraGeneration;

    internal static uint PipelineGeneration => _pipelineGeneration;

    internal static AdvancedPostProcessSnapshot Advanced => _advanced;

    internal static float DisplayGamma => _displayGamma;

    internal static float DisplayPaperWhiteNits => _displayPaperWhiteNits;

    internal static float DisplayPeakBrightnessNits => _displayPeakBrightnessNits;

    internal static ColorGradeSnapshot ColorGrade => _colorGrade;

    public static bool BypassPostProcessEffects
    {
        get => true;
        set
        {
            if (_bypassPostProcessEffects == value)
            {
                return;
            }

            _bypassPostProcessEffects = value;
            InvalidateTemporalHistory();
        }
    }

    public static bool TemporaryBypass
    {
        get => _temporaryBypass;
        set
        {
            if (_temporaryBypass == value)
            {
                return;
            }

            _temporaryBypass = value;
            InvalidateTemporalHistory();
        }
    }

    public static PostProcessDebugView DebugView
    {
        get => _debugView;
        set
        {
            PostProcessDebugView sanitized = Enum.IsDefined(typeof(PostProcessDebugView), value)
                ? value
                : PostProcessDebugView.None;
            if (_debugView == sanitized)
            {
                return;
            }

            _debugView = sanitized;
            InvalidateTemporalHistory();
        }
    }

    public static float CompareSplit
    {
        get => _compareSplit;
        set
        {
            float sanitized = float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp01(value);
            if (Mathf.Approximately(_compareSplit, sanitized))
            {
                return;
            }

            _compareSplit = sanitized;
            InvalidateTemporalHistory();
        }
    }

    public static CompareMode CompareMode
    {
        get => _compareMode;
        set
        {
            CompareMode sanitized = Enum.IsDefined(typeof(CompareMode), value)
                ? value
                : CompareMode.Off;
            if (_compareMode == sanitized)
            {
                return;
            }

            _compareMode = sanitized;
            InvalidateTemporalHistory();
        }
    }

    public static bool CompareBefore
    {
        get => _compareBefore;
        set
        {
            if (_compareBefore == value)
            {
                return;
            }

            _compareBefore = value;
            InvalidateTemporalHistory();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        MainCamera = null;
        _cameraGeneration = 0;
        _pipelineGeneration = 0;
        _advanced = default;
        _displayGamma = DisplaySettings.DefaultGamma;
        _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
        _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
        _colorGrade = ColorGradeSnapshot.FromLook();
        _colorGradeSource = default;
        _hasColorGradeSource = false;
        _debugView = PostProcessDebugView.None;
        _compareSplit = 0f;
        _compareMode = CompareMode.Off;
        _compareBefore = false;
        _bypassPostProcessEffects = true;
        _temporaryBypass = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeGlobalCameraPostProcessBypass()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != null && camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            cameraData.renderPostProcessing = false;
        }
    }

    public static void SetDisplayCalibration(float gamma, float paperWhiteNits, float peakBrightnessNits)
    {
        float sanitizedGamma = FiniteClamp(
            gamma,
            DisplaySettings.GammaMin,
            DisplaySettings.GammaMax,
            DisplaySettings.DefaultGamma);
        float sanitizedPaperWhite = FiniteClamp(
            paperWhiteNits,
            DisplaySettings.PaperWhiteMin,
            DisplaySettings.PaperWhiteMax,
            DisplaySettings.DefaultPaperWhite);
        float sanitizedPeakBrightness = Mathf.Max(
            sanitizedPaperWhite,
            FiniteClamp(
                peakBrightnessNits,
                DisplaySettings.PeakBrightnessMin,
                DisplaySettings.PeakBrightnessMax,
                DisplaySettings.DefaultPeakBrightness));
        if (Mathf.Approximately(_displayGamma, sanitizedGamma) &&
            Mathf.Approximately(_displayPaperWhiteNits, sanitizedPaperWhite) &&
            Mathf.Approximately(_displayPeakBrightnessNits, sanitizedPeakBrightness))
        {
            return;
        }

        _displayGamma = sanitizedGamma;
        _displayPaperWhiteNits = sanitizedPaperWhite;
        _displayPeakBrightnessNits = sanitizedPeakBrightness;
        InvalidateTemporalHistory();
    }

    public static void SetAdvancedSettings(AdvancedPostProcessSnapshot settings)
    {
        if (_advanced == settings)
        {
            return;
        }

        _advanced = settings;
        InvalidateTemporalHistory();
    }

    public static void SetColorGrade(ColorGradeSnapshot grade)
    {
        // Тот же грейд, что в прошлый раз, — ничего не делать. Сравнение по
        // содержимому: источники (рабочее место, смешивание зон) отдают
        // кривые свежими клонами, и сравнение по ссылке не совпадало ни разу —
        // Sanitized() копировал весь грейд каждый кадр, а поколение конвейера
        // сбрасывало историю постпроцесса.
        if (_hasColorGradeSource && _colorGradeSource.ContentEquals(grade))
        {
            return;
        }

        _colorGradeSource = grade;
        _hasColorGradeSource = true;
        ColorGradeSnapshot sanitized = _hasColorGradeSource
            ? grade.SanitizedReusing(_colorGrade)
            : grade.Sanitized();
        if (_colorGrade.ContentEquals(sanitized))
        {
            return;
        }

        _colorGrade = sanitized;
        InvalidateTemporalHistory();
    }

    public static void InvalidateTemporalHistory()
    {
        _pipelineGeneration = unchecked(_pipelineGeneration + 1);
    }

    public static void SetMainCamera(Camera? camera)
    {
        if (MainCamera != camera)
        {
            // Смена камеры обесценивает историю временных эффектов: она
            // снята с другого ракурса. Поколение сбрасывает её, не трогая
            // сам проход.
            _cameraGeneration++;
        }

        MainCamera = camera;
    }

    private static float FiniteClamp(
        float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);
}
