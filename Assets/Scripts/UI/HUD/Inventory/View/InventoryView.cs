#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
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
    public class InventoryView : MonoBehaviour
    {
        private const int HOTBAR_COLS = 9;
        private const int INVENTORY_COLS = 9;
        private const int CELLSIZE = 50;
        private const int CELL_GAP = 10;
        private const int ICON_SIZE = 36;

        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private IInventoryModel? _model;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker? _inputBlocker;
        private Dictionary<int, List<VisualElement>> _slotElements = new Dictionary<int, List<VisualElement>>();
        private VisualElement? _hotbarContainer;
        private Button? _inventoryButton;
        private VisualElement? _fullInventoryPanel;
        private bool _isInventoryOpen = false;

        // Drag-and-drop
        private VisualElement? _floatingItem;
        private int _dragFromSlot = -1;
        private ItemData? _draggedItem;

        // Context menu
        private VisualElement _contextMenu = null!;

        // Selection
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

        protected void OnDestroy()
        {
            if (_model != null)
            {
                _model.OnSlotChanged -= RefreshSlot;
                _model.OnSlotSelected -= OnModelSlotSelected;
            }

            // Снимаем drag-колбэки, чтобы не остались висячими при уничтожении
            // во время перетаскивания предмета.
            if (_doc != null && _doc.rootVisualElement != null)
            {
                _doc.rootVisualElement.UnregisterCallback<MouseMoveEvent>(OnDragMove);
                _doc.rootVisualElement.UnregisterCallback<MouseUpEvent>(OnDragDrop);
            }

            if (_floatingItem != null && _floatingItem.parent != null)
            {
                _floatingItem.RemoveFromHierarchy();
                _floatingItem = null;
            }
        }

        protected void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
                if (!_initialized)
                {
                    return;
                }
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.tabKey.wasPressedThisFrame ||
                (Keyboard.current.iKey.wasPressedThisFrame && !ChatInput.IsFocused))
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

            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            if (_model == null)
            {
                if (!Application.isPlaying)
                {
                    _model = new InventoryModel();
                }
                else
                {
                    return;
                }
            }

            _model.OnSlotChanged += RefreshSlot;
            _model.OnSlotSelected += OnModelSlotSelected;

            CreateTooltip(_doc.rootVisualElement);
            BuildUI();
            _initialized = true;
            Debug.Log("[InventoryView] Initialized successfully.");
        }

        private void OnModelSlotSelected(int slotIndex)
        {
            // Сбросить рамку у старого слота
            if (_lastSelectedSlot >= 0 && _slotElements.ContainsKey(_lastSelectedSlot))
            {
                foreach (var cell in _slotElements[_lastSelectedSlot])
                {
                    cell.RemoveFromClassList("inv-cell--selected");
                }
            }

            _lastSelectedSlot = slotIndex;

            // Поставить рамку новому слоту
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

            var uxml = Resources.Load<VisualTreeAsset>("UI/Inventory");
            if (uxml != null)
            {
                var tree = uxml.CloneTree();
                root.Add(tree);

                _hotbarContainer = tree.Q<VisualElement>("HotbarContainer");
                var hotbarSlots = tree.Q<VisualElement>("HotbarSlots") ?? _hotbarContainer;
                for (int i = 0; i < HOTBAR_COLS; i++)
                {
                    var cell = CreateCell(i, $"Hotbar_{i}");
                    hotbarSlots.Add(cell);
                }

                _inventoryButton = tree.Q<Button>("InventoryToggleBtn");
                if (_inventoryButton != null)
                {
                    _inventoryButton.clicked += ToggleInventory;
                }

                _fullInventoryPanel = tree.Q<VisualElement>("FullInventoryPanel");
                var closeBtn = tree.Q<Button>("CloseInventoryBtn");
                if (closeBtn != null)
                {
                    closeBtn.clicked += ToggleInventory;
                }

                var inventoryGrid = tree.Q<VisualElement>("InventoryGrid");
                if (inventoryGrid != null)
                {
                    var grid = CreateGrid(0, InventoryModel.TOTALSLOTS - 1, "Inv");
                    inventoryGrid.Add(grid);
                }
            }
            else
            {
                throw new InvalidOperationException("[InventoryView] Failed to load UI/Inventory.uxml");
            }
        }

        private VisualElement CreateGrid(int fromSlot, int toSlot, string prefix)
        {
            var grid = new VisualElement();
            grid.name = $"{prefix}_Grid";
            grid.AddToClassList("inv-grid");

            int slotIndex = fromSlot;
            int cols = (toSlot - fromSlot + 1 > 9) ? INVENTORY_COLS : (toSlot - fromSlot + 1);
            int rows = (toSlot - fromSlot + 1 + cols - 1) / cols;

            for (int row = 0; row < rows; row++)
            {
                var rowContainer = new VisualElement();
                rowContainer.AddToClassList("inv-grid-row");

                for (int col = 0; col < cols && slotIndex <= toSlot; col++, slotIndex++)
                {
                    rowContainer.Add(CreateCell(slotIndex, $"{prefix}_{slotIndex}"));
                }

                grid.Add(rowContainer);
            }

            return grid;
        }

        private VisualElement CreateCell(int slotIndex, string name)
        {
            var cell = new VisualElement();
            cell.name = name;
            cell.userData = slotIndex;
            cell.AddToClassList("inv-cell");
            cell.style.width = CELLSIZE;
            cell.style.height = CELLSIZE;
            cell.style.minWidth = CELLSIZE;
            cell.style.minHeight = CELLSIZE;
            cell.style.flexShrink = 0;
            cell.style.flexGrow = 0;
            cell.style.marginRight = 3;
            cell.style.marginLeft = 3;
            cell.style.marginTop = 3;
            cell.style.marginBottom = 3;
            cell.style.backgroundColor = new Color(0.08f, 0.1f, 0.15f, 0.85f);
            cell.style.borderTopWidth = 1;
            cell.style.borderBottomWidth = 1;
            cell.style.borderLeftWidth = 1;
            cell.style.borderRightWidth = 1;
            cell.style.borderTopColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderBottomColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderLeftColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderRightColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderTopLeftRadius = 4;
            cell.style.borderTopRightRadius = 4;
            cell.style.borderBottomLeftRadius = 4;
            cell.style.borderBottomRightRadius = 4;
            cell.style.justifyContent = Justify.Center;
            cell.style.alignItems = Align.Center;

            // Иконка-кружок
            var icon = new VisualElement();
            icon.name = "Icon";
            icon.AddToClassList("inv-icon");
            icon.style.display = DisplayStyle.None;
            icon.pickingMode = PickingMode.Ignore;
            cell.Add(icon);

            // Количество
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

            // Hover
            cell.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (_dragFromSlot < 0)
                {
                    cell.AddToClassList("inv-cell--highlight");
                }
            });
            cell.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (_dragFromSlot < 0)
                {
                    cell.RemoveFromClassList("inv-cell--highlight");
                }
            });

            // Выбор по клику
            cell.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _model!.SelectSlot(slotIndex);
                    var item = _model!.GetSlot(slotIndex);

                    if (item == null)
                    {
                        return;
                    }

                    _dragFromSlot = slotIndex;
                    _draggedItem = item;
                    cell.RemoveFromClassList("inv-cell--highlight");

                    HideContextMenu();

                    _floatingItem = new VisualElement();
                    _floatingItem.AddToClassList("inv-floating");
                    if (item.Icon != null)
                    {
                        _floatingItem.style.backgroundImage = new StyleBackground(item.Icon);
                        _floatingItem.style.backgroundColor = Color.clear;
                    }
                    else
                    {
                        _floatingItem.style.backgroundColor = Color.gray;
                    }

                    _floatingItem.pickingMode = PickingMode.Ignore;

                    var root = _doc.rootVisualElement;
                    root.Add(_floatingItem);
                    UpdateFloatingPosition(evt.mousePosition);

                    root.RegisterCallback<MouseMoveEvent>(OnDragMove);
                    root.RegisterCallback<MouseUpEvent>(OnDragDrop);
                    evt.StopPropagation();
                }
                else if (evt.button == 1)
                {
                    HideContextMenu();
                    ShowContextMenu(evt.mousePosition, slotIndex);
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

        private void OnDragMove(MouseMoveEvent evt)
        {
            if (_floatingItem != null)
            {
                UpdateFloatingPosition(evt.mousePosition);
            }
        }

        private void OnDragDrop(MouseUpEvent evt)
        {
            if (_dragFromSlot < 0 || _floatingItem == null)
            {
                return;
            }

            var root = _doc.rootVisualElement;
            root.UnregisterCallback<MouseMoveEvent>(OnDragMove);
            root.UnregisterCallback<MouseUpEvent>(OnDragDrop);

            var target = FindSlotUnderMouse(evt.mousePosition);
            if (target >= 0 && target != _dragFromSlot)
            {
                if (InventoryModel.CanStack(_draggedItem!, _model!.GetSlot(target)))
                {
                    _model.TryStackSlots(_dragFromSlot, target);
                }
                else
                {
                    _model.SwapSlots(_dragFromSlot, target);
                }
            }

            root.Remove(_floatingItem);
            _floatingItem = null;
            _dragFromSlot = -1;
            _draggedItem = null;
        }

        private int FindSlotUnderMouse(Vector2 mousePos)
        {
            foreach (var kvp in _slotElements)
            {
                foreach (var cell in kvp.Value)
                {
                    if (cell.worldBound.Contains(mousePos))
                    {
                        return kvp.Key;
                    }
                }
            }

            return -1;
        }

        private void UpdateFloatingPosition(Vector2 mousePos)
        {
            _floatingItem!.style.left = mousePos.x - (ICON_SIZE / 2);
            _floatingItem!.style.top = mousePos.y - (ICON_SIZE / 2);
        }

        private void RefreshSlot(int slotIndex)
        {
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

        private Button CreateInventoryButton()
        {
            var btn = new Button();
            btn.name = "InventoryButton";
            btn.AddToClassList("inv-button");
            btn.tooltip = "Открыть инвентарь (Tab)";

            var label = new Label("☰");
            label.AddToClassList("inv-button-label");
            label.style.fontSize = 24;
            label.style.color = Color.white;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.pickingMode = PickingMode.Ignore;
            btn.Add(label);

            btn.clicked += ToggleInventory;
            return btn;
        }

        private void ToggleInventory()
        {
            _isInventoryOpen = !_isInventoryOpen;
            if (_fullInventoryPanel != null)
            {
                _fullInventoryPanel.style.display = _isInventoryOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public IInventoryModel? GetModel() => _model;

        private void ShowContextMenu(Vector2 mousePos, int slotIndex)
        {
            var item = _model!.GetSlot(slotIndex);
            if (item == null)
            {
                return;
            }

            _contextMenu = new VisualElement();
            _contextMenu.name = "ContextMenu";
            _contextMenu.AddToClassList("inv-context-menu");
            _contextMenu.style.left = mousePos.x;
            _contextMenu.style.top = mousePos.y;
            _contextMenu.pickingMode = PickingMode.Position;

            AddContextMenuItem("Использовать", () =>
            {
                _model.SelectSlot(slotIndex);
                _model!.UseSelectedItem();
                HideContextMenu();
            });

            AddContextMenuItem("Информация", () =>
            {
                ShowItemInfo(item);
                HideContextMenu();
            });

            _doc.rootVisualElement.Add(_contextMenu);

            _doc.rootVisualElement.RegisterCallback<MouseDownEvent>(OnContextMenuOutsideClick, TrickleDown.TrickleDown);
            _doc.rootVisualElement.RegisterCallback<KeyDownEvent>(OnContextMenuEscape, TrickleDown.TrickleDown);
        }

        private void AddContextMenuItem(string labelText, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = labelText;
            btn.AddToClassList("inv-context-btn");

            _contextMenu.Add(btn);
        }

        private void HideContextMenu()
        {
            if (_contextMenu != null)
            {
                _contextMenu.RemoveFromHierarchy();
                _contextMenu = null!;
            }

            _doc?.rootVisualElement.UnregisterCallback<MouseDownEvent>(OnContextMenuOutsideClick, TrickleDown.TrickleDown);
            _doc?.rootVisualElement.UnregisterCallback<KeyDownEvent>(OnContextMenuEscape, TrickleDown.TrickleDown);
        }

        private void OnContextMenuOutsideClick(MouseDownEvent evt)
        {
            if (_contextMenu != null && !_contextMenu.worldBound.Contains(evt.mousePosition))
            {
                HideContextMenu();
            }
        }

        private void OnContextMenuEscape(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                HideContextMenu();
            }
        }

        private void ShowItemInfo(ItemData item)
        {
            _tooltipName.text = $"Предмет: {item.Name ?? item.ItemType.ToString()} ({item.ItemType}) x{item.Quantity}";
            _tooltipDesc.text = $"Тип: {item.ItemType}\n{item.Description ?? "Нет описания"}";
            _tooltipWrapper.style.display = DisplayStyle.Flex;
        }
    }
}
