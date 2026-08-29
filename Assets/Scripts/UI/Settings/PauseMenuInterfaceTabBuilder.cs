#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Localization;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Interface tab in the Pause Menu.
/// </summary>
internal sealed class PauseMenuInterfaceTabBuilder
{
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _clientConfig;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public PauseMenuInterfaceTabBuilder(
        UIDocument doc,
        IClientConfigManager clientConfig,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _doc = doc;
        _clientConfig = clientConfig;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView interfaceScroll)
    {
        VisualElement interfaceSection = interfaceScroll.Q<VisualElement>("InterfaceSection") ??
            throw new InvalidOperationException("[PauseMenu] InterfaceSection is missing from PauseMenu.uxml.");

        interfaceSection.Add(PauseMenuUIFactory.CreateSlider(
            _loc.Get("menu.settings.ui_scale"),
            _clientConfig.Config.UIScale,
            v =>
            {
                _clientConfig.UpdateAndSave(config => config.UIScale = v);

                // The panel scale is what actually resizes the live UI;
                // saving alone would only take effect on the next launch.
                if (_doc != null && _doc.panelSettings != null)
                {
                    _doc.panelSettings.scale = v;
                }
            },
            0.5f,
            2f));

        // Язык интерфейса. Применяется сразу: SetLanguage сохраняет выбор
        // в конфиг и стреляет OnLanguageChanged, на который подписаны все
        // экраны — они пересобирают свои тексты (PauseMenu пересобирает
        // дерево целиком через ApplyLocalizedText).
        var languageRow = new VisualElement();
        languageRow.AddToClassList("pause-slider-container");
        var languageLabel = new Label(_loc.Get("settings.interface.language"));
        languageLabel.AddToClassList("pause-slider-label");
        languageRow.Add(languageLabel);

        var languageDropdown = new DropdownField();
        languageDropdown.choices = new List<string>
        {
            _loc.Get("settings.interface.language.ru"),
            _loc.Get("settings.interface.language.en"),
        };
        languageDropdown.index = _loc.CurrentLanguage == "en" ? 1 : 0;
        languageDropdown.RegisterValueChangedCallback(_ =>
        {
            string code = languageDropdown.index == 1 ? "en" : "ru";
            if (code != _loc.CurrentLanguage)
            {
                _loc.SetLanguage(code);
            }
        });
        _refreshers.Add(() =>
        {
            languageDropdown.index = _loc.CurrentLanguage == "en" ? 1 : 0;
        });
        languageRow.Add(languageDropdown);
        interfaceSection.Add(languageRow);

        return interfaceScroll;
    }
}
