#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Core.Models;
using Fodinae.Networking;
using Fodinae.UI.HUD.Inventory.Interfaces;
using Fodinae.UI.HUD.Inventory.Model;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI.HUD.Inventory.View
{
    public class InventoryView : MonoBehaviour, ILocalizableUI
    {

        /// <summary>
        /// Сколько предметов показывает короткий список. Четыре — это ровно один
        /// столбец при четырёх строках, как в свёрнутом состоянии оригинала.
        /// </summary>
        private const int SHORTLISTSIZE = 4;

        /// <summary>Строк в сетке. Столько же было в старом клиенте.</summary>
        private const int ROWCOUNT = 4;

        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private IInventoryModel _model = null!;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private UIInputManager _uiInput = null!;

        private readonly Dictionary<int, List<VisualElement>> _slotElements = new();
        private readonly List<int> _occupiedSlots = new();
        private readonly InventoryDragAndContextMenu _dragAndContext = new();

        private VisualElement? _hotbarContainer;
        private Button? _inventoryButton;
        private VisualElement? _hotbarSlots;
        private VisualElement? _fullSlots;
        private Label? _toggleGlyph;
        private bool _isInventoryOpen;
        private Label? _capacityLabel;

        private int _lastSelectedSlot = -1;
        private VisualElement _tooltipWrapper = null!;
        private VisualElement _tooltipBg = null!;
        private Label _tooltipName = null!;
        private Label _tooltipDesc = null!;
        private bool _initialized;

        protected void Start()
        {
            TryInitialize();
        }

        public void EnsureInitialized()
        {
            TryInitialize();
        }

        protected void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_model != null)
            {
                _model.OnSlotChanged -= RefreshSlot;
                _model.OnSlotSelected -= OnModelSlotSelected;
            }

            _dragAndContext.Cleanup(_doc?.rootVisualElement);
        }

        protected void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.tabKey.wasPressedThisFrame ||
                (Keyboard.current.iKey.wasPressedThisFrame && !_uiInput.IsChatFocused))
            {
                ToggleInventory();
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(0);
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(1);
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(2);
            }
            else if (Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(3);
            }
            else if (Keyboard.current.digit5Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(4);
            }
            else if (Keyboard.current.digit6Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(5);
            }
            else if (Keyboard.current.digit7Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(6);
            }
            else if (Keyboard.current.digit8Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(7);
            }
            else if (Keyboard.current.digit9Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(8);
            }
            else if (Keyboard.current.enterKey.wasPressedThisFrame)
            {
                _model!.UseSelectedItem();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            if (_doc.rootVisualElement == null)
            {
                throw new InvalidOperationException(
                    "[InventoryView] Injected UIDocument has no root visual element.");
            }

            IInventoryModel model = _model ?? throw new InvalidOperationException(
                "[InventoryView] IInventoryModel injection is required before initialization.");
            _model = model;

            if (_inputBlocker == null)
            {
                throw new InvalidOperationException(
                    "[InventoryView] IInputBlocker injection is required before initialization.");
            }

            if (_loc == null)
            {
                throw new InvalidOperationException(
                    "[InventoryView] ILocalizationService injection is required before initialization.");
            }

            _model.OnSlotChanged += RefreshSlot;
            _model.OnSlotSelected += OnModelSlotSelected;

            CreateTooltip(_doc.rootVisualElement);
            BuildUI();
            _initialized = true;

            _loc.RegisterLocalizable(this);
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(InventoryView));
            UILocalizer.Apply(_doc.rootVisualElement, _loc);
            if (_capacityLabel != null)
            {
                _capacityLabel.text = _loc.Get("inventory.capacity", InventoryModel.TOTALSLOTS);
            }

            UILocalizer.AssertLocalized(_doc.rootVisualElement, _loc);
        }

        private void OnModelSlotSelected(int slotIndex)
        {
            if (_lastSelectedSlot >= 0 && _slotElements.ContainsKey(_lastSelectedSlot))
            {
                foreach (var cell in _slotElements[_lastSelectedSlot])
                {
                    cell.RemoveFromClassList("inv-cell--selected");
                }
            }

            _lastSelectedSlot = slotIndex;

            if (slotIndex >= 0 && _slotElements.ContainsKey(slotIndex))
            {
                foreach (var cell in _slotElements[slotIndex])
                {
                    cell.AddToClassList("inv-cell--selected");
                }
            }

            if (slotIndex >= 0)
            {
                var item = _model!.GetSlot(slotIndex);
                if (item != null)
                {
                    _tooltipName.text = item.Name;
                    _tooltipDesc.text = item.Description ?? string.Empty;
                    _tooltipWrapper.style.display = DisplayStyle.Flex;
                    return;
                }
            }

            _tooltipWrapper.style.display = DisplayStyle.None;
        }

        private void CreateTooltip(VisualElement root)
        {
            _tooltipWrapper = new VisualElement();
            _tooltipWrapper.AddToClassList("inv-tooltip-wrapper");
            _tooltipWrapper.style.display = DisplayStyle.None;

            _tooltipBg = new VisualElement();
            _tooltipBg.AddToClassList("inv-tooltip-bg");

            _tooltipName = new Label();
            _tooltipName.AddToClassList("inv-tooltip-name");
            _tooltipBg.Add(_tooltipName);

            _tooltipDesc = new Label();
            _tooltipDesc.AddToClassList("inv-tooltip-desc");
            _tooltipBg.Add(_tooltipDesc);

            _tooltipWrapper.Add(_tooltipBg);
            root.Add(_tooltipWrapper);
        }

        private void BuildUI()
        {
            var root = _doc.rootVisualElement;

            var uxml = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.InventoryUxml);
            if (uxml != null)
            {
                TemplateContainer tree = uxml.Instantiate();
                tree.AddToClassList("ui-fullscreen");
                tree.pickingMode = PickingMode.Ignore;
                root.Add(tree);

                if (_loc != null)
                {
                    UILocalizer.Apply(tree, _loc);
                }

                _hotbarContainer = tree.Q<VisualElement>("HotbarContainer");
                _hotbarSlots = tree.Q<VisualElement>("HotbarSlots") ?? _hotbarContainer;

                _inventoryButton = tree.Q<Button>("InventoryToggleBtn");
                if (_inventoryButton != null)
                {
                    _inventoryButton.clicked += ToggleFullInventory;
                    if (_loc != null)
                    {
                        _inventoryButton.tooltip = $"{_loc.Get("inventory.open")} — {_loc.Get("inventory.hotbar")}";

                        // Кнопка — узкая вертикальная полоса шириной в пятнадцать
                        // пикселей, как в старом клиенте: название туда не влезает
                        // и вылезало поверх сетки. Внутри остаётся только стрелка,
                        // которая разворачивается при сворачивании — тем же
                        // признаком, что и треугольник в эталоне.
                        _toggleGlyph = _inventoryButton.Q<Label>();
                    }

                    ApplyInventoryMode();
                }

                _fullSlots = tree.Q<VisualElement>("InventoryGrid");
                RebuildSlots();

            }
            else
            {
                throw new InvalidOperationException("[InventoryView] Failed to load UI/Inventory.uxml");
            }
        }

        private VisualElement CreateCell(int slotIndex, string name)
        {
            var cell = new VisualElement();
            cell.name = name;
            cell.userData = slotIndex;
            cell.AddToClassList("inv-cell");
            // InventoryRoot стоит в picking-mode="Ignore" (клики пустого поля
            // уходят миру); ячейка обязана явно вернуть Position, иначе Ignore
            // наследуется на поддерево и слот не получает мышь вообще — хотбар
            // выглядит как мёртвый интерфейс.
            cell.pickingMode = PickingMode.Position;

            // Вид ячейки — целиком в .inv-cell из Inventory.uss, и здесь его
            // задавать нельзя. Раньше тут стояли размер, отступы, цвет, рамки и
            // скругления инлайном; инлайн старше таблицы стилей, поэтому правки
            // в USS не действовали вовсе — оттуда и брались голубая обводка,
            // крупные скругления и отступ, ломавший шаг сетки.

            var icon = new VisualElement();
            icon.name = "Icon";
            icon.AddToClassList("inv-icon");
            icon.style.display = DisplayStyle.None;
            icon.pickingMode = PickingMode.Ignore;
            cell.Add(icon);

            var qtyLabel = new Label();
            qtyLabel.name = "Quantity";
            qtyLabel.AddToClassList("inv-qty");
            qtyLabel.style.textShadow = new TextShadow
            {
                color = Color.black,
                offset = new Vector2(1, -1),
            };
            qtyLabel.pickingMode = PickingMode.Ignore;
            cell.Add(qtyLabel);

            cell.RegisterCallback<MouseEnterEvent>(_ => cell.AddToClassList("inv-cell--highlight"));
            cell.RegisterCallback<MouseLeaveEvent>(_ => cell.RemoveFromClassList("inv-cell--highlight"));

            cell.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _model!.SelectSlot(slotIndex);
                }
                else if (evt.button == 1)
                {
                    _dragAndContext.HideContextMenu(_doc.rootVisualElement);
                    _dragAndContext.ShowContextMenu(
                        evt.mousePosition,
                        slotIndex,
                        _doc.rootVisualElement,
                        _model!,
                        _loc!,
                        ShowItemInfo);
                    evt.StopPropagation();
                }
            });

            if (!_slotElements.ContainsKey(slotIndex))
            {
                _slotElements[slotIndex] = new List<VisualElement>();
            }

            _slotElements[slotIndex].Add(cell);

            RefreshSlot(slotIndex);
            return cell;
        }

        private void RefreshSlot(int slotIndex)
        {
            bool occupied = _model?.GetSlot(slotIndex) != null;
            if (occupied != _occupiedSlots.Contains(slotIndex))
            {
                // Предмет появился или кончился: состав сетки изменился, и
                // обновлением одной ячейки тут не обойтись.
                RebuildSlots();
                return;
            }

            if (!_slotElements.ContainsKey(slotIndex))
            {
                return;
            }

            var item = _model!.GetSlot(slotIndex);

            foreach (var cell in _slotElements[slotIndex])
            {
                var icon = cell.Q<VisualElement>("Icon");
                var qty = cell.Q<Label>("Quantity");

                if (item != null)
                {
                    icon.style.display = DisplayStyle.Flex;
                    if (item.Icon != null)
                    {
                        icon.style.backgroundImage = new StyleBackground(item.Icon);
                        icon.style.backgroundColor = Color.clear;
                    }
                    else
                    {
                        icon.style.backgroundImage = null;
                        icon.style.backgroundColor = item.IconColor;
                    }

                    qty.text = item.Quantity > 1 ? item.Quantity.ToString() : string.Empty;
                }
                else
                {
                    icon.style.display = DisplayStyle.None;
                    qty.text = string.Empty;
                }
            }
        }

        /// <summary>
        /// Пересобирает обе сетки по занятым слотам.
        /// </summary>
        /// <remarks>
        /// ПОЧЕМУ НЕ ФИКСИРОВАННОЕ ЧИСЛО ЯЧЕЕК. В старом клиенте сервер
        /// присылает ровно те предметы, что есть, и сетка строится по ним.
        /// Пустых клеток там не бывает вовсе. У нас же короткий список рисовал
        /// девять слотов, а полный — все шестьдесят три, и оба почти целиком
        /// состояли из пустоты: девять клеток при четырёх строках дают рваный
        /// столбец из одной, а шестьдесят три — стену на пол-экрана.
        ///
        /// Короткий список — не больше <see cref="SHORTLISTSIZE"/> предметов,
        /// то есть ровно один столбец, как на скриншоте свёрнутого состояния.
        /// Полный — все занятые слоты.
        ///
        /// Полоса переключения прячется, когда переключать не на что: в
        /// оригинале она появляется, только если предметов больше короткого
        /// списка.
        /// </remarks>
        private void RebuildSlots()
        {
            _occupiedSlots.Clear();
            for (int i = 0; i < InventoryModel.TOTALSLOTS; i++)
            {
                if (_model?.GetSlot(i) != null)
                {
                    _occupiedSlots.Add(i);
                }
            }

            FillSlots(_hotbarSlots, "Hotbar", Math.Min(_occupiedSlots.Count, SHORTLISTSIZE));
            FillSlots(_fullSlots, "Inv", _occupiedSlots.Count);

            if (_inventoryButton != null)
            {
                _inventoryButton.style.display = _occupiedSlots.Count > SHORTLISTSIZE
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            ApplyInventoryMode();
        }

        private void FillSlots(VisualElement? container, string prefix, int count)
        {
            if (container == null)
            {
                return;
            }

            // Ячейки пересоздаются целиком: список занятых слотов меняется, и
            // сохранять привязку старых элементов к новым номерам не к чему.
            foreach (List<VisualElement> elements in _slotElements.Values)
            {
                elements.RemoveAll(cell => container.Contains(cell));
            }

            container.Clear();

            // Четыре строки, столбцы прирастают влево — как FixedRowCount = 4 со
            // StartAxis = Vertical в старом клиенте. Столбец здесь настоящий
            // контейнер, а не результат переноса: перенос раскладывал клетки
            // лесенкой, потому что высота в точности равна четырём клеткам и на
            // границе он то влезал, то нет.
            VisualElement? column = null;
            for (int i = 0; i < count; i++)
            {
                if (i % ROWCOUNT == 0)
                {
                    column = new VisualElement();
                    column.AddToClassList("inv-slot-column");
                    container.Add(column);
                }

                int slotIndex = _occupiedSlots[i];
                column!.Add(CreateCell(slotIndex, $"{prefix}_{slotIndex}"));
            }
        }

        /// <summary>
        /// Переключает угловую панель между коротким и полным списком.
        /// </summary>
        /// <remarks>
        /// Так в старом клиенте: полный список показывался В ТОЙ ЖЕ угловой
        /// сетке, а не в отдельном окне посреди экрана, и полоса слева
        /// переключала одно на другое. Треугольник на ней разворачивался по оси
        /// X — здесь ту же роль играет стрелка.
        /// </remarks>
        private void ToggleFullInventory()
        {
            _isInventoryOpen = !_isInventoryOpen;
            ApplyInventoryMode();
        }

        private void ApplyInventoryMode()
        {
            if (_hotbarSlots != null)
            {
                _hotbarSlots.style.display =
                    _isInventoryOpen ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_fullSlots != null)
            {
                _fullSlots.style.display =
                    _isInventoryOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_toggleGlyph != null)
            {
                _toggleGlyph.text = _isInventoryOpen ? "\u25B6" : "\u25C0";
            }
        }

        // Клавиша делает ровно то же, что полоса: отдельного окна больше нет.
        private void ToggleInventory() => ToggleFullInventory();
        private void ShowItemInfo(ItemData item)
        {
            _tooltipName.text = _loc!.Get("inventory.tooltip_item", item.Name ?? item.ItemType.ToString(), item.ItemType, item.Quantity);
            _tooltipDesc.text = _loc!.Get("inventory.tooltip_type", item.ItemType) + "\n" + (item.Description ?? _loc.Get("inventory.no_description"));
            _tooltipWrapper.style.display = DisplayStyle.Flex;
        }
    }
}
