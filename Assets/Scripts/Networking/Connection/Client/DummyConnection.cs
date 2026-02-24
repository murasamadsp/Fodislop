using Cysharp.Threading.Tasks;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Shared;
using System;
using System.Threading.Tasks;
using UnityEngine; // Added UnityEngine

namespace MinesServer.Networking.Connection.Client
{
    public class DummyConnection : IServerConnection
    {
        private ConnectionStatus _status = ConnectionStatus.Disconnected;

        public ConnectionStatus ConnectionStatus => _status;

        public event Action<ServerPacket> OnReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnDisconnecting;
        public event Action OnConnecting;

        public void Connect()
        {
            if (_status != ConnectionStatus.Disconnected)
                return;

            _status = ConnectionStatus.Connecting;
            OnConnecting?.Invoke();

            Task.Run(async () =>
            {
                await Task.Delay(100);
                _status = ConnectionStatus.Connected;
                OnConnected?.Invoke();
            });
        }

        public void Disconnect()
        {
            if (_status != ConnectionStatus.Connected)
                return;

            _status = ConnectionStatus.Disconnecting;
            OnDisconnecting?.Invoke();

            Task.Run(async () =>
            {
                await Task.Delay(100);
                _status = ConnectionStatus.Disconnected;
                OnDisconnected?.Invoke();
            });
        }

        public void Dispose()
        {
        }

        public void SendAsync(ClientPacket packet)
        {
            switch(packet.Data)
            {
                case ClientHelloPacket clientHello:
                    OnReceived?.Invoke(new ServerPacket(new WorldInitPacket(
                        "pallada",
                        "Pallada",
                        30000,
                        60000,
                        new CellConfigurationPacket[] { new() },
                        new byte[][] {
                            new byte[] { 37, 38, 106 }
                        })));
                    OnReceived?.Invoke(new ServerPacket(new PlayerInfoPacket(123, 42, "Darkar25")));
                    OnReceived?.Invoke(new ServerPacket(new AggressionStatePacket(false)));
                    OnReceived?.Invoke(new ServerPacket(new AutoMineStatePacket(false)));
                    OnReceived?.Invoke(new ServerPacket(new CurrencyPacket(123456, 1234)));
                    OnReceived?.Invoke(new ServerPacket(new HealthPacket(250, 500)));
                    OnReceived?.Invoke(new ServerPacket(new BasketPacket(123, new[] { 1L, 2L, 3L, 4L, 5L, 6L })));
                    OnReceived?.Invoke(new ServerPacket(new GeologyPacket(5, 10, CellType.Lava, "Lava")));
                    OnReceived?.Invoke(new ServerPacket(new LevelPacket(12345)));
                    break;
                case RuntimeAssetRequestPacket runtimeAssets:
                    HandleAssetRequest(runtimeAssets).Forget();
                    break;
                default:
                    break;
            }
        }
        private async UniTaskVoid HandleAssetRequest(RuntimeAssetRequestPacket runtimeAssets)
        {
            foreach (var assetEntry in runtimeAssets.Assets)
            {
                await Task.Delay(3000);
                // Switch to the main thread to use Unity's API
                await UniTask.SwitchToMainThread();

                var w = 5;
                var h = 5;
                var texture = new Texture2D(w, h);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                        texture.SetPixel(x, y, new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
                texture.Apply();
                var png = ImageConversion.EncodeToPNG(texture);
                UnityEngine.Object.Destroy(texture);

                var response = new RuntimeAssetPacket(assetEntry.Filename, Guid.NewGuid().ToString(), png);
                OnReceived?.Invoke(new ServerPacket(response));
            }
        }
    }
}