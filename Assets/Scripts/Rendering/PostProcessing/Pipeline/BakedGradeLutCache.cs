#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

// Снимок фактических входов ApplyCreativeGrade, а не ссылок на изменяемые
// кривые. Время, камера и пиксели кадра в запекании не участвуют.
internal sealed class BakedGradeLutCache
{
    private const int ParameterCapacity = 128;
    private readonly Vector4[] _parameters = new Vector4[ParameterCapacity];
    private int _count;
    private bool _valid;

    public bool Matches(PostProcessPassData data)
    {
        if (!_valid)
        {
            return false;
        }

        Span<Vector4> current = stackalloc Vector4[ParameterCapacity];
        int count = WriteParameters(data, current);
        if (count != _count)
        {
            return false;
        }

        for (int index = 0; index < count; index++)
        {
            // Vector4.operator== использует допуск. Для ключа нужны точные
            // значения, иначе небольшое изменение грейда потеряется.
            if (!current[index].Equals(_parameters[index]))
            {
                return false;
            }
        }

        return true;
    }

    public void Store(PostProcessPassData data)
    {
        _count = WriteParameters(data, _parameters);
        _valid = true;
    }

    public void Invalidate()
    {
        _valid = false;
    }

    private static int WriteParameters(PostProcessPassData data, Span<Vector4> target)
    {
        int count = 0;
        // Те же нейтральные значения, что в BindPostProcessParameters.
        target[count++] = new Vector4(
            data.CgActive ? data.Exposure : 0f,
            data.CgActive ? data.Contrast : 0f,
            data.CgActive ? data.Saturation : 1f,
            data.CdlSaturation);
        target[count++] = data.CgActive ? data.ColorFilter : (Vector4)Color.white;
        target[count++] = data.WhiteBalance;
        target[count++] = data.CdlSlope;
        target[count++] = data.CdlOffset;
        target[count++] = data.CdlPower;
        target[count++] = data.CdlMaster;
        target[count++] = data.PrimaryLift;
        target[count++] = data.PrimaryGamma;
        target[count++] = data.PrimaryGain;
        target[count++] = data.PrimaryOffset;
        target[count++] = data.PrimaryMaster;
        target[count++] = new Vector4(data.Vibrance, data.Hue, data.CurveInterpolation, 0f);
        target[count++] = data.ContrastControls;
        target[count++] = data.ContrastControls2;
        target[count++] = data.Qualifier0;
        target[count++] = data.Qualifier1;
        target[count++] = data.Qualifier2;
        target[count++] = data.Qualifier3;
        target[count++] = data.Qualifier4;
        target[count++] = data.Qualifier5;
        target[count++] = data.Qualifier6;
        target[count++] = new Vector4(
            data.HueVsHueCurvePointCount,
            data.HueVsSaturationCurvePointCount,
            data.HueVsLuminanceCurvePointCount,
            data.LuminanceVsSaturationCurvePointCount);
        target[count++] = new Vector4(
            data.SaturationVsSaturationCurvePointCount,
            data.QualifierHueSampleCount,
            0f,
            0f);
        Append(data.HueVsHueCurvePoints, target, ref count);
        Append(data.HueVsSaturationCurvePoints, target, ref count);
        Append(data.HueVsLuminanceCurvePoints, target, ref count);
        Append(data.LuminanceVsSaturationCurvePoints, target, ref count);
        Append(data.SaturationVsSaturationCurvePoints, target, ref count);
        Append(data.QualifierHueSamples, target, ref count);
        return count;
    }

    private static void Append(Vector4[] values, Span<Vector4> target, ref int count)
    {
        values.AsSpan().CopyTo(target.Slice(count));
        count += values.Length;
    }
}
