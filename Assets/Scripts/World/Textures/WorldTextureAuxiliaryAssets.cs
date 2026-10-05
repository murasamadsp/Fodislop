#nullable enable

using System;
using Kern;
using Kern.Core.Interfaces;
using Kern.World.Textures;
using UnityEngine;

namespace Kern.World
{
    /// <summary>Owns generated flow maps and asynchronous terrain decal atlases.</summary>
    internal sealed class WorldTextureAuxiliaryAssets : IDisposable
    {
        private readonly TerrainDecalAtlasLoader _decalLoader = new(
            "terrain-decals.png",
            "load_terrain_decal_atlas");
        private readonly TerrainDecalAtlasLoader _decalRockLoader = new(
            "terrain-decals-rock.png",
            "load_terrain_decal_rock_atlas");

        private Texture2D? _prismaticFlowMapTexture;
        private Texture2D? _flowMapTexture;

        public Texture2D? PrismaticFlowMapTexture => _prismaticFlowMapTexture;
        public Texture2D? FlowMapTexture => _flowMapTexture;
        public Texture2D? TerrainDecalAtlasTexture => _decalLoader.AtlasTexture;
        public Texture2D? TerrainDecalRockAtlasTexture => _decalRockLoader.AtlasTexture;

        public void Initialize(
            ITextureStorageService textureStorage,
            IAsyncOperationSupervisor operations,
            Action<string, Texture2D> notifyTextureLoaded)
        {
            _prismaticFlowMapTexture = WorldTextureGenerator.CreatePrismaticFlowMap();
            RegenerateFlowMap();
            _decalLoader.StartLoad(textureStorage, operations, notifyTextureLoaded);
            _decalRockLoader.StartLoad(textureStorage, operations, notifyTextureLoaded);
        }

        public void RegenerateFlowMap()
        {
            DestroyTexture(_flowMapTexture);
            _flowMapTexture = WorldTextureGenerator.CreateFlowMap();
        }

        public void Dispose()
        {
            DestroyTexture(_flowMapTexture);
            _flowMapTexture = null;

            DestroyTexture(_prismaticFlowMapTexture);
            _prismaticFlowMapTexture = null;

            _decalLoader.Dispose();
            _decalRockLoader.Dispose();
        }

        private static void DestroyTexture(Texture2D? texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
