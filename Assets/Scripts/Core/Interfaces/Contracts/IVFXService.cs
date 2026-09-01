#nullable enable

using Fodinae.Game;
using UnityEngine;

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Handle to an acquired pooled VFX instance. Implemented by
    /// <c>VFXPool.PooledSlot</c>; consumers manipulate the visual through this
    /// surface so the pool implementation never leaks into the contracts layer.
    /// </summary>
    public interface IVFXSlot
    {
        GameObject? GameObject { get; }

        void SetSprite(Sprite? sprite);

        void SetColor(Color color);

        void SetEnabled(bool enabled);
    }

    public interface IVFXService
    {
        IVFXSlot? Acquire(VFXType vfxType);
        void Release(IVFXSlot slot);
        void Preload(VFXType vfxType, int count);
    }
}
