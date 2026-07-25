using Fodinae.Scripts.Networking.Connection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class ReconnectUI : MonoBehaviour
    {
        private static ReconnectUI _instance;
        public static ReconnectUI Instance => _instance;

        private UIDocument _doc;
        private VisualElement _reconnectOverlay;
        private VisualElement _disconnectOverlay;
        private Label _reconnectLabel;
        private Label _disconnectLabel;

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[ReconnectUI] UIDocument not found");
                return;
            }

            CreateReconnectOverlay();
            CreateDisconnectOverlay();
            Hide();
        }

        private void CreateReconnectOverlay()
        {
            _reconnectOverlay = new VisualElement();
            _reconnectOverlay.name = "ReconnectOverlay";
            _reconnectOverlay.style.position = Position.Absolute;
            _reconnectOverlay.style.left = 0;
            _reconnectOverlay.style.top = 0;
            _reconnectOverlay.style.right = 0;
            _reconnectOverlay.style.bottom = 0;
            _reconnectOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            _reconnectOverlay.style.alignItems = Align.Center;
            _reconnectOverlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.paddingTop = 30;
            panel.style.paddingBottom = 30;
            panel.style.paddingLeft = 50;
            panel.style.paddingRight = 50;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.alignItems = Align.Center;
            panel.style.minWidth = 300;

            var title = new Label("Соединение потеряно");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            title.style.marginBottom = 16;
            panel.Add(title);

            _reconnectLabel = new Label();
            _reconnectLabel.style.fontSize = 15;
            _reconnectLabel.style.color = Color.white;
            _reconnectLabel.style.whiteSpace = WhiteSpace.Normal;
            _reconnectLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(_reconnectLabel);

            _reconnectOverlay.Add(panel);
        }

        private void CreateDisconnectOverlay()
        {
            _disconnectOverlay = new VisualElement();
            _disconnectOverlay.name = "DisconnectReasonOverlay";
            _disconnectOverlay.style.position = Position.Absolute;
            _disconnectOverlay.style.left = 0;
            _disconnectOverlay.style.top = 0;
            _disconnectOverlay.style.right = 0;
            _disconnectOverlay.style.bottom = 0;
            _disconnectOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            _disconnectOverlay.style.alignItems = Align.Center;
            _disconnectOverlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.paddingTop = 30;
            panel.style.paddingBottom = 30;
            panel.style.paddingLeft = 50;
            panel.style.paddingRight = 50;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.alignItems = Align.Center;
            panel.style.minWidth = 320;

            var title = new Label("Отключены от сервера");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            title.style.marginBottom = 16;
            panel.Add(title);

            _disconnectLabel = new Label();
            _disconnectLabel.style.fontSize = 14;
            _disconnectLabel.style.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            _disconnectLabel.style.whiteSpace = WhiteSpace.Normal;
            _disconnectLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _disconnectLabel.style.marginBottom = 24;
            panel.Add(_disconnectLabel);

            var reconnectBtn = new Button(OnReconnectClicked);
            reconnectBtn.text = "Переподключиться";
            reconnectBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            reconnectBtn.style.borderTopWidth = 2;
            reconnectBtn.style.borderBottomWidth = 2;
            reconnectBtn.style.borderLeftWidth = 2;
            reconnectBtn.style.borderRightWidth = 2;
            reconnectBtn.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            reconnectBtn.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            reconnectBtn.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            reconnectBtn.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            reconnectBtn.style.paddingTop = 10;
            reconnectBtn.style.paddingBottom = 10;
            reconnectBtn.style.paddingLeft = 24;
            reconnectBtn.style.paddingRight = 24;
            reconnectBtn.style.minWidth = 180;
            reconnectBtn.style.color = Color.white;
            reconnectBtn.style.fontSize = 14;
            reconnectBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(reconnectBtn);

            _disconnectOverlay.Add(panel);
        }

        private void OnReconnectClicked()
        {
            ConnectionManager.Instance?.StartManualReconnect();
            _reconnectStatusSet = false;
        }

        private bool _reconnectStatusSet;

        public void ShowReconnecting(string status)
        {
            if (_doc == null)
            {
                return;
            }

            if (_disconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_disconnectOverlay);
            }

            _reconnectLabel.text = status;
            _reconnectStatusSet = true;
            if (_reconnectOverlay.parent == null)
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

            if (_reconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_reconnectOverlay);
            }

            _disconnectLabel.text = reason;
            if (_disconnectOverlay.parent == null)
            {
                _doc.rootVisualElement.Add(_disconnectOverlay);
            }
        }

        public void SetStatus(string status)
        {
            if (_disconnectOverlay.parent != null)
            {
                return;
            }

            if (_reconnectLabel != null)
            {
                _reconnectLabel.text = status;
            }

            if (!_reconnectStatusSet && _doc != null && _reconnectOverlay.parent == null)
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

            if (_reconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_reconnectOverlay);
            }

            if (_disconnectOverlay.parent != null)
            {
                _doc.rootVisualElement.Remove(_disconnectOverlay);
            }

            _reconnectStatusSet = false;
        }
    }
}
