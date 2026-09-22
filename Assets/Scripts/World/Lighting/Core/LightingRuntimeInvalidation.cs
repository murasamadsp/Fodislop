#nullable enable

namespace Kern.World.Lighting;

/// <summary>
/// Рецепты пометки света грязным.
/// </summary>
///
/// Один и тот же набор флагов сбрасывается в четырёх местах: смена настроек
/// качества, смена debug-вида, правка констант света и смена клиентского
/// конфига. Разные наборы нужны потому, что «пересчитать с теми же полями» и
/// «пересчитать с нуля» — разные вещи, и разница между ними неочевидна.
internal static class LightingRuntimeInvalidation
{
    /// <summary>
    /// Пересчитать свет теми же полями. Нужно, когда изменилась величина,
    /// входящая в решение, но не его размерность: экспозиция сцены, флаг
    /// прохода. Сам размер поля и текстуры не трогаются.
    /// </summary>
    public static void ResetRadiance(LightingRuntimeState state)
    {
        state.HasRenderedLightState = false;
        state.HasStaticRadianceState = false;
        state.HasDynamicRadianceState = false;
        state.CompositeDirty = true;
    }

    /// <summary>
    /// Пересчитать с нуля: поле, композит и оба состояния радианса.
    /// </summary>
    public static void ResetFieldAndRadiance(LightingRuntimeState state)
    {
        state.FieldDirty = true;
        state.CompositeDirty = true;
        state.HasRenderedLightState = false;
        state.HasStaticRadianceState = false;
        state.HasDynamicRadianceState = false;
    }
}
