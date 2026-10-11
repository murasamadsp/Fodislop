#nullable enable

using UnityEngine;

namespace Kern.Core;

// Кадр игровой камеры по зуму и соотношению сторон экрана. Константы —
// ProjectRuntimeContracts.Camera.
public static class CameraViewRules
{
    // zoom — половина короткой стороны кадра в клетках. Возвращает
    // orthographicSize (половину высоты), при которой короткая сторона равна
    // зуму, а длинная не превышает MaximumVisibleAspect коротких.
    public static float OrthographicSize(float zoom, float aspect)
    {
        if (aspect <= 0f || float.IsNaN(aspect))
        {
            return zoom;
        }

        float maxAspect = ProjectRuntimeContracts.Camera.MaximumVisibleAspect;
        if (aspect >= 1f)
        {
            // Альбом: высота — короткая сторона; ширина = высота · aspect.
            return aspect > maxAspect ? zoom * maxAspect / aspect : zoom;
        }

        // Портрет (окно редактора, сплит): короткая сторона — ширина.
        float halfHeight = zoom / aspect;
        return Mathf.Min(halfHeight, zoom * maxAspect);
    }
}
