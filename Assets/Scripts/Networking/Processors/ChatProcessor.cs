using System.Linq;
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
                if (GlobalChatUI.Instance != null)
                {
                    GlobalChatUI.Instance.AddMessage(msg);
                }
            }
        }

        public void Process(LocalChatMessagePacket packet)
        {
            if (FloatingChatManager.Instance != null)
            {
                FloatingChatManager.Instance.ShowLocalChat(packet);
            }
        }

        public void Process(ChatMutePacket packet)
        {
        }

        public void Process(ChatListPacket packet)
        {
            if (GlobalChatUI.Instance == null)
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
