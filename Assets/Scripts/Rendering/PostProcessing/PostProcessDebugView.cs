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

    /// <summary>Показывает пиксели вне display gamut.</summary>
    GamutWarning = 3,

    /// <summary>Монохромная яркостная составляющая.</summary>
    LumaOnly = 4,

    /// <summary>Насыщенность как диагностическая шкала.</summary>
    SaturationOnly = 5,

    /// <summary>Чёрно-белая matte qualifier-а.</summary>
    QualifierMatte = 6,

    /// <summary>Только красный канал.</summary>
    SoloRed = 7,

    /// <summary>Только зелёный канал.</summary>
    SoloGreen = 8,

    /// <summary>Только синий канал.</summary>
    SoloBlue = 9,

    /// <summary>Подсвечивает только clipped highlights поверх изображения.</summary>
    HighlightClipping = 10,

    /// <summary>Подсвечивает только clipped shadows поверх изображения.</summary>
    ShadowClipping = 11,
}
