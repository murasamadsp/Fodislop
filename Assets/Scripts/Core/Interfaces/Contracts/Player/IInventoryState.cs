#nullable enable

using Fodinae.Core.Models;

namespace Fodinae.Core.Interfaces;

public interface IInventoryState
{
    int SelectedSlot { get; }
    ItemData? GetSlot(int index);
    void SetSlot(int index, ItemData? item);
    void ClearSelection();
}
