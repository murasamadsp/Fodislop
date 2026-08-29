#nullable enable

using System;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking;

/// <summary>Packet-to-presentation event boundary for chat data.</summary>
public sealed class ChatEventGateway
{
    public event Action<ChatMessagePacket>? MessageReceived;
    public event Action<ChatMutePacket>? MuteReceived;
    public event Action<LocalChatMessagePacket>? LocalMessageReceived;

    public void Publish(ChatMessagePacket packet) => MessageReceived?.Invoke(packet);

    public void Publish(ChatMutePacket packet) => MuteReceived?.Invoke(packet);

    public void Publish(LocalChatMessagePacket packet) => LocalMessageReceived?.Invoke(packet);
}
