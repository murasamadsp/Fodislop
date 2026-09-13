#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Core.Interfaces;
public interface IAtlasDescriptor
{
    Texture2D? Texture { get; }

    int Size { get; }

    bool ContainsCell(CellType cellType);
}
