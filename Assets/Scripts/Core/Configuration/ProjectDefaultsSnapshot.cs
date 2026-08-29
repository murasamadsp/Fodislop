#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core;

public sealed class ProjectDefaultsSnapshot : IProjectDefaults
{
    public ProjectDefaultsSnapshot(
        int schemaVersion,
        string contentHash,
        ClientDefaultsSnapshot client,
        LightingDefaultsSnapshot lighting,
        ShaderDefaultsSnapshot shaders)
    {
        SchemaVersion = schemaVersion;
        ContentHash = contentHash;
        Client = client;
        Lighting = lighting;
        Shaders = shaders;
    }

    public int SchemaVersion { get; }

    public string ContentHash { get; }

    public ClientDefaultsSnapshot Client { get; }

    public LightingDefaultsSnapshot Lighting { get; }

    public ShaderDefaultsSnapshot Shaders { get; }
}
