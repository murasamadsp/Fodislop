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
        private UIDocument? _doc;
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

        [Inject]
        private INetworkService _networkService = null!;

        [Inject]
        private IServerConfig _serverConfig = null!;

        protected void OnDestroy()
        {
            _idleCts?.Cancel();
            _idleCts?.Dispose();
        }

        protected void Start()
        {
            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                return;
            }

            CreateUI();
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.None;
            }
            _networkService.Send(new QueryChatHistoryPacket("global", 0));
        }

        protected void Update()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
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
                OnSendClicked();
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
            }
        }

        private void CreateUI()
        {
            _panel = new VisualElement();
            _panel.AddToClassList("gchat-panel");

            var header = new Label("Глобальный чат");
            header.AddToClassList("gchat-header");
            _panel.Add(header);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.AddToClassList("gchat-scroll");
            _scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _panel.Add(_scrollView);

            var bottomRow = new VisualElement();
            bottomRow.AddToClassList("gchat-bottom-row");

            _inputField = new TextField();
            _inputField.selectAllOnFocus = false;
            _inputField.selectAllOnMouseUp = false;
            _inputField.AddToClassList("gchat-input");
            _inputField.maxLength = _serverConfig.MaxGlobalChatLength;
            bottomRow.Add(_inputField);

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

            _sendButton = new Button(OnSendClicked);
            _sendButton.text = ">";
            _sendButton.AddToClassList("gchat-send-button");
            bottomRow.Add(_sendButton);

            _colorButton = new Button(ToggleColorGrid);
            _colorButton.AddToClassList("gchat-color-button");
            _colorButton.style.backgroundColor = new Color(_currentColor.R / 255f, _currentColor.G / 255f, _currentColor.B / 255f);
            bottomRow.Add(_colorButton);

            _panel.Add(bottomRow);

            _colorGrid = new VisualElement();
            _colorGrid.AddToClassList("gchat-color-grid");
            _colorGrid.style.display = DisplayStyle.None;

            var presetColors = new System.Drawing.Color[]
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

            foreach (var c in presetColors)
            {
                var swatch = new Button(() => SelectColor(c));
                swatch.AddToClassList("gchat-swatch");
                swatch.style.backgroundColor = new Color(c.R / 255f, c.G / 255f, c.B / 255f);
                _colorGrid.Add(swatch);
            }

            _panel.Add(_colorGrid);

            if (_doc != null && _panel != null)
            {
                _doc.rootVisualElement.Add(_panel);
            }

            _internalInput = _inputField.Q<VisualElement>(className: "unity-text-field__input");

            if (_internalInput != null)
            {
                _internalInput.AddToClassList("gchat-internal-input");
            }

            if (_inputField != null && _internalInput != null)
            {
                _blinker = new Controls.ChatInputBlinker(_inputField, _internalInput);
            }
            var uss = Resources.Load<StyleSheet>("chat-input");
            if (uss != null)
            {
                _panel!.styleSheets.Add(uss);
            }

            var chatUss = Resources.Load<StyleSheet>("Styles/Chat");
            if (chatUss != null)
            {
                _panel!.styleSheets.Add(chatUss);
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

            var chatMaxLen = _serverConfig.MaxGlobalChatLength;
            if (text.Length > chatMaxLen)
            {
                text = text.Substring(0, chatMaxLen);
            }

            _networkService.Send(new MinesServer.Networking.Client.Packets.Chat.SendChatMessagePacket("global", text));

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
            _networkService.Send(new ChangeChatColorPacket(color));
        }
    }
}
