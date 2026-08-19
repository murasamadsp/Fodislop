#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.UI;
using MinesServer.Networking.Client.Packets.Actions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.Player
{
    public class PlayerInteractionController : MonoBehaviour
    {
        private Camera? _mainCamera;
        private UIDocument[] _uiDocuments = [];
        private UnityEngine.InputSystem.Utilities.ReadOnlyArray<KeyControl> _cachedAllKeys;
        [Inject]
        private UIDocument? _injectedUiDoc;
        [Inject]
        private IMapDataProvider _mapManager = null!;
        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;

        protected void Awake()
        {
            _mainCamera = Camera.main;
            if (Keyboard.current != null)
            {
                _cachedAllKeys = Keyboard.current.allKeys;
            }
        }

        protected void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera == null)
            {
                return;
            }

            HandleMouseClick();
            HandleKeyboardInput();
        }

        private void HandleMouseClick()
        {
            if (Mouse.current == null)
            {
                return;
            }

            if (_inputBlocker == null || _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (ChatInput.IsFocused)
            {
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                if (IsPointerOverUI(mousePos))
                {
                    return;
                }

                if (_mainCamera == null)
                {
                    return;
                }

                Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -_mainCamera.transform.position.z));

                int unityX = Mathf.FloorToInt(worldPos.x);
                int unityY = Mathf.FloorToInt(worldPos.y);

                if (_mapManager != null && _networkService != null)
                {
                    ushort serverX = (ushort)Mathf.Clamp(unityX, 0, ushort.MaxValue);
                    ushort serverY = (ushort)Mathf.Clamp(_mapManager.WorldHeight - 1 - unityY, 0, ushort.MaxValue);

                    _networkService.SendAction(new ClickCellPacket(serverX, serverY));
                }
            }
        }

        // Клик по миру шлётся только если указатель не над UI-элементом.
        // При этом TemplateContainer/корень документа — «пустой фон»: клик должен проходить.
        private bool IsPointerOverUI(Vector2 mousePos)
        {
            var doc = _injectedUiDoc;
            if (doc != null && doc.isActiveAndEnabled)
            {
                var root = doc.rootVisualElement;
                if (root?.panel != null)
                {
                    // ScreenToPanel учитывает масштаб панели (ScaleWithScreenSize); ручной
                    // флип Y без учёта масштаба мажет далеко от верхнего левого угла —
                    // клики по UI внизу экрана проваливались в мир и двигали робота.
                    var panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, mousePos);
                    var picked = root.panel.Pick(panelPos);
                    if (picked != null && picked != root && picked is not TemplateContainer)
                    {
                        return true;
                    }
                }
            }

            if (_uiDocuments.Length == 0 || !HasLiveUiDocument())
            {
                RefreshUiDocuments();
            }

            foreach (UIDocument candidate in _uiDocuments)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || candidate == doc)
                {
                    continue;
                }

                var root = candidate.rootVisualElement;
                if (root?.panel == null)
                {
                    continue;
                }

                // ScreenToPanel переводит экранные координаты (низ-лево, как в Input
                // System) в координаты панели с учётом её масштаба.
                var panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, mousePos);
                var picked = root.panel.Pick(panelPos);
                if (picked != null && picked != root && picked is not TemplateContainer)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasLiveUiDocument()
        {
            for (int i = 0; i < _uiDocuments.Length; i++)
            {
                if (_uiDocuments[i] != null && _uiDocuments[i].isActiveAndEnabled)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshUiDocuments()
        {
            _uiDocuments = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);
        }

        private void HandleKeyboardInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (_inputBlocker == null || _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (ChatInput.IsFocused)
            {
                return;
            }

            // This is a bit expensive but since it's for "unmapped" keys,
            // we might want to check all keys if they were pressed this frame.
            for (int i = 0; i < _cachedAllKeys.Count; i++)
            {
                var keyControl = _cachedAllKeys[i];
                if (keyControl.wasPressedThisFrame)
                {
                    byte code = MapKeyToByte(keyControl.keyCode);
                    if (code != 0)
                    {
                        bool ctrl = Keyboard.current.ctrlKey.isPressed;
                        bool alt = Keyboard.current.altKey.isPressed;
                        bool shift = Keyboard.current.shiftKey.isPressed;

                        if (_networkService != null)
                        {
                            _networkService.SendAction(new UnmappedKeyPacket(code, ctrl, alt, shift));
                        }
                    }
                }
            }
        }

        private byte MapKeyToByte(Key key)
        {
            // Simple mapping to ASCII or custom codes
            return key switch
            {
                Key.Space => 32,
                Key.Enter => 13,
                Key.Escape => 27,
                Key.Tab => 9,
                Key.Backspace => 8,
                Key.Delete => 127,

                Key.A => (byte)'a',
                Key.B => (byte)'b',
                Key.C => (byte)'c',
                Key.D => (byte)'d',
                Key.E => (byte)'e',
                Key.F => (byte)'f',
                Key.G => (byte)'g',
                Key.H => (byte)'h',
                Key.I => (byte)'i',
                Key.J => (byte)'j',
                Key.K => (byte)'k',
                Key.L => (byte)'l',
                Key.M => (byte)'m',
                Key.N => (byte)'n',
                Key.O => (byte)'o',
                Key.P => (byte)'p',
                Key.Q => (byte)'q',
                Key.R => (byte)'r',
                Key.S => (byte)'s',
                Key.T => (byte)'t',
                Key.U => (byte)'u',
                Key.V => (byte)'v',
                Key.W => (byte)'w',
                Key.X => (byte)'x',
                Key.Y => (byte)'y',
                Key.Z => (byte)'z',

                Key.Digit0 => (byte)'0',
                Key.Digit1 => (byte)'1',
                Key.Digit2 => (byte)'2',
                Key.Digit3 => (byte)'3',
                Key.Digit4 => (byte)'4',
                Key.Digit5 => (byte)'5',
                Key.Digit6 => (byte)'6',
                Key.Digit7 => (byte)'7',
                Key.Digit8 => (byte)'8',
                Key.Digit9 => (byte)'9',

                _ => 0,
            };
        }
    }
}
