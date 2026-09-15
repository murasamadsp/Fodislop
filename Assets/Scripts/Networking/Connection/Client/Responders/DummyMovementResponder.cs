#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyMovementResponder(
    IAsyncOperationSupervisor operations,
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    DummyTeleportManager teleportManager,
    DummyPathFinder pathFinder,
    Action<ServerPacket> sendPacket,
    Func<bool> ignoreCollision,
    ushort playerBotId) : IDisposable
{
    private CancellationTokenSource? _pathCancellation;

    public void HandleMove(MovePacket packet)
    {
        if (teleportManager.WindowOpen)
        {
            return;
        }

        int dx = Math.Abs(packet.X - playerState.X);
        int dy = Math.Abs(packet.Y - playerState.Y);
        bool isAdjacent = (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        if (!isAdjacent || !CanEnter(packet.X, packet.Y))
        {
            SendPositionSnapshot();
            return;
        }

        playerState.SetPosition(packet.X, packet.Y);
        CancelPath();
        operations.Run("dummy_position_snapshot", _ => UpdatePositionAsync());
        teleportManager.CheckTeleportEntry(playerState.X, playerState.Y);
    }

    public void HandleRotate(RotatePacket packet)
    {
        playerState.SetDirection(packet.Direction);
        operations.Run("dummy_position_snapshot", _ => UpdatePositionAsync());
    }

    public void HandleClick(ClickCellPacket packet)
    {
        CancelPath();
        List<(ushort X, ushort Y)> path = pathFinder.FindPath(
            playerState.X,
            playerState.Y,
            packet.X,
            packet.Y,
            worldState.GetCell);
        if (path.Count == 0)
        {
            return;
        }

        _pathCancellation = new CancellationTokenSource();
        CancellationToken pathToken = _pathCancellation.Token;
        operations.Run(
            "dummy_walk_path",
            supervisorToken => WalkPathAsync(path, pathToken, supervisorToken));
    }

    public void SendPositionSnapshot()
    {
        sendPacket(new ServerPacket(new HBPacket([
            new RobotPositionPacket(
                playerBotId,
                playerState.X,
                playerState.Y,
                (byte)playerState.Direction),
        ])));
    }

    public void CancelPath()
    {
        _pathCancellation?.Cancel();
        _pathCancellation?.Dispose();
        _pathCancellation = null;
    }

    public void Dispose() => CancelPath();

    private bool CanEnter(ushort x, ushort y)
    {
        if (!worldState.HasLayer)
        {
            return true;
        }

        CellType cellType = worldState.GetCell(x, y);
        CellConfigurationPacket? cellConfig = worldState.GetCellConfig(cellType);
        if (!cellConfig.HasValue)
        {
            return true;
        }

        bool isPassable = cellType == CellType.Empty ||
            ((CellConfigProperties)cellConfig.Value.Properties)
                .HasFlag(CellConfigProperties.Passable);
        return isPassable || ignoreCollision();
    }

    private async UniTask UpdatePositionAsync()
    {
        await UniTask.Delay(ignoreCollision() ? 20 : 200);
        worldState.SendChunksAround(playerState.X, playerState.Y, sendPacket);
        SendPositionSnapshot();
    }

    private async UniTask WalkPathAsync(
        List<(ushort X, ushort Y)> path,
        CancellationToken pathToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            pathToken,
            supervisorToken);
        CancellationToken cancellationToken = linkedCancellation.Token;
        try
        {
            ushort previousX = playerState.X;
            ushort previousY = playerState.Y;
            for (int index = 0; index < path.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (ushort nextX, ushort nextY) = path[index];
                Direction direction = nextY > previousY ? Direction.Down
                    : nextY < previousY ? Direction.Up
                    : nextX < previousX ? Direction.Left
                    : Direction.Right;

                playerState.SetPosition(nextX, nextY);
                previousX = nextX;
                previousY = nextY;
                worldState.SendChunksAround(playerState.X, playerState.Y, sendPacket);
                sendPacket(new ServerPacket(new HBPacket([
                    new RobotPositionPacket(
                        playerBotId,
                        playerState.X,
                        playerState.Y,
                        (byte)direction),
                ])));
                await UniTask.Delay(100, cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A new move/click or teardown owns cancellation of the old path.
        }
    }
}
