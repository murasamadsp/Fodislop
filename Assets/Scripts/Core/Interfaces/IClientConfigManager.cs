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
        void SetCustomGraphicsSettings(GraphicsQualitySettings settings);
        void Load();
        void Save();

        /// <summary>
        /// Forces the config to load synchronously if it has not already.
        /// Safe to call immediately after Resolve, before Start() would have run.
        /// </summary>
        void EnsureInitialized();
    }
}
