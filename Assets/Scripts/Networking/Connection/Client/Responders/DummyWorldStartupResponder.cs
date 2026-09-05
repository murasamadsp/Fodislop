#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared.Packets;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWorldStartupResponder(
    IAsyncOperationSupervisor operations,
    IItemCatalog itemCatalog,
    DummyWorldSimulationState worldState,
    DummyPlayerSimulationState playerState,
    DummyBuffManager buffManager,
    DummyChatSimulator chatSimulator,
    DummyInventoryResponder inventoryResponder,
    List<(ushort X, ushort Y)> teleportPositions,
    Action<ServerPacket> sendPacket,
    Func<int, bool> loopAlive)
{
    private static readonly System.Random Rng = new();

    public async UniTask InitializeAsync(
        string worldCodeName,
        int lifecycleVersion,
        string playerName,
        long level,
        long currency,
        ushort playerBotId)
    {
        DummyWorldDescriptor world = await worldState.OpenAsync(worldCodeName);
        SendWorldIdentity(worldCodeName, world, playerName, playerBotId);
        StartBotSimulation(lifecycleVersion);

        playerState.SetPosition(25, 50);
        sendPacket(new ServerPacket(new AggressionStatePacket(false)));
        sendPacket(new ServerPacket(new AutoMineStatePacket(false)));
        sendPacket(new ServerPacket(new DailyBonusStatePacket(false)));
        buffManager.ResetDailyBonus();
        sendPacket(new ServerPacket(new CurrencyPacket(currency, 1234)));
        playerState.SetHealth(250);
        sendPacket(new ServerPacket(new HealthPacket(250, 500)));
        long[] basketContents = playerState.ResetBasket();
        sendPacket(new ServerPacket(new BasketPacket(50000, basketContents)));
        sendPacket(new ServerPacket(new GeologyPacket(5, 10, CellType.Lava, "Lava")));
        sendPacket(new ServerPacket(new LevelPacket(level)));
        worldState.SendChunksAround(playerState.X, playerState.Y, sendPacket);

        SendSkillProgress();
        chatSimulator.SendChatMock();
        StartStatusSimulation(lifecycleVersion);

        sendPacket(new ServerPacket(
            new MovementSpeedPacket(
                DummyCellConfigurationUtilities.CreateMovementSpeeds(
                    world.CellConfigurations))));

        Dictionary<ItemType, long> inventory = CreateInitialInventory(itemCatalog);
        inventoryResponder.ReplaceItems(inventory);
        sendPacket(new ServerPacket(new InventoryPacket(inventory)));

        var placeholder = new ChatMessagePacket(
            0,
            0,
            0,
            0,
            System.Drawing.Color.White,
            string.Empty,
            System.Drawing.Color.White,
            string.Empty);
        sendPacket(new ServerPacket(new ChatListPacket(
            [("global", "Global", placeholder)])));
        SendTestPacks();
        SendWorldMusic();
    }

    /// <summary>
    /// Заказывает клиенту музыку при входе в мир.
    /// </summary>
    /// <remarks>
    /// В протоколе музыка — это обычный <c>AudioPacket</c> со значением
    /// <see cref="SFX.Music"/>, оно так и подписано в перечислении. Клиент
    /// разбирал его и раньше: <c>ServerAudioEventManager.PlayEffect</c>
    /// уводит эту ветку в <c>Play2D</c> на шину музыки, минуя пул VFX —
    /// у трека нет ни позиции, ни визуального представления. Не хватало
    /// только отправителя: заглушка не слала пакет, поэтому шина музыки
    /// молчала всю игру, хотя громкость для неё была и в конфиге, и в меню.
    ///
    /// Координаты для музыки роли не играют, но пакет обязан быть
    /// осмысленным, поэтому берётся позиция игрока. Целевой бот нулевой:
    /// трек ни к кому не привязан.
    /// </remarks>
    private void SendWorldMusic()
    {
        sendPacket(new ServerPacket(new HBPacket([
            new AudioPacket(
                SFX.Music,
                0,
                playerState.X,
                playerState.Y,
                []),
        ])));
    }

    internal static Dictionary<ItemType, long> CreateInitialInventory(IItemCatalog itemCatalog)
    {
        var inventory = new Dictionary<ItemType, long>();
        foreach (ItemType type in itemCatalog.AllTypes)
        {
            inventory[type] = 1;
        }

        inventory[ItemType.Battery] = 2;
        return inventory;
    }

    private void SendWorldIdentity(
        string worldCodeName,
        DummyWorldDescriptor world,
        string playerName,
        ushort playerBotId)
    {
        sendPacket(new ServerPacket(new WorldInitPacket(
            worldCodeName,
            "Pallada",
            (ushort)world.Width,
            (ushort)world.Height,
            world.CellConfigurations,
            [[37, 38, 106]])));
        sendPacket(new ServerPacket(new PlayerInfoPacket(999, playerBotId, playerName)));
        sendPacket(new ServerPacket(new RobotInfoPacket(
            playerBotId,
            999,
            1,
            "Skin/bee.png",
            "Tail/default.png",
            string.Empty)));
        sendPacket(new ServerPacket(new HBPacket([
            new RobotPositionPacket(playerBotId, 25, 50, 0),
        ])));
    }

    private void StartBotSimulation(int lifecycleVersion)
    {
        operations.Run(
            "dummy_bot_loop",
            _ => DummyBotRunner.RunCircularBots(
                6,
                lifecycleVersion,
                sendPacket,
                () => loopAlive(lifecycleVersion)));
    }

    private void StartStatusSimulation(int lifecycleVersion)
    {
        sendPacket(new ServerPacket(new OnlinePacket(42, 3)));
        sendPacket(new ServerPacket(default(ClearStatusPacket)));
        buffManager.SendStatusPackets();
        buffManager.StartBuffLoop(lifecycleVersion);
        operations.Run("dummy_ping_loop", _ => SendPingLoopAsync(lifecycleVersion));
        operations.Run("dummy_online_loop", _ => SendOnlineLoopAsync(lifecycleVersion));
        buffManager.StartDailyBonusLoop(lifecycleVersion);
    }

    private void SendSkillProgress()
    {
        (SkillType Type, long Current, long Max)[] skills =
        [
            (SkillType.MineGeneral, 75, 100),
            (SkillType.Extraction, 120, 100),
            (SkillType.Health, 40, 100),
            (SkillType.Movement, 10, 100),
        ];
        foreach ((SkillType type, long current, long max) in skills)
        {
            sendPacket(new ServerPacket(new SkillProgressPacket(type, current, max)));
        }
    }

    private void SendTestPacks()
    {
        teleportPositions.Clear();
        teleportPositions.Add((27, 50));
        teleportPositions.Add((227, 50));
        sendPacket(new ServerPacket(new HBPacket([
            new PackPacket(27, 50, PackType.Teleport, 0, 1),
            new PackPacket(227, 50, PackType.Teleport, 0, 1),
            new PackPacket(25, 48, PackType.Market, 0, 0),
        ])));
    }

    private async UniTask SendPingLoopAsync(int lifecycleVersion)
    {
        await UniTask.Delay(2000);
        while (loopAlive(lifecycleVersion))
        {
            sendPacket(new ServerPacket(new PingPacket(
                DateTimeOffset.UtcNow.Ticks,
                Rng.Next(15, 60))));
            await UniTask.Delay(5000);
        }
    }

    private async UniTask SendOnlineLoopAsync(int lifecycleVersion)
    {
        await UniTask.Delay(3000);
        while (loopAlive(lifecycleVersion))
        {
            ushort players = (ushort)(38 + Rng.Next(0, 9));
            sendPacket(new ServerPacket(new OnlinePacket(players, 3)));
            await UniTask.Delay(12000);
        }
    }
}
