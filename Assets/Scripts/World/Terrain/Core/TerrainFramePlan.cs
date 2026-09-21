#nullable enable

using Kern.World.Streaming;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Что террейн делает в этом кадре.
/// </summary>
///
/// Запрошенное окно — куда просится камера. Зафиксированное — то, по которому
/// уже собраны тексели. Активное — то, с которым кадр работает на самом деле:
/// пока данные запрошенного окна не приехали с диска, это зафиксированное, и
/// ни ресурсы, ни освещение переезжать на новое не имеют права.
public readonly record struct TerrainFramePlan(
    StreamingWindow RequestedWindow,
    StreamingWindow CommittedWindow,
    StreamingWindow ActiveWindow,
    RectInt CameraViewport,
    RectInt LightingViewport,
    bool DimensionsChanged,
    bool ShouldProcess);
