#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.UI.Programmator;

// Thin lifecycle owner for the programmator popup. UI construction, cell
// rendering, selection, clipboard, radial menu, and program storage each
// live in their own focused type; this class only wires them together once
// and dispatches input.
public sealed class ProgrammatorGrid : IDisposable
    {
        private readonly UIDocument _doc;

        private ProgrammatorGridUIFactory? _view;
        private ProgrammatorSelectionModel? _selection;
        private ProgrammatorRadialController? _radial;
        private ProgrammatorProgramStore? _programs;
        private ProgrammatorClipboardController? _clipboard;

        private bool _isOpen;

        public static bool IsOpen { get; private set; }

        private bool _uiBuilt;

        public ProgrammatorGrid(UIDocument doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public void Initialize()
        {
            TryBuildUI();
        }

        private void TryBuildUI()
        {
            if (_uiBuilt)
            {
                return;
            }

            if (_doc == null)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            var view = new ProgrammatorGridUIFactory(_doc);
            var selection = new ProgrammatorSelectionModel(view.SetSelectionBorder);
            var radial = new ProgrammatorRadialController(_doc, view.UpdateCell);
            var programs = new ProgrammatorProgramStore(view, selection, radial);
            radial.OnLastCellPlaced = programs.AdvancePageIfAtEnd;
            var clipboard = new ProgrammatorClipboardController(selection, view.UpdateCell);

            view.Build(selection, radial, programs, clipboard, Hide);

            _view = view;
            _selection = selection;
            _radial = radial;
            _programs = programs;
            _clipboard = clipboard;

            if (_view == null)
            {
                return;
            }

            _view.Popup.style.display = DisplayStyle.None;
            _uiBuilt = true;
        }

        public void Tick()
        {
            if (!_uiBuilt)
            {
                TryBuildUI();
                if (!_uiBuilt)
                {
                    return;
                }
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (!_isOpen)
            {
                if ((Keyboard.current.pKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame) &&
                    !ChatInput.IsFocused &&
                    !PauseMenu.IsMenuOpen)
                {
                    Show();
                }

                return;
            }

            if ((Keyboard.current.pKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame) &&
                !_radial!.IsShown)
            {
                Hide();
                return;
            }

            // ESC closes the programmator or goes back to list
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_selection!.HasSelection && !_radial!.IsShown)
                {
                    _selection.ClearSelection();
                    return;
                }

                if (_selection.SelectedCells.Count > 0 && !_radial!.IsShown)
                {
                    _selection.ClearSelection();
                    return;
                }

                if (_view!.Panel.style.display == DisplayStyle.Flex)
                {
                    _programs!.CloseProgram();
                    return;
                }

                Hide();
                return;
            }

            if (_radial!.IsShown)
            {
                // DEL clears the cell when radial menu is open
                if (Keyboard.current.deleteKey.wasPressedThisFrame)
                {
                    _radial.HandleDeleteKeyWhileShown();
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
                    {
                        _programs!.RefreshAllCells();
                    }
                }
                else if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    if (ProgrammatorData.Redo())
                    {
                        _programs!.RefreshAllCells();
                    }
                }
                else if (Keyboard.current.cKey.wasPressedThisFrame && _selection!.HasAnySelection())
                {
                    _clipboard!.CopySelection();
                }
                else if (Keyboard.current.xKey.wasPressedThisFrame && _selection!.HasAnySelection())
                {
                    _clipboard!.CutSelection();
                }
                else if (Keyboard.current.vKey.wasPressedThisFrame && _clipboard!.HasClipboard)
                {
                    _clipboard.PasteClipboard();
                }

                return;
            }

            // DEL clears selected cells
            if (Keyboard.current.deleteKey.wasPressedThisFrame)
            {
                if (!_selection!.HasAnySelection())
                { /* fall through */
                }
                else if (_selection.SelectedCells.Count > 0)
                {
                    ProgrammatorData.PushUndo();
                    foreach (long key in _selection.SelectedCells)
                    {
                        int r = (int)(key / ProgrammatorData.COLS);
                        int c = (int)(key % ProgrammatorData.COLS);
                        int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                  + (r * ProgrammatorData.COLS) + c;
                        ProgrammatorData.Codes[idx] = 0;
                        _view!.UpdateCell(r, c);
                    }

                    _selection.SelectedCells.Clear();
                    _selection.HasSelection = false;
                    return;
                }
                else if (_selection.HasSelection)
                {
                    ProgrammatorData.PushUndo();
                    int minRow = Mathf.Min(_selection.SelStartRow, _selection.SelEndRow);
                    int maxRow = Mathf.Max(_selection.SelStartRow, _selection.SelEndRow);
                    int minCol = Mathf.Min(_selection.SelStartCol, _selection.SelEndCol);
                    int maxCol = Mathf.Max(_selection.SelStartCol, _selection.SelEndCol);
                    for (int r = minRow; r <= maxRow; r++)
                    {
                        for (int c = minCol; c <= maxCol; c++)
                        {
                            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                      + (r * ProgrammatorData.COLS) + c;
                            ProgrammatorData.Codes[idx] = 0;
                            _view!.UpdateCell(r, c);
                        }
                    }

                    return;
                }
            }

            // Arrow keys for page navigation
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                _programs!.PrevPage();
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                _programs!.NextPage();
            }
        }

        public void Show()
        {
            if (!_uiBuilt)
            {
                TryBuildUI();
            }

            if (!_uiBuilt || _view == null)
            {
                // UI ещё не готов (DI-инъекция не завершилась) — кнопка просто не
                // открывает программатор в этот раз; TryBuildUI ретраится из Update.
                return;
            }

            _isOpen = true;
            IsOpen = true;
            _view.Popup.style.display = DisplayStyle.Flex;
            _programs!.ShowProgramList();
        }

        public void Hide()
        {
            if (_programs!.IsRunning)
            {
                _programs.StopProgram();
            }

            _selection!.ClearSelection();
            _radial!.HideAll();
            _isOpen = false;
            IsOpen = false;
            _programs.HideCreateInput();
            _view!.ProgramListPanel.style.display = DisplayStyle.None;
            _view.Panel.style.display = DisplayStyle.None;
            _view.Popup.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            if (_uiBuilt && _isOpen)
            {
                Hide();
            }

            IsOpen = false;
        }
}
