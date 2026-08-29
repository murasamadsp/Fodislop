#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using MinesServer.Networking.Client.Packets.Chat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class LocalChatPopup : MonoBehaviour
    {
        [Inject]
        private UIDocument _doc = null!;
        private VisualElement? _tree;
        private VisualElement? _overlay;
        private TextField? _inputField;
        private VisualElement? _internalInput;
        private bool _isOpen = false;
        private Controls.ChatInputBlinker? _blinker;
        private CancellationTokenSource? _idleCts;

        [Inject]
        private INetworkService _networkService = null!;

        private bool _initialized;

        protected void Start()
        {
            // Школа (одна дорога): зарегистрированные вьюхи инжектятся при
            // сборке scope (фаза Awake), панель UIDocument создаётся в OnEnable —
            // к Start и зависимости, и панель гарантированы. Один вызов, без
            // ретраев из Update. Серверный конфиг приходит по сети — событие
            // OnInitialized ниже.
            TryInitialize();
        }

        protected void Update()
        {
            if (!_initialized)
            {
                return;
            }

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

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null ||
                _networkService == null)
            {
                // Защитный гард: к моменту [Inject]-метода зависимости и панель
                // UIDocument гарантированы — пропуск здесь означает дефект
                // проводки, а не гонку (ретраев больше нет).
                return;
            }

            _initialized = true;
            CreateUI();
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }

            ApplyChatConfig();
        }

        protected void OnDestroy()
        {
            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _blinker?.StopBlink();
            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void ApplyChatConfig()
        {
            if (_inputField != null)
            {
                _inputField.maxLength = ProjectRuntimeContracts.Chat.MaximumLocalChatLength;
            }
        }

        private void CreateUI()
        {
            var uiUxml = Resources.Load<VisualTreeAsset>("UI/LocalChat");
            if (uiUxml == null)
            {
                return;
            }

            VisualElement tree = uiUxml.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
            _tree = tree;
            _overlay = tree.Q<VisualElement>("LocalChatOverlay");
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }

            _inputField = tree.Q<TextField>("LocalChatInput");

            if (_doc != null && _overlay != null)
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

                _internalInput = _inputField.Q<VisualElement>(className: "unity-text-field__input");
                if (_internalInput != null)
                {
                    _internalInput.AddToClassList("lchat-internal-input");
                }

                if (_internalInput != null)
                {
                    _blinker = new Controls.ChatInputBlinker(_inputField, _internalInput);
                }
            }
        }

        private void SendMessage()
        {
            if (_inputField == null)
            {
                return;
            }

            string text = _inputField.value.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                const int chatMaxLen = ProjectRuntimeContracts.Chat.MaximumLocalChatLength;
                if (text.Length > chatMaxLen)
                {
                    text = text.Substring(0, chatMaxLen);
                }

                _networkService.Send(new SendLocalChatMessagePacket(text));
            }

            Hide();
        }

        public void Show()
        {
            _isOpen = true;
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.Flex;
            }

            if (_inputField != null)
            {
                _inputField.value = string.Empty;
            }

            FocusAfterDelay().Forget();
        }

        private async UniTaskVoid FocusAfterDelay()
        {
            await UniTask.DelayFrame(1);
            if (_inputField != null)
            {
                _inputField.Focus();
            }
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
