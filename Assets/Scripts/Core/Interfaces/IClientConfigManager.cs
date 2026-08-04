#nullable enable

using Fodinae.Core;

namespace Fodinae.Core.Interfaces
{
    public interface IClientConfigManager
    {
        ClientConfig Config { get; }
        void Load();
        void Save();
        void ApplyDefaults();
    }
}
