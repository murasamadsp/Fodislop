#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyChatSimulator
{
    private readonly Action<ServerPacket> _onReceived;
    private readonly int _lifecycleVersion;
    private static readonly System.Random _rng = new();

    private static readonly string[] _names = { "Alice", "Bob", "Charlie", "Darkar25", "Eve" };
    private static readonly string[] _messages =
    {
        "gg", "welcome!", "как дела?", "lol", "nice",
        "gl hf", "куда бежать?", "фармим)", "👋", "подскажите кто знает",
    };

    public DummyChatSimulator(Action<ServerPacket> onReceived, int lifecycleVersion)
    {
        _onReceived = onReceived;
        _lifecycleVersion = lifecycleVersion;
    }

    public void SendChatMock(int lifecycleVersion)
    {
        SendChatMockAsync(lifecycleVersion).Forget();
    }

    private async UniTaskVoid SendChatMockAsync(int lifecycleVersion)
    {
        while (LoopAlive(lifecycleVersion))
        {
            await UniTask.Delay(8000 + _rng.Next(4000));

            string name = _names[_rng.Next(_names.Length)];
            string msg = _messages[_rng.Next(_messages.Length)];
            System.Drawing.Color nickColor = System.Drawing.Color.FromArgb(
                255, _rng.Next(100, 256), _rng.Next(100, 256), _rng.Next(100, 256));

            var chatMsg = new ChatMessagePacket(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _rng.Next(100, 999), (byte)_rng.Next(0, 3),
                nickColor, name,
                System.Drawing.Color.White, msg);
            _onReceived.Invoke(new ServerPacket(new ChatMessageListPacket("global", new[] { chatMsg })));
        }
    }

    private bool LoopAlive(int lifecycleVersion)
    {
        return true;
    }
}
