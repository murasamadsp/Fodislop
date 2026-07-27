using System;
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
        private Button _prevBtn;
        private Button _nextBtn;
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
                        HighlightCell(row, col, true);
                        ShowCellTooltip(row, col);
                    });
                    cell.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        if (ProgrammatorData.HoveredCell == (row * ProgrammatorData.COLS) + col)
                        {
                            HighlightCell(row, col, false);
                            ProgrammatorData.HoveredCell = -1;
                        }

                        _tooltip?.Hide();
                    });

                    cell.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        _tooltip?.UpdatePosition(evt.position);
                    });

                    cell.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0 && evt.button != 1)
                        {
                            return;
                        }

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
                _radial.Hide();
                _joystick.Hide();
                _radialShown = false;
                ProgrammatorData.CurrentPage++;
                RefreshAllCells();
            }
        }

        private void AddPageClick()
        {
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

            // DEL clears the cell when radial menu is open
            if (Keyboard.current.deleteKey.wasPressedThisFrame && _radialShown)
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

            // ESC closes the programmator window
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            // Ctrl+Z / Ctrl+Y undo/redo
            if (Keyboard.current.ctrlKey.isPressed)
            {
                if (Keyboard.current.zKey.wasPressedThisFrame)
                {
                    if (ProgrammatorData.Undo())
                    {
                        RefreshAllCells();
                    }
                }
                else if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    if (ProgrammatorData.Redo())
                    {
                        RefreshAllCells();
                    }
                }

                return;
            }

            // Arrow keys for page navigation (only when radial menu is hidden)
            if (!_radialShown)
            {
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
                {
                    PrevPage();
                }
                else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
                {
                    NextPage();
                }
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
