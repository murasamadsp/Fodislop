#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public class TextPacketBuilder : PacketUIBuilderBase<TextPacket>
{
    protected override VisualElement BuildTyped(TextPacket packet, PacketUIBuilder builder)
    {
        string? imageUri = AttachedProperties.Find(packet, "PacketUI.ImageURI");
        if (imageUri != null)
        {
            return ImagePacketBuilder.BuildUriImage(imageUri, builder);
        }

        if (!string.IsNullOrEmpty(packet.OnClickContext))
        {
            var button = new Button { text = packet.Text };
            button.AddToClassList("packet-window-action");
            if (AttachedProperties.Has(packet, "PacketUI.FooterAction"))
            {
                button.AddToClassList("packet-window-footer-action");
            }

            if (AttachedProperties.Has(packet, "PacketUI.ExitAction"))
            {
                button.AddToClassList("packet-window-exit");
            }

            return button;
        }

        var label = new Label(packet.Text);
        label.AddToClassList(AttachedProperties.Has(packet, "PacketUI.Title")
            ? "sci-fi-text-title"
            : "sci-fi-text-body");
        label.AddToClassList("fit-wrap");
        return label;
    }
}
