#nullable enable

using Kern.Core;

namespace Kern.UI;

/// <summary>
/// Масштаб мировых меток — ников и облаков локального чата.
///
/// Метка живёт в панели, а не в мире, и USS задаёт её кегль в пикселях панели.
/// Размер панели от зума не зависит: камера меняет только
/// <c>camera.orthographicSize</c>. Поэтому при отдалении робот на экране
/// сжимается, а текст над ним остаётся прежних 12 px — визуально ник и
/// облако «разрастаются» вместе с миром, хотя в мире они не менялись.
///
/// Чтобы размер метки был постоянен в мире, кегль умножается на
/// <c>ReferenceOrthographicSize / orthographicSize</c>. Панельный
/// <c>PanelSettings.scale</c> в это отношение не входит и сокращается:
/// пиксели панели — это пиксели экрана, делённые на масштаб панели, а
/// пикселей на мировую единицу тоже screenHeight / (2 * orthographicSize).
/// Поэтому результат одинаков и при 1920x1080, и на HiDpi, и при любом
/// пользовательском масштабе интерфейса.
///
/// <c>PanelSettings.scale</c> остаётся единственным способом изменить
/// кегль меток для игрока целиком: мировые метки складываются с ним, а не
/// заменяют его.
/// </summary>
public static class WorldLabelScale
{
    /// <summary>
    /// Множитель кегля мировой метки для текущего зума камеры.
    /// </summary>
    /// <param name="orthographicSize">Половина видимой высоты камеры в клетках.</param>
    /// <param name="scale">100% при опорном зуме, меньше при отдалении.</param>
    /// <returns>
    /// false, если зум непригоден: подставлять прошлый или единичный масштаб
    /// нельзя — это вернуло бы ровно тот визуальный дефект, который чинится.
    /// </returns>
    public static bool TryFor(float orthographicSize, out float scale)
    {
        if (float.IsNaN(orthographicSize) || float.IsInfinity(orthographicSize) ||
            orthographicSize <= 0f)
        {
            scale = 0f;
            return false;
        }

        scale = ProjectRuntimeContracts.Camera.ReferenceOrthographicSize / orthographicSize;
        return true;
    }
}
