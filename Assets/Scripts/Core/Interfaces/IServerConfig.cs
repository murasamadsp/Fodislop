#nullable enable

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IServerConfig
    {
        float DigCooldown { get; }
        int MaxGlobalChatLength { get; }
        int MaxLocalChatLength { get; }
    }
}
