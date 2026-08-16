#nullable enable

using System;
using System.Linq;
using Fodinae;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class IntDropdownPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement? Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not IntDropdownPacket intDropPkt)
            {
                return null;
            }

            var intOptions = intDropPkt.Values.Select(x => x.ToString()).ToList();
            if (intOptions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Integer dropdown '{intDropPkt.Name}' has no options.");
            }

            var defaultValue = intDropPkt.DefaultValue.ToString();
            if (!intOptions.Contains(defaultValue))
            {
                throw new InvalidOperationException(
                    $"Integer dropdown '{intDropPkt.Name}' default '{defaultValue}' " +
                    "is not present in its options.");
            }

            var intDrop = new DropdownField(intOptions, 0)
            {
                value = defaultValue,
            };
            intDrop.SetEnabled(intDropPkt.IsEnabled);
            return intDrop;
        }
    }
}
