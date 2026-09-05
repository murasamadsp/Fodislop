#nullable enable

using System;
using Fodinae;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Tools.Imgui;

/// <summary>
/// График по кольцевому буферу отсчётов с кэшированной текстурной отрисовкой в один вызов.
/// </summary>
public sealed class ToolGraph : IDisposable
{
    private const int TextureHeight = 48;

    private readonly float[] _samples;
    private int _cursor;
    private int _count;

    private Texture2D? _texture;
    private Color32[]? _pixelBuffer;
    private bool _dirty;
    private Color _lastColor;
    private float _lastTop;

    public ToolGraph(int capacity)
    {
        _samples = new float[Mathf.Max(2, capacity)];
    }

    public float Last { get; private set; }

    public float Minimum { get; private set; }

    public float Maximum { get; private set; }

    public float Average { get; private set; }

    public void Push(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            value = 0f;
        }

        value = Mathf.Max(0f, value);
        _samples[_cursor] = value;
        _cursor = (_cursor + 1) % _samples.Length;
        _count = Mathf.Min(_count + 1, _samples.Length);
        Last = value;
        _dirty = true;

        float minimum = float.MaxValue;
        float maximum = float.MinValue;
        float sum = 0f;
        for (int i = 0; i < _count; i++)
        {
            float sample = _samples[i];
            minimum = Mathf.Min(minimum, sample);
            maximum = Mathf.Max(maximum, sample);
            sum += sample;
        }

        Minimum = _count > 0 ? minimum : 0f;
        Maximum = _count > 0 ? maximum : 0f;
        Average = _count > 0 ? sum / _count : 0f;
    }

    public void Clear()
    {
        Array.Clear(_samples, 0, _samples.Length);
        _cursor = 0;
        _count = 0;
        Last = 0f;
        Minimum = 0f;
        Maximum = 0f;
        Average = 0f;
        _dirty = true;
    }

    public void DestroyTexture()
    {
        if (_texture != null)
        {
            CoreUtils.Destroy(_texture);
            _texture = null;
            _pixelBuffer = null;
            _dirty = true;
        }
    }

    public void Dispose()
    {
        DestroyTexture();
    }

    /// <summary>
    /// Рисует график. Верх шкалы берётся из <paramref name="scaleHint"/> или из
    /// наибольшего отсчёта — что больше: иначе всплеск уезжает за рамку, а
    /// ровный участок занимает пиксель по высоте.
    /// </summary>
    public void Draw(Rect area, Color color, float scaleHint)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        GUI.Box(area, GUIContent.none, ToolTheme.Graph);
        if (_count == 0)
        {
            return;
        }

        float top = Mathf.Max(scaleHint, Maximum);
        if (top <= 0f)
        {
            return;
        }

        EnsureTextureUpdated(color, top);
        if (_texture != null)
        {
            GUI.DrawTexture(area, _texture, ScaleMode.StretchToFill, true);
        }
    }

    private void EnsureTextureUpdated(Color color, float top)
    {
        int width = _samples.Length;
        int height = TextureHeight;

        if (_texture == null)
        {
            _texture = RuntimeTextureFactory.CreateRgba32NoMip(
                width,
                height,
                "ToolGraph",
                RuntimeTextureColorSpace.Linear,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            _pixelBuffer = new Color32[width * height];
            _dirty = true;
        }

        if (!_dirty && color == _lastColor && Mathf.Approximately(top, _lastTop))
        {
            return;
        }

        Color32 barColor = color;
        Color32 clearColor = new(0, 0, 0, 0);

        for (int x = 0; x < width; x++)
        {
            int barHeight = 0;
            if (x < _count)
            {
                int index = (_cursor - _count + x + width * 2) % width;
                float normalized = Mathf.Clamp01(_samples[index] / top);
                barHeight = _samples[index] > 0f
                    ? Mathf.Max(1, Mathf.RoundToInt(normalized * height))
                    : 0;
            }

            for (int y = 0; y < height; y++)
            {
                _pixelBuffer![y * width + x] = y < barHeight ? barColor : clearColor;
            }
        }

        _texture.SetPixels32(_pixelBuffer!);
        _texture.Apply(false, false);
        _dirty = false;
        _lastColor = color;
        _lastTop = top;
    }
}
