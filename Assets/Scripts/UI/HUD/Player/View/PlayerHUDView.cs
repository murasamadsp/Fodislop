#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Core.Models;
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
    public class PlayerHUDView : MonoBehaviour, ILocalizableUI
    {
        private const int SKILL_GRID_COLS = 4;

        private Color _hpBarFillColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        private Color _hpBarLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);

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
        [Inject]
        private Fodinae.Core.Interfaces.ILocalPlayerState _localPlayer = null!;
        private IVisualElementScheduledItem? _skeletonPulse;
        private TemplateContainer? _hudRoot;
        private Button? _bonusButton;
        private VisualElement? _bonusPanel;
        private Label? _bonusStatusLabel;
        private Button? _bonusClaimButton;
        private bool _isBonusOpen;

        private Label? _nicknameLabel;
        private Label? _levelLabel;
        private Label? _hpLabel;
        private Label? _hpPercentLabel;
        private VisualElement? _hpBarFill;
        private Label? _moneyLabel;
        private Label? _credsLabel;
        private Label? _geologyLabel;
        private Label? _basketPercentLabel;
        private VisualElement? _basketContainer;
        private VisualElement? _skillContainer;
        private Button? _autoDigButton;
        private VisualElement? _autoDigIndicator;
        private Label? _autoDigLabel;
        private Button? _aggressionButton;
        private VisualElement? _aggressionIndicator;
        private Label? _aggressionLabel;

        private VisualElement? _currentSkillRow;
        private int _skillCountInRow = 0;
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
        private ILocalizationService _loc = null!;
        private VisualElement? _missionPanel;
        private Label? _missionTitleLabel;
        private Label? _missionDescLabel;
        private VisualElement? _missionProgressFill;
        private Label? _missionProgressLabel;

        protected void Start()
        {
            TryStartInitialization();
        }

        public void EnsureInitialized()
        {
            TryStartInitialization();
        }

        protected void Update()
        {
            _programmatorGrid?.Tick();
        }

        private void TryStartInitialization()
        {
            if (_initializationStarted)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null || _model == null ||
                _globalChatUI == null || _assetLoader == null || _networkService == null || _inputBlocker == null || _loc == null)
            {
                return;
            }

            _initializationStarted = true;
            StartAsync(this.destroyCancellationToken).Forget();
        }

        private async UniTaskVoid StartAsync(System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                InitializeHUD();
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"[PlayerHUD] HUD unavailable: {exception.Message}");
                return;
            }

            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            _loc.RegisterLocalizable(this);

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

        /// <summary>
        /// Переприменяет локализованный текст после смены языка: статические
        /// ключи UXML через UILocalizer, динамические лейблы — RefreshAll()
        /// (он перечитывает модель и ставит тексты заново), плюс программатор,
        /// если его дерево уже построено.
        /// </summary>
        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(PlayerHUDView));
            if (_doc == null || _doc.rootVisualElement == null || _loc == null)
            {
                // Тихий возврат безопасен: ApplyLocalizedText идемпотентен и будет
                // вызван снова (реестр / RegisterLocalizable), когда панель и
                // сервис будут готовы.
                return;
            }

            UILocalizer.Apply(_doc.rootVisualElement, _loc);
            RefreshAll();
            _programmatorGrid?.RefreshLocalization();
            UILocalizer.AssertLocalized(_doc.rootVisualElement, _loc);
        }

        protected void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _programmatorGrid?.Dispose();
            _programmatorGrid = null;
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

            var player = _localPlayer.Current;
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
            _programmatorGrid ??= new ProgrammatorGrid(_doc, _loc);
            _programmatorGrid?.Initialize();
            _tooltip = new Tooltip();
            _tooltip.Initialize(_doc);

            // Тир раскладки вместо @media: класс на корне панели.
            UILayoutTier.Attach(_doc.rootVisualElement);

            LoadTemplate(_doc.rootVisualElement);

            if (_model != null)
            {
                _model.OnSkillProgress += OnSkillProgress;
                _model.OnStatusLinesChanged += RebuildStatusPanel;
                _model.OnMissionChanged += UpdateMissionPanel;
            }

            var player = _localPlayer.Current;
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

            // Кнопка-уведомление и панель бонусов синхронизируются с текущим
            // состоянием сразу: модель может уже знать о доступном бонусе до
            // первого события изменения.
            UpdateDailyBonusPanel();

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

        private void LoadTemplate(VisualElement root)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/PlayerHUD") ??
                throw new InvalidOperationException(
                    "[PlayerHUD] Resources/UI/PlayerHUD.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
            _hudRoot = tree;
            root.Add(tree);

            // Статические ключи UXML (hud.*, тултипы) резолвятся сразу при
            // сборке, а не только по событию смены языка.
            UILocalizer.Apply(tree, _loc);

            _nicknameLabel = tree.Q<Label>("NicknameLabel") ??
                throw new InvalidOperationException("[PlayerHUD] NicknameLabel is missing from PlayerHUD.uxml.");
            _levelLabel = tree.Q<Label>("LevelLabel") ??
                throw new InvalidOperationException("[PlayerHUD] LevelLabel is missing from PlayerHUD.uxml.");
            Button clanButton = tree.Q<Button>("ClanButton") ??
                throw new InvalidOperationException("[PlayerHUD] ClanButton is missing from PlayerHUD.uxml.");
            clanButton.clicked += () => _networkService?.Send(new OpenClanClickPacket());

            _hpLabel = tree.Q<Label>("HPLabel") ??
                throw new InvalidOperationException("[PlayerHUD] HPLabel is missing from PlayerHUD.uxml.");
            _hpPercentLabel = tree.Q<Label>("HPPercentLabel") ??
                throw new InvalidOperationException("[PlayerHUD] HPPercentLabel is missing from PlayerHUD.uxml.");
            _hpBarFill = tree.Q<VisualElement>("HPBarFill") ??
                throw new InvalidOperationException("[PlayerHUD] HPBarFill is missing from PlayerHUD.uxml.");

            _moneyLabel = tree.Q<Label>("MoneyLabel") ??
                throw new InvalidOperationException("[PlayerHUD] MoneyLabel is missing from PlayerHUD.uxml.");
            _credsLabel = tree.Q<Label>("CredsLabel") ??
                throw new InvalidOperationException("[PlayerHUD] CredsLabel is missing from PlayerHUD.uxml.");
            _basketPercentLabel = tree.Q<Label>("BasketPercentLabel") ??
                throw new InvalidOperationException("[PlayerHUD] BasketPercentLabel is missing from PlayerHUD.uxml.");
            _geologyLabel = tree.Q<Label>("GeologyLabel") ??
                throw new InvalidOperationException("[PlayerHUD] GeologyLabel is missing from PlayerHUD.uxml.");

            _basketContainer = tree.Q<VisualElement>("BasketContainer") ??
                throw new InvalidOperationException("[PlayerHUD] BasketContainer is missing from PlayerHUD.uxml.");
            _skillContainer = tree.Q<VisualElement>("SkillContainer") ??
                throw new InvalidOperationException("[PlayerHUD] SkillContainer is missing from PlayerHUD.uxml.");
            _statusPanel = tree.Q<VisualElement>("StatusPanel") ??
                throw new InvalidOperationException("[PlayerHUD] StatusPanel is missing from PlayerHUD.uxml.");

            // Авто-копка и агрессия: индикатор — LED, текст кнопки статичен.
            _autoDigButton = tree.Q<Button>("AutoDigButton") ??
                throw new InvalidOperationException("[PlayerHUD] AutoDigButton is missing from PlayerHUD.uxml.");
            _autoDigButton.clicked += ToggleAutoDig;
            _autoDigIndicator = tree.Q<VisualElement>("AutoDigIndicator") ??
                throw new InvalidOperationException("[PlayerHUD] AutoDigIndicator is missing from PlayerHUD.uxml.");
            _autoDigLabel = tree.Q<Label>("AutoDigLabel") ??
                throw new InvalidOperationException("[PlayerHUD] AutoDigLabel is missing from PlayerHUD.uxml.");
            Tooltip.AttachTo(_autoDigButton, () => _loc.Get("hud.tooltip.autodig"), _tooltip!);

            _aggressionButton = tree.Q<Button>("AggressionButton") ??
                throw new InvalidOperationException("[PlayerHUD] AggressionButton is missing from PlayerHUD.uxml.");
            _aggressionButton.clicked += ToggleAggression;
            _aggressionIndicator = tree.Q<VisualElement>("AggressionIndicator") ??
                throw new InvalidOperationException("[PlayerHUD] AggressionIndicator is missing from PlayerHUD.uxml.");
            _aggressionLabel = tree.Q<Label>("AggressionLabel") ??
                throw new InvalidOperationException("[PlayerHUD] AggressionLabel is missing from PlayerHUD.uxml.");
            Tooltip.AttachTo(_aggressionButton, () => _loc.Get("hud.tooltip.aggression"), _tooltip!);

            Button chatButton = tree.Q<Button>("ChatButton") ??
                throw new InvalidOperationException("[PlayerHUD] ChatButton is missing from PlayerHUD.uxml.");
            chatButton.clicked += () => _globalChatUI.Toggle();
            Tooltip.AttachTo(chatButton, () => _loc.Get("hud.tooltip.chat"), _tooltip!);

            _bonusButton = tree.Q<Button>("BonusButton") ??
                throw new InvalidOperationException("[PlayerHUD] BonusButton is missing from PlayerHUD.uxml.");
            _bonusButton.clicked += ToggleBonusPanel;
            Tooltip.AttachTo(_bonusButton, () => _loc.Get("hud.tooltip.bonus"), _tooltip!);

            _bonusPanel = tree.Q<VisualElement>("BonusPanel") ??
                throw new InvalidOperationException("[PlayerHUD] BonusPanel is missing from PlayerHUD.uxml.");
            Button bonusClose = tree.Q<Button>("BonusCloseButton") ??
                throw new InvalidOperationException("[PlayerHUD] BonusCloseButton is missing from PlayerHUD.uxml.");
            bonusClose.clicked += ToggleBonusPanel;
            _bonusStatusLabel = tree.Q<Label>("BonusStatusLabel") ??
                throw new InvalidOperationException("[PlayerHUD] BonusStatusLabel is missing from PlayerHUD.uxml.");
            _bonusClaimButton = tree.Q<Button>("BonusClaimButton") ??
                throw new InvalidOperationException("[PlayerHUD] BonusClaimButton is missing from PlayerHUD.uxml.");
            _bonusClaimButton.clicked += ClaimDailyBonus;

            // Попапы: респавн, здания, FAQ.
            _respawnPopup = tree.Q<VisualElement>("RespawnPopup") ??
                throw new InvalidOperationException("[PlayerHUD] RespawnPopup is missing from PlayerHUD.uxml.");
            Button respawnConfirm = tree.Q<Button>("RespawnConfirmButton") ??
                throw new InvalidOperationException("[PlayerHUD] RespawnConfirmButton is missing from PlayerHUD.uxml.");
            respawnConfirm.clicked += () =>
            {
                var ns = _networkService;
                ns?.SendAction(new SuicidePacket());
                _respawnPopup.style.display = DisplayStyle.None;
            };
            Button respawnCancel = tree.Q<Button>("RespawnCancelButton") ??
                throw new InvalidOperationException("[PlayerHUD] RespawnCancelButton is missing from PlayerHUD.uxml.");
            respawnCancel.clicked += () => _respawnPopup.style.display = DisplayStyle.None;
            Button respawnButton = tree.Q<Button>("RespawnButton") ??
                throw new InvalidOperationException("[PlayerHUD] RespawnButton is missing from PlayerHUD.uxml.");
            respawnButton.clicked += () => _respawnPopup.style.display = DisplayStyle.Flex;

            _buildingsPopup = tree.Q<VisualElement>("BuildingsPopup") ??
                throw new InvalidOperationException("[PlayerHUD] BuildingsPopup is missing from PlayerHUD.uxml.");
            Button buildingsClose = tree.Q<Button>("BuildingsCloseButton") ??
                throw new InvalidOperationException("[PlayerHUD] BuildingsCloseButton is missing from PlayerHUD.uxml.");
            buildingsClose.clicked += () => _buildingsPopup.style.display = DisplayStyle.None;
            Button buildingsButton = tree.Q<Button>("BuildingsButton") ??
                throw new InvalidOperationException("[PlayerHUD] BuildingsButton is missing from PlayerHUD.uxml.");
            buildingsButton.clicked += () => _buildingsPopup.style.display = DisplayStyle.Flex;

            _faqPopup = tree.Q<VisualElement>("FaqPopup") ??
                throw new InvalidOperationException("[PlayerHUD] FaqPopup is missing from PlayerHUD.uxml.");
            Button faqClose = tree.Q<Button>("FaqCloseButton") ??
                throw new InvalidOperationException("[PlayerHUD] FaqCloseButton is missing from PlayerHUD.uxml.");
            faqClose.clicked += () => _faqPopup.style.display = DisplayStyle.None;
            Button faqButton = tree.Q<Button>("FaqButton") ??
                throw new InvalidOperationException("[PlayerHUD] FaqButton is missing from PlayerHUD.uxml.");
            faqButton.clicked += () => _faqPopup.style.display = DisplayStyle.Flex;

            Button programmatorButton = tree.Q<Button>("ProgrammatorButton") ??
                throw new InvalidOperationException("[PlayerHUD] ProgrammatorButton is missing from PlayerHUD.uxml.");
            programmatorButton.text = _loc.Get("hud.programmator");
            programmatorButton.clicked += () => _programmatorGrid?.Show();

            _missionPanel = tree.Q<VisualElement>("MissionPanel") ??
                throw new InvalidOperationException("[PlayerHUD] MissionPanel is missing from PlayerHUD.uxml.");
            _missionTitleLabel = tree.Q<Label>("MissionTitleLabel") ??
                throw new InvalidOperationException("[PlayerHUD] MissionTitleLabel is missing from PlayerHUD.uxml.");
            _missionDescLabel = tree.Q<Label>("MissionDescLabel") ??
                throw new InvalidOperationException("[PlayerHUD] MissionDescLabel is missing from PlayerHUD.uxml.");
            _missionProgressFill = tree.Q<VisualElement>("MissionProgressFill") ??
                throw new InvalidOperationException("[PlayerHUD] MissionProgressFill is missing from PlayerHUD.uxml.");
            _missionProgressLabel = tree.Q<Label>("MissionProgressLabel") ??
                throw new InvalidOperationException("[PlayerHUD] MissionProgressLabel is missing from PlayerHUD.uxml.");
        }

        private void ToggleBonusPanel()
        {
            _isBonusOpen = !_isBonusOpen;
            _bonusPanel!.style.display = _isBonusOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_isBonusOpen)
            {
                UpdateDailyBonusPanel();
            }
        }

        private void UpdateDailyBonusPanel()
        {
            if (_bonusStatusLabel == null || _bonusButton == null)
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
                // Кнопка-уведомление показывается только когда есть что забирать.
                _bonusButton.style.display = DisplayStyle.Flex;
                _bonusStatusLabel!.text = _loc.Get("hud.bonus.available");
                _bonusStatusLabel!.style.color = Color.green;
                _bonusClaimButton!.style.display = DisplayStyle.Flex;
            }
            else
            {
                _bonusButton.style.display = DisplayStyle.None;
                _bonusStatusLabel!.text = _loc.Get("hud.bonus.none");
                _bonusStatusLabel!.style.color = Color.gray;
                _bonusClaimButton!.style.display = DisplayStyle.None;
            }
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
            var ns = _networkService;
            ns?.Send(new ElementClickPacket("daily_bonus", 0, Array.Empty<StringPairPacket>()));
        }

        private void ToggleAutoDig()
        {
            var player = _localPlayer.Current;
            if (player != null)
            {
                player.AutoDig = !player.AutoDig;
            }
        }

        private void UpdateAutoDigButton(bool enabled)
        {
            _autoDigButton?.EnableInClassList("enabled", enabled);
            if (_autoDigLabel != null)
            {
                _autoDigLabel.text = enabled ? _loc.Get("hud.autodig.on") : _loc.Get("hud.autodig.off");
            }

            _autoDigIndicator?.EnableInClassList("hud-mode-led--active", enabled);
        }

        private void ToggleAggression()
        {
            var player = _localPlayer.Current;
            if (player != null)
            {
                player.ToggleAggression();
            }
        }

        private void UpdateAggressionButton(bool enabled)
        {
            _aggressionButton?.EnableInClassList("enabled", enabled);
            if (_aggressionLabel != null)
            {
                _aggressionLabel.text = enabled ? _loc.Get("hud.aggression.on") : _loc.Get("hud.aggression.off");
            }

            _aggressionIndicator?.EnableInClassList("hud-mode-led--active", enabled);
        }

        private void StartSkeletonPulse()
        {
            const float pulseMin = 0.3f;
            const float pulseMax = 0.7f;
            const float pulseDuration = 0.8f;
            float t = 0f;
            bool rising = true;

            _skeletonPulse = _hudRoot!.schedule.Execute(() =>
            {
                if (_hudRoot == null)
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

                if (_hpPercentLabel != null)
                {
                    _hpPercentLabel.style.opacity = alpha;
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

            if (_hpPercentLabel != null)
            {
                _hpPercentLabel.style.opacity = 1;
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
                _levelLabel.text = _isLoaded ? _loc.Get("hud.level", stats.Level) : _loc.Get("hud.level_unknown");
            }

            if (_hpLabel != null)
            {
                string hpPrefix = _loc.Get("hud.health");
                _hpLabel.text = _isLoaded ? $"{hpPrefix}: {stats.Health:N0} / {stats.MaxHealth:N0}" : $"{hpPrefix}: -- / --";
                _hpLabel.style.opacity = 1;
            }

            float pct = stats.HealthPercent;
            if (_hpPercentLabel != null)
            {
                _hpPercentLabel.text = $"{pct * 100f:F0}%";
            }

            if (_hpBarFill != null)
            {
                _hpBarFill!.style.width = new Length(pct * 100, LengthUnit.Percent);
                _hpBarFill!.style.backgroundColor = pct < 0.25f ? _hpBarLowColor : _hpBarFillColor;
            }

            if (_moneyLabel != null)
            {
                _moneyLabel.text = _isLoaded ? $"{stats.Money:N0}" : "---";
            }

            if (_credsLabel != null)
            {
                _credsLabel.text = _isLoaded ? $"{stats.Creds:N0}" : "---";
            }

            if (_geologyLabel != null)
            {
                _geologyLabel.text = string.IsNullOrEmpty(stats.GeologyText) || !_isLoaded
                    ? _loc.Get("hud.geology_zero")
                    : _loc.Get("hud.geology", stats.GeologyCurrent, stats.GeologyMax, stats.GeologyText);
            }

            if (_basketPercentLabel != null)
            {
                _basketPercentLabel.text = _isLoaded ? $"{stats.BasketMaxPercent}%" : "--%";
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

            _missionTitleLabel!.text = stats.MissionTitle ?? _loc.Get("hud.mission");
            _missionDescLabel!.text = stats.MissionDescription ?? string.Empty;

            float pct = stats.MissionMaxProgress > 0 ? (float)stats.MissionProgress / stats.MissionMaxProgress : 0f;
            _missionProgressFill!.style.width = new Length(Mathf.Clamp01(pct) * 100, LengthUnit.Percent);
            _missionProgressLabel!.text = $"{stats.MissionProgress:N0}/{stats.MissionMaxProgress:N0}";
        }
    }
}
