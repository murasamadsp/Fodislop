#nullable enable

using System;
using System.Linq;
using Fodinae;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class StringDropdownPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement? Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not StringDropdownPacket strDropPkt)
            {
                return null;
            }

            var strOptions = strDropPkt.Values.ToList();
            if (strOptions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"String dropdown '{strDropPkt.Name}' has no options.");
            }

            var defaultValue = strDropPkt.DefaultValue;
            if (!strOptions.Contains(defaultValue))
            {
                throw new InvalidOperationException(
                    $"String dropdown '{strDropPkt.Name}' default '{defaultValue}' " +
                    "is not present in its options.");
            }

            var strDrop = new DropdownField(strOptions, 0)
            {
                value = defaultValue,
            };
            strDrop.SetEnabled(strDropPkt.IsEnabled);
            return strDrop;
        }
    }
}
