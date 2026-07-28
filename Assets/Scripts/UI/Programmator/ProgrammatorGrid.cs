using System;
using System.Collections.Generic;
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
        private bool _radialShown;
        private int _radialCellIndex = -1;
        private Tooltip _tooltip;
        private Label _pageLabel;
        private IntegerField _pageInput;
        private Button _prevBtn;
        private Button _nextBtn;
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

            var panel = new VisualElement();
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
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.minWidth = 584;
            panel.style.minHeight = 520;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.marginBottom = 10;
            topRow.style.alignItems = Align.Center;

            var title = new Label("Программатор");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            title.style.flexGrow = 1;
            topRow.Add(title);

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
            topRow.Add(_prevBtn);

            _pageLabel = new Label("Стр. 1/1");
            _pageLabel.style.fontSize = 12;
            _pageLabel.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            _pageLabel.style.minWidth = 60;
            _pageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _pageLabel.style.marginLeft = 4;
            _pageLabel.style.marginRight = 4;
            topRow.Add(_pageLabel);

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
            topRow.Add(_nextBtn);

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
            topRow.Add(_pageInput);

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
            topRow.Add(addPageBtn);

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
            topRow.Add(removePageBtn);

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
            topRow.Add(shiftUpBtn);

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
            topRow.Add(shiftDownBtn);

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
            topRow.Add(shiftLeftBtn);

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
            topRow.Add(shiftRightBtn);

            var closeBtn = new Button(() => Hide());
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
            topRow.Add(closeBtn);

            panel.Add(topRow);

            var gridScroll = new ScrollView();
            gridScroll.style.flexGrow = 1;
            gridScroll.style.maxHeight = ProgrammatorData.ROWS * (CELLSIZE + (CELL_GAP * 2));

            _gridContainer = new VisualElement();
            _gridContainer.style.flexDirection = FlexDirection.Row;
            _gridContainer.style.flexWrap = Wrap.Wrap;
            _gridContainer.style.width = ProgrammatorData.COLS * (CELLSIZE + (CELL_GAP * 2));

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
            panel.Add(gridScroll);

            _popup.Add(panel);
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

            // ESC closes the programmator window or clears selection
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
            RefreshAllCells();
            _popup.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            ClearSelection();
            _joystick.Hide();
            _radial.Hide();
            _radialShown = false;
            _radialCellIndex = -1;
            _isOpen = false;
            IsOpen = false;
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
