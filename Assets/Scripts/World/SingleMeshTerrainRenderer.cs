#nullable enable

using UnityEngine;

namespace Fodinae.World
{
    // Scene-compatibility shim: the scene's SingleMeshTerrainRenderer component references this
    // class by GUID; all renderer logic lives in the base TerrainRenderer.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S2094:Classes should not be empty",
        Justification = "Scene compatibility shim — keeps the serialized component GUID valid")]
    public class SingleMeshTerrainRenderer : Fodinae.World.Terrain.TerrainRenderer
    {
    }
}
