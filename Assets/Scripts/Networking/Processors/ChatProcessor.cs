#nullable enable

using System;
using Fodinae.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Networking.Processors;

public sealed class ChatProcessor(ChatEventGateway events) :
    IPacketProcessor<ChatMessageListPacket>,
    IPacketProcessor<LocalChatMessagePacket>,
    IPacketProcessor<ChatMutePacket>,
    IPacketProcessor<ChatListPacket>
{
    public void Process(ChatMessageListPacket packet)
    {
        foreach (var msg in packet.Messages)
        {
            events.Publish(msg);
        }
    }

    public void Process(LocalChatMessagePacket packet) => events.Publish(packet);

    public void Process(ChatMutePacket packet) => events.Publish(packet);

    public void Process(ChatListPacket packet)
    {
        foreach (var chat in packet.Chats)
        {
            Debug.Log($"[ChatProcessor] Channel available: tag={chat.Tag}, name={chat.Name}");
        }
    }
}
