using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class Tooltip
    {
        private VisualElement _tooltipElement;
        private Label _tooltipLabel;
        private bool _isVisible;

        public void Initialize(UIDocument doc)
        {
            _tooltipElement = new VisualElement();
            _tooltipElement.name = "Tooltip";
            _tooltipElement.AddToClassList("tooltip-panel");
            // Видимость — рантайм-состояние
            _tooltipElement.style.display = DisplayStyle.None;
            _tooltipElement.pickingMode = PickingMode.Ignore;

            _tooltipLabel = new Label();
            _tooltipLabel.AddToClassList("tooltip-label");
            _tooltipElement.Add(_tooltipLabel);

            doc.rootVisualElement.Add(_tooltipElement);
        }

        public void Show(string text, Vector2 screenPos)
        {
            if (_tooltipElement == null)
            {
                return;
            }

            _tooltipLabel.text = text;
            _tooltipElement.style.display = DisplayStyle.Flex;
            _tooltipElement.style.left = screenPos.x + 12;
            _tooltipElement.style.top = screenPos.y + 12;
            _isVisible = true;
        }

        public void Hide()
        {
            if (_tooltipElement == null || !_isVisible)
            {
                return;
            }

            _tooltipElement.style.display = DisplayStyle.None;
            _isVisible = false;
        }

        public void UpdatePosition(Vector2 screenPos)
        {
            if (!_isVisible || _tooltipElement == null)
            {
                return;
            }

            _tooltipElement.style.left = screenPos.x + 12;
            _tooltipElement.style.top = screenPos.y + 12;
        }

        public static void AttachTo(VisualElement element, string text, Tooltip tooltip)
        {
            element.RegisterCallback<MouseEnterEvent>(evt =>
            {
                var screenPos = evt.mousePosition;
                tooltip.Show(text, screenPos);
            });

            element.RegisterCallback<MouseMoveEvent>(evt =>
            {
                tooltip.UpdatePosition(evt.mousePosition);
            });

            element.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                tooltip.Hide();
            });
        }
    }
}
