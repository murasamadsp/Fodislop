#nullable enable

using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.Core.DI;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Client.Packets.Utilities;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyAssetHandler
{
    public static async UniTaskVoid HandleAssetRequest(RuntimeAssetRequestPacket runtimeAssets, ISessionContainer session, Action<ServerPacket> sendPacket)
    {
        foreach (var assetEntry in runtimeAssets.Assets)
        {
            var tsm = session.TryResolve<ITextureStorageService>();
            byte[]? data = tsm != null ? await tsm.GetTextureData(assetEntry.Filename.TrimStart('/')) : null;

            RuntimeAssetPacket response;
            if (data != null)
            {
                response = new RuntimeAssetPacket(assetEntry.Filename, Guid.NewGuid().ToString(), data);
            }
            else
            {
                response = new RuntimeAssetPacket(assetEntry.Filename, string.Empty, Array.Empty<byte>());
            }

            sendPacket(new ServerPacket(response));
        }
    }
}
