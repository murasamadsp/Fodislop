#nullable enable

using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;
/// <summary>
/// Поправка вида под особенности цветовосприятия.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНО ОТ КОНТРОЛЛЕРА. Это единственное место во всём конвейере,
/// где вид меняется не ради вида, а ради читаемости: режим выбирает игрок,
/// и решение принимается не автором. Смешанное с обычным грейдом, оно
/// читалось бы как ещё один творческий слой — и его правили бы заодно с
/// прочими, хотя трогать его без нужды нельзя.
///
/// Функция чистая: ничего не спрашивает и ничего не толкает. Так её можно
/// проверить числами, не поднимая ни сцену, ни конвейер.
/// </remarks>
public static class ColorblindAdaptation
{
    /// <summary>Правит фильтр, контраст и насыщенность под режим.</summary>
    /// <remarks>
    /// Неизвестный режим возвращается без правки — молча и без исключения:
    /// испорченное значение в конфиге не повод гасить кадр. О нём сообщает
    /// вызывающая сторона, которой есть куда писать.
    /// </remarks>
    public static bool TryApply(
        int colorblindMode,
        ref Color filter,
        ref float contrast,
        ref float saturation)
    {
        switch (colorblindMode)
        {
            case 0:
                return true;
            case 1:
                filter = new Color(
                    filter.r * 0.8f + filter.g * 0.2f,
                    filter.g * 0.7f + filter.b * 0.3f,
                    filter.b);
                return true;
            case 2:
                filter = new Color(
                    filter.r * 0.6f + filter.g * 0.4f,
                    filter.g * 0.9f,
                    filter.b * 1.1f);
                return true;
            case 3:
                filter = new Color(
                    filter.r * 0.95f,
                    filter.g * 0.85f + filter.b * 0.15f,
                    filter.b * 0.5f + filter.r * 0.5f);
                return true;
            case 4:
                contrast += 0.35f;
                saturation += 0.2f;
                return true;
            default:
                return false;
        }
    }
}
