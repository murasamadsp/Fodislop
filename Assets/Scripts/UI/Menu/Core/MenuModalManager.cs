#nullable enable

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Manages modal dialog visibility, tabs and backdrop in the Main Menu.
/// </summary>
public sealed class MenuModalManager
{
    private VisualElement? _modalOverlay;
    private VisualElement? _serverBrowserModal;
    private VisualElement? _settingsModal;
    private VisualElement? _chronicleModal;
    private VisualElement? _repairModal;
    private VisualElement? _profileModal;
    private VisualElement? _updateModal;
    private VisualElement? _activeModal;

    private Button? _settingsTabGraphics;
    private Button? _settingsTabAudio;
    private Button? _settingsTabControls;
    private Button? _settingsTabNetwork;
    private VisualElement? _settingsPaneGraphics;
    private VisualElement? _settingsPaneAudio;
    private VisualElement? _settingsPaneControls;
    private VisualElement? _settingsPaneNetwork;

    private Button? _serverItemHades;
    private Button? _serverItemTartarus;
    private Button? _serverItemCyber;
    private Button? _confirmServerButton;

    public bool HasActiveModal => _activeModal != null;

    public void Bind(VisualElement tree)
    {
        _modalOverlay = tree.Q<VisualElement>("ModalOverlay");
        _serverBrowserModal = tree.Q<VisualElement>("ServerBrowserModal");
        _settingsModal = tree.Q<VisualElement>("SettingsModal");
        _chronicleModal = tree.Q<VisualElement>("ChronicleModal");
        _repairModal = tree.Q<VisualElement>("RepairModal");
        _profileModal = tree.Q<VisualElement>("ProfileModal");
        _updateModal = tree.Q<VisualElement>("UpdateModal");

        _settingsTabGraphics = tree.Q<Button>("SettingsTabGraphics");
        _settingsTabAudio = tree.Q<Button>("SettingsTabAudio");
        _settingsTabControls = tree.Q<Button>("SettingsTabControls");
        _settingsTabNetwork = tree.Q<Button>("SettingsTabNetwork");
        _settingsPaneGraphics = tree.Q<VisualElement>("SettingsPaneGraphics");
        _settingsPaneAudio = tree.Q<VisualElement>("SettingsPaneAudio");
        _settingsPaneControls = tree.Q<VisualElement>("SettingsPaneControls");
        _settingsPaneNetwork = tree.Q<VisualElement>("SettingsPaneNetwork");

        _serverItemHades = tree.Q<Button>("ServerItemHades");
        _serverItemTartarus = tree.Q<Button>("ServerItemTartarus");
        _serverItemCyber = tree.Q<Button>("ServerItemCyber");
        _confirmServerButton = tree.Q<Button>("ConfirmServerButton");

        if (_modalOverlay != null)
        {
            _modalOverlay.style.display = DisplayStyle.None;
        }
    }

    public void SubscribeEvents(VisualElement tree, Action onPlay)
    {
        BindModalClose(tree, "CloseServerModalButton");
        BindModalClose(tree, "CloseSettingsModalButton");
        BindModalClose(tree, "CloseChronicleModalButton");
        BindModalClose(tree, "CloseChronicleFooterButton");
        BindModalClose(tree, "CloseRepairModalButton");
        BindModalClose(tree, "ConfirmRepairButton");
        BindModalClose(tree, "CloseProfileModalButton");
        BindModalClose(tree, "CloseProfileFooterButton");
        BindModalClose(tree, "CloseUpdateModalButton");

        if (_settingsTabGraphics != null)
        {
            _settingsTabGraphics.clicked += () => SwitchSettingsTab(_settingsTabGraphics, _settingsPaneGraphics);
        }

        if (_settingsTabAudio != null)
        {
            _settingsTabAudio.clicked += () => SwitchSettingsTab(_settingsTabAudio, _settingsPaneAudio);
        }

        if (_settingsTabControls != null)
        {
            _settingsTabControls.clicked += () => SwitchSettingsTab(_settingsTabControls, _settingsPaneControls);
        }

        if (_settingsTabNetwork != null)
        {
            _settingsTabNetwork.clicked += () => SwitchSettingsTab(_settingsTabNetwork, _settingsPaneNetwork);
        }

        if (_serverItemHades != null)
        {
            _serverItemHades.clicked += () => SelectServer(_serverItemHades);
        }

        if (_serverItemTartarus != null)
        {
            _serverItemTartarus.clicked += () => SelectServer(_serverItemTartarus);
        }

        if (_serverItemCyber != null)
        {
            _serverItemCyber.clicked += () => SelectServer(_serverItemCyber);
        }

        if (_confirmServerButton != null)
        {
            _confirmServerButton.clicked += () =>
            {
                CloseCurrentModal();
                onPlay();
            };
        }

        var saveSettingsBtn = tree.Q<Button>("SaveSettingsButton");
        if (saveSettingsBtn != null)
        {
            saveSettingsBtn.clicked += CloseCurrentModal;
        }

        var applyUpdateBtn = tree.Q<Button>("ApplyUpdateButton");
        if (applyUpdateBtn != null)
        {
            applyUpdateBtn.clicked += () =>
            {
                CloseCurrentModal();
                onPlay();
            };
        }

        _modalOverlay?.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == _modalOverlay)
            {
                CloseCurrentModal();
            }
        });
    }

    public void OpenServerBrowser() => OpenModal(_serverBrowserModal);
    public void OpenSettings() => OpenModal(_settingsModal);
    public void OpenChronicle() => OpenModal(_chronicleModal);
    public void OpenRepair() => OpenModal(_repairModal);
    public void OpenProfile() => OpenModal(_profileModal);
    public void OpenUpdate() => OpenModal(_updateModal);

    public void OpenModal(VisualElement? modal)
    {
        if (modal == null || _modalOverlay == null)
        {
            return;
        }

        HideAllModals();
        _modalOverlay.style.display = DisplayStyle.Flex;
        modal.style.display = DisplayStyle.Flex;
        _activeModal = modal;
    }

    public void CloseCurrentModal()
    {
        if (_modalOverlay != null)
        {
            _modalOverlay.style.display = DisplayStyle.None;
        }

        HideAllModals();
        _activeModal = null;
    }

    private void HideAllModals()
    {
        if (_serverBrowserModal != null)
        {
            _serverBrowserModal.style.display = DisplayStyle.None;
        }

        if (_settingsModal != null)
        {
            _settingsModal.style.display = DisplayStyle.None;
        }

        if (_chronicleModal != null)
        {
            _chronicleModal.style.display = DisplayStyle.None;
        }

        if (_repairModal != null)
        {
            _repairModal.style.display = DisplayStyle.None;
        }

        if (_profileModal != null)
        {
            _profileModal.style.display = DisplayStyle.None;
        }

        if (_updateModal != null)
        {
            _updateModal.style.display = DisplayStyle.None;
        }
    }

    private void BindModalClose(VisualElement tree, string buttonName)
    {
        var btn = tree.Q<Button>(buttonName);
        if (btn != null)
        {
            btn.clicked += CloseCurrentModal;
        }
    }

    private void SwitchSettingsTab(Button tabBtn, VisualElement? targetPane)
    {
        _settingsTabGraphics?.RemoveFromClassList("mm-nav-tab--active");
        _settingsTabAudio?.RemoveFromClassList("mm-nav-tab--active");
        _settingsTabControls?.RemoveFromClassList("mm-nav-tab--active");
        _settingsTabNetwork?.RemoveFromClassList("mm-nav-tab--active");

        if (_settingsPaneGraphics != null)
        {
            _settingsPaneGraphics.style.display = DisplayStyle.None;
        }

        if (_settingsPaneAudio != null)
        {
            _settingsPaneAudio.style.display = DisplayStyle.None;
        }

        if (_settingsPaneControls != null)
        {
            _settingsPaneControls.style.display = DisplayStyle.None;
        }

        if (_settingsPaneNetwork != null)
        {
            _settingsPaneNetwork.style.display = DisplayStyle.None;
        }

        tabBtn.AddToClassList("mm-nav-tab--active");
        if (targetPane != null)
        {
            targetPane.style.display = DisplayStyle.Flex;
        }
    }

    private void SelectServer(Button serverCard)
    {
        _serverItemHades?.RemoveFromClassList("mm-server-card--active");
        _serverItemTartarus?.RemoveFromClassList("mm-server-card--active");
        _serverItemCyber?.RemoveFromClassList("mm-server-card--active");

        serverCard.AddToClassList("mm-server-card--active");
    }
}
