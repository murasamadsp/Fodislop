#nullable enable

using System;

namespace Fodinae.Core.Localization
{
    public interface ILocalizationService
    {
        string CurrentLanguage { get; }

        event Action? OnLanguageChanged;

        void SetLanguage(string languageCode);

        string Get(string key, params object[] args);

        bool HasKey(string key);
    }
}
