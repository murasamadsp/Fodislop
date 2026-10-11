#nullable enable

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class MinimapUIController : IDisposable
{
    private readonly UIDocument _document;
    private readonly Texture _texture;
    private readonly Texture _pathOverlay;
    private readonly Action<int, int> _moveRequested;
    private MinimapView? _view;

    public MinimapUIController(
        UIDocument document,
        Texture texture,
        Texture pathOverlay,
        Action<int, int> moveRequested)
    {
        _document = document;
        _texture = texture;
        _pathOverlay = pathOverlay;
        _moveRequested = moveRequested;
    }

    public bool IsCreated => _view != null;

    public bool TryCreate()
    {
        if (_view != null)
        {
            return true;
        }

        if (_document == null || _document.rootVisualElement == null)
        {
            // UI Toolkit can attach the panel after Start; the caller retries
            // creation from its lifecycle update.
            return false;
        }

        _view = MinimapView.Create(_document, _texture, _pathOverlay, _moveRequested);
        return true;
    }

    public void UpdateCoordinates(int x, int y) => _view?.UpdateCoordinates(x, y);

    public void MarkDirty() => _view?.MarkDirty();

    public void SetVisible(bool visible) => _view?.SetVisible(visible);

    public void Dispose()
    {
        _view?.Dispose();
        _view = null;
    }
}
