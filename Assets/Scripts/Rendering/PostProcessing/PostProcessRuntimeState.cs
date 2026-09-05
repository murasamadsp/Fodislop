#nullable enable

using System;
using UnityEngine;
using Fodinae.Core;

namespace Fodinae.Rendering.PostProcessing;
/// <summary>
/// Состояние, которое в проход ТОЛКАЮТ снаружи.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНО ОТ САМОГО ПРОХОДА. <c>ScriptableRenderPass</c> принадлежит
/// renderer asset, а не сцене, поэтому инъекции в него нет: контейнер до
/// него не дотягивается. Значит, всё, что приходит извне — камера, снимок
/// расширенных эффектов, калибровка дисплея, грейд, отладочный вид, шторка
/// сравнения — обязано лежать в статике, и лежало прямо в проходе, смешивая
/// «что показывать» с «как это нарисовать».
///
/// Здесь только первое. Каждый сеттер сам приводит значение в порядок и сам
/// решает, менялось ли оно: проход не должен ни доверять входу, ни сравнивать
/// его с прошлым кадром. И каждый настоящий сдвиг обесценивает историю
/// временных эффектов — иначе накопленная история осталась бы от прежних
/// настроек, и первые кадры после правки показывали бы смесь двух видов.
/// </remarks>
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
    private static PostProcessDebugView _debugView;
    private static float _compareSplit;
    private static bool _bypassPostProcessEffects;

    internal static uint CameraGeneration => _cameraGeneration;

    internal static uint PipelineGeneration => _pipelineGeneration;

    internal static AdvancedPostProcessSnapshot Advanced => _advanced;

    internal static float DisplayGamma => _displayGamma;

    internal static float DisplayPaperWhiteNits => _displayPaperWhiteNits;

    internal static float DisplayPeakBrightnessNits => _displayPeakBrightnessNits;

    internal static ColorGradeSnapshot ColorGrade => _colorGrade;

    /// <summary>
    /// Флаг полного отключения эффектов конвейера для бисекции/отладки через GUI.
    /// По умолчанию выключен, чтобы все настройки графики и эффектов действовали.
    /// </summary>
    public static bool BypassPostProcessEffects
    {
        get => _bypassPostProcessEffects;
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        MainCamera = null;
        _cameraGeneration = 0;
        _pipelineGeneration = 0;
        _advanced = default;
        _displayGamma = DisplaySettings.DefaultGamma;
        _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
        _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
        _colorGrade = ColorGradeSnapshot.FromLook();
        _debugView = PostProcessDebugView.None;
        _compareSplit = 0f;
        _bypassPostProcessEffects = false;
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
        ColorGradeSnapshot sanitized = grade.Sanitized();
        if (_colorGrade == sanitized)
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
