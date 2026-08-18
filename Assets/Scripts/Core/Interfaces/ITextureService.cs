#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Core.Interfaces
{
    public interface ITextureService
    {
        event Action<string, Texture2D>? OnTextureLoaded;
        void RequestTexture(CellType cellType);
        AtlasCoordinate GetCellTextureCoordinate(CellType cellType);
        Vector4 GetCellFrameRect(CellType cellType);
        int GetAnimationFrameCount(CellType cellType);
        int GetFrameSize(CellType cellType);
        float GetAnimationSpeedForCell(CellType cellType);
        UniTask<AtlasCoordinate> GetCellTextureCoordinate(
            CellType cellType,
            int globalX,
            int globalY);
        Texture2D? FlowMapTexture { get; }
        List<TextureAtlas> GetAllAtlases();
        string GetCacheStats();
        void FlushDirtyAtlases();
        void Clear();
    }
}
