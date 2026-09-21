#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Одноразовые отметки о том, как террейн проходит старт.
/// </summary>
///
/// «Камера появилась», «атласы приехали», «первая сборка» — каждая печатается
/// ровно один раз за сессию, иначе лог превращается в поток. Биты держатся
/// одним полем, а не россыпью флагов, и весь тип выключается вместе с
/// KERN_TERRAIN_DIAG.
public sealed class TerrainDiagnosticLog
{
    private int _logged;

    [System.Diagnostics.Conditional("KERN_TERRAIN_DIAG")]
    public void Once(int bit, string message)
    {
        if ((_logged & bit) != 0)
        {
            return;
        }

        _logged |= bit;
        Debug.Log(message);
    }
}
