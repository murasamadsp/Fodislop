using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Client.Packets.Chat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class LocalChatPopup : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _overlay;
        private TextField _inputField;
        private VisualElement _internalInput;
        private bool _isOpen = false;
        private Controls.ChatInputBlinker _blinker;
        private CancellationTokenSource _idleCts;

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
            _overlay.style.display = DisplayStyle.None;
        }

        private void CreateUI()
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("lchat-overlay");

            var prompt = new Label(">");
            prompt.AddToClassList("lchat-prompt");
            _overlay.Add(prompt);

            _inputField = new TextField();
            _inputField.selectAllOnFocus = false;
            _inputField.selectAllOnMouseUp = false;
            _inputField.AddToClassList("lchat-input");
            _inputField.maxLength = Fodinae.Scripts.Core.ServiceLocator.Resolve<IServerConfig>()?.MaxLocalChatLength ?? 20;
            _overlay.Add(_inputField);

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

            _doc.rootVisualElement.Add(_overlay);

            _internalInput = _inputField.Q<VisualElement>(className: "unity-text-field__input");

            if (_internalInput != null)
            {
                _internalInput.AddToClassList("lchat-internal-input");
            }

            _blinker = new Controls.ChatInputBlinker(_inputField, _internalInput);
            var uss = Resources.Load<StyleSheet>("chat-input");
            if (uss != null)
            {
                _inputField.styleSheets.Add(uss);
            }

            var chatUss = Resources.Load<StyleSheet>("Styles/Chat");
            if (chatUss != null)
            {
                _overlay.styleSheets.Add(chatUss);
            }
        }

        protected void Update()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.tKey.wasPressedThisFrame && !_isOpen && !ChatInput.IsFocused)
            {
                Show();
                return;
            }

            if (!_isOpen)
            {
                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                SendMessage();
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
            }
        }

        private void SendMessage()
        {
            string text = _inputField.value.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var chatMaxLen = Fodinae.Scripts.Core.ServiceLocator.Resolve<IServerConfig>()?.MaxLocalChatLength ?? 20;
                if (text.Length > chatMaxLen)
                {
                    text = text.Substring(0, chatMaxLen);
                }

                var networkService = Fodinae.Scripts.Core.ServiceLocator.Resolve<INetworkService>();
                networkService?.Send(new SendLocalChatMessagePacket(text));
            }

            Hide();
        }

        public void Show()
        {
            _isOpen = true;
            _overlay.style.display = DisplayStyle.Flex;
            _inputField.value = string.Empty;
            FocusAfterDelay().Forget();
        }

        private async UniTaskVoid FocusAfterDelay()
        {
            await UniTask.DelayFrame(1);
            _inputField.Focus();
        }

        public void Hide()
        {
            _isOpen = false;
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
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
    }
}
