#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Core.Models;
using Kern.UI;
using Kern.UI.HUD.Player.Model;
using Kern.UI.HUD.Player.View;
using MinesServer.Networking.Client.Packets.GUI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class PlayerHUDBonusControllerTests
{
    private sealed class MockLocalizationService : ILocalizationService
    {
        public string CurrentLanguage => "ru";

        public event Action? OnLanguageChanged;

        public bool HasKey(string key) => true;

        public string Get(string key, params object[] args) => key switch
        {
            "hud.bonus.available" => "Доступен!",
            "hud.bonus.none" => "Нет бонусов",
            _ => args is { Length: > 0 } ? string.Format(key, args) : key,
        };

        public void SetLanguage(string languageCode)
        {
        }

        public void RegisterLocalizable(ILocalizableUI target)
        {
        }

        public void UnregisterLocalizable(ILocalizableUI target)
        {
        }
    }

    private VisualElement _root = null!;
    private Button _bonusButton = null!;
    private VisualElement _bonusPanel = null!;
    private Button _bonusCloseButton = null!;
    private Label _bonusStatusLabel = null!;
    private Button _bonusClaimButton = null!;
    private List<ElementClickPacket> _sentPackets = null!;
    private PlayerHUDBonusController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _root = new VisualElement();
        _bonusButton = new Button { name = "BonusButton" };
        _bonusPanel = new VisualElement { name = "BonusPanel" };
        _bonusCloseButton = new Button { name = "BonusCloseButton" };
        _bonusStatusLabel = new Label { name = "BonusStatusLabel" };
        _bonusClaimButton = new Button { name = "BonusClaimButton" };

        _bonusPanel.Add(_bonusCloseButton);
        _bonusPanel.Add(_bonusStatusLabel);
        _bonusPanel.Add(_bonusClaimButton);

        _root.Add(_bonusButton);
        _root.Add(_bonusPanel);

        _sentPackets = [];
        _controller = new PlayerHUDBonusController(packet => _sentPackets.Add(packet), new MockLocalizationService());
        _controller.Initialize(_root);
    }

    [Test]
    public void Initialize_ButtonsAreNotFocusable()
    {
        Assert.That(_bonusButton.focusable, Is.False, "BonusButton must not steal focus");
        Assert.That(_bonusCloseButton.focusable, Is.False, "BonusCloseButton must not steal focus");
        Assert.That(_bonusClaimButton.focusable, Is.False, "BonusClaimButton must not steal focus");
    }

    [Test]
    public void ToggleBonusPanel_TogglesHiddenState()
    {
        // Initial state
        _controller.CloseBonusPanel();
        Assert.That(UIState.IsHidden(_bonusPanel), Is.True);

        _controller.ToggleBonusPanel();
        Assert.That(UIState.IsHidden(_bonusPanel), Is.False);

        _controller.ToggleBonusPanel();
        Assert.That(UIState.IsHidden(_bonusPanel), Is.True);
    }

    [Test]
    public void CloseBonusPanel_HidesPanel()
    {
        _controller.ToggleBonusPanel();
        Assert.That(UIState.IsHidden(_bonusPanel), Is.False);

        _controller.CloseBonusPanel();
        Assert.That(UIState.IsHidden(_bonusPanel), Is.True);
    }

    [Test]
    public void UpdateDailyBonusPanel_WhenAvailable_ShowsButtonAndClaim()
    {
        var model = new PlayerStatsModel();
        model.SetDailyBonusAvailable(true);

        _controller.UpdateDailyBonusPanel(model);

        Assert.That(UIState.IsHidden(_bonusButton), Is.False);
        Assert.That(UIState.IsHidden(_bonusClaimButton), Is.False);
        Assert.That(_bonusStatusLabel.text, Is.EqualTo("Доступен!"));
    }

    [Test]
    public void UpdateDailyBonusPanel_WhenUnavailable_HidesButtonAndClosesPanel()
    {
        var model = new PlayerStatsModel();
        model.SetDailyBonusAvailable(true);
        _controller.UpdateDailyBonusPanel(model);
        _controller.ToggleBonusPanel();
        Assert.That(UIState.IsHidden(_bonusPanel), Is.False);

        model.SetDailyBonusAvailable(false);
        _controller.UpdateDailyBonusPanel(model);

        Assert.That(UIState.IsHidden(_bonusButton), Is.True);
        Assert.That(UIState.IsHidden(_bonusClaimButton), Is.True);
        Assert.That(UIState.IsHidden(_bonusPanel), Is.True);
        Assert.That(_bonusStatusLabel.text, Is.EqualTo("Нет бонусов"));
    }

    [Test]
    public void ClaimButton_SendsDailyBonusClickPacket()
    {
        // Simulate click on claim button
        using var evt = NavigationSubmitEvent.GetPooled();
        evt.target = _bonusClaimButton;
        _bonusClaimButton.SendEvent(evt);

        // Or directly invoking clicked
        var model = new PlayerStatsModel();
        model.SetDailyBonusAvailable(true);
        _controller.UpdateDailyBonusPanel(model);

        // Click directly through the button's clicked event
        // UI Toolkit Button has a private clicked or can invoke via click event
        using var clickEvt = ClickEvent.GetPooled();
        clickEvt.target = _bonusClaimButton;
        _bonusClaimButton.SendEvent(clickEvt);

        Assert.That(_sentPackets.Count, Is.EqualTo(1));
        Assert.That(_sentPackets[0].WindowTag, Is.EqualTo("daily_bonus"));
    }
}
