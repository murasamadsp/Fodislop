#nullable enable

using Fodinae.Networking.Connection;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class ReconnectUI : MonoBehaviour
    {
        private static ReconnectUI? _instance;
        public static ReconnectUI? Instance => _instance;

        [Inject]
        private UIDocument _doc = null!;
        private VisualElement? _reconnectOverlay;
        private VisualElement? _disconnectOverlay;
        private Label? _reconnectLabel;
        private Label? _disconnectLabel;
        private bool _reconnectStatusSet;

        public void ShowReconnecting(string status)
        {
            if (_doc == null)
            {
                return;
            }

            if (_disconnectOverlay != null && _disconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_disconnectOverlay);
            }

            if (_reconnectLabel != null)
            {
                _reconnectLabel.text = status;
            }

            _reconnectStatusSet = true;
            if (_reconnectOverlay != null && _reconnectOverlay.parent == null)
            {
                _doc.rootVisualElement.Add(_reconnectOverlay);
            }
        }

        public void ShowDisconnectReason(string reason)
        {
            if (_doc == null)
            {
                return;
            }

            if (_reconnectOverlay != null && _reconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_reconnectOverlay);
            }

            if (_disconnectLabel != null)
            {
                _disconnectLabel.text = reason;
            }

            if (_disconnectOverlay != null && _disconnectOverlay.parent == null)
            {
                _doc.rootVisualElement.Add(_disconnectOverlay);
            }
        }

        public void SetStatus(string status)
        {
            if (_disconnectOverlay != null && _disconnectOverlay.parent != null)
            {
                return;
            }

            if (_reconnectLabel != null)
            {
                _reconnectLabel.text = status;
            }

            if (!_reconnectStatusSet && _doc != null && _reconnectOverlay != null && _reconnectOverlay.parent == null)
            {
                _doc.rootVisualElement.Add(_reconnectOverlay);
            }
        }

        public void Hide()
        {
            if (_doc == null)
            {
                return;
            }

            if (_reconnectOverlay != null && _reconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_reconnectOverlay);
            }

            if (_disconnectOverlay != null && _disconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_disconnectOverlay);
            }

            _reconnectStatusSet = false;
        }
    }
}
