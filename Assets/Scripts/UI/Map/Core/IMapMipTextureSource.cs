#nullable enable

using UnityEngine;

namespace Kern.UI;

/// <summary>
/// A prefiltered overview texture the viewport renderer samples when the visible
/// chunk grid cannot be covered by the per-cell path. The renderer depends on
/// this contract, not on any concrete cache implementation.
/// </summary>
internal interface IMapMipTextureSource
{
    bool IsReady { get; }

    int MipLevelCount { get; }

    /// <summary>World cells covered by one texel of the finest mip level.</summary>
    int MipBlockSize { get; }

    Texture Texture { get; }
}
