#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class ReconnectUI : MonoBehaviour
    {
        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private ISessionContainer _session = null!;

        private VisualElement? _reconnectOverlay;
        private VisualElement? _disconnectOverlay;
        private Label? _reconnectLabel;
        private Label? _disconnectLabel;
        private bool _reconnectStatusSet;
        private bool _initialized;

        private void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
            }
        }

        private void OnDestroy()
        {
            _reconnectOverlay?.RemoveFromHierarchy();
            _disconnectOverlay?.RemoveFromHierarchy();
            _reconnectOverlay = null;
            _disconnectOverlay = null;
        }

        private void Start()
        {
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_initialized || _session?.Current == null)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            CreateUI();
            _initialized = true;
        }

        private void CreateUI()
        {
            // Статическая структура (два оверлея с лейблами) живёт в Reconnect.uxml;
            // здесь только клон и биндинги. Видимость и enabled — рантайм-состояние.
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/Reconnect") ??
                throw new InvalidOperationException(
                    "[ReconnectUI] Resources/UI/Reconnect.uxml is required.");
            TemplateContainer tree = template.Instantiate();

            _reconnectOverlay = tree.Q<VisualElement>("ReconnectOverlay") ??
                throw new InvalidOperationException("[ReconnectUI] ReconnectOverlay is missing from Reconnect.uxml.");
            _disconnectOverlay = tree.Q<VisualElement>("DisconnectOverlay") ??
                throw new InvalidOperationException("[ReconnectUI] DisconnectOverlay is missing from Reconnect.uxml.");
            _reconnectLabel = tree.Q<Label>("ReconnectLabel") ??
                throw new InvalidOperationException("[ReconnectUI] ReconnectLabel is missing from Reconnect.uxml.");
            _disconnectLabel = tree.Q<Label>("DisconnectLabel") ??
                throw new InvalidOperationException("[ReconnectUI] DisconnectLabel is missing from Reconnect.uxml.");

            _reconnectOverlay.SetEnabled(false);
            _disconnectOverlay.SetEnabled(false);

            _doc.rootVisualElement.Add(_reconnectOverlay);
            _doc.rootVisualElement.Add(_disconnectOverlay);
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
