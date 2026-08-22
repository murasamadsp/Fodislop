#ifndef FODINAE_PLANET_SURFACE_FIELDS_INCLUDED
#define FODINAE_PLANET_SURFACE_FIELDS_INCLUDED

// Поля поверхности планеты, зависящие ТОЛЬКО от направления.
//
// Вынесены из PlanetSurface.shader в отдельный файл, потому что ровно этот же
// код исполняет запекающее вычислительное ядро. Если бы формулы жили в двух
// местах, любая правка одной из копий давала бы расхождение между запечённой
// текстурой и процедурной веткой — то есть тихую смену внешнего вида, которую
// заметишь только по скриншотам.
//
// Параметры передаются аргументами, а не читаются из CBUFFER: фрагментный
// шейдер держит их в UnityPerMaterial, вычислительный — в своём собственном
// буфере, и разделить раскладку нельзя.

#include "PlanetNoise.hlsl"

// Касательный базис на сфере. Нужен и при запекании (чтобы посчитать наклон),
// и в рантайме (чтобы из наклона собрать нормаль), поэтому он здесь, а не
// продублирован в двух местах: несовпадение базиса развернуло бы рельеф.
void FodinaePlanetTangentFrame(float3 dir, out float3 tangent, out float3 bitangent)
{
    float3 up = abs(dir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
    tangent = normalize(cross(up, dir));
    bitangent = cross(dir, tangent);
}

// Тектоническая высота без детального слоя.
//
// Детальный слой отделён намеренно: он умножается на detailFade, который
// зависит от угла обзора, — то есть не является функцией одного лишь
// направления и запечь его вместе с остальным нельзя. Смотри
// FodinaeElevationDetail.
//
// Доменный варп здесь несущий: неискажённый fBm даёт изотропные кляксы,
// похожие на облака, сколько октав в него ни клади. Искажение выборки другим
// fBm вытягивает их в ветвящиеся провинции, которые и читаются как кора.
float FodinaeElevationBase(
    float3 dir,
    float continentScale,
    float warpStrength,
    float ridgeScale,
    float mountainHeight)
{
    float3 c = dir * continentScale;
    float3 warp = float3(
        GradientNoise(c + float3(17.1, 3.2, 8.9)),
        GradientNoise(c + float3(43.7, 21.4, 2.6)),
        GradientNoise(c + float3(91.3, 12.8, 33.1)));

    float continents = Fbm(c + (warp * warpStrength), 3);
    float elev = saturate((continents * 0.5) + 0.5);

    // Хребты поднимаются только на уже приподнятой коре, поэтому горы образуют
    // пояса вдоль границ провинций, а не крапят котловины.
    float uplift = smoothstep(0.40, 0.78, elev);
    float ranges = RidgedFbm(dir * ridgeScale, 3);

    return elev + (ranges * uplift * mountainHeight);
}

/// Мелкое зерно поверхности. Остаётся процедурным: множитель detailFade
/// зависит от направления взгляда.
float FodinaeElevationDetail(float3 dir, float detailScale)
{
    return Fbm(dir * detailScale, 2);
}

// Упрощённая высота для конечных разностей. Октав вдвое меньше, чем в
// FodinaeElevationBase: она вычисляется трижды на пиксель, и полный набор
// октав здесь стоил бы дороже всего остального шейдера вместе взятого.
float FodinaeElevationNormal(
    float3 dir,
    float continentScale,
    float ridgeScale,
    float mountainHeight)
{
    float3 c = dir * continentScale;
    float continents = Fbm(c, 2);
    float elev = saturate((continents * 0.5) + 0.5);
    float ranges = RidgedFbm(dir * ridgeScale, 2);
    return saturate(elev + (ranges * mountainHeight));
}

// Наклон поверхности: конечная разность высоты по касательному базису.
// Он же задаёт нормаль и он же служит маской материалов (базальт на склонах,
// кора на плоском).
float2 FodinaeElevationSlope(
    float3 dir,
    float continentScale,
    float ridgeScale,
    float mountainHeight)
{
    float3 tangent;
    float3 bitangent;
    FodinaePlanetTangentFrame(dir, tangent, bitangent);

    const float eps = 0.006;
    float e0 = FodinaeElevationNormal(dir, continentScale, ridgeScale, mountainHeight);
    float eT = FodinaeElevationNormal(normalize(dir + (tangent * eps)), continentScale, ridgeScale, mountainHeight);
    float eB = FodinaeElevationNormal(normalize(dir + (bitangent * eps)), continentScale, ridgeScale, mountainHeight);

    return float2(eT - e0, eB - e0) / eps;
}

/// Сеть разломов. Из неё выводятся и сами рифты, и подсветка породы рядом с ними.
float FodinaeFaultField(float3 dir, float crackScale)
{
    return RidgedFbm(dir * crackScale, 4);
}

/// Разрыв рифтовой линии вдоль её длины. Остаётся процедурным: свободного
/// канала в запечённой карте нет, а стоит он три выборки шума.
float FodinaeCrackBreakup(float3 dir, float crackScale)
{
    return Fbm(dir * (crackScale * 2.7), 3);
}

// Полный набор запекаемых полей в одном float4 — ровно так они и лежат в
// кубической карте: xy — наклон, z — высота без детали, w — поле разломов.
float4 FodinaePackSurfaceFields(
    float3 dir,
    float continentScale,
    float warpStrength,
    float ridgeScale,
    float mountainHeight,
    float crackScale)
{
    float2 slope = FodinaeElevationSlope(dir, continentScale, ridgeScale, mountainHeight);
    float elevBase = FodinaeElevationBase(dir, continentScale, warpStrength, ridgeScale, mountainHeight);
    float fault = FodinaeFaultField(dir, crackScale);
    return float4(slope, elevBase, fault);
}

#endif // FODINAE_PLANET_SURFACE_FIELDS_INCLUDED
