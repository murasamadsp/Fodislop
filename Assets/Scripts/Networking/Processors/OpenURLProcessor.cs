#nullable enable

using MinesServer.Networking.Server.Packets.Utilities;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class OpenURLProcessor : IPacketProcessor<OpenURLPacket>
    {
        public void Process(OpenURLPacket packet)
        {
            Application.OpenURL(packet.URL);
        }
    }
}
