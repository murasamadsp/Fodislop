#ifndef KERN_TERRAIN_DEBUG_VIEW_INCLUDED
#define KERN_TERRAIN_DEBUG_VIEW_INCLUDED

#include "TerrainLightingData.hlsl"
#include "TerrainContour.hlsl"

// Отладочные виды террейна. Отдельно от _WorldLightDebugView: тот показывает
// свет, а тут разбираются термы самой клетки — силуэт, слой, кайма, лист.
//
// Смысл в том, чтобы не гадать, какой из множителей гасит пиксель. Каждый вид
// возвращает ровно один терм, без света и без атласа поверх.
int _TerrainDebugView;

static const int KERN_TERRAIN_DEBUG_OFF = 0;
static const int KERN_TERRAIN_DEBUG_RELIEF_RIM = 1;
static const int KERN_TERRAIN_DEBUG_FOREIGN_SIDES = 2;
static const int KERN_TERRAIN_DEBUG_COVERAGE = 3;
static const int KERN_TERRAIN_DEBUG_LAYER = 4;
static const int KERN_TERRAIN_DEBUG_ANCHORED = 5;
static const int KERN_TERRAIN_DEBUG_CELL_LOCAL = 6;
static const int KERN_TERRAIN_DEBUG_RELIEF_GROUP = 7;
static const int KERN_TERRAIN_DEBUG_CONTINUOUS_SHEET = 8;
static const int KERN_TERRAIN_DEBUG_AMBIENT_OCCLUSION = 9;

// Диапазон, а не «не ноль». Глобаль живёт в нативной части и переживает
// доменную перезагрузку: номер удалённого вида остаётся в ней и после того,
// как C# сбросился в Off. Пока проверка была «не ноль», такой осиротевший
// номер проваливался в последнюю ветку и заливал мир её цветом.
bool KernTerrainDebugActive()
{
    return _TerrainDebugView > KERN_TERRAIN_DEBUG_OFF &&
        _TerrainDebugView <= KERN_TERRAIN_DEBUG_AMBIENT_OCCLUSION;
}

// Стороны кодируются раздельными каналами, иначе «сверху и снизу» не
// отличить от «слева и справа» на глаз: верх — красный, низ — зелёный,
// левая — синий, правая — жёлтая примесь в красный и зелёный.
float3 KernTerrainForeignSideColor(float packedContour)
{
    int reliefCode = KernTerrainReliefCode(packedContour);
    if (reliefCode == 0)
    {
        return float3(0.15, 0.15, 0.15);
    }

    int foreignSides = (~(reliefCode - 1)) & 0x0F;
    float top = (foreignSides & 1) != 0 ? 1.0 : 0.0;
    float left = (foreignSides & 2) != 0 ? 1.0 : 0.0;
    float bottom = (foreignSides & 4) != 0 ? 1.0 : 0.0;
    float right = (foreignSides & 8) != 0 ? 1.0 : 0.0;
    return float3(
        max(top, right * 0.6),
        max(bottom, right * 0.6),
        left);
}

// Вид получает тот же разбор вершины, что и кадр. Раньше он собирал термы
// сам и показывал четвёртое мнение о том, что такое координата клетки: кайму
// он считал по UV тайла, тогда как кадр считал по клеточной координате, и
// подтвердить видом было нельзя ничего.
float3 KernTerrainDebugColor(
    TerrainSurfaceInputs surface,
    float coverage,
    float isForeground,
    float packedColumn,
    float ambientOcclusion)
{
    float2 cellLocal = surface.cellSample;
    float packedContour = surface.packedContour;
    float anchored = surface.anchored;
    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_RELIEF_RIM)
    {
        // Выключенная кайма красится отдельно. Иначе вид заливает мир
        // зелёным, и «кайма выключена» неотличимо от «кайма посчитана и
        // никого не трогает» — ровно та неоднозначность, из-за которой
        // белый кадр однажды уже нельзя было прочитать.
        if (_TerrainReliefRimEnabled < 0.5)
        {
            return float3(0.35, 0.25, 0.55);
        }

        // Зелёное — кайма не трогает пиксель, красное — гасит до предела.
        // Та же структура, что у кадра: выбрать «не ту» координату здесь
        // больше нечем.
        float rim = TerrainReliefRim(surface);
        return float3(1.0 - rim, rim, 0.35);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_FOREIGN_SIDES)
    {
        return KernTerrainForeignSideColor(packedContour);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_COVERAGE)
    {
        // Бирюза — пиксель принадлежит клетке, малиновый — вырезан.
        // Чёрно-белым «принадлежит» заливало экран белым и было
        // неотличимо от пересвета.
        return lerp(
            float3(0.95, 0.15, 0.55),
            float3(0.10, 0.85, 0.95),
            saturate(coverage));
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_LAYER)
    {
        return isForeground > 0.5
            ? float3(0.1, 0.9, 0.2)
            : float3(0.2, 0.3, 1.0);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_ANCHORED)
    {
        return anchored > 0.5
            ? float3(1.0, 0.85, 0.1)
            : float3(0.12, 0.12, 0.15);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_CELL_LOCAL)
    {
        // Координата внутри клетки. За пределами 0..1 канал уходит в
        // насыщение — сразу видно, где несущий прямоугольник шире клетки.
        return float3(saturate(cellLocal.x), saturate(cellLocal.y), 0.0);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_RELIEF_GROUP)
    {
        int reliefCode = KernTerrainReliefCode(packedContour);
        float shade = reliefCode == 0 ? 0.0 : (float)reliefCode / 17.0;
        return float3(shade, shade * 0.5, 1.0 - shade);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_CONTINUOUS_SHEET)
    {
        bool sheet = (((int)(packedColumn + 0.5)) & 32) != 0;
        return sheet
            ? float3(0.2, 0.9, 0.9)
            : float3(0.2, 0.1, 0.1);
    }

    // Единственный оставшийся вид; неизвестный номер сюда не доходит,
    // его отсекает KernTerrainDebugActive.
    //
    // Рампа, а не серая шкала. Серым «затенения нет» выходило белой заливкой
    // и было неотличимо от пересвеченного кадра: по скриншоту нельзя было
    // сказать, включён вид или нет. Отладочный вид обязан выглядеть как
    // отладочный вид при любом значении.
    float occlusion = 1.0 - ambientOcclusion;
    return float3(occlusion, 1.0 - occlusion, 0.35);
}

#endif
