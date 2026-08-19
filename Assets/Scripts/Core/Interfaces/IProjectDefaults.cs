#nullable enable

namespace Fodinae.Core.Interfaces;

public interface IProjectDefaults
{
    int SchemaVersion { get; }

    string ContentHash { get; }

    ClientDefaultsSnapshot Client { get; }

    LightingDefaultsSnapshot Lighting { get; }

    ShaderDefaultsSnapshot Shaders { get; }
}
