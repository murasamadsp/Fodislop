#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World;

public interface IBlockRegistry
{
    BlockDefinition Get(CellType type);

    bool TryGet(CellType type, out BlockDefinition definition);

    IReadOnlyDictionary<CellType, BlockDefinition> All { get; }
}
