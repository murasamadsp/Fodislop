#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Core.Interfaces
{
    public interface ITextureService
    {
        event Action<string, Texture2D> OnTextureLoaded;
        void RequestTexture(CellType cellType);
        AtlasCoordinate GetCellTextureCoordinate(CellType cellType);
        List<TextureAtlas> GetAllAtlases();
        void Clear();
    }
}
