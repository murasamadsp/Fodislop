#nullable enable

using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class AudioPacketProcessor : IPacketProcessor<AudioPacket>
    {
        private readonly ISessionContainer _session;

        public AudioPacketProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(AudioPacket packet)
        {
            var mgr = _session.TryResolve<IServerAudioService>();
            mgr?.PlayEffect(packet);
        }
    }
}
