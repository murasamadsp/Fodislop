#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Player.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

// Сенсорное управление дроном и кнопки, которые на клавиатуре — клавиши.
//
// Видимость решает тема, а не код: блок .hud-mobile-controls и тач-раскладка
// HUD включаются классом pointer--touch на корне панели (UIScreen), поэтому
// раскрытие раскладушки или подключение мыши переключают их без пересборки.
// Обработчики висят всегда — скрытый элемент событий не получает.
//
// Каждый удерживаемый элемент (стик, «Копать», «Лечение») захватывает свой
// палец по pointerId: стик и копка работают одновременно двумя пальцами,
// а палец, уехавший с кнопки, не оставляет её нажатой.
internal sealed class PlayerHUDTouchControls : IDisposable
{
    private readonly IPlayerInput _input;
    private readonly IInputBlocker _inputBlocker;
    private readonly ILocalPlayerState _localPlayer;
    private readonly TouchCommands _commands;
    private readonly MapModeState _mapMode;

    private VisualElement _movePad = null!;
    private VisualElement _moveKnob = null!;
    private Button _fastMoveButton = null!;
    private readonly HeldButton _dig;
    private readonly HeldButton _heal;
    private int _movePointerId = -1;

    public PlayerHUDTouchControls(
        IPlayerInput input,
        IInputBlocker inputBlocker,
        ILocalPlayerState localPlayer,
        TouchCommands commands,
        MapModeState mapMode)
    {
        _mapMode = mapMode;
        _input = input;
        _inputBlocker = inputBlocker;
        _localPlayer = localPlayer;
        _commands = commands;
        _dig = new HeldButton(this, held => _input.SetTouchDigHeld(held), onPress: null);
        _heal = new HeldButton(this, held => _input.SetTouchHealHeld(held), onPress: () => _input.QueueTouchAction(TouchPlayerAction.Heal));
    }

    private bool CanAcceptGameplayInput =>
        !_inputBlocker.IsInputBlockedExcludingMapMode &&
        _localPlayer.Current is { HasServerPosition: true, IsGameplayVisible: true };

    public void Bind(VisualElement tree)
    {
        _movePad = Require<VisualElement>(tree, "MobileMovePad");
        _moveKnob = Require<VisualElement>(tree, "MobileMoveKnob");
        _fastMoveButton = Require<Button>(tree, "TouchFastMoveButton");

        _movePad.RegisterCallback<PointerDownEvent>(OnMovePointerDown);
        _movePad.RegisterCallback<PointerMoveEvent>(OnMovePointerMove);
        _movePad.RegisterCallback<PointerUpEvent>(OnMovePointerUp);
        _movePad.RegisterCallback<PointerCaptureOutEvent>(OnMoveCaptureLost);

        _dig.Bind(Require<Button>(tree, "MobileDigButton"));
        _heal.Bind(Require<Button>(tree, "MobileHealButton"));

        BindAction(Require<Button>(tree, "MobileGeoButton"), TouchPlayerAction.Geo);
        BindAction(Require<Button>(tree, "MobileBuildCyanButton"), TouchPlayerAction.BuildCyan);
        BindAction(Require<Button>(tree, "MobileBuildGrayButton"), TouchPlayerAction.BuildGray);
        BindAction(Require<Button>(tree, "MobileBuildGreenButton"), TouchPlayerAction.BuildGreen);
        BindAction(Require<Button>(tree, "MobileBuildWhiteButton"), TouchPlayerAction.BuildWhite);

        _fastMoveButton.clicked += ToggleFastMove;
        Require<Button>(tree, "TouchMenuButton").clicked += _commands.RequestPause;
        Require<Button>(tree, "TouchChatButton").clicked += _commands.RequestGlobalChat;
        Require<Button>(tree, "TouchSayButton").clicked += _commands.RequestLocalChat;

        // Тап по миникарте — ход к блоку, как ЛКМ по миру; карта мира
        // (клавиша M) открывается своей кнопкой.
        Require<Button>(tree, "TouchMapButton").clicked += () => _mapMode.SetOpen(!_mapMode.IsOpen);
        ApplyFastMoveState();
    }

    // Окно, пауза или чат забрали ввод: отпускаем всё, что держит палец,
    // иначе дрон продолжил бы идти под модальным окном.
    public void Cancel()
    {
        _input.ClearQueuedTouchActions();
        if (_movePointerId >= 0)
        {
            int pointerId = _movePointerId;
            _movePointerId = -1;
            _movePad?.ReleasePointer(pointerId);
            ResetMovement();
        }

        _dig.Cancel();
        _heal.Cancel();
    }

    public void Dispose()
    {
        Cancel();
        _input.SetTouchFastMove(false);
    }

    private void BindAction(Button button, TouchPlayerAction action)
    {
        button.clicked += () =>
        {
            if (CanAcceptGameplayInput)
            {
                _input.QueueTouchAction(action);
            }
        };
    }

    // Тумблер, а не удержание: бегать, держа два пальца на стике и кнопке,
    // нельзя — второй палец нужен для «Копать».
    private void ToggleFastMove()
    {
        _input.SetTouchFastMove(!_input.IsTouchFastMove);
        ApplyFastMoveState();
    }

    private void ApplyFastMoveState() =>
        _fastMoveButton.EnableInClassList("hud-touch-toggle--active", _input.IsTouchFastMove);

    private void OnMovePointerDown(PointerDownEvent evt)
    {
        if (_movePointerId >= 0 || !CanAcceptGameplayInput)
        {
            return;
        }

        _movePointerId = evt.pointerId;
        _movePad.CapturePointer(evt.pointerId);
        UpdateMovement(evt.localPosition);
        evt.StopPropagation();
    }

    private void OnMovePointerMove(PointerMoveEvent evt)
    {
        if (evt.pointerId != _movePointerId)
        {
            return;
        }

        UpdateMovement(evt.localPosition);
        evt.StopPropagation();
    }

    private void OnMovePointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != _movePointerId)
        {
            return;
        }

        _movePointerId = -1;
        _movePad.ReleasePointer(evt.pointerId);
        ResetMovement();
        evt.StopPropagation();
    }

    private void OnMoveCaptureLost(PointerCaptureOutEvent evt)
    {
        if (evt.pointerId == _movePointerId)
        {
            _movePointerId = -1;
            ResetMovement();
        }
    }

    private void UpdateMovement(Vector2 pointerPosition)
    {
        Rect bounds = _movePad.contentRect;
        Vector2 center = bounds.center;
        float radius = Mathf.Min(bounds.width, bounds.height) * 0.38f;
        if (radius <= 0f)
        {
            return;
        }

        Vector2 offset = Vector2.ClampMagnitude(pointerPosition - center, radius);
        _moveKnob.style.left = center.x + offset.x;
        _moveKnob.style.top = center.y + offset.y;
        _input.SetTouchMovementInput(new Vector2(offset.x, -offset.y) / radius);
    }

    private void ResetMovement()
    {
        _input.SetTouchMovementInput(Vector2.zero);
        if (_movePad == null || _moveKnob == null)
        {
            return;
        }

        Vector2 center = _movePad.contentRect.center;
        _moveKnob.style.left = center.x;
        _moveKnob.style.top = center.y;
    }

    private static T Require<T>(VisualElement tree, string name)
        where T : VisualElement =>
        tree.Q<T>(name) ??
        throw new InvalidOperationException($"[PlayerHUD] {name} is missing from PlayerHUD.uxml.");

    // Кнопка, которая действует, пока на ней палец.
    private sealed class HeldButton
    {
        private readonly PlayerHUDTouchControls _owner;
        private readonly Action<bool> _setHeld;
        private readonly Action? _onPress;
        private Button? _button;
        private int _pointerId = -1;

        public HeldButton(PlayerHUDTouchControls owner, Action<bool> setHeld, Action? onPress)
        {
            _owner = owner;
            _setHeld = setHeld;
            _onPress = onPress;
        }

        public void Bind(Button button)
        {
            _button = button;
            button.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(OnUp);
            button.RegisterCallback<PointerCaptureOutEvent>(OnCaptureLost);
        }

        public void Cancel()
        {
            if (_pointerId < 0)
            {
                return;
            }

            int pointerId = _pointerId;
            _pointerId = -1;
            _button?.ReleasePointer(pointerId);
            _setHeld(false);
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (_button == null || _pointerId >= 0 || !_owner.CanAcceptGameplayInput)
            {
                return;
            }

            _pointerId = evt.pointerId;
            _button.CapturePointer(evt.pointerId);
            _onPress?.Invoke();
            _setHeld(true);
            evt.StopPropagation();
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId)
            {
                return;
            }

            _pointerId = -1;
            _button?.ReleasePointer(evt.pointerId);
            _setHeld(false);
            evt.StopPropagation();
        }

        private void OnCaptureLost(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == _pointerId)
            {
                _pointerId = -1;
                _setHeld(false);
            }
        }
    }
}
