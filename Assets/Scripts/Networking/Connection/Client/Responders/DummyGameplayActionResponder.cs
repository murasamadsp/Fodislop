#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Audio;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyGameplayActionResponder(
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    DummyMovementResponder movementResponder,
    DummyMissionRunner missionRunner,
    DummyInventoryResponder inventoryResponder,
    DummyChatSimulator chatSimulator,
    IDummyClock clock,
    Action<ServerPacket> sendPacket,
    ushort playerBotId,
    IAsyncOperationSupervisor operations,
    Func<int> getLifecycleVersion,
    Func<int, bool> loopAlive)
{
    private const ushort SpawnX = 25;
    private const ushort SpawnY = 50;
    private const int MilitaryBlockAssemblyDelayMilliseconds = 5000;

    private readonly Dictionary<(ushort X, ushort Y), long> _militaryBuildVersions = [];
    private long _nextMilitaryBuildVersion;

    public void Handle(ActionClientPacket packet)
    {
        switch (packet.Payload)
        {
            case MovePacket move:
                movementResponder.HandleMove(move);
                break;
            case RotatePacket rotate:
                movementResponder.HandleRotate(rotate);
                break;
            case UnmappedKeyPacket:
                break;
            case ToggleAutoDigPacket:
                sendPacket(new ServerPacket(
                    new AutoMineStatePacket(playerState.ToggleAutoDig())));
                break;
            case BzPacket:
                HandleDig(packet.X, packet.Y);
                break;
            case SuicidePacket:
                HandleSuicide();
                break;
            case GeoPacket:
                HandleGeology();
                break;
            case HealPacket:
                sendPacket(new ServerPacket(new HealthPacket(playerState.Heal(50), 500)));
                break;
            case BuildCyanPacket:
                HandleMilitaryBlockBuild();
                break;
            case BuildGrayPacket:
                HandleRoadBuild();
                break;
            case BuildGreenPacket:
                HandleUpgradeBuild(
                [
                    (CellType.Empty, CellType.GreenBlock),
                    (CellType.GreenBlock, CellType.YellowBlock),
                    (CellType.YellowBlock, CellType.RedBlock),
                ]);
                break;
            case BuildWhitePacket:
                HandleUpgradeBuild(
                [
                    (CellType.Empty, CellType.Support),
                    (CellType.Support, CellType.QuadBlock),
                ]);
                break;
            case ClickCellPacket click:
                movementResponder.HandleClick(click);
                break;
            default:
                // Заглушка — штатный транспорт, а не отладочный: необработанное
                // действие означает, что клиент шлёт то, чего сервер-заглушка
                // не умеет, и без этой ветки оно исчезало бы бесследно.
                Debug.LogError(
                    "[DummyGameplayActionResponder] Действие " +
                    $"'{packet.Payload?.GetType().Name ?? "null"}' не обработано: " +
                    "добавьте ветку сюда либо уберите отправку на клиенте.");
                break;
        }
    }

    private void HandleDig(ushort cellX, ushort cellY)
    {
        SendAudio(SFX.Bz, cellX, cellY);
        sendPacket(new ServerPacket(new HBPacket([
            new VFXPacket(
                VFX.Bz,
                playerBotId,
                cellX,
                cellY,
                Array.Empty<StringPairPacket>())])));

        if (worldState.HasLayer)
        {
            CellType cellType = worldState.GetCell(cellX, cellY);
            if (cellType == CellType.Empty)
            {
                return;
            }

            CellConfigurationPacket? cellConfig = worldState.GetCellConfig(cellType);
            bool isBreakable = cellConfig.HasValue &&
                ((CellConfigProperties)cellConfig.Value.Properties)
                    .HasFlag(CellConfigProperties.Breakable);
            if (!isBreakable)
            {
                return;
            }

            AddCrystalToBasket(cellType);
            SendCellUpdate(cellX, cellY, CellType.Empty, SFX.Destroy);
        }

        missionRunner.OnBlockMined(inventoryResponder.Items);
        chatSimulator.SendMiningReaction();
    }

    private void AddCrystalToBasket(CellType cellType)
    {
        CrystalType crystal = DummyCellConfigurationUtilities.GetMinedCrystal(cellType);
        if (crystal == CrystalType.Unknown)
        {
            return;
        }

        long[]? contents = playerState.AddToBasket(
            (int)crystal,
            clock.Random.Next(1, 101));
        if (contents != null)
        {
            sendPacket(new ServerPacket(new BasketPacket(50000, contents)));
        }
    }

    private void HandleSuicide()
    {
        ushort effectX = playerState.X;
        ushort effectY = playerState.Y;
        playerState.Respawn(SpawnX, SpawnY);
        movementResponder.CancelPath();
        worldState.QueueChunksAround(playerState.X, playerState.Y, sendPacket);
        sendPacket(new ServerPacket(new HealthPacket(500, 500)));
        sendPacket(new ServerPacket(new TeleportPacket(SpawnX, SpawnY, false)));
        sendPacket(new ServerPacket(new HBPacket([
            new RobotPositionPacket(
                playerBotId,
                SpawnX,
                SpawnY,
                (byte)playerState.Direction),
            CreateAudioPacket(SFX.Death, effectX, effectY),
        ])));
    }

    private void HandleGeology()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        if (!worldState.HasLayer)
        {
            return;
        }

        CellType cellType = worldState.GetCell(frontX, frontY);
        CellConfigurationPacket? cellConfig = worldState.GetCellConfig(cellType);
        bool isBreakable = cellConfig.HasValue &&
            ((CellConfigProperties)cellConfig.Value.Properties)
                .HasFlag(CellConfigProperties.Breakable);
        if (cellType != CellType.Empty && isBreakable)
        {
            playerState.PushGeology(cellType);
            SetGeologyCell(frontX, frontY, CellType.Empty, cellType);
        }
        else if (playerState.TryPopGeology(out CellType placeType))
        {
            SetGeologyCell(frontX, frontY, placeType, placeType);
        }
    }

    private void SetGeologyCell(
        ushort x,
        ushort y,
        CellType mapCell,
        CellType reportedCell)
    {
        sendPacket(new ServerPacket(new GeologyPacket(
            playerState.GeologyCount,
            10,
            reportedCell,
            reportedCell.ToString())));
        SendCellUpdate(x, y, mapCell, SFX.Geology);
    }

    private void HandleBuild(CellType cellType)
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        DummyBuildHandler.TryBuild(
            worldState.Layer,
            worldState.GetCell,
            worldState.SetCell,
            sendPacket,
            frontX,
            frontY,
            cellType);
    }

    private void HandleMilitaryBlockBuild()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY) ||
            !DummyBuildHandler.TryBuild(
                worldState.Layer,
                worldState.GetCell,
                worldState.SetCell,
                sendPacket,
                frontX,
                frontY,
                CellType.MilitaryBlockFrame))
        {
            return;
        }

        long buildVersion = ++_nextMilitaryBuildVersion;
        (ushort X, ushort Y) position = (frontX, frontY);
        _militaryBuildVersions[position] = buildVersion;
        int lifecycleVersion = getLifecycleVersion();
        operations.Run(
            "dummy_military_block_assembly",
            cancellationToken => CompleteMilitaryBlockAssemblyAsync(
                frontX,
                frontY,
                buildVersion,
                lifecycleVersion,
                cancellationToken));
    }

    private async UniTask CompleteMilitaryBlockAssemblyAsync(
        ushort x,
        ushort y,
        long buildVersion,
        int lifecycleVersion,
        CancellationToken cancellationToken)
    {
        await clock.Delay(MilitaryBlockAssemblyDelayMilliseconds, cancellationToken);

        (ushort X, ushort Y) position = (x, y);
        if (!_militaryBuildVersions.TryGetValue(position, out long currentBuildVersion) ||
            currentBuildVersion != buildVersion)
        {
            return;
        }

        _militaryBuildVersions.Remove(position);
        if (!loopAlive(lifecycleVersion) ||
            !worldState.TryGetCell(x, y, out CellType cellType) ||
            cellType != CellType.MilitaryBlockFrame)
        {
            return;
        }

        SendCellUpdate(x, y, CellType.MilitaryBlock);
    }

    private void HandleRoadBuild()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        if (worldState.HasLayer && worldState.GetCell(frontX, frontY) == CellType.Road)
        {
            SendCellUpdate(frontX, frontY, CellType.Empty);
            return;
        }

        HandleBuild(CellType.Road);
    }

    private void HandleUpgradeBuild((CellType From, CellType To)[] upgrades)
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        DummyBuildHandler.TryUpgradeBuild(
            worldState.Layer,
            worldState.GetCell,
            worldState.SetCell,
            sendPacket,
            frontX,
            frontY,
            upgrades);
    }

    private bool TryGetFrontCell(out ushort frontX, out ushort frontY)
    {
        Vector2Int offset = playerState.Direction switch
        {
            Direction.Down => new Vector2Int(0, 1),
            Direction.Up => new Vector2Int(0, -1),
            Direction.Left => new Vector2Int(-1, 0),
            Direction.Right => new Vector2Int(1, 0),
            _ => Vector2Int.zero,
        };

        int targetX = (int)playerState.X + offset.x;
        int targetY = (int)playerState.Y + offset.y;

        if (targetX < 0 || targetY < 0)
        {
            frontX = 0;
            frontY = 0;
            return false;
        }

        if (worldState.Layer is { } layer)
        {
            int worldWidth = layer.WidthChunks * layer.ChunkSize;
            int worldHeight = layer.HeightChunks * layer.ChunkSize;
            if (targetX >= worldWidth || targetY >= worldHeight)
            {
                frontX = 0;
                frontY = 0;
                return false;
            }
        }

        frontX = (ushort)targetX;
        frontY = (ushort)targetY;
        return true;
    }

    private void SendCellUpdate(ushort x, ushort y, CellType cell, SFX? effect = null)
    {
        worldState.SetCell(x, y, cell);
        IHBPacket[] packets = effect.HasValue
            ? [new MapRegionPacket(x, y, 0, 0, [cell]), CreateAudioPacket(effect.Value, x, y)]
            : [new MapRegionPacket(x, y, 0, 0, [cell])];
        sendPacket(new ServerPacket(new HBPacket(packets)));
    }

    private void SendAudio(SFX effect, ushort x, ushort y) =>
        sendPacket(new ServerPacket(new HBPacket([CreateAudioPacket(effect, x, y)])));

    private AudioPacket CreateAudioPacket(SFX effect, ushort x, ushort y) =>
        new(effect, playerBotId, x, y, []);
}
