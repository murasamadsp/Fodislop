#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Centralized UI Input and Modal Stack Manager for Fodinae.
    /// Manages open modal windows, chat focus state, and escape key modal stack popping.
    /// </summary>
    public class UIInputManager : MonoBehaviour
    {
        private readonly List<VisualElement> _modalStack = [];

        public bool IsChatFocused { get; set; }

        public bool IsPauseMenuOpen { get; set; }

        public bool IsProgrammatorOpen { get; set; }

        public bool IsModalOpen
        {
            get
            {
                PruneDetachedModals();
                return _modalStack.Count > 0;
            }
        }

        public bool IsInputBlocked =>
            IsModalOpen || IsChatFocused || IsPauseMenuOpen || IsProgrammatorOpen;

        public void PushModal(VisualElement modalElement)
        {
            if (modalElement != null && !_modalStack.Contains(modalElement))
            {
                _modalStack.Add(modalElement);
            }
        }

        public void PopModal(VisualElement modalElement)
        {
            if (modalElement != null)
            {
                _modalStack.Remove(modalElement);
            }

            PruneDetachedModals();
        }

        private void PruneDetachedModals()
        {
            for (int i = _modalStack.Count - 1; i >= 0; i--)
            {
                VisualElement el = _modalStack[i];
                if (el == null || el.panel == null)
                {
                    _modalStack.RemoveAt(i);
                }
            }
        }
    }
}
