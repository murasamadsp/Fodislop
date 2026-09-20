#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

internal sealed class PlayerHUDModeController : IDisposable
{
    private readonly ILocalPlayerState _localPlayer;
    private readonly ILocalizationService _loc;

    private Button? _autoDigButton;
    private VisualElement? _autoDigIndicator;
    private Label? _autoDigLabel;

    public PlayerHUDModeController(ILocalPlayerState localPlayer, ILocalizationService loc)
    {
        _localPlayer = localPlayer;
        _loc = loc;
    }

    public void Initialize(VisualElement root, Tooltip tooltip)
    {
        _autoDigButton = root.Q<Button>("AutoDigButton") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigButton is missing from PlayerHUD.uxml.");
        _autoDigButton.clicked += ToggleAutoDig;

        _autoDigIndicator = root.Q<VisualElement>("AutoDigIndicator") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigIndicator is missing from PlayerHUD.uxml.");

        _autoDigLabel = root.Q<Label>("AutoDigLabel") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigLabel is missing from PlayerHUD.uxml.");

        Tooltip.AttachTo(_autoDigButton, () => _loc.Get("hud.tooltip.autodig"), tooltip);

        var player = _localPlayer.Current;
        if (player != null)
        {
            player.OnAutoDigChanged += UpdateAutoDigButton;
            UpdateAutoDigButton(player.AutoDig);
        }
    }

    public void ToggleAutoDig()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            player.AutoDig = !player.AutoDig;
        }
    }

    public void UpdateAutoDigButton(bool enabled)
    {
        _autoDigButton?.EnableInClassList("enabled", enabled);
        if (_autoDigLabel != null)
        {
            _autoDigLabel.text = enabled ? _loc.Get("hud.autodig.on") : _loc.Get("hud.autodig.off");
        }

        _autoDigIndicator?.EnableInClassList("hud-mode-led--active", enabled);
    }

    public void Dispose()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            player.OnAutoDigChanged -= UpdateAutoDigButton;
        }
    }
}
