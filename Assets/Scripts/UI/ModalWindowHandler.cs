#nullable enable

using MinesServer.Networking.Server.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class ModalWindowHandler
    {
        private readonly UIDocument _doc;
        private VisualElement _overlay;

        public ModalWindowHandler(UIDocument doc)
        {
            _doc = doc;
        }

        public void Show(ModalWindowPacket packet)
        {
            Hide();

            _overlay = new VisualElement();
            _overlay.AddToClassList("modal-overlay");

            var panel = new VisualElement();
            panel.AddToClassList("popup-panel");

            // Размеры окна под контент пакета
            panel.style.minWidth = 300;
            panel.style.maxWidth = 500;

            if (!string.IsNullOrEmpty(packet.IconURI))
            {
                var icon = new VisualElement();
                icon.AddToClassList("modal-icon");
                panel.Add(icon);
            }

            var titleLabel = new Label(packet.Title);
            titleLabel.AddToClassList("popup-title");
            panel.Add(titleLabel);

            var descLabel = new Label(packet.Description);
            descLabel.AddToClassList("modal-desc");
            panel.Add(descLabel);

            var okButton = new Button(() => Hide());
            okButton.text = packet.ButtonText;
            okButton.AddToClassList("popup-btn");
            panel.Add(okButton);

            _overlay.Add(panel);
            _doc.rootVisualElement.Add(_overlay);
        }

        public bool IsShowing => _overlay != null && _overlay.parent != null;

        public void Hide()
        {
            if (_overlay != null && _overlay.parent != null)
            {
                _overlay.parent.Remove(_overlay);
            }

            _overlay = null;
        }
    }
}
