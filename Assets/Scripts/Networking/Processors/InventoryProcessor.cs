#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Models;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Inventory;

namespace Kern.Networking.Processors;

public sealed class InventoryProcessor(IInventoryState model) :
    IPacketProcessor<InventoryPacket>,
    IPacketProcessor<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>,
    IPacketProcessor<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>
{
    private const int TotalSlots = 63;

    public void Process(InventoryPacket packet)
    {
        Dictionary<ItemType, long> remaining = new(packet.Changes);

        for (int i = 0; i < TotalSlots; i++)
        {
            var existing = model.GetSlot(i);
            if (existing == null || !remaining.TryGetValue(existing.ItemType, out long quantity))
            {
                continue;
            }

            if (quantity <= 0)
            {
                model.SetSlot(i, null);
            }
            else
            {
                ItemData updated = existing.Clone();
                updated.Quantity = (int)quantity;
                model.SetSlot(i, updated);
            }

            remaining.Remove(existing.ItemType);
        }

        foreach ((ItemType itemType, long quantity) in remaining)
        {
            if (quantity <= 0)
            {
                continue;
            }

            for (int i = 0; i < TotalSlots; i++)
            {
                if (model.GetSlot(i) != null)
                {
                    continue;
                }

                model.SetSlot(i, new Kern.Core.Models.ItemData(
                    itemType.ToString(),
                    UnityEngine.Color.gray,
                    (int)quantity)
                {
                    ItemType = itemType,
                });
                break;
            }
        }
    }

    public void Process(MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket packet)
    {
        int slot = model.SelectedSlot;
        if (slot < 0)
        {
            return;
        }

        var item = model.GetSlot(slot);
        if (item == null)
        {
            return;
        }

        ItemData updated = item.Clone();
        updated.Name = packet.Name;
        updated.Description = packet.Description;
        model.SetSlot(slot, updated);
    }

    public void Process(MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket packet) =>
        model.ClearSelection();
}
