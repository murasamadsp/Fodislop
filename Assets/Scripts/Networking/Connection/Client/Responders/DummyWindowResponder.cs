#nullable enable

using System;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;

using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWindowResponder(
    Action<ServerPacket> sendPacket,
    DummyBuffManager buffManager,
    DummyInventoryResponder inventoryResponder,
    DummyTeleportManager teleportManager,
    DummyClanManager clanManager,
    DummyMissionRunner missionRunner)
{

    public void Handle(ElementClickPacket packet, ushort playerX, ushort playerY)
    {
        switch (packet.WindowTag)
        {
            case "daily_bonus":
                buffManager.HandleDailyBonusClaim(inventoryResponder.Items);
                break;
            case "teleport":
                HandleTeleport(packet);
                break;
            case "test_modal":
                sendPacket(DummyWindowBuilder.BuildTestModalWindow());
                break;
            case "join_clan":
            case "leave_clan":
            case "clan_list":
            case "clan_info":
                clanManager.HandleElementClick(packet);
                break;
            case "open_missions":
                missionRunner.SendMissionWindow(playerX, playerY);
                break;
            case "missions":
                HandleMission(packet, playerX, playerY);
                break;
            case "open_url_test":
                sendPacket(DummyWindowBuilder.BuildOpenUrlPacket("https://vk.ru/mines4reborn"));
                break;
            case "test_mission_arrow":
                sendPacket(DummyWindowBuilder.BuildTestMissionArrowPacket(playerX, playerY));
                break;
            default:
                // Без этой ветки нажатие в незнакомом окне не делало ничего и
                // ничего не сообщало: окно просто не открывалось, и причину
                // приходилось искать глазами по коду.
                Debug.LogError(
                    $"[DummyWindowResponder] Окно '{packet.WindowTag}' не обработано: " +
                    "добавьте ветку сюда либо уберите тег на клиенте.");
                break;
        }
    }

    private void HandleTeleport(ElementClickPacket packet)
    {
        if (!teleportManager.WindowOpen)
        {
            return;
        }

        if (packet.ElementIndex == 0)
        {
            teleportManager.WindowOpen = false;
            sendPacket(new ServerPacket(new CloseWindowPacket()));
            return;
        }

        teleportManager.HandleTeleportClick(packet.ElementIndex - 1);
    }

    private void HandleMission(ElementClickPacket packet, ushort playerX, ushort playerY)
    {
        if (packet.ElementIndex == 0)
        {
            sendPacket(new ServerPacket(new CloseWindowPacket()));
        }
        else if (packet.ElementIndex <= missionRunner.MissionCount)
        {
            missionRunner.StartMission(packet.ElementIndex - 1, playerX, playerY);
        }
        else
        {
            missionRunner.CancelMission();
        }
    }
}
