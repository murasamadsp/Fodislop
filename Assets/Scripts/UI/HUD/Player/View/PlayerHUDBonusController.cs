#nullable enable

using System;
using Fodinae.Core.Localization;
using Fodinae.UI.HUD.Player.Model;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Player.View;

/// <summary>
/// Controls daily bonus panel and claiming interactions in Player HUD.
/// </summary>
public sealed class PlayerHUDBonusController
{
    private readonly Action<ElementClickPacket> _sendPacket;
    private readonly ILocalizationService _loc;
    private Button? _bonusButton;
    private VisualElement? _bonusPanel;
    private Label? _bonusStatusLabel;
    private Button? _bonusClaimButton;
    private bool _isBonusOpen;

    public PlayerHUDBonusController(Action<ElementClickPacket> sendPacket, ILocalizationService loc)
    {
        _sendPacket = sendPacket;
        _loc = loc;
    }

    public void Initialize(VisualElement root)
    {
        _bonusButton = root.Q<Button>("BonusButton");
        if (_bonusButton != null)
        {
            _bonusButton.clicked += ToggleBonusPanel;
        }

        _bonusPanel = root.Q<VisualElement>("BonusPanel");
        _bonusStatusLabel = root.Q<Label>("BonusStatusLabel");
        _bonusClaimButton = root.Q<Button>("BonusClaimButton");
        if (_bonusClaimButton != null)
        {
            _bonusClaimButton.clicked += ClaimDailyBonus;
        }
    }

    public void ToggleBonusPanel()
    {
        if (_bonusPanel == null)
        {
            return;
        }

        _isBonusOpen = !_isBonusOpen;
        _bonusPanel.style.display = _isBonusOpen ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void UpdateDailyBonusPanel(PlayerStatsModel? stats)
    {
        if (_bonusStatusLabel == null || _bonusButton == null || stats == null)
        {
            return;
        }

        if (stats.DailyBonusAvailable)
        {
            _bonusButton.style.display = DisplayStyle.Flex;
            _bonusStatusLabel.text = _loc.Get("hud.bonus.available");
            _bonusStatusLabel.style.color = Color.green;
            if (_bonusClaimButton != null)
            {
                _bonusClaimButton.style.display = DisplayStyle.Flex;
            }
        }
        else
        {
            _bonusButton.style.display = DisplayStyle.None;
            _bonusStatusLabel.text = _loc.Get("hud.bonus.none");
            _bonusStatusLabel.style.color = Color.gray;
            if (_bonusClaimButton != null)
            {
                _bonusClaimButton.style.display = DisplayStyle.None;
            }
        }
    }

    private void ClaimDailyBonus()
    {
        _sendPacket(new ElementClickPacket("daily_bonus", 0, Array.Empty<StringPairPacket>()));
    }
}
