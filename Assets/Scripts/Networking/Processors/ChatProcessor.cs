#nullable enable

using System;
using Fodinae.Core.DI;
using Fodinae.UI;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class ChatProcessor : IPacketProcessor<ChatMessageListPacket>, IPacketProcessor<LocalChatMessagePacket>, IPacketProcessor<ChatMutePacket>, IPacketProcessor<ChatListPacket>
    {
        private readonly ISessionContainer _session;

        public ChatProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(ChatMessageListPacket packet)
        {
            var globalChat = _session.TryResolve<GlobalChatUI>();
            if (globalChat != null)
            {
                foreach (var msg in packet.Messages)
                {
                    globalChat.AddMessage(msg);
                }
            }
        }

        public void Process(LocalChatMessagePacket packet)
        {
            var floatingChat = _session.TryResolve<FloatingChatManager>();
            floatingChat?.ShowLocalChat(packet);
        }

        public void Process(ChatMutePacket packet)
        {
            var globalChat = _session.TryResolve<GlobalChatUI>();
            if (globalChat == null)
            {
                throw new InvalidOperationException(
                    "ChatMutePacket received before GlobalChatUI was initialized.");
            }

            globalChat.ApplyMute(packet);
        }

        public void Process(ChatListPacket packet)
        {
            var chatUi = _session.TryResolve<GlobalChatUI>();
            if (chatUi == null)
            {
                return;
            }

            foreach (var chat in packet.Chats)
            {
                Debug.Log($"[ChatProcessor] Channel available: tag={chat.Tag}, name={chat.Name}");
            }
        }
    }
}
