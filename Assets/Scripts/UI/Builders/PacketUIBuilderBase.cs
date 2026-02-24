using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public abstract class PacketUIBuilderBase
    {
        public abstract VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder);
    }
}
