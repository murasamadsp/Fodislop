#nullable enable

using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Models;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Inventory;

namespace Fodinae.Networking.Processors
{
    public class InventoryProcessor : IPacketProcessor<InventoryPacket>, IPacketProcessor<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>, IPacketProcessor<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>
    {
        private const int TotalSlots = 60;
        private readonly IInventoryState _model;

        public InventoryProcessor(IInventoryState model)
        {
            _model = model;
        }

        public void Process(InventoryPacket packet)
        {
            Dictionary<ItemType, long> remaining = new(packet.Changes);

            for (int i = 0; i < TotalSlots; i++)
            {
                var existing = _model.GetSlot(i);
                if (existing == null || !remaining.TryGetValue(existing.ItemType, out long quantity))
                {
                    continue;
                }

                if (quantity <= 0)
                {
                    _model.SetSlot(i, null);
                }
                else
                {
                    existing.Quantity = (int)quantity;
                    _model.SetSlot(i, existing);
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
                    if (_model.GetSlot(i) != null)
                    {
                        continue;
                    }

                    _model.SetSlot(i, new Fodinae.Core.Models.ItemData(
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
            int slot = _model.SelectedSlot;
            if (slot < 0)
            {
                return;
            }

            var item = _model.GetSlot(slot);
            if (item == null)
            {
                return;
            }

            item.Name = packet.Name;
            item.Description = packet.Description;
            _model.SetSlot(slot, item);
        }

        public void Process(MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket packet)
        {
            _model.ClearSelection();
        }
    }
}
