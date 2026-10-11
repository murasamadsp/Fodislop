#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyMovementResponder : IDisposable
{
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IDummyClock _clock;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyWorldSimulationState _worldState;
    private readonly DummyTeleportManager _teleportManager;
    private readonly DummyPathFinder _pathFinder;
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Func<bool> _ignoreCollision;
    private readonly ushort _playerBotId;
    internal DummyMovementResponder(
    IAsyncOperationSupervisor operations,
    IDummyClock clock,
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    DummyTeleportManager teleportManager,
    DummyPathFinder pathFinder,
    Action<ServerPacket> sendPacket,
    Func<bool> ignoreCollision,
    ushort playerBotId)
    {
        _operations = operations;
        _clock = clock;
        _playerState = playerState;
        _worldState = worldState;
        _teleportManager = teleportManager;
        _pathFinder = pathFinder;
        _sendPacket = sendPacket;
        _ignoreCollision = ignoreCollision;
        _playerBotId = playerBotId;
    }

    private CancellationTokenSource? _pathCancellation;
    private CancellationTokenSource? _pendingMoveLoadCancellation;
    private CancellationTokenSource? _positionSnapshotCancellation;
    private long _moveRequestVersion;

    public void HandleMove(MovePacket packet)
    {
        CancelPendingMoveLoad();
        CancelPositionSnapshot();
        if (_teleportManager.WindowOpen)
        {
            return;
        }

        long requestVersion = ++_moveRequestVersion;
        int dx = Math.Abs(packet.X - _playerState.X);
        int dy = Math.Abs(packet.Y - _playerState.Y);
        bool isAdjacent = (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        if (!isAdjacent)
        {
            SendPositionSnapshot();
            return;
        }

        CellType cellType = CellType.Unloaded;
        if (_worldState.HasLayer &&
            !_ignoreCollision() &&
            !_worldState.TryGetCell(packet.X, packet.Y, out cellType))
        {
            var pendingLoadCancellation = new CancellationTokenSource();
            _pendingMoveLoadCancellation = pendingLoadCancellation;
            _operations.Run(
                "dummy_move_wait_for_cell",
                supervisorToken => WaitForCellAndMoveAsync(
                    packet,
                    requestVersion,
                    pendingLoadCancellation,
                    supervisorToken));
            return;
        }

        if (!CanEnter(cellType))
        {
            SendPositionSnapshot();
            return;
        }

        CancelPath();
        ScheduleMoveAfterPrefetch(packet.X, packet.Y, requestVersion);
    }

    // Сервер отвечает на поворот сразу и раньше, чем на следующее действие:
    // клиент сам бота не поворачивает, а ждёт этот ответ. Отложенный на
    // 200 мс снимок пропускал вперёд копание, и эффект копания рисовался по
    // старому углу бота. Позиция при повороте не меняется, чанки не нужны;
    // отложенный снимок после шага, если он ждёт, остаётся — он пришлёт то же
    // актуальное состояние вместе с чанками.
    public void HandleRotate(RotatePacket packet)
    {
        _playerState.SetDirection(packet.Direction);
        SendPositionSnapshot();
    }

    public void HandleClick(ClickCellPacket packet)
    {
        CancelPendingMoveLoad();
        CancelPositionSnapshot();
        _moveRequestVersion++;
        CancelPath();
        List<(ushort X, ushort Y)> path = _pathFinder.FindPath(
            _playerState.X,
            _playerState.Y,
            packet.X,
            packet.Y,
            _worldState.GetCell);
        if (path.Count == 0)
        {
            return;
        }

        _pathCancellation = new CancellationTokenSource();
        CancellationToken pathToken = _pathCancellation.Token;
        _operations.Run(
            "dummy_walk_path",
            supervisorToken => WalkPathAsync(path, pathToken, supervisorToken));
    }

    public void SendPositionSnapshot()
    {
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new RobotPositionPacket(
                _playerBotId,
                _playerState.X,
                _playerState.Y,
                (byte)_playerState.Direction),
        })));
    }

    public void CancelPath()
    {
        _pathCancellation?.Cancel();
        _pathCancellation?.Dispose();
        _pathCancellation = null;
    }

    public void Dispose()
    {
        CancelPendingMoveLoad();
        CancelPositionSnapshot();
        CancelPath();
    }

    private async UniTask WaitForCellAndMoveAsync(
        MovePacket packet,
        long requestVersion,
        CancellationTokenSource pendingLoadCancellation,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            pendingLoadCancellation.Token,
            supervisorToken);
        try
        {
            if (!await _worldState.EnsureCellAvailableAsync(
                    packet.X,
                    packet.Y,
                    linkedCancellation.Token) ||
                requestVersion != _moveRequestVersion ||
                _teleportManager.WindowOpen ||
                !IsAdjacent(packet.X, packet.Y) ||
                !_worldState.TryGetCell(packet.X, packet.Y, out CellType cellType) ||
                !CanEnter(cellType))
            {
                SendPositionSnapshot();
                return;
            }

            await _worldState.SendChunksAroundAsync(
                packet.X,
                packet.Y,
                _sendPacket,
                linkedCancellation.Token);
            if (requestVersion != _moveRequestVersion ||
                _teleportManager.WindowOpen ||
                !IsAdjacent(packet.X, packet.Y))
            {
                SendPositionSnapshot();
                return;
            }

            _playerState.SetPosition(packet.X, packet.Y);
            CancelPath();
            SchedulePositionSnapshot();
            _teleportManager.CheckTeleportEntry(_playerState.X, _playerState.Y);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            // A newer move request owns the pending cell load.
        }
        finally
        {
            if (ReferenceEquals(_pendingMoveLoadCancellation, pendingLoadCancellation))
            {
                _pendingMoveLoadCancellation = null;
            }

            pendingLoadCancellation.Dispose();
        }
    }

    private void CancelPendingMoveLoad()
    {
        _pendingMoveLoadCancellation?.Cancel();
    }

    private void ScheduleMoveAfterPrefetch(
        ushort targetX,
        ushort targetY,
        long requestVersion)
    {
        var pendingMoveLoadCancellation = new CancellationTokenSource();
        _pendingMoveLoadCancellation = pendingMoveLoadCancellation;
        _operations.Run(
            "dummy_move_prefetch",
            supervisorToken => PrefetchAndMoveAsync(
                targetX,
                targetY,
                requestVersion,
                pendingMoveLoadCancellation,
                supervisorToken));
    }

    private async UniTask PrefetchAndMoveAsync(
        ushort targetX,
        ushort targetY,
        long requestVersion,
        CancellationTokenSource pendingMoveLoadCancellation,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            pendingMoveLoadCancellation.Token,
            supervisorToken);
        try
        {
            await _worldState.SendChunksAroundAsync(
                targetX,
                targetY,
                _sendPacket,
                linkedCancellation.Token);
            if (requestVersion != _moveRequestVersion ||
                _teleportManager.WindowOpen ||
                !IsAdjacent(targetX, targetY))
            {
                SendPositionSnapshot();
                return;
            }

            _playerState.SetPosition(targetX, targetY);
            CancelPath();
            SchedulePositionSnapshot();
            _teleportManager.CheckTeleportEntry(_playerState.X, _playerState.Y);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            // A newer move request owns the prefetch.
        }
        finally
        {
            if (ReferenceEquals(_pendingMoveLoadCancellation, pendingMoveLoadCancellation))
            {
                _pendingMoveLoadCancellation = null;
            }

            pendingMoveLoadCancellation.Dispose();
        }
    }

    private void CancelPositionSnapshot()
    {
        _positionSnapshotCancellation?.Cancel();
    }

    private bool IsAdjacent(ushort x, ushort y)
    {
        int dx = Math.Abs(x - _playerState.X);
        int dy = Math.Abs(y - _playerState.Y);
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }

    private bool CanEnter(CellType cellType)
    {
        CellConfigurationPacket? cellConfig = _worldState.GetCellConfig(cellType);
        if (!cellConfig.HasValue)
        {
            return true;
        }

        bool isPassable = cellType == CellType.Empty ||
            ((CellConfigProperties)cellConfig.Value.Properties)
                .HasFlag(CellConfigProperties.Passable);
        return isPassable || _ignoreCollision();
    }

    private void SchedulePositionSnapshot()
    {
        CancelPositionSnapshot();
        var positionCancellation = new CancellationTokenSource();
        _positionSnapshotCancellation = positionCancellation;
        _operations.Run(
            "dummy_position_snapshot",
            supervisorToken => UpdatePositionAsync(positionCancellation, supervisorToken));
    }

    private async UniTask UpdatePositionAsync(
        CancellationTokenSource positionCancellation,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            positionCancellation.Token,
            supervisorToken);
        try
        {
            await _clock.Delay(
                GetMoveDelay(_playerState.X, _playerState.Y),
                linkedCancellation.Token);
            await _worldState.SendChunksAroundAsync(
                _playerState.X,
                _playerState.Y,
                _sendPacket,
                linkedCancellation.Token);
            SendPositionSnapshot();
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            // A newer position snapshot owns the stream request.
        }
        catch (ObjectDisposedException) when (linkedCancellation.IsCancellationRequested)
        {
            // Teardown or responder reset in progress.
        }
        finally
        {
            if (ReferenceEquals(_positionSnapshotCancellation, positionCancellation))
            {
                _positionSnapshotCancellation = null;
            }

            positionCancellation.Dispose();
        }
    }

    private int GetMoveDelay(ushort x, ushort y)
    {
        if (_ignoreCollision())
        {
            return 20;
        }

        if (_worldState.HasLayer &&
            _worldState.TryGetCell(x, y, out CellType cellType))
        {
            CellConfigurationPacket? config = _worldState.GetCellConfig(cellType);
            if (config.HasValue &&
                !((CellConfigProperties)config.Value.Properties).HasFlag(CellConfigProperties.Passable) &&
                cellType != CellType.Empty)
            {
                return 100;
            }
        }

        return 20;
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
            ushort previousX = _playerState.X;
            ushort previousY = _playerState.Y;
            for (int index = 0; index < path.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (ushort nextX, ushort nextY) = path[index];
                Direction direction = nextY > previousY ? Direction.Down
                    : nextY < previousY ? Direction.Up
                    : nextX < previousX ? Direction.Left
                    : Direction.Right;

                await _worldState.SendChunksAroundAsync(
                    nextX,
                    nextY,
                    _sendPacket,
                    cancellationToken);
                _playerState.SetPosition(nextX, nextY);
                previousX = nextX;
                previousY = nextY;
                _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
                {
                    new RobotPositionPacket(
                        _playerBotId,
                        _playerState.X,
                        _playerState.Y,
                        (byte)direction),
                })));
                await _clock.Delay(GetMoveDelay(nextX, nextY), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A new move/click or teardown owns cancellation of the old path.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // Teardown in progress.
        }
    }
}
