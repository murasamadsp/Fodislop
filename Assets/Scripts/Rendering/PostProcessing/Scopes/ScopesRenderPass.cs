#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static Fodinae.Rendering.PostProcessing.Scopes.ScopeShaderConstants;

namespace Fodinae.Rendering.PostProcessing.Scopes;

/// <summary>
/// Считает приборы разбора с готового кадра.
/// </summary>
/// <remarks>
/// Отдельный проход, а не ветка внутри <see cref="PostProcessRenderPass"/>,
/// по двум причинам. Во-первых, снимать надо ПОСЛЕ всего постпроцесса:
/// смысл прибора в том, что он показывает уходящее на экран, а не
/// промежуточное состояние. Во-вторых, тот файл уже у предела в 500 строк,
/// за которым линтер требует разделения ответственностей, а не приписки.
///
/// Проход выключен, пока рабочее место закрыто: ни одно ядро не
/// запускается, ресурсы не создаются.
/// </remarks>
internal sealed class ScopesRenderPass : ScriptableRenderPass2D
{
    private const float CaptureIntervalSeconds = 0.2f;

    private readonly ComputeShader _scopesCS;
    private readonly int _kernelClear;
    private readonly int _kernelGather;
    private readonly int _kernelHistogram;
    private readonly int _kernelWaveform;
    private readonly int _kernelVectorscope;
    private readonly ScopeResources _resources = new();
    private float _nextCaptureTime;
    private string? _failure;

    private static bool _enabled;

    // Проход не резолвится контейнером — он принадлежит renderer asset.
    // Снимки состояния в него ТОЛКАЮТ (SetAdvancedSettings, Enabled), но
    // приборы надо ТЯНУТЬ: их считает GPU, а показывает интерфейс. Поэтому
    // живой проход публикует себя здесь. Это не синглтон-точка доступа к
    // логике: наружу видны только три текстуры, и записать сюда нельзя.
    private static ScopesRenderPass? _live;

    public ScopesRenderPass(ComputeShader scopesCS)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        renderPassEvent2D = RenderPassEvent2D.AfterRenderingPostProcessing;
        _scopesCS = scopesCS;
        _kernelClear = _scopesCS.FindKernel("ScopesClear");
        _kernelGather = _scopesCS.FindKernel("ScopesGather");
        _kernelHistogram = _scopesCS.FindKernel("HistogramResolve");
        _kernelWaveform = _scopesCS.FindKernel("WaveformResolve");
        _kernelVectorscope = _scopesCS.FindKernel("VectorscopeResolve");
        _live = this;
    }

    public static RenderTexture? LiveHistogram => _live?._resources.HistogramTexture;

    public static RenderTexture? LiveWaveform => _live?._resources.WaveformTexture;

    public static RenderTexture? LiveVectorscope => _live?._resources.VectorscopeTexture;

    public static bool Available => _live != null && _live._failure == null;

    public static string? FailureMessage => _live?._failure;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        _enabled = false;
        if (_live != null)
        {
            _live._failure = null;
            _live._nextCaptureTime = 0f;
            _live._resources.Dispose();
        }

        _live = null;
    }

    /// <summary>
    /// Приборы считаются только пока открыто рабочее место. Статика по той
    /// же причине, что и во всём остальном постпроцессе: проход принадлежит
    /// renderer asset, инъекции в него нет.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (value && _live != null)
            {
                _live._failure = null;
                _live._nextCaptureTime = 0f;
            }
            else if (!value && _live != null)
            {
                _live._resources.Dispose();
            }
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (!_enabled)
        {
            return;
        }

        // При отключённом Domain Reload статическое поле очищается через
        // SubsystemRegistration, а экземпляр renderer feature может
        // пережить вход в Play Mode. Возвращаем живую ссылку до чтения
        // текстур интерфейсом.
        _live = this;
        if (_failure != null)
        {
            return;
        }

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (cameraData.renderType != CameraRenderType.Base ||
            cameraData.camera.cameraType != CameraType.Game ||
            cameraData.camera != PostProcessRuntimeState.MainCamera)
        {
            return;
        }

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        TextureHandle activeColor = resourceData.activeColorTexture;
        if (!activeColor.IsValid())
        {
            return;
        }

        // Ограничитель частоты ставится только после выбора настоящей
        // игровой камеры. Scene View или overlay-камера могут пройти
        // через feature раньше Base-камеры; если занять слот на них,
        // приборы будут пропускать валидный кадр и выглядеть зависшими.
        float now = Time.unscaledTime;
        if (now < _nextCaptureTime)
        {
            return;
        }

        _nextCaptureTime = now + CaptureIntervalSeconds;

        try
        {
            _resources.EnsureAllocated();
        }
        catch (System.Exception exception)
        {
            _failure = exception.Message;
            Debug.LogError(
                $"[ScopesRenderPass] Приборы остановлены: {exception.Message}");
            return;
        }

        using var builder = renderGraph.AddUnsafePass<ScopesPassData>(
            PassName, out ScopesPassData passData, profilingSampler);

        passData.ScopesCS = _scopesCS;
        passData.KernelClear = _kernelClear;
        passData.KernelGather = _kernelGather;
        passData.KernelHistogram = _kernelHistogram;
        passData.KernelWaveform = _kernelWaveform;
        passData.KernelVectorscope = _kernelVectorscope;
        passData.Resources = _resources;
        passData.SourceTexture = activeColor;
        TextureDesc sourceDescriptor = activeColor.GetDescriptor(renderGraph);
        RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
        passData.SourceWidth = Mathf.Max(
            1,
            sourceDescriptor.sizeMode == TextureSizeMode.Explicit
                ? sourceDescriptor.width
                : cameraDescriptor.width);
        passData.SourceHeight = Mathf.Max(
            1,
            sourceDescriptor.sizeMode == TextureSizeMode.Explicit
                ? sourceDescriptor.height
                : cameraDescriptor.height);
        if (cameraData.isHDROutputActive)
        {
            HDROutputSettings output = HDROutputSettings.main;
            float nativePaperWhite = output.available && output.paperWhiteNits > 10f
                ? output.paperWhiteNits
                : Fodinae.Core.DisplaySettings.DefaultPaperWhite;
            float peakScale = PostProcessRuntimeState.DisplayPeakBrightnessNits /
                nativePaperWhite;
            passData.SignalScale = 1f / Mathf.Max(0.01f, peakScale);
        }
        else
        {
            passData.SignalScale = 1f;
        }

        builder.UseTexture(activeColor, AccessFlags.Read);
        builder.AllowPassCulling(false);
        builder.SetRenderFunc(
            static (ScopesPassData data, UnsafeGraphContext context) =>
                ScopesPassExecutor.Render(data, context));
    }

    public void Dispose()
    {
        if (ReferenceEquals(_live, this))
        {
            _live = null;
        }

        _resources.Dispose();
    }
}
