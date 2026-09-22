#ifndef KERN_PIXEL_ART_FILTERING_INCLUDED
#define KERN_PIXEL_ART_FILTERING_INCLUDED

// Тумблер режима выборки. Ноль — ближайшая без сглаживания,
// единица — со сглаженной границей текселя. Раздаётся глобально
// из DisplayManager: террейн и сущности рисуются разными
// материалами, часть из них создаётся в рантайме.
float _PixelArtFiltering;

// Сглаженная ближайшая выборка.
//
// ОТКУДА МУАР. Тайл занимает 32 текселя, а на экране тексель
// занимает дробное число пикселей — при высоте 1080 и обычном
// зуме около 4.8. Ближайшая выборка обязана в этом случае
// какие-то строки текселей вывести дважды, а какие-то потерять:
// на регулярной кладке это муар, и он ползёт вместе с камерой.
//
// Сглаживание идёт по ширине пикселя, а не по фиксированной
// доле текселя: иначе на приближении картинка размывалась бы
// тем сильнее, чем крупнее тексель, — а нужно ровно обратное.
float2 PixelArtSampleUV(float2 uv, float2 textureSize)
{
    if (_PixelArtFiltering < 0.5)
    {
        return uv;
    }

    float2 uvTexels = uv * textureSize;
    float2 seam = floor(uvTexels + 0.5);
    float2 pixelWidth = max(fwidth(uvTexels), 1e-5);
    uvTexels = seam + clamp((uvTexels - seam) / pixelWidth, -0.5, 0.5);
    return uvTexels / textureSize;
}

// Shared texture-sampling entry point used by entity shaders. Keep the
// filtering math in PixelArtSampleUV so terrain and entities use the same
// pixel-grid correction without duplicating the implementation.
half4 PixelArtSample(TEXTURE2D_PARAM(tex, sampler_PointClamp), float2 uv, float2 textureSize)
{
    float2 filteredUV = PixelArtSampleUV(uv, textureSize);
    return SAMPLE_TEXTURE2D(tex, sampler_PointClamp, filteredUV);
}

#endif
