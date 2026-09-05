#nullable enable

namespace Fodinae.Rendering.PostProcessing;

/// <summary>
/// Отладочный вид готового кадра.
/// </summary>
/// <remarks>
/// Устройство повторяет <c>LightingEngine.DebugView</c> намеренно: смотреть на
/// кадр числами в проекте уже умеют так, и второй способ означал бы, что их
/// надо помнить оба. Значения совпадают с <c>_PostDebugView</c> в
/// <c>PostProcess.compute</c>.
/// </remarks>
public enum PostProcessDebugView
{
    /// <summary>Обычный кадр.</summary>
    None = 0,

    /// <summary>
    /// Ложный цвет по зонам экспозиции. Зелёное — ключевой тон, жёлтое и
    /// оранжевое — света, красное — пересвет, синее — провал в чёрное.
    /// </summary>
    FalseColor = 1,

    /// <summary>
    /// Отсечка. Кадр монохромный, горят только пиксели с потерянной
    /// информацией: красные упёрлись в потолок, синие сели в пол.
    /// </summary>
    Clipping = 2,
}
