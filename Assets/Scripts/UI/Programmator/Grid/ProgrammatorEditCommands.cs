#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Kern.UI.Programmator;

// Правка программы: отмена, повтор, буфер обмена, удаление выделенного.
// Одна дорога для клавиш (Ctrl+Z/Y/C/X/V, Del — ProgrammatorGrid.Tick) и
// для панели инструментов сенсорного экрана, где клавиатуры нет.
internal sealed class ProgrammatorEditCommands
{
    private readonly ProgrammatorData _data;
    private readonly ProgrammatorSelectionModel _selection;
    private readonly ProgrammatorClipboardController _clipboard;
    private readonly ProgrammatorProgramStore _programs;
    private readonly ProgrammatorGridUIFactory _view;

    public ProgrammatorEditCommands(
        ProgrammatorData data,
        ProgrammatorSelectionModel selection,
        ProgrammatorClipboardController clipboard,
        ProgrammatorProgramStore programs,
        ProgrammatorGridUIFactory view)
    {
        _data = data;
        _selection = selection;
        _clipboard = clipboard;
        _programs = programs;
        _view = view;
    }

    public void Undo()
    {
        if (_data.Undo())
        {
            _programs.RefreshAllCells();
        }
    }

    public void Redo()
    {
        if (_data.Redo())
        {
            _programs.RefreshAllCells();
        }
    }

    public void Copy()
    {
        if (_selection.HasAnySelection())
        {
            _clipboard.CopySelection();
        }
    }

    public void Cut()
    {
        if (_selection.HasAnySelection())
        {
            _clipboard.CutSelection();
        }
    }

    public void Paste()
    {
        if (_clipboard.HasClipboard)
        {
            _clipboard.PasteClipboard();
        }
    }

    // Очистка выделенного: набор отдельных клеток или прямоугольник.
    public void DeleteSelection()
    {
        if (_selection.SelectedCells.Count > 0)
        {
            _data.PushUndo();
            foreach (long key in _selection.SelectedCells)
            {
                int r = (int)(key / ProgrammatorData.COLS);
                int c = (int)(key % ProgrammatorData.COLS);
                ClearCell(r, c);
            }

            _selection.SelectedCells.Clear();
            _selection.HasSelection = false;
            return;
        }

        if (!_selection.HasSelection)
        {
            return;
        }

        _data.PushUndo();
        int minRow = Mathf.Min(_selection.SelStartRow, _selection.SelEndRow);
        int maxRow = Mathf.Max(_selection.SelStartRow, _selection.SelEndRow);
        int minCol = Mathf.Min(_selection.SelStartCol, _selection.SelEndCol);
        int maxCol = Mathf.Max(_selection.SelStartCol, _selection.SelEndCol);
        for (int r = minRow; r <= maxRow; r++)
        {
            for (int c = minCol; c <= maxCol; c++)
            {
                ClearCell(r, c);
            }
        }
    }

    private void ClearCell(int row, int col)
    {
        int index = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE) + (row * ProgrammatorData.COLS) + col;
        _data.Codes[index] = 0;
        _view.UpdateCell(row, col);
    }
}
