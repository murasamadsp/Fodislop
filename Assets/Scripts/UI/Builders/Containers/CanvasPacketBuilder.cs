#nullable enable

using System;
using System.Globalization;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public class CanvasPacketBuilder : PacketUIBuilderBase<CanvasPacket>
{
    protected override VisualElement BuildTyped(CanvasPacket packet, PacketUIBuilder builder)
    {
        var element = new VisualElement();
        element.AddToClassList("rel");
        string? height = AttachedProperties.Find(packet, "PacketUI.CanvasHeight");
        if (height != null)
        {
            if (!float.TryParse(height, NumberStyles.Integer, CultureInfo.InvariantCulture, out float parsed) ||
                parsed <= 0f)
            {
                throw new InvalidOperationException($"[PacketUI] Invalid canvas height '{height}'.");
            }

            element.style.height = parsed;
        }

        builder.AddChildren(element, packet);
        return element;
    }
}
