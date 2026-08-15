#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class ReconnectUI : MonoBehaviour
    {
        private static ReconnectUI? _instance;
        public static ReconnectUI? Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            _instance = null;
        }

        [Inject]
        private UIDocument _doc = null!;

        private VisualElement? _reconnectOverlay;
        private VisualElement? _disconnectOverlay;
        private Label? _reconnectLabel;
        private Label? _disconnectLabel;
        private bool _reconnectStatusSet;
        private bool _initialized;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            _reconnectOverlay?.RemoveFromHierarchy();
            _disconnectOverlay?.RemoveFromHierarchy();
            _reconnectOverlay = null;
            _disconnectOverlay = null;
        }

        private void Start()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            CreateUI();
        }

        private void CreateUI()
        {
            if (_doc?.rootVisualElement == null)
            {
                return;
            }

            _reconnectOverlay = new VisualElement();
            ApplyOverlayState(_reconnectOverlay);
            _doc.rootVisualElement.Add(_reconnectOverlay);

            _reconnectLabel = new Label("Переподключение к серверу...");
            _reconnectLabel.AddToClassList("ui-overlay-label");
            _reconnectOverlay.Add(_reconnectLabel);

            _disconnectOverlay = new VisualElement();
            ApplyOverlayState(_disconnectOverlay);
            _doc.rootVisualElement.Add(_disconnectOverlay);

            _disconnectLabel = new Label();
            _disconnectLabel.AddToClassList("ui-overlay-label");
            _disconnectOverlay.Add(_disconnectLabel);
        }

        private static void ApplyOverlayState(VisualElement overlay)
        {
            overlay.AddToClassList("ui-overlay");
            overlay.AddToClassList("ui-overlay--blocking");
            overlay.AddToClassList("ui-overlay-message");
            overlay.SetEnabled(false);
            overlay.style.display = DisplayStyle.None;
            overlay.pickingMode = PickingMode.Ignore;
        }

        public void ShowReconnecting(string status)
        {
            if (_doc == null || _reconnectOverlay == null || _reconnectLabel == null)
            {
                return;
            }

            HideOverlay(_disconnectOverlay);

            _reconnectLabel.text = status;

            _reconnectStatusSet = true;
            _reconnectOverlay.style.display = DisplayStyle.Flex;
            _reconnectOverlay.SetEnabled(true);
            _reconnectOverlay.pickingMode = PickingMode.Position;
        }

        public void ShowDisconnectReason(string reason)
        {
            if (_doc == null || _disconnectOverlay == null || _disconnectLabel == null)
            {
                return;
            }

            HideOverlay(_reconnectOverlay);

            _disconnectLabel.text = reason;

            _disconnectOverlay.style.display = DisplayStyle.Flex;
            _disconnectOverlay.SetEnabled(true);
            _disconnectOverlay.pickingMode = PickingMode.Position;
        }

        public void SetStatus(string status)
        {
            if (_disconnectOverlay?.style.display == DisplayStyle.Flex)
            {
                return;
            }

            if (_reconnectLabel != null)
            {
                _reconnectLabel.text = status;
            }

            if (!_reconnectStatusSet && _doc != null && _reconnectOverlay != null)
            {
                _reconnectOverlay.style.display = DisplayStyle.Flex;
                _reconnectOverlay.SetEnabled(true);
                _reconnectOverlay.pickingMode = PickingMode.Position;
            }
        }

        public void Hide()
        {
            if (_doc == null)
            {
                return;
            }

            if (_reconnectOverlay != null)
            {
                HideOverlay(_reconnectOverlay);
            }

            if (_disconnectOverlay != null)
            {
                HideOverlay(_disconnectOverlay);
            }

            _reconnectStatusSet = false;
        }

        private static void HideOverlay(VisualElement? overlay)
        {
            if (overlay == null)
            {
                return;
            }

            overlay.style.display = DisplayStyle.None;
            overlay.SetEnabled(false);
            overlay.pickingMode = PickingMode.Ignore;
        }
    }
}
