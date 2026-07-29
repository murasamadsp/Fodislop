#nullable enable

using System.Linq;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.UI;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class ChatProcessor : IPacketProcessor<ChatMessageListPacket>, IPacketProcessor<LocalChatMessagePacket>, IPacketProcessor<ChatMutePacket>, IPacketProcessor<ChatListPacket>
    {
        public void Process(ChatMessageListPacket packet)
        {
            foreach (var msg in packet.Messages)
            {
                if (Fodinae.Scripts.Core.ServiceLocator.Resolve<GlobalChatUI>() != null)
                {
                    Fodinae.Scripts.Core.ServiceLocator.Resolve<GlobalChatUI>().AddMessage(msg);
                }
            }
        }

        public void Process(LocalChatMessagePacket packet)
        {
            if (Fodinae.Scripts.Core.ServiceLocator.Resolve<FloatingChatManager>() != null)
            {
                Fodinae.Scripts.Core.ServiceLocator.Resolve<FloatingChatManager>().ShowLocalChat(packet);
            }
        }

        public void Process(ChatMutePacket packet)
        {
        }

        public void Process(ChatListPacket packet)
        {
            var chatUi = Fodinae.Scripts.Core.ServiceLocator.Resolve<GlobalChatUI>();
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
