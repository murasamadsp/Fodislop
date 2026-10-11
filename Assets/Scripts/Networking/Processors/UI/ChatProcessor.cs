#nullable enable

using System;
using Kern.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class ChatProcessor :
    IPacketProcessor<ChatMessageListPacket>,
    IPacketProcessor<LocalChatMessagePacket>,
    IPacketProcessor<ChatMutePacket>,
    IPacketProcessor<ChatListPacket>
{
    private readonly ChatEventGateway _events;
    public ChatProcessor(ChatEventGateway events)
    {
        _events = events;
    }

    public void Process(ChatMessageListPacket packet)
        => _events.Publish(packet);

    public void Process(LocalChatMessagePacket packet) => _events.Publish(packet);

    public void Process(ChatMutePacket packet) => _events.Publish(packet);

    public void Process(ChatListPacket packet) => _events.Publish(packet);
}
