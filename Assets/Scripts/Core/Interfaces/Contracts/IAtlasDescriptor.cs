#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Neutral view over a terrain atlas. Implemented by <c>TextureAtlas</c>;
    /// keeps the rendering implementation out of the contracts layer.
    /// </summary>
    public interface IAtlasDescriptor
    {
        Texture2D? Texture { get; }

        int Size { get; }

        bool ContainsCell(CellType cellType);
    }
}
