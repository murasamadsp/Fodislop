#nullable enable

using Fodinae.Scripts.Game;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IVFXService
    {
        VFXPool.PooledSlot? Acquire(VFXType vfxType);
        void Release(VFXPool.PooledSlot slot);
        void Preload(VFXType vfxType, int count);
    }
}
