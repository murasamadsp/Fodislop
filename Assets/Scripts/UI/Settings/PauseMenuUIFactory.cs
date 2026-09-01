#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    internal static class PauseMenuUIFactory
    {
        public static VisualElement CreateSlider(string labelText, float initialValue, Action<float> onChange, float min, float max)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");

            var label = new Label();
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(min, max);
            slider.SetValueWithoutNotify(initialValue);
            void UpdateLabel(float value)
            {
                label.text = $"{labelText}: {value:F2}";
            }

            UpdateLabel(initialValue);
            slider.RegisterValueChangedCallback(evt =>
            {
                UpdateLabel(evt.newValue);
                onChange(evt.newValue);
            });
            container.Add(slider);

            return container;
        }

        public static VisualElement CreateBoundSlider(
            string labelText,
            Func<float> readValue,
            Action<float> onChange,
            float minimum,
            float maximum,
            ICollection<Action> refreshers)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");

            var label = new Label();
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(minimum, maximum);
            void Refresh()
            {
                float value = readValue();
                slider.SetValueWithoutNotify(value);
                label.text = $"{labelText}: {value:F2}";
            }

            slider.RegisterValueChangedCallback(evt =>
            {
                label.text = $"{labelText}: {evt.newValue:F2}";
                onChange(evt.newValue);
            });
            container.Add(slider);
            refreshers.Add(Refresh);
            Refresh();
            return container;
        }

        public static VisualElement CreateBoundColorControls(
            string labelText,
            Func<Color> readValue,
            Action<Color> onChange,
            float minimum,
            float maximum,
            ICollection<Action> refreshers)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");
            container.Add(CreateLabel(labelText));
            container.Add(CreateBoundSlider(
                $"{labelText} R",
                () => readValue().r,
                value =>
                {
                    Color color = readValue();
                    color.r = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            container.Add(CreateBoundSlider(
                $"{labelText} G",
                () => readValue().g,
                value =>
                {
                    Color color = readValue();
                    color.g = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            container.Add(CreateBoundSlider(
                $"{labelText} B",
                () => readValue().b,
                value =>
                {
                    Color color = readValue();
                    color.b = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            return container;
        }

        public static Toggle CreateBoundToggle(
            string label,
            Func<bool> readValue,
            Action<bool> onChange,
            ICollection<Action> refreshers)
        {
            var toggle = new Toggle(label);
            void Refresh()
            {
                toggle.SetValueWithoutNotify(readValue());
            }

            toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            refreshers.Add(Refresh);
            Refresh();
            return toggle;
        }

        public static Button CreateButton(string text, Action action)
        {
            var btn = new Button(action);
            btn.text = text;
            btn.AddToClassList("pause-btn");
            return btn;
        }

        public static Label CreateLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("pause-slider-label");
            return label;
        }

        public static void ShowConfirmation(UIDocument doc, string title, string description, string confirmText, Action onConfirm, ILocalizationService loc)
        {
            if (doc == null || doc.rootVisualElement == null)
            {
                // Защитный гард: без готового документа показывать подтверждение
                // негде; вызывающий сам решает, когда документ готов.
                return;
            }

            var root = doc.rootVisualElement;

            var overlay = new VisualElement();
            overlay.name = "ConfirmOverlay";
            overlay.AddToClassList("pause-confirm-overlay");
            overlay.AddToClassList("ui-overlay");
            overlay.AddToClassList("ui-overlay--modal");

            var panel = new VisualElement();
            panel.AddToClassList("pause-confirm-panel");
            panel.AddToClassList("ui-panel");
            panel.AddToClassList("ui-panel--modal");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("pause-confirm-title");
            panel.Add(titleLabel);

            var descLabel = new Label(description);
            descLabel.AddToClassList("pause-confirm-desc");
            panel.Add(descLabel);

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("pause-confirm-buttons");
            buttonsRow.AddToClassList("ui-actions-row");

            var confirmBtn = new Button(() =>
            {
                root.Remove(overlay);
                onConfirm();
            });
            confirmBtn.text = confirmText;
            confirmBtn.AddToClassList("pause-btn-confirm");

            var cancelBtn = new Button(() => root.Remove(overlay));
            cancelBtn.text = loc.Get("common.cancel");
            cancelBtn.AddToClassList("pause-btn");

            buttonsRow.Add(confirmBtn);
            buttonsRow.Add(cancelBtn);
            panel.Add(buttonsRow);

            overlay.Add(panel);
            root.Add(overlay);
        }
    }
}
