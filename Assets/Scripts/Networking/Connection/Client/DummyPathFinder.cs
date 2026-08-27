#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.DI;
using Fodinae.Game.Managers;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Movement;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyPathFinder
{
    private readonly Action<ServerPacket> _onReceived;
    private readonly ISessionContainer _session;

    public DummyPathFinder(Action<ServerPacket> onReceived, ISessionContainer session)
    {
        _onReceived = onReceived;
        _session = session;
    }

    public List<(ushort X, ushort Y)> FindPath(ushort startX, ushort startY, ushort targetX, ushort targetY, Func<ushort, ushort, CellType> getCell)
    {
        const int MaximumCellsChecked = 20000;

        MapManager? mapManager = _session.TryResolve<MapManager>();
        var dirs = new (int dx, int dy)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
        var visited = new HashSet<(ushort, ushort)>();
        var cameFrom = new Dictionary<(ushort, ushort), (ushort, ushort)>();
        var queue = new Queue<(ushort X, ushort Y)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));
        int cellsChecked = 0;
        bool found = false;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            cellsChecked++;
            if (cellsChecked > MaximumCellsChecked)
            {
                break;
            }

            if (cur.X == targetX && cur.Y == targetY)
            {
                found = true;
                break;
            }

            foreach (var (dx, dy) in dirs)
            {
                int nx = cur.X + dx;
                int ny = cur.Y + dy;
                if (nx < 0 || ny < 0 || nx > ushort.MaxValue || ny > ushort.MaxValue)
                {
                    continue;
                }

                var next = ((ushort)nx, (ushort)ny);
                if (visited.Contains(next))
                {
                    continue;
                }

                CellType cellType = getCell((ushort)nx, (ushort)ny);

                var cellConfig = mapManager?.GetCellConfig(cellType);
                bool isPassable = cellType == CellType.Empty || (cellConfig.HasValue && ((CellConfigProperties)cellConfig.Value.Properties).HasFlag(CellConfigProperties.Passable));
                if (!isPassable)
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = cur;
                queue.Enqueue(next);
            }
        }

        if (!found)
        {
            return new List<(ushort, ushort)>();
        }

        var path = new List<(ushort, ushort)>();
        var current = (targetX, targetY);
        while (current != (startX, startY))
        {
            path.Add(current);
            current = cameFrom[current];
        }

        path.Reverse();
        return path;
    }
}
