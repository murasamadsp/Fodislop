#nullable enable

using System;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking;

public sealed class ChatEventGateway
{
    public ChatListPacket? LastChatList { get; private set; }

    public event Action<ChatMessagePacket>? MessageReceived;
    public event Action<ChatMessageListPacket>? HistoryReceived;
    public event Action<ChatMutePacket>? MuteReceived;
    public event Action<LocalChatMessagePacket>? LocalMessageReceived;
    public event Action<ChatListPacket>? ChatListReceived;

    public void Publish(ChatMessagePacket packet) => MessageReceived?.Invoke(packet);

    public void Publish(ChatMessageListPacket packet) => HistoryReceived?.Invoke(packet);

    public void Publish(ChatMutePacket packet) => MuteReceived?.Invoke(packet);

    public void Publish(LocalChatMessagePacket packet) => LocalMessageReceived?.Invoke(packet);

    public void Publish(ChatListPacket packet)
    {
        LastChatList = packet;
        ChatListReceived?.Invoke(packet);
    }
}
