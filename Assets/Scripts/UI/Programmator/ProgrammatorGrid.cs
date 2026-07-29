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
        private TextField[,] _cellTextInputs;
        private TextField[,] _cellTextInputs2;
        private TextField[,] _cellNumInputs;
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

            var sheet = Resources.Load<StyleSheet>("Styles/Programmator");
            if (sheet != null)
            {
                _doc.rootVisualElement.styleSheets.Add(sheet);
            }

            CreateUI();
            _popup.style.display = DisplayStyle.None;

            _tooltip = new Tooltip();
            _tooltip.Initialize(_doc);
        }

        private void CreateUI()
        {
            _popup = new VisualElement();
            _popup.AddToClassList("programmator-popup");

            var dimmer = new VisualElement();
            dimmer.AddToClassList("programmator-dimmer");
            dimmer.pickingMode = PickingMode.Ignore;
            _popup.Add(dimmer);

            _panel = new VisualElement();
            var panel = _panel;
            panel.AddToClassList("programmator-panel");

            var topRow = new VisualElement();
            topRow.AddToClassList("programmator-header-top");

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("programmator-toolbar");
            topRow.Add(buttonsRow);

            var actionRow = new VisualElement();
            actionRow.AddToClassList("programmator-action-row");
            topRow.Add(actionRow);

            _programTitle = new Label("Программатор");
            _programTitle.AddToClassList("programmator-header-title");
            buttonsRow.Add(_programTitle);

            _prevBtn = new Button(PrevPage) { text = "<" };
            _prevBtn.AddToClassList("programmator-btn-icon");
            buttonsRow.Add(_prevBtn);

            _pageLabel = new Label("Стр. 1/1");
            _pageLabel.AddToClassList("programmator-page-label");
            buttonsRow.Add(_pageLabel);

            _nextBtn = new Button(NextPage) { text = ">" };
            _nextBtn.AddToClassList("programmator-btn-icon");
            buttonsRow.Add(_nextBtn);

            _pageInput = new IntegerField();
            _pageInput.value = ProgrammatorData.CurrentPage + 1;
            _pageInput.AddToClassList("programmator-page-input");
            _pageInput.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var ti = _pageInput.Q("unity-text-input");
                ti?.AddToClassList("programmator-page-input-inner");
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

            var addPageBtn = new Button(AddPageClick) { text = "+" };
            addPageBtn.AddToClassList("programmator-btn-icon");
            buttonsRow.Add(addPageBtn);

            var removePageBtn = new Button(RemovePageClick) { text = "−" };
            removePageBtn.AddToClassList("programmator-btn-icon");
            removePageBtn.AddToClassList("programmator-btn-icon-mr");
            buttonsRow.Add(removePageBtn);

            var shiftUpBtn = new Button(() => ShiftSelection(0, -1)) { text = "↑" };
            shiftUpBtn.AddToClassList("programmator-btn-icon");
            buttonsRow.Add(shiftUpBtn);

            var shiftDownBtn = new Button(() => ShiftSelection(0, 1)) { text = "↓" };
            shiftDownBtn.AddToClassList("programmator-btn-icon");
            buttonsRow.Add(shiftDownBtn);

            var shiftLeftBtn = new Button(() => ShiftSelection(-1, 0)) { text = "←" };
            shiftLeftBtn.AddToClassList("programmator-btn-icon");
            buttonsRow.Add(shiftLeftBtn);

            var shiftRightBtn = new Button(() => ShiftSelection(1, 0)) { text = "→" };
            shiftRightBtn.AddToClassList("programmator-btn-icon");
            shiftRightBtn.AddToClassList("programmator-btn-icon-mr");
            buttonsRow.Add(shiftRightBtn);

            _saveBtn = new Button(SaveProgram) { text = "\U0001f4be" };
            _saveBtn.AddToClassList("programmator-btn-save");
            actionRow.Add(_saveBtn);

            _runBtn = new Button(RunProgram) { text = "\u25b6" };
            _runBtn.AddToClassList("programmator-btn-run");
            actionRow.Add(_runBtn);

            _stopBtn = new Button(StopProgram) { text = "\u25a0" };
            _stopBtn.AddToClassList("programmator-btn-stop");
            _stopBtn.SetEnabled(false);
            actionRow.Add(_stopBtn);

            var closeBtn = new Button(CloseProgram) { text = "\u00d7" };
            closeBtn.AddToClassList("programmator-btn-close");
            var headerRow = new VisualElement();
            headerRow.AddToClassList("programmator-header");
            headerRow.Add(topRow);
            headerRow.Add(closeBtn);
            panel.Add(headerRow);

            var gridScroll = new VisualElement();
            gridScroll.AddToClassList("programmator-grid-scroll");

            _gridContainer = new VisualElement();
            _gridContainer.AddToClassList("programmator-grid-container");

            _cells = new VisualElement[ProgrammatorData.ROWS, ProgrammatorData.COLS];
            _cellLabels = new Label[ProgrammatorData.ROWS, ProgrammatorData.COLS];
            _cellTextInputs = new TextField[ProgrammatorData.ROWS, ProgrammatorData.COLS];
            _cellTextInputs2 = new TextField[ProgrammatorData.ROWS, ProgrammatorData.COLS];
            _cellNumInputs = new TextField[ProgrammatorData.ROWS, ProgrammatorData.COLS];

            for (int i = 0; i < ProgrammatorData.ROWS; i++)
            {
                for (int j = 0; j < ProgrammatorData.COLS; j++)
                {
                    int row = i, col = j;
                    var cell = new VisualElement();
                    cell.AddToClassList("programmator-cell");

                    cell.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        ProgrammatorData.HoveredCell = (row * ProgrammatorData.COLS) + col;
                        ShowCellTooltip(row, col);
                    });
                    cell.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        ProgrammatorData.HoveredCell = -1;
                        _tooltip?.Hide();
                    });

                    cell.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        _tooltip?.UpdatePosition(evt.position);
                    });

                    // LMB — selection (ignore clicks on input fields)
                    cell.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        if (evt.target is TextField) return;

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
                    label.AddToClassList("programmator-cell-label");
                    label.pickingMode = PickingMode.Ignore;
                    cell.Add(label);

                    var textInput = new TextField();
                    textInput.AddToClassList("programmator-cell-text-input");
                    textInput.style.display = DisplayStyle.None;
                    textInput.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        var ti = textInput.Q("unity-text-input");
                        ti?.AddToClassList("programmator-cell-text-input-inner");
                    });
                    textInput.RegisterValueChangedCallback(evt =>
                    {
                        int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                  + (row * ProgrammatorData.COLS) + col;
                        ProgrammatorData.Labels[idx] = evt.newValue;
                    });
                    cell.Add(textInput);
                    _cellTextInputs[row, col] = textInput;

                    var numInput = new TextField();
                    numInput.AddToClassList("programmator-cell-num-input");
                    numInput.style.display = DisplayStyle.None;
                    numInput.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        var ti = numInput.Q("unity-text-input");
                        ti?.AddToClassList("programmator-cell-num-input-inner");
                    });
                    numInput.RegisterValueChangedCallback(evt =>
                    {
                        int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                  + (row * ProgrammatorData.COLS) + col;
                        ProgrammatorData.Values[idx] = evt.newValue;
                    });
                    cell.Add(numInput);
                    _cellNumInputs[row, col] = numInput;

                    var textInput2 = new TextField();
                    textInput2.AddToClassList("programmator-cell-text-input");
                    textInput2.style.display = DisplayStyle.None;
                    textInput2.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        var ti = textInput2.Q("unity-text-input");
                        ti?.AddToClassList("programmator-cell-text-input-inner");
                    });
                    textInput2.RegisterValueChangedCallback(evt =>
                    {
                        int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                  + (row * ProgrammatorData.COLS) + col;
                        ProgrammatorData.Values[idx] = evt.newValue;
                    });
                    cell.Add(textInput2);
                    _cellTextInputs2[row, col] = textInput2;

                    _cells[row, col] = cell;
                    _cellLabels[row, col] = label;
                    _gridContainer.Add(cell);
                }
            }

            gridScroll.Add(_gridContainer);

            var gridRow = new VisualElement();
            gridRow.AddToClassList("programmator-grid-row");
            gridRow.Add(gridScroll);

            panel.Add(gridRow);

            _popup.Add(panel);

            _programListPanel = new VisualElement();
            _programListPanel.AddToClassList("programmator-list-panel");
            _programListPanel.style.display = DisplayStyle.None;

            var listHeaderRow = new VisualElement();
            listHeaderRow.AddToClassList("programmator-header-row");

            var listTitle = new Label("Программы");
            listTitle.AddToClassList("programmator-header-title");
            listHeaderRow.Add(listTitle);

            var listCloseBtn = new Button(() => Hide()) { text = "\u00d7" };
            listCloseBtn.AddToClassList("programmator-btn-close");
            listHeaderRow.Add(listCloseBtn);

            _programListPanel.Add(listHeaderRow);

            _listScroll = new ScrollView();
            _listScroll.AddToClassList("programmator-list-scroll");
            _programListPanel.Add(_listScroll);

            _createContainer = new VisualElement();
            _createContainer.AddToClassList("programmator-create-container");

            _createBtn = new Button(ShowCreateInput) { text = "+ Создать программу" };
            _createBtn.AddToClassList("programmator-create-btn");
            _createContainer.Add(_createBtn);

            _programListPanel.Add(_createContainer);

            _popup.Add(_programListPanel);

            _createDialog = new VisualElement();
            _createDialog.AddToClassList("programmator-dialog");
            _createDialog.style.display = DisplayStyle.None;

            var dialogPanel = new VisualElement();
            dialogPanel.AddToClassList("programmator-dialog-panel");

            var dialogTitle = new Label("Новая программа");
            dialogTitle.AddToClassList("programmator-dialog-title");
            dialogPanel.Add(dialogTitle);

            _createInput = new TextField();
            _createInput.value = $"Программа {_programItems.Count + 1}";
            _createInput.AddToClassList("programmator-dialog-input");
            _createInput.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var ti = _createInput.Q("unity-text-input");
                ti?.AddToClassList("programmator-dialog-input-inner");
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
            dialogButtons.AddToClassList("programmator-dialog-buttons");

            var dialogCancelBtn = new Button(HideCreateInput) { text = "Отмена" };
            dialogCancelBtn.AddToClassList("programmator-dialog-btn-cancel");
            dialogButtons.Add(dialogCancelBtn);

            var dialogConfirmBtn = new Button(() => CreateNewProgram(_createInput.value)) { text = "Создать" };
            dialogConfirmBtn.AddToClassList("programmator-dialog-btn-confirm");
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
                cell.AddToClassList("programmator-cell-highlighted");
            }
            else
            {
                cell.RemoveFromClassList("programmator-cell-highlighted");
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
            if (selected)
            {
                cell.AddToClassList("programmator-cell-selected");
            }
            else
            {
                cell.RemoveFromClassList("programmator-cell-selected");
            }
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
                row.AddToClassList("programmator-list-item");
                var nameLabel = new Label(item.Name);
                nameLabel.AddToClassList("programmator-list-item-name");
                row.Add(nameLabel);

                var delBtn = new Button(() => DeleteProgram(idx)) { text = "\u00d7" };
                delBtn.AddToClassList("programmator-list-item-delete");
                row.Add(delBtn);

                row.RegisterCallback<ClickEvent>(_ => OpenProgram(idx));

                _listScroll.Add(row);
            }
        }

        private void RunProgram()
        {
            _isRunning = true;
            _runBtn.SetEnabled(false);
            _stopBtn.SetEnabled(true);
            _panel.AddToClassList("programmator-panel-running");
            Debug.Log("[Programmator] Program running");
        }

        private void StopProgram()
        {
            _isRunning = false;
            _runBtn.SetEnabled(true);
            _stopBtn.SetEnabled(false);
            _panel.RemoveFromClassList("programmator-panel-running");
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

            cell.RemoveFromClassList("programmator-cell-empty");
            cell.RemoveFromClassList("programmator-cell-unknown");

            var tex = ProgrammatorTextureRegistry.GetTexture(action);
            if (tex != null)
            {
                cell.style.backgroundImage = new StyleBackground(tex);
                cell.style.backgroundSize = new BackgroundSize(tex.width, tex.height);
                cell.style.backgroundColor = StyleKeyword.None;
                label.text = string.Empty;
            }
            else if (id == 0)
            {
                cell.style.backgroundImage = null;
                cell.AddToClassList("programmator-cell-empty");
                label.text = string.Empty;
            }
            else
            {
                cell.style.backgroundImage = null;
                cell.AddToClassList("programmator-cell-unknown");
                string name = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? n : string.Empty;
                label.text = name;
            }

            UpdateInputFields(row, col, idx);
        }

        private static bool NeedsTextInput(ProgAction action)
        {
            return action == ProgAction.Label
                || action == ProgAction.Goto
                || action == ProgAction.Call
                || action == ProgAction.CallArg
                || action == ProgAction.CallState
                || action == ProgAction.YesNoGoto
                || action == ProgAction.NoYesGoto
                || action == ProgAction.CallWhenDied
                || action == ProgAction.DebugPause
                || action == ProgAction.DebugShow
                || action == ProgAction.WriteStateToVar
                || action == ProgAction.ReadVarToState
                || (action >= ProgAction.AddStateToVar && action <= ProgAction.SubStateToVar)
                || (action >= ProgAction.VarLessThanState && action <= ProgAction.VarNotEqualsState)
                || (action >= ProgAction.VarGreaterThanNumber && action <= ProgAction.VarNotEqualsNumber)
                || (action >= ProgAction.VarRound && action <= ProgAction.VarFloor)
                || (action >= ProgAction.SetNumberToVar && action <= ProgAction.SubNumberToVar)
                || (action >= ProgAction.AddVarToVar && action <= ProgAction.SubVarToVar);
        }

        private static bool NeedsNumInput(ProgAction action)
        {
            return (action >= ProgAction.VarGreaterThanNumber && action <= ProgAction.VarNotEqualsNumber)
                || (action >= ProgAction.SetNumberToVar && action <= ProgAction.SubNumberToVar);
        }

        private static bool NeedsTextInput2(ProgAction action)
        {
            return action >= ProgAction.AddVarToVar && action <= ProgAction.SubVarToVar;
        }

        private static readonly string[] TextPosClasses =
        {
            "programmator-cell-input-pos-label",
            "programmator-cell-input-pos-goto",
            "programmator-cell-input-pos-gosub",
            "programmator-cell-input-pos-if",
            "programmator-cell-input-pos-debug",
            "programmator-cell-input-pos-var",
            "programmator-cell-input-pos-writestate",
            "programmator-cell-input-pos-readstate",
            "programmator-cell-input-pos-state",
            "programmator-cell-input-pos-compare",
            "programmator-cell-input-pos-round",
            "programmator-cell-input-pos-setnum",
            "programmator-cell-input-pos-varop1",
        };

        private static readonly string[] Text2PosClasses =
        {
            "programmator-cell-input-pos-varop2",
        };

        private static readonly string[] NumPosClasses =
        {
            "programmator-cell-num-pos-var",
            "programmator-cell-num-pos-setnum",
        };

        private static readonly string[] TextSizeClasses =
        {
            "programmator-cell-input-sz-label",
            "programmator-cell-input-sz-goto",
            "programmator-cell-input-sz-gosub",
            "programmator-cell-input-sz-if",
            "programmator-cell-input-sz-debug",
            "programmator-cell-input-sz-var",
            "programmator-cell-input-sz-writestate",
            "programmator-cell-input-sz-readstate",
            "programmator-cell-input-sz-state",
            "programmator-cell-input-sz-compare",
            "programmator-cell-input-sz-round",
            "programmator-cell-input-sz-setnum",
            "programmator-cell-input-sz-varop1",
        };

        private static readonly string[] Text2SizeClasses =
        {
            "programmator-cell-input-sz-varop2",
        };

        private static readonly string[] NumSizeClasses =
        {
            "programmator-cell-num-sz-var",
            "programmator-cell-num-sz-setnum",
        };

        private static string GetTextInputPositionClass(ProgAction action)
        {
            switch (action)
            {
                case ProgAction.Label:          return "programmator-cell-input-pos-label";
                case ProgAction.Goto:           return "programmator-cell-input-pos-goto";
                case ProgAction.Call:
                case ProgAction.CallArg:
                case ProgAction.CallState:      return "programmator-cell-input-pos-gosub";
                case ProgAction.YesNoGoto:
                case ProgAction.NoYesGoto:
                case ProgAction.CallWhenDied:   return "programmator-cell-input-pos-if";
                case ProgAction.DebugPause:
                case ProgAction.DebugShow:      return "programmator-cell-input-pos-debug";
                case ProgAction.WriteStateToVar: return "programmator-cell-input-pos-writestate";
                case ProgAction.ReadVarToState: return "programmator-cell-input-pos-readstate";
                case ProgAction.AddStateToVar:
                case ProgAction.MultStateToVar:
                case ProgAction.DivStateToVar:
                case ProgAction.SubStateToVar:  return "programmator-cell-input-pos-state";
                case ProgAction.VarLessThanState:
                case ProgAction.VarGreaterThanState:
                case ProgAction.VarGreaterThanOrEqualsState:
                case ProgAction.VarLessThanOrEqualState:
                case ProgAction.VarEqualsState:
                case ProgAction.VarNotEqualsState: return "programmator-cell-input-pos-compare";
                case ProgAction.VarRound:
                case ProgAction.VarCeil:
                case ProgAction.VarFloor:       return "programmator-cell-input-pos-round";
                case ProgAction.SetNumberToVar:
                case ProgAction.AddNumberToVar:
                case ProgAction.MultNumberToVar:
                case ProgAction.DivNumberToVar:
                case ProgAction.SubNumberToVar: return "programmator-cell-input-pos-setnum";
                case ProgAction.AddVarToVar:
                case ProgAction.MultVarToVar:
                case ProgAction.DivVarToVar:
                case ProgAction.SubVarToVar:    return "programmator-cell-input-pos-varop1";
                default:                        return "programmator-cell-input-pos-var";
            }
        }

        private static string GetTextInput2PositionClass(ProgAction action)
        {
            switch (action)
            {
                case ProgAction.AddVarToVar:
                case ProgAction.MultVarToVar:
                case ProgAction.DivVarToVar:
                case ProgAction.SubVarToVar:    return "programmator-cell-input-pos-varop2";
                default:                        return "programmator-cell-input-pos-varop2";
            }
        }

        private static string GetNumInputPositionClass(ProgAction action)
        {
            switch (action)
            {
                case ProgAction.SetNumberToVar:
                case ProgAction.AddNumberToVar:
                case ProgAction.MultNumberToVar:
                case ProgAction.DivNumberToVar:
                case ProgAction.SubNumberToVar: return "programmator-cell-num-pos-setnum";
                default:                        return "programmator-cell-num-pos-var";
            }
        }

        private static string GetTextInputSizeClass(ProgAction action)
        {
            switch (action)
            {
                case ProgAction.Label:          return "programmator-cell-input-sz-label";
                case ProgAction.Goto:           return "programmator-cell-input-sz-goto";
                case ProgAction.Call:
                case ProgAction.CallArg:
                case ProgAction.CallState:      return "programmator-cell-input-sz-gosub";
                case ProgAction.YesNoGoto:
                case ProgAction.NoYesGoto:
                case ProgAction.CallWhenDied:   return "programmator-cell-input-sz-if";
                case ProgAction.DebugPause:
                case ProgAction.DebugShow:      return "programmator-cell-input-sz-debug";
                case ProgAction.WriteStateToVar: return "programmator-cell-input-sz-writestate";
                case ProgAction.ReadVarToState: return "programmator-cell-input-sz-readstate";
                case ProgAction.AddStateToVar:
                case ProgAction.MultStateToVar:
                case ProgAction.DivStateToVar:
                case ProgAction.SubStateToVar:  return "programmator-cell-input-sz-state";
                case ProgAction.VarLessThanState:
                case ProgAction.VarGreaterThanState:
                case ProgAction.VarGreaterThanOrEqualsState:
                case ProgAction.VarLessThanOrEqualState:
                case ProgAction.VarEqualsState:
                case ProgAction.VarNotEqualsState: return "programmator-cell-input-sz-compare";
                case ProgAction.VarRound:
                case ProgAction.VarCeil:
                case ProgAction.VarFloor:       return "programmator-cell-input-sz-round";
                case ProgAction.SetNumberToVar:
                case ProgAction.AddNumberToVar:
                case ProgAction.MultNumberToVar:
                case ProgAction.DivNumberToVar:
                case ProgAction.SubNumberToVar: return "programmator-cell-input-sz-setnum";
                case ProgAction.AddVarToVar:
                case ProgAction.MultVarToVar:
                case ProgAction.DivVarToVar:
                case ProgAction.SubVarToVar:    return "programmator-cell-input-sz-varop1";
                default:                        return "programmator-cell-input-sz-var";
            }
        }

        private static string GetTextInput2SizeClass(ProgAction action)
        {
            switch (action)
            {
                case ProgAction.AddVarToVar:
                case ProgAction.MultVarToVar:
                case ProgAction.DivVarToVar:
                case ProgAction.SubVarToVar:    return "programmator-cell-input-sz-varop2";
                default:                        return "programmator-cell-input-sz-varop2";
            }
        }

        private static string GetNumInputSizeClass(ProgAction action)
        {
            switch (action)
            {
                case ProgAction.SetNumberToVar:
                case ProgAction.AddNumberToVar:
                case ProgAction.MultNumberToVar:
                case ProgAction.DivNumberToVar:
                case ProgAction.SubNumberToVar: return "programmator-cell-num-sz-setnum";
                default:                        return "programmator-cell-num-sz-var";
            }
        }

        private void UpdateInputFields(int row, int col, int idx)
        {
            var action = (ProgAction)ProgrammatorData.Codes[idx];
            var textInput = _cellTextInputs[row, col];
            var textInput2 = _cellTextInputs2[row, col];
            var numInput = _cellNumInputs[row, col];

            bool showText = NeedsTextInput(action);
            bool showText2 = NeedsTextInput2(action);
            bool showNum = NeedsNumInput(action);

            if (showText)
            {
                foreach (var cls in TextPosClasses)
                    textInput.RemoveFromClassList(cls);
                foreach (var cls in TextSizeClasses)
                    textInput.RemoveFromClassList(cls);
                textInput.AddToClassList(GetTextInputPositionClass(action));
                textInput.AddToClassList(GetTextInputSizeClass(action));
                textInput.SetValueWithoutNotify(ProgrammatorData.Labels[idx] ?? string.Empty);
                textInput.style.display = DisplayStyle.Flex;
            }
            else
            {
                textInput.style.display = DisplayStyle.None;
            }

            if (showText2)
            {
                foreach (var cls in Text2PosClasses)
                    textInput2.RemoveFromClassList(cls);
                foreach (var cls in Text2SizeClasses)
                    textInput2.RemoveFromClassList(cls);
                textInput2.AddToClassList(GetTextInput2PositionClass(action));
                textInput2.AddToClassList(GetTextInput2SizeClass(action));
                textInput2.SetValueWithoutNotify(ProgrammatorData.Values[idx] ?? string.Empty);
                textInput2.style.display = DisplayStyle.Flex;
            }
            else
            {
                textInput2.style.display = DisplayStyle.None;
            }

            if (showNum)
            {
                foreach (var cls in NumPosClasses)
                    numInput.RemoveFromClassList(cls);
                foreach (var cls in NumSizeClasses)
                    numInput.RemoveFromClassList(cls);
                numInput.AddToClassList(GetNumInputPositionClass(action));
                numInput.AddToClassList(GetNumInputSizeClass(action));
                numInput.SetValueWithoutNotify(ProgrammatorData.Values[idx] ?? string.Empty);
                numInput.style.display = DisplayStyle.Flex;
            }
            else
            {
                numInput.style.display = DisplayStyle.None;
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
