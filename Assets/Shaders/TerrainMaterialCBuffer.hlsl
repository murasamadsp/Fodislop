#ifndef KERN_TERRAIN_MATERIAL_CBUFFER_INCLUDED
#define KERN_TERRAIN_MATERIAL_CBUFFER_INCLUDED

// ВСЁ, ЧТО ЗАВИСИТ ОТ МАТЕРИАЛА ТЕРРЕЙНА, ОБЪЯВЛЕНО ЗДЕСЬ, И ТОЛЬКО ЗДЕСЬ.
//
// SRP Batcher склеивает вызовы отрисовки только у шейдеров, где ни одно
// свойство материала не объявлено снаружи UnityPerMaterial, И где КАЖДЫЙ пасс
// объявляет этот блок одинаково. Пасс без блока или с другой раскладкой делает
// несовместимым весь шейдер целиком, а не только себя, — батчер молча
// выключается, и счётчик пакетов показывает ноль при трёх сотнях смен
// материала.
//
// `_BaseMap_TexelSize` Unity заводит сам под текстуру _BaseMap, то есть это
// свойство материала; стоя снаружи блока, оно ломало совместимость целиком.
//
// Раньше блок стоял в обоих пассах скопированным слово в слово, и совпадение
// держалось на дисциплине. Теперь оно механическое: файл один. Ни одно из этих
// свойств не читается в пассе поля материалов — блок стоит там ради раскладки,
// и убирать его как «мёртвый» нельзя.
CBUFFER_START(UnityPerMaterial)
    float4 _ShimmerColor;
    float4 _FlowScale;
    float _ShimmerSpeedScale;
    float _PulseSpeedScale;
    float4 _DebugColor;
    float _DebugMode;
    float4 _BaseMap_TexelSize;
    float4 _FlowMap_TexelSize;
    float4 _TerrainDecalAtlas_TexelSize;
    float _TerrainAtlasIndex;
    float4 _TerrainAtlas0_TexelSize;
    float4 _TerrainAtlas1_TexelSize;
    float4 _TerrainAtlas2_TexelSize;
    float4 _TerrainAtlas3_TexelSize;
    float4 _TerrainAtlas4_TexelSize;
    float4 _TerrainAtlas5_TexelSize;
    float4 _TerrainAtlas6_TexelSize;
    float4 _TerrainAtlas7_TexelSize;
CBUFFER_END

// Размер текселя атласа, из которого читается клетка.
//
// В режиме клеток атлас выбирает сама клетка, поэтому размер берётся по её
// слоту. Вне его рисуется накладка дверей — у неё материал на атлас, и слот
// один. Требует TerrainAtlasSampling.hlsl.
float4 TerrainMaterialAtlasTexelSize(int slot)
{
#if defined(KERN_TERRAIN_CELLS)
    return TerrainAtlasTexelSize(
        slot,
        _TerrainAtlas0_TexelSize,
        _TerrainAtlas1_TexelSize,
        _TerrainAtlas2_TexelSize,
        _TerrainAtlas3_TexelSize,
        _TerrainAtlas4_TexelSize,
        _TerrainAtlas5_TexelSize,
        _TerrainAtlas6_TexelSize,
        _TerrainAtlas7_TexelSize);
#else
    return _BaseMap_TexelSize;
#endif
}

#endif
