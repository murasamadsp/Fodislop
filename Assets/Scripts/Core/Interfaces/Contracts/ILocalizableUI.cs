#nullable enable

namespace Fodinae.Core.Localization
{
    /// <summary>
    /// UI-сущность, которая умеет переприменять свой текст (статические ключи
    /// UXML + динамические лейблы). Регистрируется в <see cref="ILocalizationService"/>
    /// через RegisterLocalizable/UnregisterLocalizable: сервис применяет её при
    /// регистрации и при каждой смене языка — вьюхе не нужно самой подписываться
    /// на OnLanguageChanged и помнить про стартовое применение.
    /// </summary>
    public interface ILocalizableUI
    {
        /// <summary>Переприменяет весь текст сущности. Идемпотентен и безопасен
        /// для вызова до готовности дерева (внутренние гарды).</summary>
        void ApplyLocalizedText();
    }
}
