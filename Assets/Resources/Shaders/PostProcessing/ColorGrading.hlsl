#ifndef FODINAE_COLOR_GRADING_INCLUDED
#define FODINAE_COLOR_GRADING_INCLUDED

#define FODINAE_CURVE_MAX_POINTS 16

float4 _MasterCurve[FODINAE_CURVE_MAX_POINTS];
float4 _RedCurve[FODINAE_CURVE_MAX_POINTS];
float4 _GreenCurve[FODINAE_CURVE_MAX_POINTS];
float4 _BlueCurve[FODINAE_CURVE_MAX_POINTS];
float4 _HueVsHueCurve[FODINAE_CURVE_MAX_POINTS];
float4 _HueVsSaturationCurve[FODINAE_CURVE_MAX_POINTS];
float4 _HueVsLuminanceCurve[FODINAE_CURVE_MAX_POINTS];
float4 _LuminanceVsSaturationCurve[FODINAE_CURVE_MAX_POINTS];
float4 _SaturationVsSaturationCurve[FODINAE_CURVE_MAX_POINTS];
float3 _ContrastControls2;
int _MasterCurvePointCount;
int _RedCurvePointCount;
int _GreenCurvePointCount;
int _BlueCurvePointCount;
int _HueVsHueCurvePointCount;
int _HueVsSaturationCurvePointCount;
int _HueVsLuminanceCurvePointCount;
int _LuminanceVsSaturationCurvePointCount;
int _SaturationVsSaturationCurvePointCount;
int _CurveInterpolation;

// ============================================================================
// Цветовой конвейер: лог-кодирование, творческий грейд, кривая вывода.
// ============================================================================
//
// ЗАЧЕМ ОТДЕЛЬНЫМ ФАЙЛОМ. PostProcess.compute занят кадром: блум, оптика,
// зерно, композит. Цвет — другая работа и другой словарь, и держать их в одном
// файле значило бы, что правку кривой надо искать между ветками анаморфных
// лучей.
//
// ПОРЯДОК ОПЕРАЦИЙ. Он не произволен и повторяет устройство настоящего
// цветового конвейера:
//
//   сцен-линейный кадр
//     -> экспозиция, баланс белого        линейные операции: они физичны,
//                                          это свойства съёмки, а не вкуса
//     -> лог-кодирование
//     -> CDL (slope/offset/power)         творческий грейд: он перцептивен,
//     -> контраст                          и потому живёт в логе
//     -> лог-декодирование
//     -> фотометрическая насыщенность      линейные веса Rec.709 без сдвига оттенка
//     -> кривая вывода (DRT)
//     -> кодирование дисплея
//
// Шаг в логарифме равномерен по восприятию, в линейном — нет: одна и та же
// прибавка контраста в линейном означала бы разное в тенях и в светах.

// ----------------------------------------------------------------------------
// Слой 1: линейные операции
// ----------------------------------------------------------------------------

// Баланс белого через LMS — пространство откликов колбочек глаза. Умножать
// надо именно там: в RGB множитель по каналам меняет и оттенок, а не только
// температуру, потому что каналы RGB не соответствуют ничему в зрении.
//
// Температура и оттенок здесь — не кельвины, а сдвиг в [-100, 100]
// относительно точки съёмки: крутят «холоднее/теплее», а не выставляют
// абсолютное значение.
float3 WhiteBalanceCoefficients(float temperature, float tint)
{
    float3 coefficients = float3(1.0, 1.0, 1.0);
    if (abs(temperature) >= 1e-5 || abs(tint) >= 1e-5)
    {
        float t1 = temperature / 65.0;
        float t2 = tint / 65.0;

    // Отрицательный сдвиг (в холод) уводит по x сильнее положительного:
    // планковский локус несимметричен, и равные шаги в кельвинах дают
    // неравные шаги в цветности.
        float x = 0.31271 - t1 * (t1 < 0.0 ? 0.1 : 0.05);
        float standardY = 2.87 * x - 3.0 * x * x - 0.27509507;
        float y = standardY + t2 * 0.05;

        float divisor = max(y, 1e-5);
        float3 targetXyz = float3(x / divisor, 1.0, (1.0 - x - y) / divisor);
        const float3 sourceXyz = float3(0.95047, 1.0, 1.08883); // D65
        const float3x3 bradford = float3x3(
         0.8951,  0.2664, -0.1614,
        -0.7502,  1.7135,  0.0367,
         0.0389, -0.0685,  1.0296);
        float3 sourceLms = mul(bradford, sourceXyz);
        float3 targetLms = mul(bradford, targetXyz);
        coefficients = targetLms / max(sourceLms, 1e-5);
    }

    return coefficients;
}

float3 ApplyWhiteBalance(float3 color, float3 lmsCoefficients)
{
    // The linter names the two reciprocal adaptation-basis matrices toLms and
    // fromLms. They contain the RGB<->XYZ basis used immediately around the
    // Bradford LMS multiplication below.
    const float3x3 toLms = float3x3(
        0.4124564, 0.3575761, 0.1804375,
        0.2126729, 0.7151522, 0.0721750,
        0.0193339, 0.1191920, 0.9503041);
    const float3x3 fromLms = float3x3(
         3.2404542, -1.5371385, -0.4985314,
        -0.9692660,  1.8760108,  0.0415560,
         0.0556434, -0.2040259,  1.0572252);
    const float3x3 bradford = float3x3(
         0.8951,  0.2664, -0.1614,
        -0.7502,  1.7135,  0.0367,
         0.0389, -0.0685,  1.0296);
    const float3x3 bradfordInverse = float3x3(
         0.9869929, -0.1470543,  0.1599627,
         0.4323053,  0.5183603,  0.0492912,
        -0.0085287,  0.0400428,  0.9684867);

    float3 xyz = mul(toLms, color);
    float3 lms = mul(bradford, xyz);
    lms *= lmsCoefficients;
    return mul(fromLms, mul(bradfordInverse, lms));
}

// ----------------------------------------------------------------------------
// Слой 2: лог-кодирование
// ----------------------------------------------------------------------------
//
// Своё, а не ACEScct и не лог AgX: обе чужие шкалы привязаны к чужим белым
// точкам и чужим примарам, и брать из них только кодирование значит тащить
// привязку, которой в проекте нет. Здесь шкала объявлена явно — стопы
// относительно средне-серого, нормированные в [0, 1].
//
// FodinaeSceneToeStops стопов ниже серого и FodinaeSceneHeadStops выше него.
// Диапазон выбран под сцену, а не под кинонегатив: 10 стопов вниз это
// серый/1024, 6.5 вверх — серый*90, чего хватает и на неон, и на тени.

static const float FodinaeMidGrey = 0.18;
static const float FodinaeSceneToeStops = 10.0;
static const float FodinaeSceneHeadStops = 6.5;
static const float FodinaeSceneStops = FodinaeSceneToeStops + FodinaeSceneHeadStops;

float3 FodinaeLogEncode(float3 linearColor)
{
    // Encode magnitude and carry the sign separately. `max(color, 1e-7)`
    // looked safe but silently turned every negative HDR intermediate into a
    // positive value, so even a neutral CDL was not an identity operation.
    // The signed representation keeps values outside the nominal range until
    // the display stage, where gamut/tone mapping is finally allowed.
    float3 magnitudeStops = log2(max(abs(linearColor), 1e-7) / FodinaeMidGrey);
    float3 encodedMagnitude =
        (magnitudeStops + FodinaeSceneToeStops) / FodinaeSceneStops;
    return sign(linearColor) * encodedMagnitude;
}

float3 FodinaeLogDecode(float3 logColor)
{
    float3 magnitude = abs(logColor);
    float3 stops = magnitude * FodinaeSceneStops - FodinaeSceneToeStops;
    return sign(logColor) * exp2(stops) * FodinaeMidGrey;
}

float3 FodinaeDisplayEncode(float3 linearColor)
{
    float3 magnitude = abs(linearColor);
    float3 encoded = lerp(
        magnitude * 12.92,
        1.055 * pow(magnitude, 1.0 / 2.4) - 0.055,
        step(0.0031308, magnitude));
    return sign(linearColor) * encoded;
}

float3 FodinaeDisplayDecode(float3 encodedColor)
{
    float3 magnitude = abs(encodedColor);
    float3 linearValue = lerp(
        magnitude / 12.92,
        pow(max(magnitude + 0.055, 0.0) / 1.055, 2.4),
        step(0.04045, magnitude));
    return sign(encodedColor) * linearValue;
}

// ----------------------------------------------------------------------------
// Слой 3: ASC CDL — стандарт обмена грейдом
// ----------------------------------------------------------------------------
//
// out = (in * slope + offset) ^ power, по каналам. Формулу описал American
// Society of Cinematographers, её понимает любой инструмент цветокоррекции, и
// грейд в этом виде вывозится файлом .cdl.
//
// Соответствие привычным словам: slope — усиление (gain), offset — подъём
// (lift), power — гамма средних тонов.
float3 ApplyCdl(float3 color, float3 slope, float3 offset, float3 power)
{
    float3 graded = color * slope + offset;
    // Отрицательное основание в дробной степени даёт NaN, и один такой пиксель
    // расползается по кадру временным накоплением. Но при power == 1 clamp не
    // нужен и вреден: нейтральный CDL обязан сохранить лог-значения ниже нуля,
    // иначе глубокие тени прижимаются к нижней границе рабочего диапазона.
    float3 powered = pow(max(graded, 0.0), max(power, 1e-3));
    float3 unitPower = 1.0 - step(1e-5, abs(power - 1.0));
    return lerp(powered, graded, unitPower);
}

float3 ApplyPrimaryWheels(
    float3 color,
    float3 lift,
    float3 gamma,
    float3 gain,
    float3 offset,
    float4 master)
{
    lift += master.xxx;
    gamma *= master.yyy;
    gain *= master.zzz;
    offset += master.www;
    float3 adjusted = color * gain + lift + offset;
    float3 powered = pow(max(adjusted, 0.0), max(gamma, 1e-3));
    float3 unitGamma = 1.0 - step(1e-5, abs(gamma - 1.0));
    return lerp(powered, adjusted, unitGamma);
}

float3 ApplySaturation(float3 color, float saturation, float3 lumaWeights)
{
    float luma = dot(color, lumaWeights);
    return lerp(float3(luma, luma, luma), color, saturation);
}

float3 ApplyVibrance(float3 color, float vibrance, float3 lumaWeights)
{
    float luma = dot(color, lumaWeights);
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float normalizedChroma = chroma / max(maximum, 1e-5);
    float strength = 1.0 - saturate(normalizedChroma);
    // Positive vibrance boosts weak colors; negative vibrance gently reduces
    // them while leaving already saturated colors close to their source.
    float factor = 1.0 + vibrance * strength;
    float3 adjusted = lerp(float3(luma, luma, luma), color, factor);
    float active = step(1e-5, abs(vibrance));
    return lerp(color, adjusted, active);
}

float3 ApplyContrast(float3 color, float contrast, float pivot)
{
    return (color - pivot) * (1.0 + contrast) + pivot;
}

float3 ApplyContrastControls(float3 color, float contrast, float4 controls, float3 controls2)
{
    float pivot = controls.x;
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    float shadowWeight = 1.0 - smoothstep(0.0, 0.5, luma);
    float highlightWeight = smoothstep(0.5, 1.0, luma);
    float3 adjusted = ApplyContrast(color, contrast, pivot);
    adjusted += controls.y * shadowWeight + controls.z * highlightWeight;
    adjusted += controls.w * shadowWeight + controls2.x * highlightWeight;

    float3 belowBlack = min(adjusted, 0.0);
    adjusted = max(adjusted, belowBlack / (1.0 + controls2.y * abs(belowBlack)));
    float3 aboveWhite = max(adjusted - 1.0, 0.0);
    adjusted = min(adjusted, 1.0) + aboveWhite / (1.0 + controls2.z * aboveWhite);
    return adjusted;
}

float HueDegrees(float3 color)
{
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float delta = maximum - minimum;

    float hue = 0.0;
    if (maximum == color.r)
    {
        hue = (color.g - color.b) / max(delta, 1e-6);
    }
    else if (maximum == color.g)
    {
        hue = (color.b - color.r) / max(delta, 1e-6) + 2.0;
    }
    else
    {
        hue = (color.r - color.g) / max(delta, 1e-6) + 4.0;
    }
    return frac(hue / 6.0) * 360.0 * step(1e-6, delta);
}

float HueDistanceDegrees(float a, float b)
{
    float distance = abs(a - b);
    return min(distance, 360.0 - distance);
}

float HueRangeWeight(float hue, float4 parameters)
{
    float distance = HueDistanceDegrees(hue, parameters.x);
    float feather = max(parameters.z, 0.0);
    float weight = step(distance, parameters.y);
    if (feather > 1e-5)
    {
        weight = 1.0 - smoothstep(parameters.y, parameters.y + feather, distance);
    }

    // A zero-width range with non-zero feather is still a valid soft
    // qualifier edge. Checking only the hard width made the control appear
    // enabled in the UI while producing an empty matte on the GPU.
    return weight * step(1e-5, parameters.y + feather);
}

float3 FodinaeHSVToRGB(float3 hsv)
{
    const float4 constants = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 permutation = abs(frac(hsv.xxx + constants.xyz) * 6.0 - constants.www);
    return hsv.z * lerp(constants.xxx, saturate(permutation - constants.xxx), hsv.y);
}

float3 ApplyGlobalHue(float3 color, float shiftDegrees)
{
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float saturation = chroma / max(maximum, 1e-5);
    float3 hsv = float3(HueDegrees(color) / 360.0, saturation, maximum);
    hsv.x = frac(hsv.x + shiftDegrees / 360.0);
    float3 adjusted = FodinaeHSVToRGB(hsv);
    return lerp(color, adjusted, step(1e-5, abs(shiftDegrees)) * step(1e-6, chroma));
}

float3 ApplyHueVsHue(float3 color, float4 parameters)
{
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float saturation = chroma / max(maximum, 1e-5);
    float hue = HueDegrees(color);
    float weight = HueRangeWeight(hue, parameters) * step(1e-6, chroma);
    float3 hsv = float3(hue / 360.0, saturation, maximum);
    hsv.x = frac(hsv.x + parameters.w / 360.0);
    float3 adjusted = FodinaeHSVToRGB(hsv);
    return lerp(color, adjusted, weight);
}

float3 ApplyHueVsSaturation(float3 color, float4 parameters)
{
    float multiplier = parameters.w;
    float chroma = max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
    float weight = HueRangeWeight(HueDegrees(color), parameters);
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    float3 adjusted = lerp(float3(luma, luma, luma), color, 1.0 + (multiplier - 1.0) * weight);
    float active = step(1e-5, abs(multiplier - 1.0)) *
        step(1e-5, parameters.y) * step(1e-6, chroma);
    return lerp(color, adjusted, active);
}

float3 ApplyHueVsLuminance(float3 color, float4 parameters)
{
    float amount = parameters.w;
    float weight = HueRangeWeight(HueDegrees(color), parameters);
    float3 adjusted = color * (1.0 + amount * weight);
    float active = step(1e-5, abs(amount)) * step(1e-6, max(color.r, max(color.g, color.b)));
    return lerp(color, adjusted, active);
}

float ScalarRangeWeight(float value, float4 parameters)
{
    float distance = abs(value - parameters.x);
    float feather = max(parameters.z, 0.0);
    float weight = step(distance, parameters.y);
    if (feather > 1e-5)
    {
        weight = 1.0 - smoothstep(parameters.y, parameters.y + feather, distance);
    }

    return weight * step(1e-5, parameters.y);
}

float3 ApplyLuminanceVsSaturation(float3 color, float4 parameters)
{
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    float normalizedLuma = saturate(luma / (1.0 + max(luma, 0.0)));
    float weight = ScalarRangeWeight(normalizedLuma, parameters);
    float multiplier = parameters.w;
    float3 adjusted = ApplySaturation(color, multiplier, float3(0.2126, 0.7152, 0.0722));
    float active = step(1e-5, abs(multiplier - 1.0));
    return lerp(color, adjusted, weight * active);
}

float3 ApplySaturationVsSaturation(float3 color, float4 parameters)
{
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float saturation = (maximum - minimum) / max(maximum, 1e-5);
    float weight = ScalarRangeWeight(saturate(saturation), parameters);
    float multiplier = parameters.w;
    float3 adjusted = ApplySaturation(color, multiplier, float3(0.2126, 0.7152, 0.0722));
    float active = step(1e-5, abs(multiplier - 1.0));
    return lerp(color, adjusted, weight * active);
}

float3 ApplyInputRange(float3 color, float blackPoint, float whitePoint, float recovery)
{
    float range = max(whitePoint - blackPoint, 1e-5);
    float3 normalized = (color - blackPoint) / range;
    float3 excess = max(normalized - 1.0, 0.0);
    float3 recovered = min(normalized, 1.0) + excess / (1.0 + excess * recovery * 8.0);
    normalized = lerp(normalized, recovered, step(1e-5, recovery));
    float active = max(
        step(1e-5, abs(blackPoint)),
        max(step(1e-5, abs(whitePoint - 1.0)), step(1e-5, recovery)));
    return lerp(color, normalized, active);
}

inline float EvaluateColorCurve(float value, float4 points[FODINAE_CURVE_MAX_POINTS], int pointCount)
{
    value = saturate(value);
    float result = value;
    bool authored = pointCount >= 2;
    bool identity = pointCount == 2 &&
        all(abs(points[0].xy - float2(0.0, 0.0)) < 1e-5) &&
        all(abs(points[1].xy - float2(1.0, 1.0)) < 1e-5);
    if (authored && !identity)
    {
        result = points[pointCount - 1].y;
        for (int index = 1; index < FODINAE_CURVE_MAX_POINTS; index++)
        {
            if (index >= pointCount)
            {
                break;
            }

            float2 left = points[index - 1].xy;
            float2 right = points[index].xy;
            if (value <= right.x)
            {
                float t = saturate((value - left.x) / max(right.x - left.x, 1e-5));
                if (_CurveInterpolation == 1)
                {
                    t = t * t * (3.0 - 2.0 * t);
                }

                result = lerp(left.y, right.y, t);
                break;
            }
        }
    }

    return saturate(result);
}

inline bool IsIdentityColorCurve(float4 points[FODINAE_CURVE_MAX_POINTS], int pointCount)
{
    bool identity = pointCount == 2;
    if (identity)
    {
        identity = all(abs(points[0].xy - float2(0.0, 0.0)) < 1e-5) &&
            all(abs(points[1].xy - float2(1.0, 1.0)) < 1e-5);
    }

    return identity;
}

float3 ApplySelectiveCurves(float3 color)
{
    float3 lumaWeights = float3(0.2126, 0.7152, 0.0722);
    float luma = dot(color, lumaWeights);
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float saturation = chroma / max(maximum, 1e-5);
    float hue = HueDegrees(color) / 360.0;

    if (!IsIdentityColorCurve(_HueVsHueCurve, _HueVsHueCurvePointCount))
    {
        float targetHue = EvaluateColorCurve(hue, _HueVsHueCurve, _HueVsHueCurvePointCount);
        float3 hsv = float3(targetHue, saturation, maximum);
        float3 adjusted = FodinaeHSVToRGB(hsv);
        color = lerp(color, adjusted, step(1e-6, chroma));
        luma = dot(color, lumaWeights);
    }

    if (!IsIdentityColorCurve(_HueVsSaturationCurve, _HueVsSaturationCurvePointCount))
    {
        float targetSaturation = EvaluateColorCurve(hue, _HueVsSaturationCurve, _HueVsSaturationCurvePointCount);
        float factor = targetSaturation / max(saturation, 1e-5);
        float3 adjusted = lerp(float3(luma, luma, luma), color, factor);
        color = lerp(color, adjusted, step(1e-6, chroma));
    }

    if (!IsIdentityColorCurve(_HueVsLuminanceCurve, _HueVsLuminanceCurvePointCount))
    {
        float targetLuma = EvaluateColorCurve(hue, _HueVsLuminanceCurve, _HueVsLuminanceCurvePointCount);
        float factor = targetLuma / max(abs(luma), 1e-5);
        color = lerp(color, color * factor, step(1e-5, abs(luma)));
    }

    if (!IsIdentityColorCurve(_LuminanceVsSaturationCurve, _LuminanceVsSaturationCurvePointCount))
    {
        float input = saturate(luma / (1.0 + max(luma, 0.0)));
        float targetSaturation = EvaluateColorCurve(
            input,
            _LuminanceVsSaturationCurve,
            _LuminanceVsSaturationCurvePointCount);
        float factor = targetSaturation / max(saturation, 1e-5);
        float3 adjusted = lerp(float3(luma, luma, luma), color, factor);
        color = lerp(color, adjusted, step(1e-6, chroma));
    }

    if (!IsIdentityColorCurve(_SaturationVsSaturationCurve, _SaturationVsSaturationCurvePointCount))
    {
        float targetSaturation = EvaluateColorCurve(
            saturate(saturation),
            _SaturationVsSaturationCurve,
            _SaturationVsSaturationCurvePointCount);
        float factor = targetSaturation / max(saturation, 1e-5);
        float3 adjusted = lerp(float3(luma, luma, luma), color, factor);
        color = lerp(color, adjusted, step(1e-6, chroma));
    }

    return color;
}

inline float3 ApplyDisplayCurves(float3 color)
{
    // The identity curve must remain a true no-op for HDR and negative
    // intermediate values. EvaluateColorCurve is display-domain bounded, so
    // avoid entering it until a user-authored curve is present.
    bool identity = IsIdentityColorCurve(_MasterCurve, _MasterCurvePointCount) &&
        IsIdentityColorCurve(_RedCurve, _RedCurvePointCount) &&
        IsIdentityColorCurve(_GreenCurve, _GreenCurvePointCount) &&
        IsIdentityColorCurve(_BlueCurve, _BlueCurvePointCount);
    float3 result = color;
    if (!identity)
    {
        float3 nonNegative = max(color, 0.0);
        float luma = dot(nonNegative, float3(0.2126, 0.7152, 0.0722));
        if (luma > 1e-5)
        {
            float mappedLuma = EvaluateColorCurve(luma, _MasterCurve, _MasterCurvePointCount);
            result = nonNegative * (mappedLuma / luma);
            result = float3(
                EvaluateColorCurve(result.r, _RedCurve, _RedCurvePointCount),
                EvaluateColorCurve(result.g, _GreenCurve, _GreenCurvePointCount),
                EvaluateColorCurve(result.b, _BlueCurve, _BlueCurvePointCount));
        }
    }

    return result;
}

float3 ApplyDisplayRollOff(float3 color)
{
    float toe = saturate(_ContrastControls2.y);
    float shoulder = saturate(_ContrastControls2.z);
    float3 result = color;
    if (toe > 1e-5)
    {
        float3 belowBlack = min(result, 0.0);
        // Compress only the out-of-range shadow excursion. The previous
        // subtraction could cross zero when toe was strong and turn a
        // negative signal into a positive one instead of a soft clip.
        float3 compressedBlack = belowBlack /
            (1.0 + toe * 8.0 * abs(belowBlack));
        result = max(result, compressedBlack);
    }

    if (shoulder > 1e-5)
    {
        float3 excess = max(result - 1.0, 0.0);
        result = min(result, 1.0) + excess / (1.0 + excess * shoulder * 8.0);
    }

    return result;
}

inline float3 ApplyDisplayTransform(float3 color)
{
    // DisplayTransform.None is a deliberate exact bypass. This keeps the
    // neutral/default pipeline bit-identical while allowing the authored DRT
    // to use the complete display-grade controls when enabled.
    float3 result = color;
    if ((int)_DisplayGrade0.w != 0)
    {
      float whitePoint = max(_DisplayGrade0.x, 1e-4);
    float greyOut = clamp(_DisplayGrade0.y, 0.05, 0.5);
    float slope = max(_DisplayGrade0.z, 1e-3);
    float shoulderPower = max(_DisplayGrade1.x, 1e-3);
    float toePower = max(_DisplayGrade1.y, 1e-3);
    float toeStops = clamp(_DisplayGrade1.z, 0.0, 32.0);
    float pathAmount = saturate(_DisplayGrade1.w);
    float pathPower = max(_DisplayGradePathPower, 1e-3);

      float3 scene = max(color, 0.0) / whitePoint;
    float toeFloor = exp2(-toeStops);
    scene = max(scene - toeFloor, 0.0) / max(1.0 - toeFloor, 1e-4);
    scene = pow(scene, 1.0 / max(slope * toePower, 1e-3));

    float3 mapped = scene / (1.0 + scene);
    float midInput = (0.18 / whitePoint - toeFloor) / max(1.0 - toeFloor, 1e-4);
    midInput = pow(max(midInput, 0.0), 1.0 / max(slope * toePower, 1e-3));
    float midMapped = midInput / (1.0 + midInput);
    mapped *= greyOut / max(midMapped, 1e-4);

    mapped = 1.0 - pow(max(1.0 - saturate(mapped), 0.0), shoulderPower);
    // Path-to-white is a bounded display roll-off. Cap only its local input
    // before the high exponent so extreme finite HDR values cannot overflow
    // into Inf; the scene value itself remains untouched for the other terms.
    float3 pathInput = min(max(scene, 0.0), 16.0);
    float3 pathToWhite = 1.0 - exp(-pow(pathInput, pathPower));
    mapped = lerp(mapped, pathToWhite, pathAmount);
      result = max(mapped, 0.0);
    }

    return result;
}

float ScalarQualifierWeight(float value, float center, float width, float softness)
{
    float distance = abs(value - center);
    float result = step(distance, width);
    if (softness > 1e-5)
    {
        result = 1.0 - smoothstep(width, width + softness, distance);
    }

    return result * step(1e-5, width + softness);
}

inline float QualifierMask(float3 color)
{
    float result = 0.0;
    if (abs(_Qualifier0.w) >= 0.5)
    {
      float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float saturation = chroma / max(maximum, 1e-5);
    float luma = dot(max(color, 0.0), float3(0.2126, 0.7152, 0.0722));
    float hue = HueDegrees(color);
    float mask = HueRangeWeight(hue, _Qualifier0);
    for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++)
    {
        if (sampleIndex >= _QualifierHueSampleCount)
        {
            break;
        }

        mask = max(mask, HueRangeWeight(hue, _QualifierHueSamples[sampleIndex]));
    }
    mask *= ScalarQualifierWeight(
        saturation, _Qualifier1.x, _Qualifier1.y, _Qualifier1.z);
    mask *= ScalarQualifierWeight(
        saturate(luma), _Qualifier1.w, _Qualifier2.x, _Qualifier2.y);
    mask = _Qualifier0.w < 0.0 ? 1.0 - mask : mask;
      result = mask;
    }

    return result;
}

float3 ApplyQualifier(float3 color)
{
    float mask = QualifierMask(color);
    float3 result = color;
    if (mask > 1e-5)
    {
        result = color * exp2(_Qualifier3.x * mask);
    if (abs(_Qualifier3.y) > 0.001 || abs(_Qualifier3.z) > 0.001)
    {
        float3 whiteBalanced = ApplyWhiteBalance(
            result,
            WhiteBalanceCoefficients(_Qualifier3.y, _Qualifier3.z));
        result = lerp(result, whiteBalanced, mask);
    }
    result *= lerp(float3(1.0, 1.0, 1.0), _Qualifier6.rgb, mask);
    result += _Qualifier4.rgb * mask;
    float3 gamma = lerp(float3(1.0, 1.0, 1.0), _Qualifier5.rgb, mask);
    float3 powered = pow(max(result, 0.0), max(gamma, 1e-3));
    float3 unitGamma = 1.0 - step(1e-5, abs(gamma - 1.0));
    result = lerp(powered, result, unitGamma);
    result = ApplySaturation(
        result,
        lerp(1.0, _Qualifier2.w, mask),
        float3(0.2126, 0.7152, 0.0722));
        result = ApplyGlobalHue(result, _Qualifier2.z * mask);
    }

    return result;
}

inline float3 ApplyCubeLut(float3 color)
{
    float3 lutColor = color;
    if (_GradeLutParams.x > 1e-5)
    {
        float3 lookup = saturate(
            (color - _GradeLutDomainMin) /
            max(_GradeLutDomainMax - _GradeLutDomainMin, 1e-5));
        if (_GradeLutParams.z > 0.5)
        {
            float3 low = lookup * 12.92;
            float3 high = 1.055 * pow(max(lookup, 0.0), 1.0 / 2.4) - 0.055;
            lookup = lerp(high, low, step(lookup, 0.0031308));
        }

    // Initialize before the 1D/3D branch so Metal's definite-assignment
    // analysis cannot produce an undefined value if a future LUT mode is
    // added without a matching branch.
    if (_GradeLutParams.y < 1.5)
    {
        lutColor = _GradeLut1D.SampleLevel(sampler_GradeLut1D_LinearClamp, float2(lookup.r, 0.5), 0).rgb;
        lutColor.g = _GradeLut1D.SampleLevel(sampler_GradeLut1D_LinearClamp, float2(lookup.g, 0.5), 0).g;
        lutColor.b = _GradeLut1D.SampleLevel(sampler_GradeLut1D_LinearClamp, float2(lookup.b, 0.5), 0).b;
    }
    else
    {
        float lutSize = max(_GradeLutParams.w, 2.0);
        float3 position = lookup * (lutSize - 1.0);
        float3 cell = floor(position);
        float3 fraction = position - cell;
        float3 texel = 1.0 / lutSize;
        float3 baseUv = (cell + 0.5) * texel;
        float3 c000 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv, 0).rgb;
        float3 c100 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(texel.x, 0, 0), 0).rgb;
        float3 c010 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(0, texel.y, 0), 0).rgb;
        float3 c001 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(0, 0, texel.z), 0).rgb;
        float3 c110 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(texel.x, texel.y, 0), 0).rgb;
        float3 c101 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(texel.x, 0, texel.z), 0).rgb;
        float3 c011 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(0, texel.y, texel.z), 0).rgb;
        float3 c111 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + texel, 0).rgb;
        if (fraction.x >= fraction.y)
        {
            lutColor = fraction.y >= fraction.z
                ? c000 + (c100 - c000) * fraction.x + (c110 - c100) * fraction.y + (c111 - c110) * fraction.z
                : fraction.x >= fraction.z
                    ? c000 + (c100 - c000) * fraction.x + (c101 - c100) * fraction.z + (c111 - c101) * fraction.y
                    : c000 + (c001 - c000) * fraction.z + (c101 - c001) * fraction.x + (c111 - c101) * fraction.y;
        }
        else
        {
            lutColor = fraction.x >= fraction.z
                ? c000 + (c010 - c000) * fraction.y + (c110 - c010) * fraction.x + (c111 - c110) * fraction.z
                : fraction.y >= fraction.z
                    ? c000 + (c010 - c000) * fraction.y + (c011 - c010) * fraction.z + (c111 - c011) * fraction.x
                    : c000 + (c001 - c000) * fraction.z + (c011 - c001) * fraction.y + (c111 - c011) * fraction.x;
        }
    }

    if (_GradeLutParams.z > 0.5)
    {
        float3 low = lutColor / 12.92;
        float3 high = pow(max((lutColor + 0.055) / 1.055, 0.0), 2.4);
        lutColor = lerp(high, low, step(lutColor, 0.04045));
    }

    }

    return lerp(color, lutColor, saturate(_GradeLutParams.x));
}

float3 DecodeGradeTransfer(float3 color, int transfer)
{
    float3 result = color;
    if (transfer == 1)
    {
        float3 low = color / 12.92;
        float3 high = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
        result = lerp(high, low, step(color, 0.04045));
    }

    if (transfer == 2)
    {
        // SMPTE ST 2084 inverse EOTF, normalized to the project HDR range.
        const float m1 = 2610.0 / 16384.0;
        const float m2 = 2523.0 / 32.0;
        const float c1 = 3424.0 / 4096.0;
        const float c2 = 2413.0 / 128.0;
        const float c3 = 2392.0 / 128.0;
        float3 encoded = min(max(color, 0.0), 4.0);
        float3 powered = pow(encoded, 1.0 / m2);
        result = pow(max(powered - c1, 0.0) / max(c2 - c3 * powered, 1e-5), 1.0 / m1);
    }

    if (transfer == 3)
    {
        // ARIB STD-B67 inverse OETF. Negative encoded values are treated as
        // black at this transfer-function boundary; this prevents NaN when a
        // diagnostic pass deliberately feeds an out-of-range signal into it.
        float3 encoded = max(color, 0.0);
        float3 safeEncoded = min(encoded, 4.0);
        float3 low = safeEncoded * safeEncoded / 3.0;
        // HLG is nominally defined on [0, 1]. HDR intermediates can be much
        // larger, so cap only the exponential branch to keep finite values
        // from turning into Inf while preserving the complete nominal range.
        float3 exponentialInput = safeEncoded;
        float3 high =
            (exp((exponentialInput - 0.5599107) / 0.17883277) + 0.28466892) / 12.0;
        result = lerp(high, low, step(encoded, 0.5));
    }

    return result;
}

inline float3 GradeRec709ToSpace(float3 color, int space)
{
    float3 result = color;
    if (space == 1)
    {
        result = mul(float3x3(
            0.8226, 0.1775, 0.0000,
            0.0332, 0.9668, 0.0000,
            0.0171, 0.0724, 0.9108), color);
    }

    if (space == 2)
    {
        result = mul(float3x3(
            0.6274, 0.3293, 0.0433,
            0.0691, 0.9195, 0.0114,
            0.0164, 0.0880, 0.8956), color);
    }

    return result;
}

inline float3 GradeSpaceToRec709(float3 color, int space)
{
    float3 result = color;
    if (space == 1)
    {
        result = mul(float3x3(
            1.2247, -0.2249, 0.0000,
            -0.0421, 1.0421, 0.0000,
            -0.0196, -0.0787, 1.0985), color);
    }

    if (space == 2)
    {
        result = mul(float3x3(
            1.6605, -0.5876, -0.0728,
            -0.1246, 1.1329, -0.0083,
            -0.0182, -0.1006, 1.1187), color);
    }

    return result;
}

float3 ApplyColorManagementInput(float3 color)
{
    color = DecodeGradeTransfer(color, (int)_ColorManagement1.x);
    color = GradeSpaceToRec709(color, (int)_ColorManagement0.x);
    color = GradeRec709ToSpace(color, (int)_ColorManagement0.y);
    return color;
}

float3 ApplyColorManagementOutput(float3 color)
{
    color = GradeSpaceToRec709(color, (int)_ColorManagement0.y);
    // URP owns the final transfer encoding. Keeping it out of this pass avoids
    // encoding sRGB twice while still making the declared output primaries explicit.
    return GradeRec709ToSpace(color, (int)_ColorManagement0.z);
}

#endif // FODINAE_COLOR_GRADING_INCLUDED
