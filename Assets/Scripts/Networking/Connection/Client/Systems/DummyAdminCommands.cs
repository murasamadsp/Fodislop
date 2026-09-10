#nullable enable

using System;
using System.Globalization;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

/// <summary>
/// Административные команды, набираемые в глобальном чате.
/// </summary>
/// <remarks>
/// ПОЧЕМУ В ЧАТЕ, А НЕ ОТДЕЛЬНЫМ ОКНОМ. Команда — это строка с аргументами, и
/// поле ввода под неё уже есть. Отдельное окно потребовало бы формы под каждую
/// команду и жило бы отдельной жизнью от списка команд.
///
/// ПОЧЕМУ ЗДЕСЬ. Это сторона сервера: команда меняет состояние мира и отвечает
/// пакетами, как ответил бы настоящий сервер. Клиент о командах не знает
/// ничего — он отправляет обычное сообщение чата и получает обычные пакеты.
/// Когда сервер появится, разбор переедет туда, а клиент останется нетронутым.
///
/// Ответы приходят как сообщения глобального чата от отправителя «Сервер»:
/// отдельного канала под системный вывод в протоколе нет, а заводить его ради
/// трёх команд значит менять протокол под отладку.
/// </remarks>
internal sealed class DummyAdminCommands(
    Action<ServerPacket> sendPacket,
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState)
{
    private const string CommandPrefix = "/";

    /// <summary>Блок, который ставит <c>/set</c> без аргумента.</summary>
    private const CellType DefaultPlacedCell = CellType.SuperRainbow;

    private static readonly System.Drawing.Color _ServerColor =
        System.Drawing.Color.FromArgb(255, 255, 180, 60);

    /// <summary>
    /// Разбирает сообщение. Возвращает <c>true</c>, если это была команда и
    /// обычная рассылка в чат не нужна.
    /// </summary>
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
                Set(parts);
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

        playerState.SetPosition(x, y);
        sendPacket(new ServerPacket(new TeleportPacket(x, y, false)));
        Reply($"Телепорт в ({x}, {y}).");
    }

    private void Set(string[] parts)
    {
        CellType placed = DefaultPlacedCell;
        if (parts.Length >= 2 && !Enum.TryParse(parts[1], ignoreCase: true, out placed))
        {
            Reply($"Неизвестный тип клетки «{parts[1]}».");
            return;
        }

        (int offsetX, int offsetY) = playerState.Direction switch
        {
            Direction.Up => (0, -1),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            _ => (0, 0),
        };

        int targetX = playerState.X + offsetX;
        int targetY = playerState.Y + offsetY;
        if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue)
        {
            Reply("Перед роботом край мира.");
            return;
        }

        var cellX = (ushort)targetX;
        var cellY = (ushort)targetY;
        worldState.SetCell(cellX, cellY, placed);

        // Тот же пакет, которым отвечает постройка: клиент не должен различать,
        // откуда клетка изменилась — командой или обычным действием.
        sendPacket(new ServerPacket(new HBPacket(
        [
            new MapRegionPacket(cellX, cellY, 0, 0, [placed]),
        ])));

        Reply($"{placed} поставлен в ({cellX}, {cellY}).");
    }

    private static bool TryParseCoordinate(string text, out ushort value) =>
        ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void Reply(string text)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var message = new ChatMessagePacket(
            now,
            now,
            0,
            0,
            _ServerColor,
            "Сервер",
            _ServerColor,
            text);
        sendPacket(new ServerPacket(new ChatMessageListPacket("global", [message])));
    }
}
