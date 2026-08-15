#nullable enable

using MinesServer.Networking.Server.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    public class ModalWindowHandler
    {
        private readonly UIDocument _doc;
        private VisualElement? _overlay;
        private VisualElement? _panel;

        public ModalWindowHandler(UIDocument doc)
        {
            _doc = doc;
        }

        public void Show(ModalWindowPacket packet)
        {
            EnsureCreated();
            _panel!.Clear();

            if (!string.IsNullOrEmpty(packet.IconURI))
            {
                var icon = new VisualElement();
                icon.AddToClassList("modal-icon");
                _panel.Add(icon);
            }

            var titleLabel = new Label(packet.Title);
            titleLabel.AddToClassList("popup-title");
            _panel.Add(titleLabel);

            var descLabel = new Label(packet.Description);
            descLabel.AddToClassList("modal-desc");
            _panel.Add(descLabel);

            var okButton = new Button(() => Hide());
            okButton.text = packet.ButtonText;
            okButton.AddToClassList("popup-btn");
            _panel.Add(okButton);

            _overlay!.style.display = DisplayStyle.Flex;
            UIContainerLayers.SetInteractive(_doc, UIContainerLayers.Modal, true);
            _overlay.SetEnabled(true);
            _overlay.pickingMode = PickingMode.Position;
        }

        public bool IsShowing => _overlay?.style.display == DisplayStyle.Flex;

        public void Hide()
        {
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
                UIContainerLayers.SetInteractive(_doc, UIContainerLayers.Modal, false);
                _overlay.SetEnabled(false);
                _overlay.pickingMode = PickingMode.Ignore;
            }
        }

        private void EnsureCreated()
        {
            if (_overlay != null)
            {
                return;
            }

            _overlay = new VisualElement();
            _overlay.AddToClassList("modal-overlay");
            _overlay.AddToClassList("ui-overlay");
            _overlay.AddToClassList("ui-overlay--modal");
            _overlay.style.display = DisplayStyle.None;
            _overlay.SetEnabled(false);
            _overlay.pickingMode = PickingMode.Ignore;

            _panel = new VisualElement();
            _panel.AddToClassList("popup-panel");
            _panel.AddToClassList("ui-panel");
            _panel.AddToClassList("ui-panel--modal");
            _overlay.Add(_panel);
            _doc.rootVisualElement.Add(_overlay);
        }
    }
}
