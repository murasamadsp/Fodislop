#nullable enable

using System;
using Fodinae.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class ChatProcessor : IPacketProcessor<ChatMessageListPacket>, IPacketProcessor<LocalChatMessagePacket>, IPacketProcessor<ChatMutePacket>, IPacketProcessor<ChatListPacket>
    {
        private readonly ChatEventGateway _events;

        public ChatProcessor(ChatEventGateway events)
        {
            _events = events;
        }

        public void Process(ChatMessageListPacket packet)
        {
            foreach (var msg in packet.Messages)
            {
                _events.Publish(msg);
            }
        }

        public void Process(LocalChatMessagePacket packet)
        {
            _events.Publish(packet);
        }

        public void Process(ChatMutePacket packet)
        {
            _events.Publish(packet);
        }

        public void Process(ChatListPacket packet)
        {
            foreach (var chat in packet.Chats)
            {
                Debug.Log($"[ChatProcessor] Channel available: tag={chat.Tag}, name={chat.Name}");
            }
        }
    }
}
