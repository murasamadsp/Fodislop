#nullable enable

using System;

namespace Kern.Core;

[Serializable]
public sealed class InterfaceSettings
{
    public const float UIScaleMin = 0.5f;
    public const float UIScaleMax = 2.5f;

    [SettingRange(UIScaleMin, UIScaleMax)]
    [SettingLabel("menu.settings.ui_scale")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "PauseMenu / UIDocument panelSettings.scale")]
    public float UIScale = 1f;

    [SettingUnbounded("Код языка из перечня локализаций; проверяется списком.")]
    [SettingLabel("settings.interface.language")]
    [SettingConsumer(SettingConsumerTarget.LocalizationService, "LocalizationService.SetLanguage")]
    public string Language = "ru";

    // Дефолтная схема управления: клавиатура (WASD / стрелки). Используется
    // кнопкой «Сбросить до стандартных» на вкладке «Управление».
    public const int DefaultControlScheme = 0;

    [SettingRange(0f, 1f)]
    [SettingLabel("gateway.onb.controls_scheme_label")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "Controls / Onboarding scheme")]
    public int ControlScheme;

    [SettingUnbounded("Тумблер автовхода в воротах авторизации.")]
    [SettingLabel("gateway.auth.auto_login")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "AuthGate auto sign-in toggle")]
    public bool AutoLogin;

    [SettingUnbounded("Флаг пройденного онбординга; внутреннего тумблера нет.")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "GatewayController onboarding gate")]
    public bool OnboardingDone;

    // Привязки клавиш клавиатурной схемы (вкладка «Управление» настроек
    // паузы). Значения — имена enum UnityEngine.InputSystem.Key;
    // PlayerInputHandler читает их из живого конфига, вкладка настроек
    // перебиндивает по клику, «Сбросить до стандартных» возвращает дефолты.
    // Неизвестное имя (правка конфига руками) откатывается к дефолту
    // действия на месте использования, поэтому валидатор их не проверяет.
    public const string DefaultKeyDig = "Space";
    public const string DefaultKeyAutoDig = "E";
    public const string DefaultKeyAggression = "L";
    public const string DefaultKeyGeo = "G";
    public const string DefaultKeyHeal = "V";
    public const string DefaultKeyBuildCyan = "Y";
    public const string DefaultKeyBuildGray = "H";
    public const string DefaultKeyBuildGreen = "F";
    public const string DefaultKeyBuildWhite = "J";

    public string KeyDig = DefaultKeyDig;
    public string KeyAutoDig = DefaultKeyAutoDig;
    public string KeyAggression = DefaultKeyAggression;
    public string KeyGeo = DefaultKeyGeo;
    public string KeyHeal = DefaultKeyHeal;
    public string KeyBuildCyan = DefaultKeyBuildCyan;
    public string KeyBuildGray = DefaultKeyBuildGray;
    public string KeyBuildGreen = DefaultKeyBuildGreen;
    public string KeyBuildWhite = DefaultKeyBuildWhite;
}
