#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Networking;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.UI.Programmator;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI.HUD.Player.View
{
    public class PlayerHUDView : MonoBehaviour
    {
        private const int BTN_SIZE = 50;
        private const int GAP = 6;
        private const int SKILL_GRID_COLS = 4;

        private Color _hpBarFillColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        private Color _hpBarLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        private Color _accentColor = new Color(0.7f, 0.65f, 0.5f, 1f);
        private Color _accentHoverColor = new Color(0.8f, 0.75f, 0.6f, 1f);

        private readonly List<Texture2D> _crystalTextures = new();
        private readonly List<Label> _basketCrystalLabels = new();
        private readonly Dictionary<SkillType, (Label arrow, VisualElement barFill)> _skillIcons = new();
        private readonly Dictionary<SkillType, IVisualElementScheduledItem> _bounceSchedules = new();
        private readonly Dictionary<SkillType, IVisualElementScheduledItem> _pulseSchedules = new();
        private readonly Dictionary<string, VisualElement> _statusLineElements = new();
        private readonly Dictionary<string, IVisualElementScheduledItem> _statusSchedules = new();

        [Inject]
        private UIDocument _doc = null!;
        private Tooltip? _tooltip;
        private bool _isLoaded;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        private IVisualElementScheduledItem? _skeletonPulse;
        private VisualElement? _panel;
        private Button? _bonusButton;
        private VisualElement? _bonusPanel;
        private Label? _bonusStatusLabel;
        private Button? _bonusClaimButton;
        private bool _isBonusOpen;

        private Label? _nicknameLabel;
        private Label? _levelLabel;
        private Label? _hpLabel;
        private VisualElement? _hpBarFill;
        private Label? _moneyLabel;
        private Label? _credsLabel;
        private Label? _geologyLabel;
        private Label? _basketPercentLabel;
        private VisualElement? _basketContainer;
        private VisualElement? _skillContainer;
        private Button? _autoDigButton;
        private Label? _autoDigLabel;
        private Button? _aggressionButton;
        private Label? _aggressionLabel;

        private VisualElement? _currentSkillRow;
        private int _skillCountInRow = 0;
        private Button? _chatButton;
        private VisualElement? _statusPanel;
        private VisualElement? _respawnPopup;
        private VisualElement? _buildingsPopup;
        private VisualElement? _faqPopup;
        private ProgrammatorGrid? _programmatorGrid;
        private bool _initializationStarted;

        [Inject]
        private PlayerStatsModel _model = null!;
        [Inject]
        private GlobalChatUI _globalChatUI = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IObjectResolver _resolver = null!;
        [Inject]
        private ISessionContainer _session = null!;
        private VisualElement? _missionPanel;
        private Label? _missionTitleLabel;
        private Label? _missionDescLabel;
        private VisualElement? _missionProgressFill;
        private Label? _missionProgressLabel;

        protected void Start()
        {
            TryStartInitialization();
        }

        protected void Update()
        {
            if (!_initializationStarted)
            {
                TryStartInitialization();
            }
        }

        private void TryStartInitialization()
        {
            if (_initializationStarted || _session?.Current == null)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null || _model == null ||
                _globalChatUI == null || _assetLoader == null || _networkService == null || _inputBlocker == null)
            {
                // Не бросаем: инъекция может завершиться после этого Start (PostStart).
                // Update ретраит TryStartInitialization — ждём готовности молча, иначе
                // каждый кадр до инжекта будет сыпать исключениями.
                return;
            }

            _initializationStarted = true;
            StartAsync(this.destroyCancellationToken).Forget();
        }

        private async UniTaskVoid StartAsync(System.Threading.CancellationToken cancellationToken)
        {
            InitializeHUD();
            try
            {
                await LoadCrystalTextures(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Crystal icons are optional HUD content. Keep the gameplay HUD
                // alive and report the asset miss only to diagnostics; an optional
                // visual asset must not cover or block the game UI.
                Debug.LogWarning($"[PlayerHUD] Optional crystal textures unavailable: {ex.Message}");
            }

            if (cancellationToken.IsCancellationRequested || this == null)
            {
                return;
            }

            RebuildCrystalRows();
        }

        protected void OnDestroy()
        {
            StopSkeletonPulse();
            foreach (var schedule in _bounceSchedules.Values)
            {
                schedule.Pause();
            }

            foreach (var schedule in _pulseSchedules.Values)
            {
                schedule.Pause();
            }

            _bounceSchedules.Clear();
            _pulseSchedules.Clear();

            if (_model != null)
            {
                _model.OnStatsChanged -= RefreshAll;
                _model.OnSkillProgress -= OnSkillProgress;
                _model.OnDailyBonusChanged -= UpdateDailyBonusPanel;
                _model.OnStatusLinesChanged -= RebuildStatusPanel;
                _model.OnMissionChanged -= UpdateMissionPanel;
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.OnAutoDigChanged -= UpdateAutoDigButton;
                player.OnAggressionChanged -= UpdateAggressionButton;
            }

            if (_globalChatUI != null)
            {
                _globalChatUI.Hide();
            }
        }

        private async UniTask LoadCrystalTextures(System.Threading.CancellationToken cancellationToken)
        {
            _crystalTextures.Clear();
            foreach (CrystalType ct in Enum.GetValues(typeof(CrystalType)))
            {
                if (ct == CrystalType.Unknown)
                {
                    continue;
                }

                string name = ct.ToString().ToLowerInvariant();
                Texture2D? tex;
                try
                {
                    tex = await _assetLoader.GetTextureAsync(
                        "Crystals/" + name,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[PlayerHUD] Optional crystal texture '{name}' was skipped: " +
                        exception.Message);
                    continue;
                }

                if (cancellationToken.IsCancellationRequested || this == null)
                {
                    return;
                }

                if (tex != null)
                {
                    _crystalTextures.Add(tex);
                }
            }
        }

        public void InitializeEditorPreview(UIDocument doc)
        {
            _doc = doc;
            _model = new PlayerStatsModel();
            InitializeHUD();
        }

        private void InitializeHUD()
        {
            _tooltip = new Tooltip();
            _tooltip.Initialize(_doc);

            // Тир раскладки вместо @media: класс на корне панели.
            UiLayoutTier.Attach(_doc.rootVisualElement);

            CreatePanel(_doc.rootVisualElement);
            CreateBonusButton(_doc.rootVisualElement);
            CreateBonusPanel(_doc.rootVisualElement);
            CreateAggressionToggle(_doc.rootVisualElement);
            CreateAutoDigToggle(_doc.rootVisualElement);
            CreateChatButton(_doc.rootVisualElement);
            CreateButtonsAndPopups(_doc.rootVisualElement);
            CreateStatusPanel(_doc.rootVisualElement);
            CreateSkillContainer(_doc.rootVisualElement);
            CreateMissionPanel(_doc.rootVisualElement);
            if (_model != null)
            {
                _model.OnSkillProgress += OnSkillProgress;
                _model.OnStatusLinesChanged += RebuildStatusPanel;
                _model.OnMissionChanged += UpdateMissionPanel;
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.OnAutoDigChanged += UpdateAutoDigButton;
                player.OnAggressionChanged += UpdateAggressionButton;

                UpdateAutoDigButton(player.AutoDig);
                UpdateAggressionButton(player.Aggression);
            }

            if (_model != null)
            {
                _model.OnDailyBonusChanged += UpdateDailyBonusPanel;
            }

            RebuildCrystalRows();
            if (_model != null)
            {
                _model.OnStatsChanged += RefreshAll;
                _isLoaded = _model.Health > 0 || _model.Level > 0;
            }

            if (!_isLoaded)
            {
                StartSkeletonPulse();
            }

            RefreshAll();

            var root = _doc.rootVisualElement;
            Debug.Log("[PlayerHUD] InitializeHUD complete, skills container created=" + (_skillContainer != null));

            // Клавиатурная навигация по интерфейсу вырезана насовсем: стрелки/WASD не
            // должны двигать фокус по кнопкам, а Enter — активировать их. Подавляем
            // навигационные события глобально (TrickleDown ловит их до фокус-контроллера).
            root.RegisterCallback<NavigationMoveEvent>(
                evt => evt.StopPropagation(), TrickleDown.TrickleDown);

            root.RegisterCallback<NavigationSubmitEvent>(
                evt => evt.StopPropagation(), TrickleDown.TrickleDown);

            // Tab тоже не должен перемещать фокус по кнопкам.
            root.RegisterCallback<KeyDownEvent>(
                evt =>
            {
                if (evt.keyCode == KeyCode.Tab)
                {
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        private void CreatePanel(VisualElement root)
        {
            _panel = new VisualElement();
            _panel.name = "PlayerHUD";
            _panel.AddToClassList("hud-panel");

            var topRow = new VisualElement();
            topRow.AddToClassList("hud-title-row");

            _nicknameLabel = new Label("---");
            _nicknameLabel.AddToClassList("hud-nickname");
            topRow.Add(_nicknameLabel);

            _levelLabel = new Label("Ур: ---");
            _levelLabel.AddToClassList("hud-level");
            topRow.Add(_levelLabel);

            var clanButton = new Button(() => _networkService?.Send(new OpenClanClickPacket()));
            clanButton.AddToClassList("hud-clan-button");
            clanButton.tooltip = "Клан";
            topRow.Add(clanButton);

            _panel.Add(topRow);

            var separator = new VisualElement();
            separator.AddToClassList("hud-separator");
            _panel.Add(separator);

            _hpLabel = new Label("Прочность: --/--");
            _hpLabel.AddToClassList("hud-stat");
            _panel.Add(_hpLabel);

            var hpContainer = new VisualElement();
            hpContainer.AddToClassList("hud-hp-bar");

            _hpBarFill = new VisualElement();
            _hpBarFill.AddToClassList("hud-hp-fill");
            hpContainer.Add(_hpBarFill);

            _panel.Add(hpContainer);

            _moneyLabel = new Label("$ ---");
            _moneyLabel.AddToClassList("hud-money");
            _panel.Add(_moneyLabel);

            _credsLabel = new Label("C ---");
            _credsLabel.AddToClassList("hud-creds");
            _panel.Add(_credsLabel);

            _geologyLabel = new Label("Геология: --/--");
            _geologyLabel.AddToClassList("hud-stat");
            _panel.Add(_geologyLabel);

            var basketSep = new VisualElement();
            basketSep.AddToClassList("hud-separator");
            _panel.Add(basketSep);

            _basketPercentLabel = new Label("Груз: --%");
            _basketPercentLabel.AddToClassList("hud-basket");
            _panel.Add(_basketPercentLabel);

            _basketContainer = new VisualElement();
            _basketContainer.name = "BasketCrystals";
            _basketContainer.AddToClassList("hud-basket-container");
            _panel.Add(_basketContainer);

            root.Add(_panel);
        }

        private void CreateBonusButton(VisualElement root)
        {
            _bonusButton = new Button(ToggleBonusPanel);
            _bonusButton.text = "Бонусы";
            _bonusButton.AddToClassList("hud-button-accent");
            _bonusButton.AddToClassList("hud-bonus-button");
            Tooltip.AttachTo(_bonusButton, "Открыть панель бонусов", _tooltip);

            root.Add(_bonusButton);
        }

        private void CreateBonusPanel(VisualElement root)
        {
            _bonusPanel = new VisualElement();
            _bonusPanel.AddToClassList("hud-bonus-panel");

            var titleRow = new VisualElement();
            titleRow.AddToClassList("hud-title-row");

            var title = new Label("Бонусы");
            title.AddToClassList("hud-stat");
            titleRow.Add(title);

            var closeBtn = new Button(ToggleBonusPanel);
            closeBtn.text = "×";
            closeBtn.AddToClassList("hud-button-close");
            titleRow.Add(closeBtn);

            _bonusPanel.Add(titleRow);

            _bonusStatusLabel = new Label("Ежедневный бонус: ...");
            _bonusStatusLabel.AddToClassList("hud-stat");
            _bonusStatusLabel.AddToClassList("hud-stat-wrap");
            _bonusPanel.Add(_bonusStatusLabel);

            _bonusClaimButton = new Button(ClaimDailyBonus);
            _bonusClaimButton.text = "Забрать";
            _bonusClaimButton.AddToClassList("hud-button-claim");
            _bonusClaimButton.style.display = DisplayStyle.None;
            _bonusPanel.Add(_bonusClaimButton);

            _bonusPanel.style.display = DisplayStyle.None;
            root.Add(_bonusPanel);
        }

        private void ToggleBonusPanel()
        {
            _isBonusOpen = !_isBonusOpen;
            _bonusPanel!.style.display = _isBonusOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _bonusButton!.style.backgroundColor = _isBonusOpen ? _accentHoverColor : _accentColor;
            if (_isBonusOpen)
            {
                UpdateDailyBonusPanel();
            }

            UpdateStatusPanelPosition();
        }

        private void UpdateStatusPanelPosition()
        {
            if (_statusPanel == null)
            {
                return;
            }

            if (_isBonusOpen && _bonusPanel != null)
            {
                _bonusPanel.schedule.Execute(() =>
                {
                    if (!_isBonusOpen)
                    {
                        return;
                    }

                    _statusPanel.style.top = 10 + GAP + _bonusPanel.resolvedStyle.height;
                }).StartingIn(16);
            }
            else
            {
                _statusPanel.style.top = 10 + BTN_SIZE + GAP;
            }
        }

        private void UpdateDailyBonusPanel()
        {
            if (_bonusStatusLabel == null)
            {
                return;
            }

            var stats = _model;
            if (stats == null)
            {
                return;
            }

            if (stats.DailyBonusAvailable)
            {
                _bonusStatusLabel!.text = "Ежедневный бонус: <color=lime>Доступен!</color>";
                _bonusStatusLabel!.style.color = Color.green;
                _bonusClaimButton!.style.display = DisplayStyle.Flex;
            }
            else
            {
                _bonusStatusLabel!.text = "Ежедневный бонус: Нет активных бонусов";
                _bonusStatusLabel!.style.color = Color.gray;
                _bonusClaimButton!.style.display = DisplayStyle.None;
            }

            UpdateStatusPanelPosition();
        }

        private void CreateStatusPanel(VisualElement root)
        {
            _statusPanel = new VisualElement();
            _statusPanel.name = "StatusPanel";
            _statusPanel.AddToClassList("hud-status-panel");
            _statusPanel.style.display = DisplayStyle.None;
            root.Add(_statusPanel);
        }

        private void RebuildStatusPanel()
        {
            if (_statusPanel == null)
            {
                return;
            }

            var stats = _model;
            if (stats == null)
            {
                return;
            }

            var currentLines = stats.StatusLines;
            if (currentLines.Count == 0)
            {
                _statusPanel.style.display = DisplayStyle.None;
                foreach (var schedule in _statusSchedules.Values)
                {
                    schedule.Pause();
                }

                _statusSchedules.Clear();
                _statusLineElements.Clear();
                _statusPanel.Clear();
                return;
            }

            _statusPanel.style.display = DisplayStyle.Flex;
            var toRemove = new List<string>();
            foreach (var kvp in _statusLineElements)
            {
                if (!currentLines.ContainsKey(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _statusPanel.Remove(_statusLineElements[key]);
                if (_statusSchedules.TryGetValue(key, out var schedule))
                {
                    schedule.Pause();
                    _statusSchedules.Remove(key);
                }

                _statusLineElements.Remove(key);
            }

            foreach (var kvp in currentLines)
            {
                if (_statusLineElements.TryGetValue(kvp.Key, out var existing))
                {
                    var label = existing as Label;
                    if (label != null)
                    {
                        UpdateStatusLabel(label, kvp.Value);
                    }

                    label!.style.color = kvp.Value.Color;
                }
                else
                {
                    var row = new Label();
                    row.AddToClassList("hud-status-line");
                    row.style.color = kvp.Value.Color;
                    UpdateStatusLabel(row, kvp.Value);
                    _statusPanel.Add(row);

                    if (kvp.Value.Expiry > 0)
                    {
                        var schedule = row.schedule.Execute(() =>
                        {
                            if (_statusPanel == null || !_statusLineElements.ContainsKey(kvp.Key))
                            {
                                return;
                            }

                            var entry = stats.StatusLines.GetValueOrDefault(kvp.Key);
                            if (entry.Text == null)
                            {
                                return;
                            }

                            UpdateStatusLabel(row, entry);
                        }).Every(1000);
                        _statusSchedules[kvp.Key] = schedule;
                    }

                    _statusLineElements[kvp.Key] = row;
                }
            }
        }

        private static void UpdateStatusLabel(Label label, StatusLineEntry entry)
        {
            if (entry.Text == null || entry.Text.Length == 0)
            {
                label.text = string.Empty;
                return;
            }

            var name = entry.Text[0];
            if (entry.Expiry > 0)
            {
                var remaining = Math.Max(0, entry.Expiry - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                label.text = $"{name}: {FormatTime(remaining)}";
            }
            else if (entry.Text.Length > 1)
            {
                label.text = $"{name}: {entry.Text[1]}";
            }
            else
            {
                label.text = name;
            }
        }

        private static string FormatTime(long seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }

            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private void ClaimDailyBonus()
        {
            Debug.Log("[PlayerHUD] ClaimDailyBonus: sending claim request");
            var ns = _networkService;
            ns?.Send(new ElementClickPacket("daily_bonus", 0, Array.Empty<StringPairPacket>()));
        }

        private void CreateAutoDigToggle(VisualElement root)
        {
            _autoDigButton = new Button(ToggleAutoDig);
            _autoDigButton.text = string.Empty;
            _autoDigButton.AddToClassList("hud-button");
            _autoDigButton.AddToClassList("hud-toggle-btn");
            _autoDigButton.AddToClassList("hud-toggle-auto-dig");

            _autoDigLabel = new Label("Копать ✗");
            _autoDigLabel.AddToClassList("hud-toggle-btn-label");
            _autoDigButton.Add(_autoDigLabel);


            Tooltip.AttachTo(_autoDigButton, "Автоматическое копание блоков", _tooltip);

            root.Add(_autoDigButton);
        }

        private void CreateAggressionToggle(VisualElement root)
        {
            _aggressionButton = new Button(ToggleAggression);
            _aggressionButton.text = string.Empty;
            _aggressionButton.AddToClassList("hud-button");
            _aggressionButton.AddToClassList("hud-toggle-btn");
            _aggressionButton.AddToClassList("hud-toggle-aggression");

            _aggressionLabel = new Label("Агрессия ✗");
            _aggressionLabel.AddToClassList("hud-toggle-btn-label");
            _aggressionButton.Add(_aggressionLabel);
            Tooltip.AttachTo(_aggressionButton, "Робот копает под чужими пушками", _tooltip);

            root.Add(_aggressionButton);
        }

        private void CreateSkillContainer(VisualElement root)
        {
            _skillContainer = new VisualElement();
            _skillContainer.name = "MiniSkills";
            _skillContainer.AddToClassList("hud-skill-container");
            root.Add(_skillContainer);
            Debug.Log("[PlayerHUD] Skill container created");
        }

        private void EnsureSkillRow()
        {
            if (_currentSkillRow != null && _skillCountInRow < SKILL_GRID_COLS)
            {
                return;
            }

            _currentSkillRow = new VisualElement();
            _currentSkillRow!.AddToClassList("hud-skill-row");
            _skillContainer!.Add(_currentSkillRow!);
            _skillCountInRow = 0;
        }

        private (Label arrow, VisualElement barFill) CreateSkillIcon(SkillType skill)
        {
            EnsureSkillRow();

            var cell = new VisualElement();
            cell.AddToClassList("hud-skill-icon");

            var iconColumn = new VisualElement();
            iconColumn.AddToClassList("hud-skill-icon-column");

            var arrow = new Label("up");
            arrow.AddToClassList("hud-skill-arrow");
            iconColumn.Add(arrow);

            var iconImage = new Image();
            iconImage.AddToClassList("hud-skill-icon-image");

            var tex = Resources.Load<Texture2D>($"Skills/{skill}");
            if (tex != null)
            {
                RuntimeTextureFactory.ApplySampling(
                    tex,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                iconImage.image = tex;
            }

            iconColumn.Add(iconImage);
            cell.Add(iconColumn);

            var barContainer = new VisualElement();
            barContainer.AddToClassList("hud-skill-bar-container");

            var barFill = new VisualElement();
            barFill.AddToClassList("hud-skill-bar-fill");
            barFill.AddToClassList("hud-skill-bar-segment");
            barContainer.Add(barFill);
            cell.Add(barContainer);

            _currentSkillRow!.Add(cell);
            _skillCountInRow++;

            _skillIcons[skill] = (arrow, barFill);
            return (arrow, barFill);
        }

        private void ToggleAutoDig()
        {
            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.AutoDig = !player.AutoDig;
            }
        }

        private void UpdateAutoDigButton(bool enabled)
        {
            if (_autoDigLabel == null)
            {
                return;
            }

            _autoDigLabel.text = enabled ? "Копать ✓" : "Копать ✗";
            _autoDigLabel.EnableInClassList("hud-toggle-btn-label", true);
            if (_autoDigButton != null)
            {
                _autoDigButton.EnableInClassList("hud-toggle-btn", true);
                _autoDigButton.EnableInClassList("enabled", enabled);
            }

            _autoDigLabel.EnableInClassList("enabled", enabled);
        }

        private void ToggleAggression()
        {
            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.ToggleAggression();
            }
        }

        private void UpdateAggressionButton(bool enabled)
        {
            if (_aggressionLabel == null)
            {
                return;
            }

            _aggressionLabel.text = enabled ? "Агрессия ✓" : "Агрессия ✗";
            _aggressionLabel.EnableInClassList("enabled", enabled);
            _aggressionButton?.EnableInClassList("enabled", enabled);
        }

        private void StartSkeletonPulse()
        {
            const float pulseMin = 0.3f;
            const float pulseMax = 0.7f;
            const float pulseDuration = 0.8f;
            float t = 0f;
            bool rising = true;

            _skeletonPulse = _panel!.schedule.Execute(() =>
            {
                if (_panel == null)
                {
                    return;
                }

                float dt = Time.unscaledDeltaTime;
                t += rising ? dt : -dt;
                if (t >= pulseDuration)
                {
                    t = pulseDuration;
                    rising = false;
                }
                else if (t <= 0f)
                {
                    t = 0f;
                    rising = true;
                }

                float alpha = Mathf.Lerp(pulseMin, pulseMax, t / pulseDuration);
                if (_nicknameLabel != null)
                {
                    _nicknameLabel.style.opacity = alpha;
                }

                if (_levelLabel != null)
                {
                    _levelLabel.style.opacity = alpha;
                }

                if (_hpLabel != null)
                {
                    _hpLabel.style.opacity = alpha;
                }

                if (_hpBarFill != null)
                {
                    _hpBarFill.style.opacity = alpha;
                }

                if (_moneyLabel != null)
                {
                    _moneyLabel.style.opacity = alpha;
                }

                if (_credsLabel != null)
                {
                    _credsLabel.style.opacity = alpha;
                }

                if (_geologyLabel != null)
                {
                    _geologyLabel.style.opacity = alpha;
                }

                if (_basketPercentLabel != null)
                {
                    _basketPercentLabel.style.opacity = alpha;
                }
            }).Every(33);
        }

        private void StopSkeletonPulse()
        {
            if (_skeletonPulse != null)
            {
                _skeletonPulse.Pause();
                _skeletonPulse = null;
            }

            if (_nicknameLabel != null)
            {
                _nicknameLabel.style.opacity = 1;
            }

            if (_levelLabel != null)
            {
                _levelLabel.style.opacity = 1;
            }

            if (_hpLabel != null)
            {
                _hpLabel.style.opacity = 1;
            }

            if (_hpBarFill != null)
            {
                _hpBarFill.style.opacity = 1;
            }

            if (_moneyLabel != null)
            {
                _moneyLabel.style.opacity = 1;
            }

            if (_credsLabel != null)
            {
                _credsLabel.style.opacity = 1;
            }

            if (_geologyLabel != null)
            {
                _geologyLabel.style.opacity = 1;
            }

            if (_basketPercentLabel != null)
            {
                _basketPercentLabel.style.opacity = 1;
            }
        }

        private void RefreshAll()
        {
            if (this == null)
            {
                return;
            }

            var stats = _model;
            if (stats == null)
            {
                return;
            }

            if (!_isLoaded && (stats.Health > 0 || stats.Level > 0 || stats.Money > 0 || !string.IsNullOrEmpty(stats.Nickname)))
            {
                _isLoaded = true;
                StopSkeletonPulse();
            }

            if (_nicknameLabel != null)
            {
                _nicknameLabel.text = string.IsNullOrEmpty(stats.Nickname) ? "---" : stats.Nickname;
            }

            if (_levelLabel != null)
            {
                _levelLabel.text = _isLoaded ? $"Ур: {stats.Level:N0}" : "Ур: ---";
            }

            if (_hpLabel != null)
            {
                _hpLabel.text = _isLoaded ? $"Прочность: {stats.Health:N0}/{stats.MaxHealth:N0}" : "Прочность: --/--";
                _hpLabel.style.opacity = 1;
            }

            float pct = stats.HealthPercent;
            if (_hpBarFill != null)
            {
                _hpBarFill!.style.width = new Length(pct * 100, LengthUnit.Percent);
                _hpBarFill!.style.backgroundColor = pct < 0.25f ? _hpBarLowColor : _hpBarFillColor;
            }

            if (_moneyLabel != null)
            {
                _moneyLabel.text = _isLoaded ? $"$ {stats.Money:N0}" : "$ ---";
            }

            if (_credsLabel != null)
            {
                _credsLabel.text = _isLoaded ? $"C {stats.Creds:N0}" : "C ---";
            }

            if (_geologyLabel != null)
            {
                _geologyLabel.text = string.IsNullOrEmpty(stats.GeologyText) || !_isLoaded
                    ? "Геология: 0/0"
                    : $"Геология: {stats.GeologyCurrent}/{stats.GeologyMax} ({stats.GeologyText})";
            }

            if (_basketPercentLabel != null)
            {
                _basketPercentLabel.text = _isLoaded ? $"Груз: {stats.BasketMaxPercent}%" : "Груз: --%";
            }

            for (int i = 0; i < _basketCrystalLabels.Count && i < stats.BasketContents.Length; i++)
            {
                _basketCrystalLabels[i].text = $"{FormatCompact(stats.BasketContents[i])}/{FormatCompact(stats.BasketCapacity)}";
            }
        }

        private void RebuildCrystalRows()
        {
            if (_basketContainer == null)
            {
                return;
            }

            _basketContainer.Clear();
            _basketCrystalLabels.Clear();

            for (int i = 0; i < _crystalTextures.Count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("hud-crystal-row");

                var dot = new Image();
                dot.AddToClassList("hud-crystal-dot");
                if (_crystalTextures[i] != null)
                {
                    dot.style.backgroundImage = new StyleBackground(_crystalTextures[i]);
                }

                row.Add(dot);

                var label = new Label("0/0");
                label.AddToClassList("hud-crystal-label");
                row.Add(label);

                _basketCrystalLabels.Add(label);
                _basketContainer.Add(row);
            }
        }

        private static string FormatCompact(long val)
        {
            if (val >= 1_000_000)
            {
                return $"{val / 1_000_000f:F1}M";
            }

            if (val >= 10_000)
            {
                return $"{val / 1_000}K";
            }

            return val.ToString("N0");
        }

        private void OnSkillProgress(SkillType skill, long current, long max)
        {
            Debug.Log($"[PlayerHUD] OnSkillProgress: skill={skill}, current={current}, max={max}");
            if (!_skillIcons.TryGetValue(skill, out var icon))
            {
                var created = CreateSkillIcon(skill);
                icon.arrow = created.arrow;
                icon.barFill = created.barFill;
            }

            float progress = max > 0 ? (float)current / max : 0f;

            icon.barFill.style.backgroundColor = Color.Lerp(Color.green, Color.red, Mathf.Clamp01(progress));

            icon.arrow.text = progress >= 1f ? "up" : string.Empty;

            if (progress >= 1f)
            {
                StopBarPulse(skill);
                StartBounce(skill, icon.arrow);
            }
            else
            {
                StopBounce(skill, icon.arrow);
                StartBarPulse(skill, icon.barFill, progress);
            }
        }

        private void StartBounce(SkillType skill, Label arrow)
        {
            StopBounce(skill, arrow);
            arrow.style.translate = new Translate(0, 0);
        }

        private void StopBounce(SkillType skill, Label arrow)
        {
            if (_bounceSchedules.TryGetValue(skill, out var existing))
            {
                existing.Pause();
                _bounceSchedules.Remove(skill);
            }

            arrow.style.translate = new Translate(0, 0);
        }

        // Full height of .hud-skill-bar-container in HUD.uss. The fill is sized
        // to this once and then scaled, so the animation never touches layout.
        private const float SkillBarHeightPixels = 24f;

        /// <summary>
        /// Applies skill progress without installing a permanent UI scheduler.
        /// </summary>
        /// <remarks>
        /// A scheduler per skill used to mutate inline transforms every 33 ms
        /// for the entire session. Even though transforms avoid Yoga layout,
        /// every mutation still invalidates UI Toolkit painting. Progress only
        /// changes when a new packet arrives, so its visual state does too.
        /// </remarks>
        private void StartBarPulse(SkillType skill, VisualElement barFill, float progress)
        {
            StopBarPulse(skill);

            float normalizedProgress = Mathf.Clamp01(progress);

            barFill.style.height = new Length(SkillBarHeightPixels, LengthUnit.Pixel);
            barFill.style.transformOrigin =
                new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
            barFill.style.scale = new Scale(new Vector2(1f, normalizedProgress));
        }

        private void StopBarPulse(SkillType skill)
        {
            if (_pulseSchedules.TryGetValue(skill, out var existing))
            {
                existing.Pause();
                _pulseSchedules.Remove(skill);
            }

            // Leaves the bar full rather than frozen mid-pulse. Both callers
            // want that: the skill reached maximum (StopBounce path), or
            // StartBarPulse is about to set its own scale immediately after.
            if (_skillIcons.TryGetValue(skill, out var icon) && icon.barFill != null)
            {
                icon.barFill.style.scale = new Scale(Vector2.one);
            }
        }

        private void CreateChatButton(VisualElement root)
        {
            _chatButton = new Button(() => _globalChatUI.Toggle());
            _chatButton.text = "Чат";
            _chatButton.AddToClassList("hud-btn-action");
            _chatButton.AddToClassList("hud-chat-button");
            Tooltip.AttachTo(_chatButton, "Открыть чат", _tooltip);

            root.Add(_chatButton);
        }

        private void CreateButtonsAndPopups(VisualElement root)
        {
            _respawnPopup = CreateRespawnPopup();
            _buildingsPopup = CreatePopup("Мои здания");
            _faqPopup = CreatePopup("FAQ");

            // ProgrammatorGrid создаётся динамически, поэтому инжектим его через
            // текущий игровой resolver сразу после AddComponent.
            _programmatorGrid = gameObject.AddComponent<ProgrammatorGrid>();
            _resolver.Inject(_programmatorGrid);

            root.Add(_respawnPopup);
            root.Add(_buildingsPopup);
            root.Add(_faqPopup);

            CreateRespawnButton(root, () => _respawnPopup.style.display = DisplayStyle.Flex);
            CreateMyBuildingsButton(root, () => _buildingsPopup.style.display = DisplayStyle.Flex);
            CreateFaqButton(root, () => _faqPopup.style.display = DisplayStyle.Flex);
            CreateProgrammatorButton(root, () => _programmatorGrid.Show());
        }

        private VisualElement CreatePopup(string title)
        {
            var popup = new VisualElement();
            popup.AddToClassList("popup-overlay");
            popup.style.display = DisplayStyle.None;

            var dimmer = new VisualElement();
            dimmer.pickingMode = PickingMode.Ignore;
            dimmer.AddToClassList("popup-dimmer");
            popup.Add(dimmer);

            var panel = new VisualElement();
            panel.AddToClassList("popup-panel");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("popup-title");
            panel.Add(titleLabel);

            var closeBtn = new Button(() => popup.style.display = DisplayStyle.None);
            closeBtn.text = "Закрыть";
            closeBtn.AddToClassList("popup-close-btn");

            panel.Add(closeBtn);
            popup.Add(panel);
            return popup;
        }

        private VisualElement CreateRespawnPopup()
        {
            var popup = new VisualElement();
            popup.AddToClassList("popup-overlay");
            popup.style.display = DisplayStyle.None;

            var dimmer = new VisualElement();
            dimmer.pickingMode = PickingMode.Ignore;
            dimmer.AddToClassList("popup-dimmer");
            popup.Add(dimmer);

            var panel = new VisualElement();
            panel.AddToClassList("popup-panel");

            var titleLabel = new Label("Респавн");
            titleLabel.AddToClassList("popup-title");
            panel.Add(titleLabel);

            var btnRow = new VisualElement();
            btnRow.AddToClassList("popup-btn-row");

            var okBtn = new Button(() =>
            {
                var ns = _networkService;
                ns?.SendAction(new SuicidePacket());
                popup.style.display = DisplayStyle.None;
            });
            okBtn.text = "ОК";
            okBtn.AddToClassList("popup-btn");
            btnRow.Add(okBtn);

            var backBtn = new Button(() => popup.style.display = DisplayStyle.None);
            backBtn.text = "Назад";
            backBtn.AddToClassList("popup-btn");
            btnRow.Add(backBtn);

            panel.Add(btnRow);
            popup.Add(panel);
            return popup;
        }

        private void CreateRespawnButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Респавн";
            btn.AddToClassList("hud-btn-action");
            btn.AddToClassList("hud-btn-top-row");
            btn.style.right = 10 + ((100 + 6) * 2);
            root.Add(btn);
        }

        private void CreateMyBuildingsButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Мои здания";
            btn.AddToClassList("hud-btn-action");
            btn.AddToClassList("hud-btn-top-row");
            btn.style.right = 10 + (100 + 6);
            root.Add(btn);
        }

        private void CreateFaqButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "FAQ";
            btn.AddToClassList("hud-btn-action");
            btn.AddToClassList("hud-btn-top-row");
            btn.style.right = 10;
            root.Add(btn);
        }

        private void CreateMissionPanel(VisualElement root)
        {
            _missionPanel = new VisualElement();
            _missionPanel.name = "MissionPanel";
            _missionPanel.AddToClassList("hud-mission-panel");
            _missionPanel.style.display = DisplayStyle.None;

            _missionTitleLabel = new Label("---");
            _missionTitleLabel.AddToClassList("hud-stat");
            _missionTitleLabel.AddToClassList("hud-mission-title");
            _missionPanel.Add(_missionTitleLabel);

            _missionDescLabel = new Label(string.Empty);
            _missionDescLabel.AddToClassList("hud-stat");
            _missionDescLabel.AddToClassList("hud-stat-wrap");
            _missionDescLabel.AddToClassList("hud-mission-desc");
            _missionPanel.Add(_missionDescLabel);

            var progressRow = new VisualElement();
            progressRow.AddToClassList("hud-mission-progress-row");

            _missionProgressLabel = new Label("0/0");
            _missionProgressLabel.AddToClassList("hud-stat");
            _missionProgressLabel.AddToClassList("hud-mission-progress-label");
            progressRow.Add(_missionProgressLabel);

            var barBg = new VisualElement();
            barBg.AddToClassList("hud-mission-progress-bar");

            _missionProgressFill = new VisualElement();
            _missionProgressFill.AddToClassList("hud-mission-progress-fill");
            barBg.Add(_missionProgressFill);

            progressRow.Add(barBg);
            _missionPanel.Add(progressRow);

            root.Add(_missionPanel);
        }

        private void UpdateMissionPanel()
        {
            var stats = _model;
            if (stats == null)
            {
                return;
            }

            if (stats.IsMissionActive)
            {
                _missionPanel!.style.display = DisplayStyle.Flex;
            }
            else
            {
                _missionPanel!.style.display = DisplayStyle.None;
                return;
            }

            _missionTitleLabel!.text = stats.MissionTitle ?? "Миссия";
            _missionDescLabel!.text = stats.MissionDescription ?? string.Empty;

            float pct = stats.MissionMaxProgress > 0 ? (float)stats.MissionProgress / stats.MissionMaxProgress : 0f;
            _missionProgressFill!.style.width = new Length(Mathf.Clamp01(pct) * 100, LengthUnit.Percent);
            _missionProgressLabel!.text = $"{stats.MissionProgress:N0}/{stats.MissionMaxProgress:N0}";
        }

        private void CreateProgrammatorButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Программатор";
            btn.AddToClassList("hud-btn-action");
            btn.AddToClassList("hud-programmator-button");
            root.Add(btn);
        }
    }
}
