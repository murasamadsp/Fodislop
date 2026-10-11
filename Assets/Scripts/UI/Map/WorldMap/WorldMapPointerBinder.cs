#nullable enable

using System;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class WorldMapPointerBinder : IDisposable
{
    private readonly WorldMapPanel _panel;
    private bool _bound;
    private EventCallback<PointerDownEvent>? _onDown;
    private EventCallback<PointerMoveEvent>? _onMove;
    private EventCallback<PointerUpEvent>? _onUp;
    private EventCallback<ClickEvent>? _onClick;

    public WorldMapPointerBinder(WorldMapPanel panel)
    {
        _panel = panel;
    }

    public void Bind(
        EventCallback<PointerDownEvent> onDown,
        EventCallback<PointerMoveEvent> onMove,
        EventCallback<PointerUpEvent> onUp,
        EventCallback<ClickEvent> onClick)
    {
        Image? image = _panel.Image;
        if (image == null || _bound)
        {
            return;
        }

        image.RegisterCallback(onDown);
        image.RegisterCallback(onMove);
        image.RegisterCallback(onUp);
        image.RegisterCallback(onClick);
        _onDown = onDown;
        _onMove = onMove;
        _onUp = onUp;
        _onClick = onClick;
        _bound = true;
    }

    public void Dispose()
    {
        if (_onDown == null || _onMove == null || _onUp == null || _onClick == null)
        {
            return;
        }

        Dispose(_onDown, _onMove, _onUp, _onClick);
    }

    public void Dispose(
        EventCallback<PointerDownEvent> onDown,
        EventCallback<PointerMoveEvent> onMove,
        EventCallback<PointerUpEvent> onUp,
        EventCallback<ClickEvent> onClick)
    {
        if (!_bound || _panel.Image == null)
        {
            return;
        }

        _panel.Image.UnregisterCallback(onDown);
        _panel.Image.UnregisterCallback(onMove);
        _panel.Image.UnregisterCallback(onUp);
        _panel.Image.UnregisterCallback(onClick);
        _bound = false;
        _onDown = null;
        _onMove = null;
        _onUp = null;
        _onClick = null;
    }
}
