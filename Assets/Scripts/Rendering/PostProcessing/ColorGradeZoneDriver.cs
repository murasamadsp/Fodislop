#nullable enable

using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;
/// <summary>
/// Толкает грейд зоны в проход по положению камеры.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНО ОТ <see cref="ColorGradeZones"/>. Тот набор — данные: он
/// умеет отвечать, каким должен быть грейд на такой-то высоте, и ничего не
/// знает ни про камеру, ни про проход. Здесь наоборот: только связывание,
/// без единого решения о виде. Смешанные, они дали бы тип, который нельзя
/// проверить числами, потому что он лезет в сцену.
/// </remarks>
public static class ColorGradeZoneDriver
{
    /// <summary>
    /// Собирает грейд по высоте камеры и отправляет его в проход.
    /// </summary>
    /// <remarks>
    /// Толкать каждый кадр дёшево: снимок неизменяем и сравнивается
    /// целиком, поэтому одинаковое значение до прохода не доходит.
    /// </remarks>
    public static ColorGradeZones.Resolution Push(ColorGradeZones? zones, Camera? camera)
    {
        ColorGradeZones.Resolution resolution = ColorGradeZones.Resolution.FromLook();
        if (zones != null && zones.Enabled && zones.Count > 0 && camera != null)
        {
            Vector3 camPos = camera.transform.position;
            resolution = zones.Resolve(resolution, camPos.x, camPos.y);
        }

        // Пустой набор, выключенный мастер-тумблер и потерянная камера —
        // тоже состояния, которые надо протолкнуть. Ранний return здесь
        // оставлял в проходе последнюю активную зону навсегда.
        PostProcessRuntimeState.SetColorGrade(resolution.Grade);
        return resolution;
    }
}
