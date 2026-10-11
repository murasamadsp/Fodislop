#nullable enable

using System;
using System.Globalization;
using Kern.Core;
using Kern.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyAdminCommands
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyWorldSimulationState _worldState;
    private readonly IDummyClock _clock;
    internal DummyAdminCommands(
    Action<ServerPacket> sendPacket,
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    IDummyClock clock)
    {
        _sendPacket = sendPacket;
        _playerState = playerState;
        _worldState = worldState;
        _clock = clock;
    }

    private const string CommandPrefix = "/";

    private const CellType DefaultPlacedCell = CellType.SuperRainbow;
    private const int FillWidth = 100;
    private const int FillHeight = 100;
    private const CellType DefaultFillCell = CellType.SuperRainbow;

    private static readonly System.Drawing.Color s_serverColor =
        System.Drawing.Color.FromArgb(255, 255, 180, 60);

    public bool TryHandle(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string trimmed = message.Trim();
        if (!trimmed.StartsWith(CommandPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = trimmed[CommandPrefix.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "help":
                Help();
                return true;
            case "tp":
                Teleport(parts);
                return true;
            case "set":
                ApplySetCommand(parts);
                return true;
            case "fill":
                ApplyFillCommand(parts);
                return true;
            default:
                Reply($"Неизвестная команда «{parts[0]}». Список — /help");
                return true;
        }
    }

    private void Help()
    {
        Reply("Команды:");
        Reply("/help — этот список");
        Reply("/tp <x> <y> — телепорт в указанную клетку");
        Reply($"/set [тип] — поставить блок перед роботом (без аргумента — {DefaultPlacedCell})");
        Reply(
            $"/fill [тип] — заполнить область {FillWidth}x{FillHeight} вокруг робота " +
            $"(без аргумента — {DefaultFillCell})");
    }

    private void Teleport(string[] parts)
    {
        if (parts.Length < 3)
        {
            Reply("Нужны обе координаты: /tp <x> <y>");
            return;
        }

        if (!TryParseCoordinate(parts[1], out ushort x) ||
            !TryParseCoordinate(parts[2], out ushort y))
        {
            Reply("Координаты — целые числа от 0 до 65535.");
            return;
        }

        _playerState.SetPosition(x, y);
        _worldState.QueueChunksAround(x, y, _sendPacket);
        _sendPacket(new ServerPacket(new TeleportPacket(x, y, false)));
        Reply($"Телепорт в ({x}, {y}).");
    }

    private void ApplySetCommand(string[] parts)
    {
        CellType placed = DefaultPlacedCell;
        if (parts.Length >= 2 && !Enum.TryParse(parts[1], ignoreCase: true, out placed))
        {
            Reply($"Неизвестный тип клетки «{parts[1]}».");
            return;
        }

        (int offsetX, int offsetY) = _playerState.Direction switch
        {
            Direction.Up => (0, -1),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            _ => (0, 0),
        };

        int targetX = _playerState.X + offsetX;
        int targetY = _playerState.Y + offsetY;
        if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue)
        {
            Reply("Перед роботом край мира.");
            return;
        }

        var cellX = (ushort)targetX;
        var cellY = (ushort)targetY;
        _worldState.SetCell(cellX, cellY, placed);

        // Тот же пакет, которым отвечает постройка: клиент не должен различать,
        // откуда клетка изменилась — командой или обычным действием.
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new MapRegionPacket(cellX, cellY, 0, 0, new[] { placed }),
        })));

        Reply($"{placed} поставлен в ({cellX}, {cellY}).");
    }

    private void ApplyFillCommand(string[] parts)
    {
        CellType fillType = DefaultFillCell;
        if (parts.Length >= 2 && !Enum.TryParse(parts[1], ignoreCase: true, out fillType))
        {
            Reply($"Неизвестный тип клетки «{parts[1]}».");
            return;
        }

        WorldLayer<CellType>? layer = _worldState.Layer;
        if (layer == null)
        {
            Reply("Мир ещё не загружен.");
            return;
        }

        int worldWidth = Math.Min(layer.WidthChunks * layer.ChunkSize, ushort.MaxValue + 1);
        int worldHeight = Math.Min(layer.HeightChunks * layer.ChunkSize, ushort.MaxValue + 1);
        if (worldWidth < FillWidth || worldHeight < FillHeight)
        {
            Reply($"Мир меньше требуемой области {FillWidth}x{FillHeight}.");
            return;
        }

        int maxX = worldWidth - FillWidth;
        int maxY = worldHeight - FillHeight;
        int startX = Math.Clamp(_playerState.X - (FillWidth / 2), 0, maxX);
        int startY = Math.Clamp(_playerState.Y - (FillHeight / 2), 0, maxY);

        var payload = new CellType[FillWidth * FillHeight];
        Array.Fill(payload, fillType);
        layer.SetRegion(startX, startY, FillWidth, FillHeight, payload);

        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new MapRegionPacket(
                (ushort)startX,
                (ushort)startY,
                (byte)(FillWidth - 1),
                (byte)(FillHeight - 1),
                payload),
        })));

        Reply($"Область {FillWidth}x{FillHeight} заполнена клеткой {fillType} от ({startX}, {startY}).");
    }

    private static bool TryParseCoordinate(string text, out ushort value) =>
        ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void Reply(string text)
    {
        long now = DummyClockTime.UnixMilliseconds(_clock);
        var message = new ChatMessagePacket(
            now,
            now,
            0,
            0,
            s_serverColor,
            "Сервер",
            s_serverColor,
            text);
        _sendPacket(new ServerPacket(new ChatMessageListPacket(
            ProjectRuntimeContracts.Chat.GlobalChannelTag,
            new[] { message })));
    }
}
