#nullable enable

using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Kern.UI;

// Чистый сервис контейнера: ни рендера, ни transform (SCENE_STANDARD.md §1).
public sealed class UIInputManager
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
            VisualElement element = _modalStack[i];
            if (element == null || element.panel == null)
            {
                _modalStack.RemoveAt(i);
            }
        }
    }
}
