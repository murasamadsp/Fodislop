#nullable enable

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Rendering.PostProcessing.Scopes;

/// <summary>
/// Буферы накопления и текстуры приборов разбора.
/// </summary>
/// <remarks>
/// Владение вынесено из прохода по образцу
/// <c>World/Lighting/Core/LightingResourceManager</c>: проход занят порядком
/// вычислений, ресурсы — своим временем жизни, и смешивать их значит получить
/// утечку при первой же смене разрешения.
///
/// Ресурсы создаются лениво и только когда рабочее место открыто: полмегабайта
/// буферов и три текстуры не должны существовать в обычной игре.
/// </remarks>
internal sealed class ScopeResources : IDisposable
{
    /// <summary>Корзин на канал. 256 — ровно байт, и прибор совпадает с 8-битным выводом.</summary>
    public const int Bins = 256;

    /// <summary>Сторона квадратных приборов: waveform и вектороскоп.</summary>
    public const int Size = 256;

    private const int HistogramWidth = 256;
    private const int HistogramHeight = 128;

    public ComputeBuffer? HistogramBuffer { get; private set; }

    public ComputeBuffer? WaveformBuffer { get; private set; }

    public ComputeBuffer? VectorscopeBuffer { get; private set; }

    public ComputeBuffer? StatsBuffer { get; private set; }

    public uint ClippedBlackSamples { get; private set; }

    public uint ClippedHighlightSamples { get; private set; }

    public RenderTexture? HistogramTexture { get; private set; }

    public RenderTexture? WaveformTexture { get; private set; }

    public RenderTexture? VectorscopeTexture { get; private set; }

    public bool IsAllocated =>
        HistogramBuffer != null &&
        WaveformBuffer != null &&
        VectorscopeBuffer != null &&
        StatsBuffer != null &&
        HistogramTexture != null && HistogramTexture.IsCreated() &&
        WaveformTexture != null && WaveformTexture.IsCreated() &&
        VectorscopeTexture != null && VectorscopeTexture.IsCreated();

    public void EnsureAllocated()
    {
        if (IsAllocated)
        {
            return;
        }

        Dispose();
        try
        {
            // Четыре канала: красный, зелёный, синий и яркость. Яркость считается
            // отдельно, а не выводится из троих: по трём каналам её не восстановить,
            // а именно по ней читается экспозиция.
            HistogramBuffer = new ComputeBuffer(Bins * 4, sizeof(uint), ComputeBufferType.Structured);
            // Четыре плоскости: RGB и отдельная luma для режима Luma waveform.
            WaveformBuffer = new ComputeBuffer(Size * Size * 4, sizeof(uint), ComputeBufferType.Structured);
            VectorscopeBuffer = new ComputeBuffer(Size * Size, sizeof(uint), ComputeBufferType.Structured);
            StatsBuffer = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);

            HistogramTexture = CreateTexture(HistogramWidth, HistogramHeight, "_ScopeHistogram");
            WaveformTexture = CreateTexture(Size, Size, "_ScopeWaveform");
            VectorscopeTexture = CreateTexture(Size, Size, "_ScopeVectorscope");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static RenderTexture CreateTexture(int width, int height, string name)
    {
        var texture = new RenderTexture(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear)
        {
            name = name,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
        };

        // Создать до первой привязки обязательно: без этого Create() случится
        // внутри SetComputeTextureParam, и UAV окажется невалидным ровно на том
        // кадре, где прибор впервые открыли.
        if (!texture.Create())
        {
            CoreUtils.Destroy(texture);
            throw new InvalidOperationException(
                $"Failed to create scope texture '{name}' ({width}x{height}).");
        }

        return texture;
    }

    public void Dispose()
    {
        HistogramBuffer?.Release();
        HistogramBuffer = null;
        WaveformBuffer?.Release();
        WaveformBuffer = null;
        VectorscopeBuffer?.Release();
        VectorscopeBuffer = null;
        StatsBuffer?.Release();
        StatsBuffer = null;
        ClippedBlackSamples = 0;
        ClippedHighlightSamples = 0;

        Release(HistogramTexture);
        HistogramTexture = null;
        Release(WaveformTexture);
        WaveformTexture = null;
        Release(VectorscopeTexture);
        VectorscopeTexture = null;
    }

    public void ApplyStats(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            return;
        }

        NativeArray<uint> values = request.GetData<uint>();
        if (values.Length >= 2)
        {
            ClippedBlackSamples = values[0];
            ClippedHighlightSamples = values[1];
        }
    }

    private static void Release(RenderTexture? texture)
    {
        if (texture == null)
        {
            return;
        }

        CoreUtils.Destroy(texture);
    }
}
