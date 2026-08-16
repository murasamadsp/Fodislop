#nullable enable

using Fodinae.Core;
using Fodinae.Rendering;

namespace Fodinae.Core.Interfaces
{
    public interface IClientConfigManager
    {
        ClientConfig Config { get; }
        string ConfigFilePath { get; }
        GraphicsPreset SelectedGraphicsPreset { get; }
        void MarkGraphicsAsCustom();
        void SelectGraphicsPreset(GraphicsPreset preset);
        void Load();
        void Save();
    }
}
