#nullable enable

using System;

namespace Fodinae.Core.Localization
{
    public interface ILocalizationService
    {
        string CurrentLanguage { get; }

        event Action? OnLanguageChanged;

        /// <summary>
        /// Регистрирует локализуемую UI-сущность: сервис сразу применяет её
        /// текст и переприменяет при каждой смене языка. Вьюха не подписывается
        /// на OnLanguageChanged сама — это обязанность сервиса.
        /// </summary>
        void RegisterLocalizable(ILocalizableUI target);

        void UnregisterLocalizable(ILocalizableUI target);

        void SetLanguage(string languageCode);

        string Get(string key, params object[] args);

        bool HasKey(string key);
    }
}
