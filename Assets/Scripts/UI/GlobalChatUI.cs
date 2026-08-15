#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class GlobalChatUI : MonoBehaviour
    {
        [Inject]
        private UIDocument _doc = null!;
        private VisualElement? _tree;
        private VisualElement? _panel;
        private ScrollView? _scrollView;
        private TextField? _inputField;
        private VisualElement? _internalInput;
        private Button? _sendButton;
        private Button? _colorButton;
        private VisualElement? _colorGrid;
        private System.Drawing.Color _currentColor = System.Drawing.Color.FromArgb(255, 200, 180, 100);
        private bool _isOpen = false;
        private const int MAX_MESSAGES = 20;
        private Controls.ChatInputBlinker? _blinker;
        private CancellationTokenSource? _idleCts;

        private static readonly System.Drawing.Color[] PresetColors =
        {
            System.Drawing.Color.White,
            System.Drawing.Color.FromArgb(255, 60, 60),
            System.Drawing.Color.FromArgb(60, 255, 60),
            System.Drawing.Color.FromArgb(60, 130, 255),
            System.Drawing.Color.FromArgb(255, 220, 60),
            System.Drawing.Color.FromArgb(60, 255, 255),
            System.Drawing.Color.FromArgb(255, 60, 255),
            System.Drawing.Color.FromArgb(255, 160, 60),
        };

        [Inject]
        private INetworkService _networkService = null!;

        [Inject]
        private IServerConfig _serverConfig = null!;

        [Inject]
        private IInputBlocker _inputBlocker = null!;

        protected void Start()
        {
            _serverConfig.OnInitialized += ApplyServerConfig;
            CreateUI();
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.None;
            }

            if (_serverConfig.IsInitialized)
            {
                ApplyServerConfig();
            }

            try
            {
                _networkService.Send(new QueryChatHistoryPacket("global", 0));
            }
            catch (Exception ex)
            {
                GameErrorUI.ReportError("Не удалось запросить историю чата", ex);
            }
        }

        protected void OnDestroy()
        {
            if (_serverConfig != null)
            {
                _serverConfig.OnInitialized -= ApplyServerConfig;
            }

            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _blinker?.StopBlink();
            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void ApplyServerConfig()
        {
            if (_inputField != null && _serverConfig.IsInitialized)
            {
                _inputField.maxLength = _serverConfig.MaxGlobalChatLength;
            }
        }

        protected void Update()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            bool inputBlocked = _inputBlocker != null && _inputBlocker.IsInputBlocked;

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                if (inputBlocked && !_isOpen)
                {
                    return;
                }

                if (_isOpen || !ChatInput.IsFocused)
                {
                    Toggle();
                }

                return;
            }

            if (!_isOpen)
            {
                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                if (!inputBlocked)
                {
                    OnSendClicked();
                }

                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
            }
        }

        private void CreateUI()
        {
            var uiUxml = Resources.Load<VisualTreeAsset>("UI/GlobalChat");
            if (uiUxml != null)
            {
                VisualElement tree = uiUxml.CloneTree();
                tree.AddToClassList("ui-fullscreen");
                tree.pickingMode = PickingMode.Ignore;
                _tree = tree;
                _panel = tree.Q<VisualElement>("ChatPanel");
                _scrollView = tree.Q<ScrollView>("ChatScroll");
                _inputField = tree.Q<TextField>("ChatInput");
                _sendButton = tree.Q<Button>("SendButton");
                _colorButton = tree.Q<Button>("ColorButton");
                _colorGrid = tree.Q<VisualElement>("ColorGrid");

                if (_doc != null && _panel != null)
                {
                    _doc.rootVisualElement.Add(tree);
                }

                if (_inputField != null)
                {
                    _inputField.selectAllOnFocus = false;
                    _inputField.selectAllOnMouseUp = false;
                    _inputField.RegisterCallback<FocusEvent>(_ =>
                    {
                        StartBlink();
                        ChatInput.OnFocus();
                    });
                    _inputField.RegisterCallback<BlurEvent>(_ =>
                    {
                        StopBlink();
                        ChatInput.OnBlur();
                    });
                    _inputField.RegisterValueChangedCallback(_ => OnInputChanged());
                }

                if (_sendButton != null)
                {
                    _sendButton.clicked += OnSendClicked;
                }

                if (_colorButton != null)
                {
                    _colorButton.clicked += ToggleColorGrid;
                    _colorButton.style.backgroundColor = new Color(_currentColor.R / 255f, _currentColor.G / 255f, _currentColor.B / 255f);
                }

                if (_colorGrid != null)
                {
                    foreach (var c in PresetColors)
                    {
                        var swatch = new Button(() => SelectColor(c));
                        swatch.AddToClassList("gchat-swatch");
                        swatch.style.backgroundColor = new Color(c.R / 255f, c.G / 255f, c.B / 255f);
                        _colorGrid.Add(swatch);
                    }
                }

                _internalInput = _inputField != null
                    ? _inputField.Q<VisualElement>(className: "unity-text-field__input")
                    : null;
                if (_internalInput != null)
                {
                    _internalInput.AddToClassList("gchat-internal-input");
                }

                if (_inputField != null && _internalInput != null)
                {
                    _blinker = new Controls.ChatInputBlinker(_inputField, _internalInput);
                }
            }
        }

        private void OnSendClicked()
        {
            if (_inputField == null)
            {
                return;
            }

            string text = _inputField.value.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_serverConfig.IsInitialized)
            {
                var chatMaxLen = _serverConfig.MaxGlobalChatLength;
                if (text.Length > chatMaxLen)
                {
                    text = text.Substring(0, chatMaxLen);
                }
            }

            try
            {
                _networkService.Send(new MinesServer.Networking.Client.Packets.Chat.SendChatMessagePacket("global", text));
            }
            catch (Exception ex)
            {
                GameErrorUI.ReportError("Не удалось отправить сообщение в чат", ex);
            }

            _inputField.value = string.Empty;
            _inputField.Focus();
        }

        public void Toggle()
        {
            if (_isOpen)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Show()
        {
            _isOpen = true;
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.Flex;
            }

            _inputField?.Focus();
        }

        public void Hide()
        {
            _isOpen = false;
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.None;
            }

            if (_inputField != null)
            {
                _inputField.value = string.Empty;
                _inputField.Blur();
            }
        }

        private void StartBlink()
        {
            _blinker?.StartBlink();
        }

        private void StopBlink()
        {
            _blinker?.StopBlink();
            _idleCts?.Cancel();
        }

        private void OnInputChanged()
        {
            _blinker?.StopBlink();
            _idleCts?.Cancel();
            _idleCts = new CancellationTokenSource();
            var token = _idleCts.Token;
            DelayedStartBlink(token).Forget();
        }

        private async UniTaskVoid DelayedStartBlink(CancellationToken token)
        {
            await UniTask.Delay(500, cancellationToken: token);
            if (!token.IsCancellationRequested)
            {
                StartBlink();
            }
        }

        public void AddMessage(ChatMessagePacket msg)
        {
            if (_scrollView == null)
            {
                return;
            }

            var time = DateTime.Now.ToString("HH:mm");
            var nickHex = $"#{msg.NicknameColor.R:X2}{msg.NicknameColor.G:X2}{msg.NicknameColor.B:X2}";
            var msgHex = $"#{msg.MessageColor.R:X2}{msg.MessageColor.G:X2}{msg.MessageColor.B:X2}";

            var label = new Label($"<color=#888888>[{time}]</color> <color={nickHex}>{msg.PlayerName}</color>: <color={msgHex}>{msg.Message}</color>");
            label.AddToClassList("gchat-message");

            _scrollView.Add(label);

            while (_scrollView.childCount > MAX_MESSAGES)
            {
                _scrollView.RemoveAt(0);
            }

            _scrollView.scrollOffset = new Vector2(0, float.MaxValue);
        }

        private void ToggleColorGrid()
        {
            if (_colorGrid != null)
            {
                _colorGrid.style.display = _colorGrid.style.display == DisplayStyle.None
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private void SelectColor(System.Drawing.Color color)
        {
            _currentColor = color;
            if (_colorButton != null)
            {
                _colorButton.style.backgroundColor = new Color(color.R / 255f, color.G / 255f, color.B / 255f);
            }

            if (_colorGrid != null)
            {
                _colorGrid.style.display = DisplayStyle.None;
            }

            try
            {
                _networkService.Send(new ChangeChatColorPacket(color));
            }
            catch (Exception ex)
            {
                GameErrorUI.ReportError("Не удалось отправить изменение цвета чата", ex);
            }
        }
    }
}
