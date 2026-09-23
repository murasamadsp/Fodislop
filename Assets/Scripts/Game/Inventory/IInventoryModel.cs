#nullable enable

using System;
using MinesServer.Data;
using Kern.Core.Models;

namespace Kern.Game.Inventory;

/// <summary>
/// Player-facing inventory state: an ordered list of held item types (the
/// selected item sits first), matching the old client's inventory order that
/// the server grants through snapshots. Holds <see cref="ItemData"/> per type
/// and exposes the client-to-server selection commands.
/// </summary>
public interface IInventoryModel
{
    /// <summary>Raised when the held set, quantities, or display order change.</summary>
    event Action? OnItemsChanged;

    /// <summary>Raised when the selected item changes (null = nothing selected).</summary>
    event Action<ItemType?>? OnSelectedChanged;

    /// <summary>Held types in display order; the selected one first.</summary>
    IReadOnlyList<ItemType> OrderedTypes { get; }

    /// <summary>The currently selected type, or null.</summary>
    ItemType? SelectedItem { get; }

    /// <summary>True when an item is selected and still held with quantity &gt; 0.</summary>
    bool HasSelectedItem { get; }

    ItemData? GetItem(ItemType type);
    long GetQuantity(ItemType type);

    /// <summary>Selects a held item: moves it to the front and sends a client SelectItemPacket.</summary>
    void Select(ItemType type);

    /// <summary>Clears the selection and sends a client DeselectItemPacket.</summary>
    void Deselect();

    /// <summary>Clears the selection locally without talking to the server.</summary>
    void ClearSelection();

    /// <summary>Sends a client UseItemPacket if an item is selected and held.</summary>
    void UseSelectedItem();
}