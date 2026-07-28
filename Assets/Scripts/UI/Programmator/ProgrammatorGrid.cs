using System;
using System.Collections.Generic;
using System.IO;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Programmator
{
    public class ProgrammatorGrid : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _popup;
        private VisualElement _gridContainer;
        private VisualElement[,] _cells;
        private Label[,] _cellLabels;
        private RadialMenu _radial;
        private ObserverJoystick _joystick; 
        private bool _isOpen;
        private bool _isRunning;
        private bool _radialShown;
        private int _radialCellIndex = -1;
        private Tooltip _tooltip;
        private Label _pageLabel;
        private IntegerField _pageInput;
        private Button _prevBtn;
        private Button _nextBtn;
        private Button _saveBtn;
        private Button _runBtn;
        private Button _stopBtn;
        private VisualElement _panel;
        private bool _hasSelection;
        private int _selStartRow, _selStartCol;
        private int _selEndRow, _selEndCol;
        private readonly HashSet<long> _selectedCells = new HashSet<long>();
        private int[] _clipboardCodes;
        private string[] _clipboardLabels;
        private string[] _clipboardValues;
        private int _clipboardWidth;
        private int _clipboardHeight;
        private bool _hasClipboard;
        private const float CELLSIZE = 32f;
        private const float CELL_GAP = 2f;

        private class ProgramItem
        {
            public string Name;
            public List<int> Codes = new();
            public List<string> Labels = new();
            public List<string> Values = new();
        }

        private readonly List<ProgramItem> _programItems = new();
        private int _activeIndex = -1;
        private VisualElement _programListPanel;
        private ScrollView _listScroll;
        private Label _programTitle;
        private VisualElement _createContainer;
        private Button _createBtn;
        private TextField _createInput;
        private VisualElement _createDialog;

        public static bool IsOpen { get; private set; }

        protected void Start()
        {
            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                return;
            }

            CreateUI();
            _popup.style.display = DisplayStyle.None;

            _tooltip = new Tooltip();
            _tooltip.Initialize(_doc);
        }

        private void CreateUI()
        {
            _popup = new VisualElement();
            _popup.style.position = Position.Absolute;
            _popup.style.left = 0;
            _popup.style.top = 0;
            _popup.style.right = 0;
            _popup.style.bottom = 0;
            _popup.style.justifyContent = Justify.Center;
            _popup.style.alignItems = Align.Center;

            var dimmer = new VisualElement();
            dimmer.style.position = Position.Absolute;
            dimmer.style.left = 0;
            dimmer.style.top = 0;
            dimmer.style.right = 0;
            dimmer.style.bottom = 0;
            dimmer.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            dimmer.pickingMode = PickingMode.Ignore;
            _popup.Add(dimmer);

            _panel = new VisualElement();
            var panel = _panel;
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.paddingLeft = 10;
            panel.style.paddingRight = 10;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.width = 618;
            panel.style.minHeight = 520;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Column;
            topRow.style.marginBottom = 10;

            var buttonsRow = new VisualElement();
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.alignItems = Align.Center;
            topRow.Add(buttonsRow);

            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.alignItems = Align.Center;
            actionRow.style.marginTop = 4;
            topRow.Add(actionRow);

            _programTitle = new Label("Программатор");
            _programTitle.style.fontSize = 18;
            _programTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _programTitle.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            buttonsRow.Add(_programTitle);

            _prevBtn = new Button(PrevPage);
            _prevBtn.text = "<";
            _prevBtn.style.width = 22;
            _prevBtn.style.height = 22;
            _prevBtn.style.backgroundColor = Color.clear;
            _prevBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            _prevBtn.style.fontSize = 14;
            _prevBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            _prevBtn.style.borderTopWidth = 0;
            _prevBtn.style.borderBottomWidth = 0;
            _prevBtn.style.borderLeftWidth = 0;
            _prevBtn.style.borderRightWidth = 0;
            _prevBtn.style.paddingTop = 0;
            _prevBtn.style.paddingBottom = 0;
            _prevBtn.style.paddingLeft = 2;
            _prevBtn.style.paddingRight = 2;
            buttonsRow.Add(_prevBtn);

            _pageLabel = new Label("Стр. 1/1");
            _pageLabel.style.fontSize = 12;
            _pageLabel.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            _pageLabel.style.minWidth = 60;
            _pageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _pageLabel.style.marginLeft = 4;
            _pageLabel.style.marginRight = 4;
            buttonsRow.Add(_pageLabel);

            _nextBtn = new Button(NextPage);
            _nextBtn.text = ">";
            _nextBtn.style.width = 22;
            _nextBtn.style.height = 22;
            _nextBtn.style.backgroundColor = Color.clear;
            _nextBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            _nextBtn.style.fontSize = 14;
            _nextBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            _nextBtn.style.borderTopWidth = 0;
            _nextBtn.style.borderBottomWidth = 0;
            _nextBtn.style.borderLeftWidth = 0;
            _nextBtn.style.borderRightWidth = 0;
            _nextBtn.style.paddingTop = 0;
            _nextBtn.style.paddingBottom = 0;
            _nextBtn.style.paddingLeft = 2;
            _nextBtn.style.paddingRight = 2;
            buttonsRow.Add(_nextBtn);

            _pageInput = new IntegerField();
            _pageInput.value = ProgrammatorData.CurrentPage + 1;
            _pageInput.style.width = 52;
            _pageInput.style.height = 22;
            _pageInput.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
            _pageInput.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            _pageInput.style.fontSize = 12;
            _pageInput.style.unityTextAlign = TextAnchor.MiddleCenter;
            _pageInput.style.borderTopWidth = 0;
            _pageInput.style.borderBottomWidth = 0;
            _pageInput.style.borderLeftWidth = 0;
            _pageInput.style.borderRightWidth = 0;
            _pageInput.style.marginLeft = 4;
            _pageInput.style.marginRight = 4;
            _pageInput.style.paddingTop = 0;
            _pageInput.style.paddingBottom = 0;
            _pageInput.style.paddingLeft = 0;
            _pageInput.style.paddingRight = 0;
            _pageInput.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var ti = _pageInput.Q("unity-text-input");
                if (ti != null)
                {
                    ti.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
                    ti.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
                    ti.style.fontSize = 12;
                    ti.style.unityTextAlign = TextAnchor.MiddleCenter;
                    ti.style.borderTopWidth = 0;
                    ti.style.borderBottomWidth = 0;
                    ti.style.borderLeftWidth = 0;
                    ti.style.borderRightWidth = 0;
                    ti.style.paddingTop = 0;
                    ti.style.paddingBottom = 0;
                    ti.style.paddingLeft = 0;
                    ti.style.paddingRight = 0;
                    ti.style.marginTop = 0;
                    ti.style.marginBottom = 0;
                    ti.style.marginLeft = 0;
                    ti.style.marginRight = 0;
                }
            });
            _pageInput.RegisterValueChangedCallback(evt =>
            {
                int page = evt.newValue - 1;
                if (page >= 0 && page < ProgrammatorData.PageCount && page != ProgrammatorData.CurrentPage)
                {
                    ClearSelection();
                    _radial.Hide();
                    _joystick.Hide();
                    _radialShown = false;
                    ProgrammatorData.CurrentPage = page;
                    RefreshAllCells();
                }
                else
                {
                    _pageInput.SetValueWithoutNotify(ProgrammatorData.CurrentPage + 1);
                }
            });
            buttonsRow.Add(_pageInput);

            var addPageBtn = new Button(AddPageClick);
            addPageBtn.text = "+";
            addPageBtn.style.width = 22;
            addPageBtn.style.height = 22;
            addPageBtn.style.backgroundColor = Color.clear;
            addPageBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            addPageBtn.style.fontSize = 14;
            addPageBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            addPageBtn.style.borderTopWidth = 0;
            addPageBtn.style.borderBottomWidth = 0;
            addPageBtn.style.borderLeftWidth = 0;
            addPageBtn.style.borderRightWidth = 0;
            addPageBtn.style.paddingTop = 0;
            addPageBtn.style.paddingBottom = 0;
            addPageBtn.style.paddingLeft = 2;
            addPageBtn.style.paddingRight = 2;
            buttonsRow.Add(addPageBtn);

            var removePageBtn = new Button(RemovePageClick);
            removePageBtn.text = "−";
            removePageBtn.style.width = 22;
            removePageBtn.style.height = 22;
            removePageBtn.style.backgroundColor = Color.clear;
            removePageBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            removePageBtn.style.fontSize = 14;
            removePageBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            removePageBtn.style.borderTopWidth = 0;
            removePageBtn.style.borderBottomWidth = 0;
            removePageBtn.style.borderLeftWidth = 0;
            removePageBtn.style.borderRightWidth = 0;
            removePageBtn.style.paddingTop = 0;
            removePageBtn.style.paddingBottom = 0;
            removePageBtn.style.paddingLeft = 2;
            removePageBtn.style.paddingRight = 2;
            removePageBtn.style.marginRight = 8;
            buttonsRow.Add(removePageBtn);

            var shiftUpBtn = new Button(() => ShiftSelection(0, -1));
            shiftUpBtn.text = "↑";
            shiftUpBtn.style.width = 22;
            shiftUpBtn.style.height = 22;
            shiftUpBtn.style.backgroundColor = Color.clear;
            shiftUpBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            shiftUpBtn.style.fontSize = 14;
            shiftUpBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            shiftUpBtn.style.borderTopWidth = 0;
            shiftUpBtn.style.borderBottomWidth = 0;
            shiftUpBtn.style.borderLeftWidth = 0;
            shiftUpBtn.style.borderRightWidth = 0;
            shiftUpBtn.style.paddingTop = 0;
            shiftUpBtn.style.paddingBottom = 0;
            shiftUpBtn.style.paddingLeft = 2;
            shiftUpBtn.style.paddingRight = 2;
            buttonsRow.Add(shiftUpBtn);

            var shiftDownBtn = new Button(() => ShiftSelection(0, 1));
            shiftDownBtn.text = "↓";
            shiftDownBtn.style.width = 22;
            shiftDownBtn.style.height = 22;
            shiftDownBtn.style.backgroundColor = Color.clear;
            shiftDownBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            shiftDownBtn.style.fontSize = 14;
            shiftDownBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            shiftDownBtn.style.borderTopWidth = 0;
            shiftDownBtn.style.borderBottomWidth = 0;
            shiftDownBtn.style.borderLeftWidth = 0;
            shiftDownBtn.style.borderRightWidth = 0;
            shiftDownBtn.style.paddingTop = 0;
            shiftDownBtn.style.paddingBottom = 0;
            shiftDownBtn.style.paddingLeft = 2;
            shiftDownBtn.style.paddingRight = 2;
            buttonsRow.Add(shiftDownBtn);

            var shiftLeftBtn = new Button(() => ShiftSelection(-1, 0));
            shiftLeftBtn.text = "←";
            shiftLeftBtn.style.width = 22;
            shiftLeftBtn.style.height = 22;
            shiftLeftBtn.style.backgroundColor = Color.clear;
            shiftLeftBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            shiftLeftBtn.style.fontSize = 14;
            shiftLeftBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            shiftLeftBtn.style.borderTopWidth = 0;
            shiftLeftBtn.style.borderBottomWidth = 0;
            shiftLeftBtn.style.borderLeftWidth = 0;
            shiftLeftBtn.style.borderRightWidth = 0;
            shiftLeftBtn.style.paddingTop = 0;
            shiftLeftBtn.style.paddingBottom = 0;
            shiftLeftBtn.style.paddingLeft = 2;
            shiftLeftBtn.style.paddingRight = 2;
            buttonsRow.Add(shiftLeftBtn);

            var shiftRightBtn = new Button(() => ShiftSelection(1, 0));
            shiftRightBtn.text = "→";
            shiftRightBtn.style.width = 22;
            shiftRightBtn.style.height = 22;
            shiftRightBtn.style.backgroundColor = Color.clear;
            shiftRightBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            shiftRightBtn.style.fontSize = 14;
            shiftRightBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            shiftRightBtn.style.borderTopWidth = 0;
            shiftRightBtn.style.borderBottomWidth = 0;
            shiftRightBtn.style.borderLeftWidth = 0;
            shiftRightBtn.style.borderRightWidth = 0;
            shiftRightBtn.style.paddingTop = 0;
            shiftRightBtn.style.paddingBottom = 0;
            shiftRightBtn.style.paddingLeft = 2;
            shiftRightBtn.style.paddingRight = 2;
            shiftRightBtn.style.marginRight = 8;
            buttonsRow.Add(shiftRightBtn);

            _saveBtn = new Button(SaveProgram);
            _saveBtn.text = "💾";
            _saveBtn.style.width = 24;
            _saveBtn.style.height = 24;
            _saveBtn.style.backgroundColor = Color.clear;
            _saveBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            _saveBtn.style.fontSize = 14;
            _saveBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            _saveBtn.style.borderTopWidth = 0;
            _saveBtn.style.borderBottomWidth = 0;
            _saveBtn.style.borderLeftWidth = 0;
            _saveBtn.style.borderRightWidth = 0;
            _saveBtn.style.paddingTop = 0;
            _saveBtn.style.paddingBottom = 0;
            _saveBtn.style.paddingLeft = 0;
            _saveBtn.style.paddingRight = 0;
            actionRow.Add(_saveBtn);

            _runBtn = new Button(RunProgram);
            _runBtn.text = "▶";
            _runBtn.style.width = 22;
            _runBtn.style.height = 22;
            _runBtn.style.backgroundColor = new Color(0f, 0.35f, 0f, 0.3f);
            _runBtn.style.color = new Color(0.4f, 0.9f, 0.4f, 1f);
            _runBtn.style.fontSize = 12;
            _runBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            _runBtn.style.borderTopWidth = 0;
            _runBtn.style.borderBottomWidth = 0;
            _runBtn.style.borderLeftWidth = 0;
            _runBtn.style.borderRightWidth = 0;
            _runBtn.style.paddingTop = 0;
            _runBtn.style.paddingBottom = 0;
            _runBtn.style.paddingLeft = 0;
            _runBtn.style.paddingRight = 0;
            _runBtn.style.marginLeft = 4;
            actionRow.Add(_runBtn);

            _stopBtn = new Button(StopProgram);
            _stopBtn.text = "■";
            _stopBtn.style.width = 22;
            _stopBtn.style.height = 22;
            _stopBtn.style.backgroundColor = new Color(0.35f, 0f, 0f, 0.3f);
            _stopBtn.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
            _stopBtn.style.fontSize = 12;
            _stopBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            _stopBtn.style.borderTopWidth = 0;
            _stopBtn.style.borderBottomWidth = 0;
            _stopBtn.style.borderLeftWidth = 0;
            _stopBtn.style.borderRightWidth = 0;
            _stopBtn.style.paddingTop = 0;
            _stopBtn.style.paddingBottom = 0;
            _stopBtn.style.paddingLeft = 0;
            _stopBtn.style.paddingRight = 0;
            _stopBtn.SetEnabled(false);
            actionRow.Add(_stopBtn);

            var closeBtn = new Button(CloseProgram);
            closeBtn.text = "×";
            closeBtn.style.width = 24;
            closeBtn.style.height = 24;
            closeBtn.style.backgroundColor = Color.clear;
            closeBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            closeBtn.style.fontSize = 18;
            closeBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.paddingTop = 0;
            closeBtn.style.paddingBottom = 0;
            closeBtn.style.paddingLeft = 0;
            closeBtn.style.paddingRight = 0;
            closeBtn.RegisterCallback<MouseEnterEvent>(_ =>
                closeBtn.style.color = Color.white);
            closeBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                closeBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f));
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.FlexStart;
            topRow.style.flexGrow = 1;
            headerRow.Add(topRow);
            headerRow.Add(closeBtn);
            panel.Add(headerRow);

            var gridScroll = new VisualElement();
            gridScroll.style.maxHeight = ProgrammatorData.ROWS * (CELLSIZE + (CELL_GAP * 2) + 2f);

            _gridContainer = new VisualElement();
            _gridContainer.style.flexDirection = FlexDirection.Row;
            _gridContainer.style.flexWrap = Wrap.Wrap;
            _gridContainer.style.width = ProgrammatorData.COLS * (CELLSIZE + (CELL_GAP * 2) + 2f);

            _cells = new VisualElement[ProgrammatorData.ROWS, ProgrammatorData.COLS];
            _cellLabels = new Label[ProgrammatorData.ROWS, ProgrammatorData.COLS];

            for (int i = 0; i < ProgrammatorData.ROWS; i++)
            {
                for (int j = 0; j < ProgrammatorData.COLS; j++)
                {
                    int row = i, col = j;
                    var cell = new VisualElement();
                    cell.style.width = CELLSIZE;
                    cell.style.height = CELLSIZE;
                    cell.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
                    cell.style.borderTopWidth = 1;
                    cell.style.borderBottomWidth = 1;
                    cell.style.borderLeftWidth = 1;
                    cell.style.borderRightWidth = 1;
                    cell.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.marginLeft = CELL_GAP;
                    cell.style.marginRight = CELL_GAP;
                    cell.style.marginTop = CELL_GAP;
                    cell.style.marginBottom = CELL_GAP;

                    cell.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        ProgrammatorData.HoveredCell = (row * ProgrammatorData.COLS) + col;
                        if (!IsSelected(row, col))
                            HighlightCell(row, col, true);
                        ShowCellTooltip(row, col);
                    });
                    cell.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        if (ProgrammatorData.HoveredCell == (row * ProgrammatorData.COLS) + col)
                        {
                            if (!IsSelected(row, col))
                                HighlightCell(row, col, false);
                            ProgrammatorData.HoveredCell = -1;
                        }

                        _tooltip?.Hide();
                    });

                    cell.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        _tooltip?.UpdatePosition(evt.position);
                    });

                    // LMB — selection
                    cell.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;

                        if (_radialShown)
                        {
                            _joystick.Hide();
                            _radial.Hide();
                            _radialShown = false;
                            _radialCellIndex = -1;
                            return;
                        }

                        if (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed)
                        {
                            ToggleCellSelection(row, col);
                        }
                        else if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed)
                        {
                            ExtendSelection(row, col);
                        }
                        else
                        {
                            SelectCell(row, col);
                        }
                    });

                    // RMB — radial menu
                    cell.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 1) return;

                        if (_radialShown)
                        {
                            _joystick.Hide();
                            _radial.Hide();
                            _radialShown = false;
                            _radialCellIndex = -1;
                            return;
                        }

                        _radialCellIndex = (row * ProgrammatorData.COLS) + col;
                        ShowCategoryRing();
                        var cellCenter = _cells[row, col].worldBound.center;
                        _radial.ShowAt(_doc.rootVisualElement, cellCenter);
                        _radialShown = true;
                    });

                    var label = new Label();
                    label.style.fontSize = 8;
                    label.style.color = Color.white;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    label.style.position = Position.Absolute;
                    label.style.left = 0;
                    label.style.right = 0;
                    label.style.top = 0;
                    label.style.bottom = 0;
                    label.style.paddingTop = 0;
                    label.style.paddingBottom = 0;
                    label.pickingMode = PickingMode.Ignore;
                    cell.Add(label);

                    _cells[row, col] = cell;
                    _cellLabels[row, col] = label;
                    _gridContainer.Add(cell);
                }
            }

            gridScroll.Add(_gridContainer);

            var gridRow = new VisualElement();
            gridRow.style.flexDirection = FlexDirection.Row;
            gridRow.style.justifyContent = Justify.Center;
            gridRow.Add(gridScroll);

            panel.Add(gridRow);

            _popup.Add(panel);

            _programListPanel = new VisualElement();
            _programListPanel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            _programListPanel.style.borderTopWidth = 2;
            _programListPanel.style.borderBottomWidth = 2;
            _programListPanel.style.borderLeftWidth = 2;
            _programListPanel.style.borderRightWidth = 2;
            _programListPanel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _programListPanel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _programListPanel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _programListPanel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _programListPanel.style.paddingTop = 10;
            _programListPanel.style.paddingBottom = 10;
            _programListPanel.style.paddingLeft = 20;
            _programListPanel.style.paddingRight = 20;
            _programListPanel.style.flexDirection = FlexDirection.Column;
            _programListPanel.style.width = 400;
            _programListPanel.style.minHeight = 300;
            _programListPanel.style.display = DisplayStyle.None;

            var listHeaderRow = new VisualElement();
            listHeaderRow.style.flexDirection = FlexDirection.Row;
            listHeaderRow.style.marginBottom = 10;

            var listTitle = new Label("Программы");
            listTitle.style.fontSize = 18;
            listTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            listTitle.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            listTitle.style.flexGrow = 1;
            listHeaderRow.Add(listTitle);

            var listCloseBtn = new Button(() => Hide());
            listCloseBtn.text = "\u00d7";
            listCloseBtn.style.width = 24;
            listCloseBtn.style.height = 24;
            listCloseBtn.style.backgroundColor = Color.clear;
            listCloseBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            listCloseBtn.style.fontSize = 18;
            listCloseBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            listCloseBtn.style.borderTopWidth = 0;
            listCloseBtn.style.borderBottomWidth = 0;
            listCloseBtn.style.borderLeftWidth = 0;
            listCloseBtn.style.borderRightWidth = 0;
            listCloseBtn.style.paddingTop = 0;
            listCloseBtn.style.paddingBottom = 0;
            listCloseBtn.style.paddingLeft = 0;
            listCloseBtn.style.paddingRight = 0;
            listCloseBtn.RegisterCallback<MouseEnterEvent>(_ =>
                listCloseBtn.style.color = Color.white);
            listCloseBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                listCloseBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f));
            listHeaderRow.Add(listCloseBtn);

            _programListPanel.Add(listHeaderRow);

            _listScroll = new ScrollView();
            _listScroll.style.flexGrow = 1;
            _listScroll.style.minHeight = 200;
            _programListPanel.Add(_listScroll);

            _createContainer = new VisualElement();
            _createContainer.style.flexDirection = FlexDirection.Column;
            _createContainer.style.marginTop = 10;

            _createBtn = new Button(ShowCreateInput);
            _createBtn.text = "+ Создать программу";
            _createBtn.style.width = Length.Percent(100);
            _createBtn.style.height = 30;
            _createBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            _createBtn.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            _createBtn.style.fontSize = 14;
            _createBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            _createBtn.style.borderTopWidth = 1;
            _createBtn.style.borderBottomWidth = 1;
            _createBtn.style.borderLeftWidth = 1;
            _createBtn.style.borderRightWidth = 1;
            _createBtn.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _createBtn.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _createBtn.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _createBtn.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _createBtn.RegisterCallback<MouseEnterEvent>(_ =>
                _createBtn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f));
            _createBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                _createBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));
            _createContainer.Add(_createBtn);

            _programListPanel.Add(_createContainer);

            _popup.Add(_programListPanel);

            _createDialog = new VisualElement();
            _createDialog.style.position = Position.Absolute;
            _createDialog.style.left = 0;
            _createDialog.style.top = 0;
            _createDialog.style.right = 0;
            _createDialog.style.bottom = 0;
            _createDialog.style.justifyContent = Justify.Center;
            _createDialog.style.alignItems = Align.Center;
            _createDialog.style.display = DisplayStyle.None;

            var dialogPanel = new VisualElement();
            dialogPanel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            dialogPanel.style.borderTopWidth = 2;
            dialogPanel.style.borderBottomWidth = 2;
            dialogPanel.style.borderLeftWidth = 2;
            dialogPanel.style.borderRightWidth = 2;
            dialogPanel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            dialogPanel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            dialogPanel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            dialogPanel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            dialogPanel.style.paddingTop = 16;
            dialogPanel.style.paddingBottom = 16;
            dialogPanel.style.paddingLeft = 20;
            dialogPanel.style.paddingRight = 20;
            dialogPanel.style.flexDirection = FlexDirection.Column;
            dialogPanel.style.alignItems = Align.Stretch;
            dialogPanel.style.width = 350;

            var dialogTitle = new Label("Новая программа");
            dialogTitle.style.fontSize = 16;
            dialogTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            dialogTitle.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            dialogTitle.style.marginBottom = 12;
            dialogPanel.Add(dialogTitle);

            _createInput = new TextField();
            _createInput.value = $"Программа {_programItems.Count + 1}";
            _createInput.style.height = 32;
            _createInput.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
            _createInput.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            _createInput.style.fontSize = 14;
            _createInput.style.borderTopWidth = 1;
            _createInput.style.borderBottomWidth = 1;
            _createInput.style.borderLeftWidth = 1;
            _createInput.style.borderRightWidth = 1;
            _createInput.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _createInput.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _createInput.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _createInput.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _createInput.style.paddingLeft = 8;
            _createInput.style.marginBottom = 16;
            _createInput.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var ti = _createInput.Q("unity-text-input");
                if (ti != null)
                {
                    ti.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
                    ti.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
                    ti.style.fontSize = 14;
                    ti.style.borderTopWidth = 0;
                    ti.style.borderBottomWidth = 0;
                    ti.style.borderLeftWidth = 0;
                    ti.style.borderRightWidth = 0;
                }
            });
            dialogPanel.Add(_createInput);

            _createInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    CreateNewProgram(_createInput.value);
                }
            });

            var dialogButtons = new VisualElement();
            dialogButtons.style.flexDirection = FlexDirection.Row;
            dialogButtons.style.justifyContent = Justify.FlexEnd;

            var dialogCancelBtn = new Button(HideCreateInput);
            dialogCancelBtn.text = "Отмена";
            dialogCancelBtn.style.height = 30;
            dialogCancelBtn.style.minWidth = 80;
            dialogCancelBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            dialogCancelBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            dialogCancelBtn.style.fontSize = 14;
            dialogCancelBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            dialogCancelBtn.style.borderTopWidth = 1;
            dialogCancelBtn.style.borderBottomWidth = 1;
            dialogCancelBtn.style.borderLeftWidth = 1;
            dialogCancelBtn.style.borderRightWidth = 1;
            dialogCancelBtn.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            dialogCancelBtn.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            dialogCancelBtn.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            dialogCancelBtn.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            dialogCancelBtn.style.marginRight = 8;
            dialogButtons.Add(dialogCancelBtn);

            var dialogConfirmBtn = new Button(() => CreateNewProgram(_createInput.value));
            dialogConfirmBtn.text = "Создать";
            dialogConfirmBtn.style.height = 30;
            dialogConfirmBtn.style.minWidth = 80;
            dialogConfirmBtn.style.backgroundColor = new Color(0f, 0.3f, 0f, 0.3f);
            dialogConfirmBtn.style.color = new Color(0.4f, 0.9f, 0.4f, 1f);
            dialogConfirmBtn.style.fontSize = 14;
            dialogConfirmBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            dialogConfirmBtn.style.borderTopWidth = 1;
            dialogConfirmBtn.style.borderBottomWidth = 1;
            dialogConfirmBtn.style.borderLeftWidth = 1;
            dialogConfirmBtn.style.borderRightWidth = 1;
            dialogConfirmBtn.style.borderTopColor = new Color(0.2f, 0.5f, 0.2f, 1f);
            dialogConfirmBtn.style.borderBottomColor = new Color(0.2f, 0.5f, 0.2f, 1f);
            dialogConfirmBtn.style.borderLeftColor = new Color(0.2f, 0.5f, 0.2f, 1f);
            dialogConfirmBtn.style.borderRightColor = new Color(0.2f, 0.5f, 0.2f, 1f);
            dialogButtons.Add(dialogConfirmBtn);

            dialogPanel.Add(dialogButtons);
            _createDialog.Add(dialogPanel);
            _popup.Add(_createDialog);
            _doc.rootVisualElement.Add(_popup);

            _radial = new RadialMenu();
            _radial.OnCategoryClicked += OnRadialCategoryClicked;
            _radial.OnItemClicked += OnRadialItemClicked;
            _radial.OnBackClicked += OnRadialBackClicked;

            _joystick = new ObserverJoystick();
            _joystick.OnOperatorSelected += OnJoystickOperatorSelected;
        }

        private void ShowCategoryRing()
        {
            _joystick.Hide();
            var cats = ProgrammatorData.CATEGORIES;
            var colors = new Color[cats.Length];
            for (int i = 0; i < cats.Length; i++)
            {
                colors[i] = ProgrammatorData.CATEGORY_COLORS[cats[i]];
            }

            _radial.SetInnerItems(cats, colors);
            _radial.ClearOuterItems();
        }

        private void PrevPage()
        {
            if (ProgrammatorData.CurrentPage > 0)
            {
                ClearSelection();
                _radial.Hide();
                _joystick.Hide();
                _radialShown = false;
                ProgrammatorData.CurrentPage--;
                RefreshAllCells();
            }
        }

        private void NextPage()
        {
            if (ProgrammatorData.CurrentPage < ProgrammatorData.PageCount - 1)
            {
                ClearSelection();
                _radial.Hide();
                _joystick.Hide();
                _radialShown = false;
                ProgrammatorData.CurrentPage++;
                RefreshAllCells();
            }
        }

        private void AddPageClick()
        {
            if (ProgrammatorData.PageCount >= 100) return;
            ProgrammatorData.AddPage();
            UpdatePageLabel();
        }

        private void RemovePageClick()
        {
            if (ProgrammatorData.RemoveLastPage())
            {
                RefreshAllCells();
            }
        }

        private void UpdatePageLabel()
        {
            _pageLabel.text = $"Стр. {ProgrammatorData.CurrentPage + 1}/{ProgrammatorData.PageCount}";
            _prevBtn.SetEnabled(ProgrammatorData.CurrentPage > 0);
            _nextBtn.SetEnabled(ProgrammatorData.CurrentPage < ProgrammatorData.PageCount - 1);
        }

        private void HighlightCell(int row, int col, bool highlight)
        {
            var cell = _cells[row, col];
            if (highlight)
            {
                cell.style.borderTopColor = new Color(1f, 0.84f, 0f, 1f);
                cell.style.borderBottomColor = new Color(1f, 0.84f, 0f, 1f);
                cell.style.borderLeftColor = new Color(1f, 0.84f, 0f, 1f);
                cell.style.borderRightColor = new Color(1f, 0.84f, 0f, 1f);
            }
            else
            {
                cell.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                cell.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                cell.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                cell.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            }
        }

        private bool IsSelected(int row, int col)
        {
            if (_selectedCells.Count > 0)
                return _selectedCells.Contains((long)row * ProgrammatorData.COLS + col);
            if (!_hasSelection) return false;
            int minRow = Mathf.Min(_selStartRow, _selEndRow);
            int maxRow = Mathf.Max(_selStartRow, _selEndRow);
            int minCol = Mathf.Min(_selStartCol, _selEndCol);
            int maxCol = Mathf.Max(_selStartCol, _selEndCol);
            return row >= minRow && row <= maxRow && col >= minCol && col <= maxCol;
        }

        private void SetSelectionBorder(int row, int col, bool selected)
        {
            var cell = _cells[row, col];
            var color = selected
                ? new Color(0.2f, 0.5f, 1f, 1f)
                : new Color(0.25f, 0.25f, 0.25f, 1f);
            cell.style.borderTopColor = color;
            cell.style.borderBottomColor = color;
            cell.style.borderLeftColor = color;
            cell.style.borderRightColor = color;
        }

        private void RefreshSelectionBorders()
        {
            for (int r = 0; r < ProgrammatorData.ROWS; r++)
            {
                for (int c = 0; c < ProgrammatorData.COLS; c++)
                {
                    if (IsSelected(r, c))
                        SetSelectionBorder(r, c, true);
                    else if (ProgrammatorData.HoveredCell != (r * ProgrammatorData.COLS) + c)
                        SetSelectionBorder(r, c, false);
                }
            }
        }

        private void ToggleCellSelection(int row, int col)
        {
            long key = (long)row * ProgrammatorData.COLS + col;
            if (!_selectedCells.Remove(key))
            {
                if (_hasSelection)
                {
                    int minR = Mathf.Min(_selStartRow, _selEndRow);
                    int maxR = Mathf.Max(_selStartRow, _selEndRow);
                    int minC = Mathf.Min(_selStartCol, _selEndCol);
                    int maxC = Mathf.Max(_selStartCol, _selEndCol);
                    for (int r = minR; r <= maxR; r++)
                        for (int c = minC; c <= maxC; c++)
                        {
                            _selectedCells.Add((long)r * ProgrammatorData.COLS + c);
                            SetSelectionBorder(r, c, true);
                        }
                    _hasSelection = false;
                }
                _selectedCells.Add(key);
                SetSelectionBorder(row, col, true);
            }
            else
            {
                SetSelectionBorder(row, col, false);
            }
        }

        private void SelectCell(int row, int col)
        {
            ClearSelection();
            _selStartRow = _selEndRow = row;
            _selStartCol = _selEndCol = col;
            _hasSelection = true;
            SetSelectionBorder(row, col, true);
        }

        private void ExtendSelection(int row, int col)
        {
            if (_selectedCells.Count > 0)
            {
                foreach (long key in _selectedCells)
                {
                    int r = (int)(key / ProgrammatorData.COLS);
                    int c = (int)(key % ProgrammatorData.COLS);
                    SetSelectionBorder(r, c, false);
                }
                _selectedCells.Clear();
                _hasSelection = false;
            }
            if (!_hasSelection)
            {
                SelectCell(row, col);
                return;
            }
            int oldMinRow = Mathf.Min(_selStartRow, _selEndRow);
            int oldMaxRow = Mathf.Max(_selStartRow, _selEndRow);
            int oldMinCol = Mathf.Min(_selStartCol, _selEndCol);
            int oldMaxCol = Mathf.Max(_selStartCol, _selEndCol);
            _selEndRow = row;
            _selEndCol = col;
            int newMinRow = Mathf.Min(_selStartRow, _selEndRow);
            int newMaxRow = Mathf.Max(_selStartRow, _selEndRow);
            int newMinCol = Mathf.Min(_selStartCol, _selEndCol);
            int newMaxCol = Mathf.Max(_selStartCol, _selEndCol);
            for (int r = Mathf.Min(oldMinRow, newMinRow); r <= Mathf.Max(oldMaxRow, newMaxRow); r++)
            {
                for (int c = Mathf.Min(oldMinCol, newMinCol); c <= Mathf.Max(oldMaxCol, newMaxCol); c++)
                {
                    bool nowSelected = r >= newMinRow && r <= newMaxRow && c >= newMinCol && c <= newMaxCol;
                    if (nowSelected)
                        SetSelectionBorder(r, c, true);
                    else if (ProgrammatorData.HoveredCell != (r * ProgrammatorData.COLS) + c)
                        SetSelectionBorder(r, c, false);
                }
            }
        }

        private void ClearSelection()
        {
            if (_selectedCells.Count > 0)
            {
                foreach (long key in _selectedCells)
                {
                    int r = (int)(key / ProgrammatorData.COLS);
                    int c = (int)(key % ProgrammatorData.COLS);
                    if (ProgrammatorData.HoveredCell != (r * ProgrammatorData.COLS) + c)
                        SetSelectionBorder(r, c, false);
                }
                _selectedCells.Clear();
            }
            if (_hasSelection)
            {
                int minRow = Mathf.Min(_selStartRow, _selEndRow);
                int maxRow = Mathf.Max(_selStartRow, _selEndRow);
                int minCol = Mathf.Min(_selStartCol, _selEndCol);
                int maxCol = Mathf.Max(_selStartCol, _selEndCol);
                for (int r = minRow; r <= maxRow; r++)
                    for (int c = minCol; c <= maxCol; c++)
                        if (ProgrammatorData.HoveredCell != (r * ProgrammatorData.COLS) + c)
                            SetSelectionBorder(r, c, false);
                _hasSelection = false;
            }
        }

        private (int minRow, int maxRow, int minCol, int maxCol) GetSetBounds()
        {
            int minR = int.MaxValue, maxR = int.MinValue;
            int minC = int.MaxValue, maxC = int.MinValue;
            foreach (long key in _selectedCells)
            {
                int r = (int)(key / ProgrammatorData.COLS);
                int c = (int)(key % ProgrammatorData.COLS);
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
                if (c < minC) minC = c;
                if (c > maxC) maxC = c;
            }
            return (minR, maxR, minC, maxC);
        }

        private bool HasAnySelection() => _hasSelection || _selectedCells.Count > 0;

        private void CopySelection()
        {
            if (!HasAnySelection()) return;
            int minRow, maxRow, minCol, maxCol;
            if (_selectedCells.Count > 0)
            {
                var b = GetSetBounds();
                minRow = b.minRow; maxRow = b.maxRow;
                minCol = b.minCol; maxCol = b.maxCol;
            }
            else
            {
                minRow = Mathf.Min(_selStartRow, _selEndRow);
                maxRow = Mathf.Max(_selStartRow, _selEndRow);
                minCol = Mathf.Min(_selStartCol, _selEndCol);
                maxCol = Mathf.Max(_selStartCol, _selEndCol);
            }
            _clipboardWidth = (maxCol - minCol) + 1;
            _clipboardHeight = (maxRow - minRow) + 1;
            _clipboardCodes = new int[_clipboardWidth * _clipboardHeight];
            _clipboardLabels = new string[_clipboardWidth * _clipboardHeight];
            _clipboardValues = new string[_clipboardWidth * _clipboardHeight];
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    int srcIdx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                 + (r * ProgrammatorData.COLS) + c;
                    int dstIdx = ((r - minRow) * _clipboardWidth) + (c - minCol);
                    _clipboardCodes[dstIdx] = ProgrammatorData.Codes[srcIdx];
                    _clipboardLabels[dstIdx] = ProgrammatorData.Labels[srcIdx];
                    _clipboardValues[dstIdx] = ProgrammatorData.Values[srcIdx];
                }
            }
            _hasClipboard = true;
        }

        private void CutSelection()
        {
            if (!HasAnySelection()) return;
            CopySelection();
            ProgrammatorData.PushUndo();
            int minRow, maxRow, minCol, maxCol;
            if (_selectedCells.Count > 0)
            {
                var b = GetSetBounds();
                minRow = b.minRow; maxRow = b.maxRow;
                minCol = b.minCol; maxCol = b.maxCol;
            }
            else
            {
                minRow = Mathf.Min(_selStartRow, _selEndRow);
                maxRow = Mathf.Max(_selStartRow, _selEndRow);
                minCol = Mathf.Min(_selStartCol, _selEndCol);
                maxCol = Mathf.Max(_selStartCol, _selEndCol);
            }
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    if (_selectedCells.Count > 0 && !_selectedCells.Contains((long)r * ProgrammatorData.COLS + c))
                        continue;
                    int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                              + (r * ProgrammatorData.COLS) + c;
                    ProgrammatorData.Codes[idx] = 0;
                    UpdateCell(r, c);
                }
            }
        }

        private void PasteClipboard()
        {
            if (!_hasClipboard) return;
            ProgrammatorData.PushUndo();
            int anchorRow = 0, anchorCol = 0;
            if (_selectedCells.Count > 0)
            {
                var b = GetSetBounds();
                anchorRow = b.minRow;
                anchorCol = b.minCol;
            }
            else if (_hasSelection)
            {
                anchorRow = Mathf.Min(_selStartRow, _selEndRow);
                anchorCol = Mathf.Min(_selStartCol, _selEndCol);
            }
            for (int r = 0; r < _clipboardHeight; r++)
            {
                for (int c = 0; c < _clipboardWidth; c++)
                {
                    int targetRow = anchorRow + r;
                    int targetCol = anchorCol + c;
                    if (targetRow >= ProgrammatorData.ROWS || targetCol >= ProgrammatorData.COLS)
                        continue;
                    int dstIdx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                 + (targetRow * ProgrammatorData.COLS) + targetCol;
                    int srcIdx = (r * _clipboardWidth) + c;
                    ProgrammatorData.Codes[dstIdx] = _clipboardCodes[srcIdx];
                    ProgrammatorData.Labels[dstIdx] = _clipboardLabels[srcIdx];
                    ProgrammatorData.Values[dstIdx] = _clipboardValues[srcIdx];
                    UpdateCell(targetRow, targetCol);
                }
            }
            SelectCell(anchorRow, anchorCol);
            _selEndRow = Mathf.Min(anchorRow + _clipboardHeight - 1, ProgrammatorData.ROWS - 1);
            _selEndCol = Mathf.Min(anchorCol + _clipboardWidth - 1, ProgrammatorData.COLS - 1);
            RefreshSelectionBorders();
        }

        private void ShiftSelection(int dx, int dy)
        {
            if (!HasAnySelection()) return;
            int page = ProgrammatorData.CurrentPage;
            int cols = ProgrammatorData.COLS;
            int rows = ProgrammatorData.ROWS;
            int cellsPerPage = ProgrammatorData.CELLS_PER_PAGE;

            if (_selectedCells.Count > 0)
            {
                var b = GetSetBounds();
                if (b.minRow + dy < 0 || b.maxRow + dy >= rows ||
                    b.minCol + dx < 0 || b.maxCol + dx >= cols)
                    return;
                var temp = new Dictionary<long, (int code, string label, string value)>();
                foreach (long key in _selectedCells)
                {
                    int idx = (page * cellsPerPage) + (int)key;
                    temp[key] = (ProgrammatorData.Codes[idx], ProgrammatorData.Labels[idx], ProgrammatorData.Values[idx]);
                }
                foreach (long key in _selectedCells)
                {
                    int r = (int)(key / cols);
                    int c = (int)(key % cols);
                    int idx = (page * cellsPerPage) + (int)key;
                    ProgrammatorData.Codes[idx] = 0;
                    ProgrammatorData.Labels[idx] = null;
                    ProgrammatorData.Values[idx] = null;
                    UpdateCell(r, c);
                    SetSelectionBorder(r, c, false);
                }
                var ordered = new List<long>(_selectedCells);
                if (dx > 0) ordered.Sort((a, b) => (int)((b % cols) - (a % cols)));
                else if (dx < 0) ordered.Sort((a, b) => (int)((a % cols) - (b % cols)));
                else if (dy > 0) ordered.Sort((a, b) => (int)((b / cols) - (a / cols)));
                else if (dy < 0) ordered.Sort((a, b) => (int)((a / cols) - (b / cols)));
                ProgrammatorData.PushUndo();
                var newSet = new HashSet<long>();
                foreach (long key in ordered)
                {
                    int oldR = (int)(key / cols);
                    int oldC = (int)(key % cols);
                    int newR = oldR + dy;
                    int newC = oldC + dx;
                    if (newR < 0 || newR >= rows || newC < 0 || newC >= cols)
                    {
                        int origIdx = (page * cellsPerPage) + (int)key;
                        ProgrammatorData.Codes[origIdx] = temp[key].code;
                        ProgrammatorData.Labels[origIdx] = temp[key].label;
                        ProgrammatorData.Values[origIdx] = temp[key].value;
                        UpdateCell(oldR, oldC);
                        SetSelectionBorder(oldR, oldC, true);
                        newSet.Add(key);
                        continue;
                    }
                    int destIdx = (page * cellsPerPage) + (newR * cols) + newC;
                    if (ProgrammatorData.Codes[destIdx] != 0)
                    {
                        int pushR = newR + dy;
                        int pushC = newC + dx;
                        bool pushed = false;
                        while (pushR >= 0 && pushR < rows && pushC >= 0 && pushC < cols)
                        {
                            int pushIdx = (page * cellsPerPage) + (pushR * cols) + pushC;
                            if (ProgrammatorData.Codes[pushIdx] == 0)
                            {
                                ProgrammatorData.Codes[pushIdx] = ProgrammatorData.Codes[destIdx];
                                ProgrammatorData.Labels[pushIdx] = ProgrammatorData.Labels[destIdx];
                                ProgrammatorData.Values[pushIdx] = ProgrammatorData.Values[destIdx];
                                ProgrammatorData.Codes[destIdx] = 0;
                                ProgrammatorData.Labels[destIdx] = null;
                                ProgrammatorData.Values[destIdx] = null;
                                UpdateCell(newR, newC);
                                UpdateCell(pushR, pushC);
                                pushed = true;
                                break;
                            }
                            pushR += dy;
                            pushC += dx;
                        }
                        if (!pushed)
                        {
                            int origIdx = (page * cellsPerPage) + (int)key;
                            ProgrammatorData.Codes[origIdx] = temp[key].code;
                            ProgrammatorData.Labels[origIdx] = temp[key].label;
                            ProgrammatorData.Values[origIdx] = temp[key].value;
                            UpdateCell(oldR, oldC);
                            SetSelectionBorder(oldR, oldC, true);
                            newSet.Add(key);
                            continue;
                        }
                    }
                    ProgrammatorData.Codes[destIdx] = temp[key].code;
                    ProgrammatorData.Labels[destIdx] = temp[key].label;
                    ProgrammatorData.Values[destIdx] = temp[key].value;
                    UpdateCell(newR, newC);
                    SetSelectionBorder(newR, newC, true);
                    newSet.Add((long)newR * cols + newC);
                }
                _selectedCells.Clear();
                foreach (long k in newSet) _selectedCells.Add(k);
                _hasSelection = false;
                return;
            }

            int minRow = Mathf.Min(_selStartRow, _selEndRow);
            int maxRow = Mathf.Max(_selStartRow, _selEndRow);
            int minCol = Mathf.Min(_selStartCol, _selEndCol);
            int maxCol = Mathf.Max(_selStartCol, _selEndCol);
            int newMinRow = minRow + dy;
            int newMaxRow = maxRow + dy;
            int newMinCol = minCol + dx;
            int newMaxCol = maxCol + dx;
            if (newMinRow < 0 || newMaxRow >= rows ||
                newMinCol < 0 || newMaxCol >= cols)
                return;
            if (dx > 0)
            {
                for (int r = minRow; r <= maxRow; r++)
                    for (int c = maxCol + 1; c <= maxCol + dx; c++)
                        if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = c + dx; e < cols; e++)
                                if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                                { found = true; break; }
                            if (!found) return;
                        }
            }
            else if (dx < 0)
            {
                int absDx = -dx;
                for (int r = minRow; r <= maxRow; r++)
                    for (int c = minCol + dx; c <= minCol - 1; c++)
                        if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = c - absDx; e >= 0; e--)
                                if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                                { found = true; break; }
                            if (!found) return;
                        }
            }
            else if (dy > 0)
            {
                for (int c = minCol; c <= maxCol; c++)
                    for (int r = maxRow + 1; r <= maxRow + dy; r++)
                        if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = r + dy; e < rows; e++)
                                if (ProgrammatorData.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                                { found = true; break; }
                            if (!found) return;
                        }
            }
            else if (dy < 0)
            {
                int absDy = -dy;
                for (int c = minCol; c <= maxCol; c++)
                    for (int r = minRow + dy; r <= minRow - 1; r++)
                        if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = r - absDy; e >= 0; e--)
                                if (ProgrammatorData.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                                { found = true; break; }
                            if (!found) return;
                        }
            }
            ProgrammatorData.PushUndo();
            int width = (maxCol - minCol) + 1;
            int height = (maxRow - minRow) + 1;
            int[] tmpCodes = new int[width * height];
            string[] tmpLabels = new string[width * height];
            string[] tmpValues = new string[width * height];
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    int srcIdx = (page * cellsPerPage) + (r * cols) + c;
                    int tmpIdx = ((r - minRow) * width) + (c - minCol);
                    tmpCodes[tmpIdx] = ProgrammatorData.Codes[srcIdx];
                    tmpLabels[tmpIdx] = ProgrammatorData.Labels[srcIdx];
                    tmpValues[tmpIdx] = ProgrammatorData.Values[srcIdx];
                    ProgrammatorData.Codes[srcIdx] = 0;
                    ProgrammatorData.Labels[srcIdx] = null;
                    ProgrammatorData.Values[srcIdx] = null;
                    UpdateCell(r, c);
                    SetSelectionBorder(r, c, false);
                }
            }
            if (dx > 0)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = maxCol + dx; c >= maxCol + 1; c--)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (ProgrammatorData.Codes[idx] == 0) continue;
                        int emptyCol = -1;
                        for (int e = c + dx; e < cols; e++)
                        {
                            if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                            { emptyCol = e; break; }
                        }
                        if (emptyCol < 0) continue;
                        int dst = (page * cellsPerPage) + (r * cols) + emptyCol;
                        ProgrammatorData.Codes[dst] = ProgrammatorData.Codes[idx];
                        ProgrammatorData.Labels[dst] = ProgrammatorData.Labels[idx];
                        ProgrammatorData.Values[dst] = ProgrammatorData.Values[idx];
                        ProgrammatorData.Codes[idx] = 0;
                        ProgrammatorData.Labels[idx] = null;
                        ProgrammatorData.Values[idx] = null;
                        UpdateCell(r, c);
                        UpdateCell(r, emptyCol);
                    }
                }
            }
            else if (dx < 0)
            {
                int absDx = -dx;
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = minCol + dx; c <= minCol - 1; c++)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (ProgrammatorData.Codes[idx] == 0) continue;
                        int emptyCol = -1;
                        for (int e = c - absDx; e >= 0; e--)
                        {
                            if (ProgrammatorData.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                            { emptyCol = e; break; }
                        }
                        if (emptyCol < 0) continue;
                        int dst = (page * cellsPerPage) + (r * cols) + emptyCol;
                        ProgrammatorData.Codes[dst] = ProgrammatorData.Codes[idx];
                        ProgrammatorData.Labels[dst] = ProgrammatorData.Labels[idx];
                        ProgrammatorData.Values[dst] = ProgrammatorData.Values[idx];
                        ProgrammatorData.Codes[idx] = 0;
                        ProgrammatorData.Labels[idx] = null;
                        ProgrammatorData.Values[idx] = null;
                        UpdateCell(r, c);
                        UpdateCell(r, emptyCol);
                    }
                }
            }
            else if (dy > 0)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    for (int r = maxRow + dy; r >= maxRow + 1; r--)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (ProgrammatorData.Codes[idx] == 0) continue;
                        int emptyRow = -1;
                        for (int e = r + dy; e < rows; e++)
                        {
                            if (ProgrammatorData.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                            { emptyRow = e; break; }
                        }
                        if (emptyRow < 0) continue;
                        int dst = (page * cellsPerPage) + (emptyRow * cols) + c;
                        ProgrammatorData.Codes[dst] = ProgrammatorData.Codes[idx];
                        ProgrammatorData.Labels[dst] = ProgrammatorData.Labels[idx];
                        ProgrammatorData.Values[dst] = ProgrammatorData.Values[idx];
                        ProgrammatorData.Codes[idx] = 0;
                        ProgrammatorData.Labels[idx] = null;
                        ProgrammatorData.Values[idx] = null;
                        UpdateCell(r, c);
                        UpdateCell(emptyRow, c);
                    }
                }
            }
            else if (dy < 0)
            {
                int absDy = -dy;
                for (int c = minCol; c <= maxCol; c++)
                {
                    for (int r = minRow + dy; r <= minRow - 1; r++)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (ProgrammatorData.Codes[idx] == 0) continue;
                        int emptyRow = -1;
                        for (int e = r - absDy; e >= 0; e--)
                        {
                            if (ProgrammatorData.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                            { emptyRow = e; break; }
                        }
                        if (emptyRow < 0) continue;
                        int dst = (page * cellsPerPage) + (emptyRow * cols) + c;
                        ProgrammatorData.Codes[dst] = ProgrammatorData.Codes[idx];
                        ProgrammatorData.Labels[dst] = ProgrammatorData.Labels[idx];
                        ProgrammatorData.Values[dst] = ProgrammatorData.Values[idx];
                        ProgrammatorData.Codes[idx] = 0;
                        ProgrammatorData.Labels[idx] = null;
                        ProgrammatorData.Values[idx] = null;
                        UpdateCell(r, c);
                        UpdateCell(emptyRow, c);
                    }
                }
            }
            for (int r = newMinRow; r <= newMaxRow; r++)
            {
                for (int c = newMinCol; c <= newMaxCol; c++)
                {
                    int dstIdx = (page * cellsPerPage) + (r * cols) + c;
                    int tmpIdx = ((r - newMinRow) * width) + (c - newMinCol);
                    ProgrammatorData.Codes[dstIdx] = tmpCodes[tmpIdx];
                    ProgrammatorData.Labels[dstIdx] = tmpLabels[tmpIdx];
                    ProgrammatorData.Values[dstIdx] = tmpValues[tmpIdx];
                    UpdateCell(r, c);
                    SetSelectionBorder(r, c, true);
                }
            }
            _selStartRow = newMinRow;
            _selStartCol = newMinCol;
            _selEndRow = newMaxRow;
            _selEndCol = newMaxCol;
        }

        [System.Serializable]
        private class ProgrammatorSave
        {
            public int[] Codes;
            public string[] Labels;
            public string[] Values;
        }

        private string SavePath => Path.Combine(Application.persistentDataPath, "programmator.json");

        private void SaveProgram()
        {
            var data = new ProgrammatorSave
            {
                Codes = ProgrammatorData.Codes.ToArray(),
                Labels = ProgrammatorData.Labels.ToArray(),
                Values = ProgrammatorData.Values.ToArray(),
            };
            File.WriteAllText(SavePath, JsonUtility.ToJson(data));
            Debug.Log("[Programmator] Program saved");
        }

        private void LoadProgramFromDisk()
        {
            if (!File.Exists(SavePath)) return;
            try
            {
                var data = JsonUtility.FromJson<ProgrammatorSave>(File.ReadAllText(SavePath));
                if (data.Codes == null || data.Codes.Length == 0) return;
                int total = data.Codes.Length;
                ProgrammatorData.Codes = new List<int>(total);
                ProgrammatorData.Labels = new List<string>(total);
                ProgrammatorData.Values = new List<string>(total);
                ProgrammatorData.Codes.AddRange(data.Codes);
                ProgrammatorData.Labels.AddRange(data.Labels ?? new string[total]);
                ProgrammatorData.Values.AddRange(data.Values ?? new string[total]);
                ProgrammatorData.CurrentPage = 0;
                Debug.Log($"[Programmator] Program loaded ({total} cells)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Programmator] Failed to load program: {e.Message}");
            }
        }

        private void ShowProgramList()
        {
            ClearSelection();
            _joystick.Hide();
            _radial.Hide();
            _radialShown = false;
            _radialCellIndex = -1;
            if (_isRunning) StopProgram();
            _programTitle.text = "Программатор";
            RefreshProgramList();
            _panel.style.display = DisplayStyle.None;
            _programListPanel.style.display = DisplayStyle.Flex;
            _activeIndex = -1;
        }

        private void OpenProgram(int index)
        {
            if (index < 0 || index >= _programItems.Count) return;
            var item = _programItems[index];
            ProgrammatorData.Codes = new List<int>(item.Codes);
            ProgrammatorData.Labels = new List<string>(item.Labels);
            ProgrammatorData.Values = new List<string>(item.Values);
            _activeIndex = index;
            ProgrammatorData.CurrentPage = 0;
            _programTitle.text = item.Name;
            _programListPanel.style.display = DisplayStyle.None;
            _panel.style.display = DisplayStyle.Flex;
            RefreshAllCells();
        }

        private void CloseProgram()
        {
            if (_isRunning) StopProgram();
            if (_activeIndex >= 0 && _activeIndex < _programItems.Count)
            {
                var item = _programItems[_activeIndex];
                item.Codes = new List<int>(ProgrammatorData.Codes);
                item.Labels = new List<string>(ProgrammatorData.Labels);
                item.Values = new List<string>(ProgrammatorData.Values);
            }
            ShowProgramList();
        }

        private void CreateNewProgram(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = $"Программа {_programItems.Count + 1}";
            var item = new ProgramItem
            {
                Name = name,
                Codes = new List<int>(new int[ProgrammatorData.CELLS_PER_PAGE]),
                Labels = new List<string>(new string[ProgrammatorData.CELLS_PER_PAGE]),
                Values = new List<string>(new string[ProgrammatorData.CELLS_PER_PAGE]),
            };
            _programItems.Add(item);
            HideCreateInput();
            OpenProgram(_programItems.Count - 1);
        }

        private void ShowCreateInput()
        {
            _createInput.value = $"Программа {_programItems.Count + 1}";
            _createDialog.style.display = DisplayStyle.Flex;
            _createInput.Focus();
        }

        private void HideCreateInput()
        {
            _createDialog.style.display = DisplayStyle.None;
        }

        private void DeleteProgram(int index)
        {
            if (index < 0 || index >= _programItems.Count) return;
            _programItems.RemoveAt(index);
            RefreshProgramList();
        }

        private void RefreshProgramList()
        {
            _listScroll.Clear();
            for (int i = 0; i < _programItems.Count; i++)
            {
                int idx = i;
                var item = _programItems[i];
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 1f);
                var nameLabel = new Label(item.Name);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
                nameLabel.style.fontSize = 14;
                nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(nameLabel);

                var delBtn = new Button(() => DeleteProgram(idx));
                delBtn.text = "\u00d7";
                delBtn.style.width = 22;
                delBtn.style.height = 22;
                delBtn.style.backgroundColor = new Color(0.3f, 0f, 0f, 0.3f);
                delBtn.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
                delBtn.style.fontSize = 14;
                delBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                delBtn.style.borderTopWidth = 0;
                delBtn.style.borderBottomWidth = 0;
                delBtn.style.borderLeftWidth = 0;
                delBtn.style.borderRightWidth = 0;
                delBtn.style.paddingTop = 0;
                delBtn.style.paddingBottom = 0;
                delBtn.style.paddingLeft = 0;
                delBtn.style.paddingRight = 0;
                delBtn.style.marginLeft = 8;
                row.Add(delBtn);

                row.RegisterCallback<ClickEvent>(_ => OpenProgram(idx));
                row.RegisterCallback<MouseEnterEvent>(_ =>
                    row.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));
                row.RegisterCallback<MouseLeaveEvent>(_ =>
                    row.style.backgroundColor = Color.clear);

                _listScroll.Add(row);
            }
        }

        private void RunProgram()
        {
            _isRunning = true;
            _runBtn.SetEnabled(false);
            _stopBtn.SetEnabled(true);
            _panel.style.borderTopColor = new Color(0.2f, 0.8f, 0.2f, 1f);
            _panel.style.borderBottomColor = new Color(0.2f, 0.8f, 0.2f, 1f);
            _panel.style.borderLeftColor = new Color(0.2f, 0.8f, 0.2f, 1f);
            _panel.style.borderRightColor = new Color(0.2f, 0.8f, 0.2f, 1f);
            Debug.Log("[Programmator] Program running");
        }

        private void StopProgram()
        {
            _isRunning = false;
            _runBtn.SetEnabled(true);
            _stopBtn.SetEnabled(false);
            _panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            Debug.Log("[Programmator] Program stopped");
        }

        private void UpdateCell(int row, int col)
        {
            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                      + (row * ProgrammatorData.COLS) + col;
            int id = ProgrammatorData.Codes[idx];
            var action = (ProgAction)id;
            var cell = _cells[row, col];
            var label = _cellLabels[row, col];

            var tex = ProgrammatorTextureRegistry.GetTexture(action);
            if (tex != null)
            {
                cell.style.backgroundImage = new StyleBackground(tex);
                cell.style.backgroundSize = new BackgroundSize(tex.width, tex.height);
                cell.style.backgroundColor = Color.clear;
                label.text = string.Empty;
            }
            else if (id == 0)
            {
                cell.style.backgroundImage = null;
                cell.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
                label.text = string.Empty;
            }
            else
            {
                cell.style.backgroundImage = null;
                cell.style.backgroundColor = new Color(0.3f, 0.1f, 0.1f, 1f);
                string name = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? n : string.Empty;
                label.text = name;
            }
        }

        private void OnRadialCategoryClicked(int categoryId)
        {
            // Category clicked — populate outer ring with operators
            if (!ProgrammatorData.CATEGORY_OPERATORS.TryGetValue(categoryId, out var ops))
            {
                return;
            }

            // CAT_OBSERVER uses a joystick instead of the outer ring
            if (categoryId == ProgrammatorData.CAT_OBSERVER)
            {
                _radial.ClearOuterItems();
                _joystick.Hide();
                var cellCenter = _cells[_radialCellIndex / ProgrammatorData.COLS,
                                         _radialCellIndex % ProgrammatorData.COLS].worldBound.center;
                _joystick.ShowAt(_doc.rootVisualElement, cellCenter);
                return;
            }

            // Other categories: populate standard outer ring
            _joystick.Hide();

            if (!ProgrammatorData.CATEGORY_COLORS.TryGetValue(categoryId, out var catColor))
            {
                catColor = Color.white;
            }

            var colors = new Color[ops.Length];
            for (int i = 0; i < ops.Length; i++)
            {
                colors[i] = catColor;
            }

            _radial.SetOuterItems(Array.ConvertAll(ops, op => (int)op), colors);
        }

        private void OnJoystickOperatorSelected(ProgAction action)
        {
            if (_radialCellIndex < 0)
            {
                return;
            }

            int row = _radialCellIndex / ProgrammatorData.COLS;
            int col = _radialCellIndex % ProgrammatorData.COLS;
            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                      + (row * ProgrammatorData.COLS) + col;
            ProgrammatorData.PushUndo();
            ProgrammatorData.Codes[idx] = (int)action;
            UpdateCell(row, col);

            if (row * ProgrammatorData.COLS + col == ProgrammatorData.CELLS_PER_PAGE - 1
                && ProgrammatorData.CurrentPage == ProgrammatorData.PageCount - 1)
            {
                ProgrammatorData.AddPage();
                UpdatePageLabel();
            }

            _joystick.Hide();
            _radial.Hide();
            _radialShown = false;
            _radialCellIndex = -1;
        }

        private void OnRadialItemClicked(int selectedId)
        {
            // Outer ring item clicked — place the operator in the cell
            if (_radialCellIndex < 0)
            {
                return;
            }

            int row = _radialCellIndex / ProgrammatorData.COLS;
            int col = _radialCellIndex % ProgrammatorData.COLS;
            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                      + (row * ProgrammatorData.COLS) + col;
            ProgrammatorData.PushUndo();
            ProgrammatorData.Codes[idx] = selectedId;
            UpdateCell(row, col);

            if (row * ProgrammatorData.COLS + col == ProgrammatorData.CELLS_PER_PAGE - 1
                && ProgrammatorData.CurrentPage == ProgrammatorData.PageCount - 1)
            {
                ProgrammatorData.AddPage();
                UpdatePageLabel();
            }

            _radial.Hide();
            _radialShown = false;
            _radialCellIndex = -1;
        }

        private void OnRadialBackClicked()
        {
            // Back button — clear outer ring and joystick, keep inner ring visible
            _radial.ClearOuterItems();
            _joystick.Hide();
        }

        protected void Update()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (!_isOpen)
            {
                return;
            }

            // ESC closes the programmator or goes back to list
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_hasSelection && !_radialShown)
                {
                    ClearSelection();
                    return;
                }
                if (_selectedCells.Count > 0 && !_radialShown)
                {
                    ClearSelection();
                    return;
                }
                if (_panel.style.display == DisplayStyle.Flex)
                {
                    CloseProgram();
                    return;
                }
                Hide();
                return;
            }

            if (_radialShown)
            {
                // DEL clears the cell when radial menu is open
                if (Keyboard.current.deleteKey.wasPressedThisFrame)
                {
                    if (_radialCellIndex >= 0)
                    {
                        int row = _radialCellIndex / ProgrammatorData.COLS;
                        int col = _radialCellIndex % ProgrammatorData.COLS;
                        int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                  + (row * ProgrammatorData.COLS) + col;
                        ProgrammatorData.PushUndo();
                        ProgrammatorData.Codes[idx] = 0;
                        UpdateCell(row, col);
                    }

                    _joystick.Hide();
                    _radial.Hide();
                    _radialShown = false;
                    _radialCellIndex = -1;
                    return;
                }
                return;
            }

            // Ctrl shortcuts
            if (Keyboard.current.ctrlKey.isPressed)
            {
                if (Keyboard.current.zKey.wasPressedThisFrame)
                {
                    if (ProgrammatorData.Undo())
                        RefreshAllCells();
                }
                else if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    if (ProgrammatorData.Redo())
                        RefreshAllCells();
                }
                else if (Keyboard.current.cKey.wasPressedThisFrame && HasAnySelection())
                {
                    CopySelection();
                }
                else if (Keyboard.current.xKey.wasPressedThisFrame && HasAnySelection())
                {
                    CutSelection();
                }
                else if (Keyboard.current.vKey.wasPressedThisFrame && _hasClipboard)
                {
                    PasteClipboard();
                }
                return;
            }

            // DEL clears selected cells
            if (Keyboard.current.deleteKey.wasPressedThisFrame)
            {
                if (!HasAnySelection()) { /* fall through */ }
                else if (_selectedCells.Count > 0)
                {
                    ProgrammatorData.PushUndo();
                    foreach (long key in _selectedCells)
                    {
                        int r = (int)(key / ProgrammatorData.COLS);
                        int c = (int)(key % ProgrammatorData.COLS);
                        int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                  + (r * ProgrammatorData.COLS) + c;
                        ProgrammatorData.Codes[idx] = 0;
                        UpdateCell(r, c);
                    }
                    _selectedCells.Clear();
                    _hasSelection = false;
                    return;
                }
                else if (_hasSelection)
                {
                    ProgrammatorData.PushUndo();
                    int minRow = Mathf.Min(_selStartRow, _selEndRow);
                    int maxRow = Mathf.Max(_selStartRow, _selEndRow);
                    int minCol = Mathf.Min(_selStartCol, _selEndCol);
                    int maxCol = Mathf.Max(_selStartCol, _selEndCol);
                    for (int r = minRow; r <= maxRow; r++)
                    {
                        for (int c = minCol; c <= maxCol; c++)
                        {
                            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                      + (r * ProgrammatorData.COLS) + c;
                            ProgrammatorData.Codes[idx] = 0;
                            UpdateCell(r, c);
                        }
                    }
                    return;
                }
            }

            // Arrow keys for page navigation
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                PrevPage();
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                NextPage();
            }
        }

        public void Show()
        {
            _isOpen = true;
            IsOpen = true;
            _popup.style.display = DisplayStyle.Flex;
            ShowProgramList();
        }

        public void Hide()
        {
            if (_isRunning) StopProgram();
            ClearSelection();
            _joystick.Hide();
            _radial.Hide();
            _radialShown = false;
            _radialCellIndex = -1;
            _isOpen = false;
            IsOpen = false;
            HideCreateInput();
            _programListPanel.style.display = DisplayStyle.None;
            _panel.style.display = DisplayStyle.None;
            _popup.style.display = DisplayStyle.None;
        }

        private void RefreshAllCells()
        {
            _selectedCells.Clear();
            _hasSelection = false;
            UpdatePageLabel();
            for (int i = 0; i < ProgrammatorData.ROWS; i++)
            {
                for (int j = 0; j < ProgrammatorData.COLS; j++)
                {
                    UpdateCell(i, j);
                }
            }
        }

        private void ShowCellTooltip(int row, int col)
        {
            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                      + (row * ProgrammatorData.COLS) + col;
            int opId = ProgrammatorData.Codes[idx];
            var action = (ProgAction)opId;
            string name = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? n : $"Код {opId}";
            string desc = ProgrammatorData.OPERATOR_DESCRIPTIONS.TryGetValue(action, out var d) ? d : string.Empty;
            string text = string.IsNullOrEmpty(desc)
                ? $"Ячейка [{col},{row}]: {name}"
                : $"Ячейка [{col},{row}]: {name} — {desc}";
            _tooltip?.Show(text, Vector2.zero);
        }
    }
}
