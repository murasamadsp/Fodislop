#ifndef FODINAE_COLOR_GRADING_INCLUDED
#define FODINAE_COLOR_GRADING_INCLUDED

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
    float t1 = temperature / 65.0;
    float t2 = tint / 65.0;

    // Отрицательный сдвиг (в холод) уводит по x сильнее положительного:
    // планковский локус несимметричен, и равные шаги в кельвинах дают
    // неравные шаги в цветности.
    float x = 0.31271 - t1 * (t1 < 0.0 ? 0.1 : 0.05);
    float standardY = 2.87 * x - 3.0 * x * x - 0.27509507;
    float y = standardY + t2 * 0.05;

    float3 w1 = float3(0.949237, 1.03542, 1.08728);
    float divisor = max(y, 1e-5);
    float3 xyz = float3(x / divisor, 1.0, (1.0 - x - y) / divisor);
    float3 w2 = float3(
        dot(xyz, float3( 0.7328, 0.4296, -0.1624)),
        dot(xyz, float3(-0.7036, 1.6975,  0.0061)),
        dot(xyz, float3( 0.0030, 0.0136,  0.9834)));

    return w1 / max(w2, 1e-5);
}

float3 ApplyWhiteBalance(float3 color, float3 lmsCoefficients)
{
    // Пара обязана быть взаимно обратной: это одно преобразование туда и
    // обратно, и расхождение в любом знаке даёт постоянный цветовой сдвиг
    // даже при нейтральном балансе. Суммы строк здесь НЕ равны единице, и это
    // правильно: LMS не сохраняет белое покомпонентно — его сохраняет цепочка
    // целиком.
    const float3x3 toLms = float3x3(
        3.90405e-1, 5.49941e-1, 8.92632e-3,
        7.08416e-2, 9.63172e-1, 1.35775e-3,
        2.31082e-2, 1.28021e-1, 9.36245e-1);
    const float3x3 fromLms = float3x3(
         2.85847e+0, -1.62879e+0, -2.48910e-2,
        -2.10182e-1,  1.15820e+0,  3.24281e-4,
        -4.18120e-2, -1.18169e-1,  1.06867e+0);

    float3 lms = mul(toLms, color);
    lms *= lmsCoefficients;
    return mul(fromLms, lms);
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
    float3 stops = log2(max(linearColor, 1e-7) / FodinaeMidGrey);
    // Do not clamp here. A neutral encode/decode round-trip must preserve HDR
    // values outside the nominal working range; clipping belongs to the
    // display transform, not to the grading space.
    return (stops + FodinaeSceneToeStops) / FodinaeSceneStops;
}

float3 FodinaeLogDecode(float3 logColor)
{
    float3 stops = logColor * FodinaeSceneStops - FodinaeSceneToeStops;
    return exp2(stops) * FodinaeMidGrey;
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

float3 ApplySaturation(float3 color, float saturation, float3 lumaWeights)
{
    float luma = dot(color, lumaWeights);
    return lerp(float3(luma, luma, luma), color, saturation);
}

float3 ApplyContrast(float3 color, float contrast, float pivot)
{
    return (color - pivot) * (1.0 + contrast) + pivot;
}

#endif // FODINAE_COLOR_GRADING_INCLUDED
