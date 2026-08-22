#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Networking.Auth;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Ворота входа главного меню.
    ///
    /// ВАЖНО о протоколе. В MinesProtocol нет ни логина, ни пароля, ни
    /// регистрации: <c>ClientHelloPacket</c> несёт токен из PlayerPrefs
    /// (пустой при первом запуске), а сервер в ответ присылает
    /// <c>AuthTokenPacket</c>, после чего клиент вызывает AuthorizeUI.
    ///
    /// Поэтому поля пароля, вкладка регистрации и EULA — заглушки по макету
    /// visual/main-menu-mirror: они отрисованы, но ни во что не отправляются,
    /// и экран честно сообщает об этом подсказкой. Реально работают два пути:
    /// «Войти» (обычное подключение с существующим или пустым токеном) и
    /// «Офлайн режим (Dummy)» (локальная песочница без сервера).
    ///
    /// Разметка живёт в Resources/UI/MainMenu.uxml, стили — в
    /// Resources/Styles/Auth.uss.
    /// </summary>
    public sealed class AuthGate
    {
        /// <summary>
        /// Согласие на авто-вход. Хранится отдельно от самого токена: токен
        /// сервер выдаёт сам при первом же подключении, то есть он есть почти
        /// всегда, — а вот «пускать без экрана входа» игрок должен разрешить
        /// явно. Значение по умолчанию 0: ворота показываются, пока галочку не
        /// поставили и не прошли вход хотя бы раз.
        /// </summary>
        private const string AutoLoginPrefsKey = "Auth.AutoLogin";

        private const string ActiveTabClass = "auth-tab--active";
        private const string HiddenFormClass = "auth-form--hidden";
        private const string HintWarnClass = "auth-hint--warn";

        private readonly VisualElement _loginForm;
        private readonly VisualElement _registerForm;
        private readonly Button _tabLogin;
        private readonly Button _tabRegister;
        private readonly TextField _login;
        private readonly Toggle _autoLogin;
        private readonly Label _hint;

        /// <summary>Вызывается, когда игрок прошёл ворота и меню можно показывать.</summary>
        public event Action? Passed;

        private AuthGate(
            VisualElement loginForm,
            VisualElement registerForm,
            Button tabLogin,
            Button tabRegister,
            TextField login,
            Toggle autoLogin,
            Label hint)
        {
            _loginForm = loginForm;
            _registerForm = registerForm;
            _tabLogin = tabLogin;
            _tabRegister = tabRegister;
            _login = login;
            _autoLogin = autoLogin;
            _hint = hint;
        }

        /// <summary>
        /// Собирает ворота из уже склонированного дерева. Возвращает null, если
        /// разметки нет — тогда меню просто работает как раньше.
        /// </summary>
        public static AuthGate? TryCreate(VisualElement tree)
        {
            var loginForm = tree.Q<VisualElement>("AuthLoginForm");
            var registerForm = tree.Q<VisualElement>("AuthRegisterForm");
            var tabLogin = tree.Q<Button>("AuthTabLogin");
            var tabRegister = tree.Q<Button>("AuthTabRegister");
            var login = tree.Q<TextField>("AuthLogin");
            var autoLogin = tree.Q<Toggle>("AuthAutoLogin");
            var hint = tree.Q<Label>("AuthHint");

            if (loginForm == null || registerForm == null ||
                tabLogin == null || tabRegister == null || login == null ||
                autoLogin == null || hint == null)
            {
                Debug.LogWarning("[AuthGate] Разметка ворот входа не найдена в MainMenu.uxml — экран пропущен.");
                return null;
            }

            var gate = new AuthGate(loginForm, registerForm, tabLogin, tabRegister, login, autoLogin, hint);
            gate.Bind(tree);
            return gate;
        }

        private void Bind(VisualElement tree)
        {
            _tabLogin.clicked += () => SelectTab(register: false);
            _tabRegister.clicked += () => SelectTab(register: true);

            tree.Q<Button>("AuthSubmitButton")!.clicked += Submit;

            var offline = tree.Q<Button>("AuthOfflineButton");
            if (offline != null)
            {
                offline.clicked += StartOffline;
            }

            var recover = tree.Q<Button>("AuthRecoverButton");
            if (recover != null)
            {
                recover.clicked += () => ShowHint(
                    "Восстановление доступа не предусмотрено протоколом: сессия привязана к токену устройства.",
                    warn: true);
            }

            _login.SetValueWithoutNotify(GenerateCallsign());
            _autoLogin.SetValueWithoutNotify(PlayerPrefs.GetInt(AutoLoginPrefsKey, 0) == 1);
        }

        /// <summary>
        /// Готовит форму входа. Если токен уже получен и игрок разрешил
        /// авто-вход, ворота сразу отдают Passed — повторять экран на каждом
        /// запуске незачем.
        ///
        /// Видимость слоя здесь не трогается: ею владеет GatewayController
        /// через состояние на корне, потому что состояние у ворот ровно одно
        /// и держать его в двух местах — способ показать два экрана разом.
        /// </summary>
        public void Show()
        {
            if (!GatewayDevFlags.ForceGates && AuthTokenManager.HasToken && _autoLogin.value)
            {
                Pass();
                return;
            }

            SelectTab(register: false);
        }

        private void SelectTab(bool register)
        {
            _tabLogin.EnableInClassList(ActiveTabClass, !register);
            _tabRegister.EnableInClassList(ActiveTabClass, register);
            _loginForm.EnableInClassList(HiddenFormClass, register);
            _registerForm.EnableInClassList(HiddenFormClass, !register);

            ShowHint(
                register
                    ? "Регистрация не предусмотрена протоколом: аккаунт заводится сервером автоматически при первом подключении."
                    : "Пароль протоколом не используется — вход выполняется по токену устройства.",
                warn: register);
        }

        private void Submit()
        {
            ShowHint("Подключение…", warn: false);
            Pass();
        }

        private void StartOffline()
        {
            ClientConfigManager? config = ClientConfigManager.Instance;
            if (config == null)
            {
                ShowHint("Конфигурация клиента недоступна — офлайн-режим не включён.", warn: true);
                return;
            }

            config.Config.UseDummyConnection = true;
            config.Save();
            ShowHint("Офлайн-режим: локальная песочница без сервера.", warn: false);
            Pass();
        }

        private void Pass()
        {
            // Согласие фиксируем только на выходе из ворот: до этого момента
            // галочка — намерение, а не решение.
            PlayerPrefs.SetInt(AutoLoginPrefsKey, _autoLogin.value ? 1 : 0);
            PlayerPrefs.Save();

            Passed?.Invoke();
        }

        private void ShowHint(string text, bool warn)
        {
            _hint.text = text;
            _hint.EnableInClassList(HintWarnClass, warn);
        }

        /// <summary>
        /// Позывной из отпечатка устройства — тот же приём, что и
        /// generateSeededCallsign() в макете, и он совпадает с реальной
        /// моделью: сервер и так опознаёт клиента по токену, а не по имени.
        /// </summary>
        private static string GenerateCallsign()
        {
            string seed = SystemInfo.deviceUniqueIdentifier;
            int hash = seed.GetHashCode();
            string[] clans = { "DVM", "VOID", "NEO", "CORE", "ORE", "HDS" };
            int number = Math.Abs(hash % 900) + 100;
            string clan = clans[Math.Abs(hash / 900) % clans.Length];
            return $"ШАХТЁР-{number} [{clan}]";
        }
    }
}
