#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fodinae;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyChatSimulator(
    Action<ServerPacket> onReceived,
    Func<bool> loopAlive,
    IAsyncOperationSupervisor operations)
{
    private static readonly System.Random _rng = new();

    // Имена берём из DummyBotRunner.BotNames — единый источник: чат-«игроки»
    // всегда те же, кого видно на карте, и дубль списка не расходится.
    private static readonly string[] _messages =
    [
        "gg", "welcome!", "как дела?", "lol", "nice",
        "gl hf", "куда бежать?", "фармим)", "👋", "подскажите кто знает",
    ];

    // Реакции на действия игрока — мир ощущается живым.
    private static readonly string[] _miningReactions =
    [
        "кто-то копает рядом!",
        "красиво копает 🎉",
        "привет, сосед!",
        "ого, добыча пошла",
        "уважаю труд)",
    ];

    public void SendChatMock() =>
        operations.Run("dummy_chat_loop", _ => SendChatMockAsync());

    private async UniTask SendChatMockAsync()
    {
        while (loopAlive())
        {
            await UniTask.Delay(8000 + _rng.Next(4000));

            SendChatLine(DummyBotRunner.BotNames[_rng.Next(DummyBotRunner.BotNames.Length)], _messages[_rng.Next(_messages.Length)]);
        }
    }

    /// <summary>
    /// Случайная реакция «игрока» на то, что игрок копает блок. Вызывается
    /// сервером при BzPacket; без действия — молчит.
    /// </summary>
    public void SendMiningReaction()
    {
        if (_rng.Next(100) >= 25)
        {
            return;
        }

        SendChatLine(DummyBotRunner.BotNames[_rng.Next(DummyBotRunner.BotNames.Length)], _miningReactions[_rng.Next(_miningReactions.Length)]);
    }

    private void SendChatLine(string name, string message)
    {
        System.Drawing.Color nickColor = System.Drawing.Color.FromArgb(
            255, _rng.Next(100, 256), _rng.Next(100, 256), _rng.Next(100, 256));

        var chatMsg = new ChatMessagePacket(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            _rng.Next(100, 999), (byte)_rng.Next(0, 3),
            nickColor, name,
            System.Drawing.Color.White, message);
        onReceived.Invoke(new ServerPacket(new ChatMessageListPacket("global", [chatMsg])));
    }
}
