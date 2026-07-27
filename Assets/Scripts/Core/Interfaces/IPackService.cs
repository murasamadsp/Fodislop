using Fodinae.Scripts.Game;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IPackService
    {
        void AddOrUpdatePack(ushort x, ushort y, PackType packType, byte variant, byte linkedClan);
        void RemovePack(ushort x, ushort y);
        void ClearAllPacks();
    }
}
