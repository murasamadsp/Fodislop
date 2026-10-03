#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

/// <summary>
/// Раз в 50 мс меняет случайную твёрдую клетку в радиусе видимости игрока на
/// случайный блок — тем же пакетом, каким сервер сообщает о копании. Нагружает
/// путь правки мира: террейн, свет, AO и карту, не засыпая пустоту.
/// </summary>
internal sealed class DummyBlockSpawner(
    IAsyncOperationSupervisor operations,
    IDummyClock clock,
    DummyWorldSimulationState worldState,
    DummyPlayerSimulationState playerState,
    Action<ServerPacket> sendPacket,
    Func<int, bool> loopAlive,
    Func<CellType, bool> hasCellTexture)
{
    private const int IntervalMilliseconds = 50;

    // Полувысота кадра при обычном зуме: клетка в этом круге видна на экране.
    private const int RadiusCells = (int)ProjectRuntimeContracts.Camera.ReferenceOrthographicSize;

    // Выключен по умолчанию: мир, меняющийся 20 раз в секунду вокруг игрока,
    // загрязняет любое наблюдение за краями блоков, светом и AO. Включается
    // отладочной кнопкой меню паузы (тег toggle_block_spawner).
    public bool Enabled { get; set; }

    public void Start(int lifecycleVersion, DummyWorldDescriptor world)
    {
        CellType[] blocks = CollectBlocks(world.CellConfigurations, hasCellTexture);
        if (blocks.Length == 0)
        {
            return;
        }

        operations.Run(
            "dummy_block_spawner",
            cancellationToken => RunAsync(lifecycleVersion, world.Width, world.Height, blocks, cancellationToken));
    }

    // Блок — объявленный тип с конфигурацией, сквозь который нельзя пройти и
    // текстуру которого заглушка может отдать. Тип без текстуры клиент
    // запрашивал впустую: ошибка загрузки со стеком и цвет карты вместо блока.
    internal static CellType[] CollectBlocks(
        CellConfigurationPacket[] configurations,
        Func<CellType, bool> hasCellTexture)
    {
        var blocks = new List<CellType>();
        for (int index = 0; index < configurations.Length && index <= byte.MaxValue; index++)
        {
            var type = (CellType)index;
            if (type != CellType.Unloaded &&
                Enum.IsDefined(typeof(CellType), type) &&
                (configurations[index].Properties & CellConfigProperties.Passable) == 0 &&
                hasCellTexture(type))
            {
                blocks.Add(type);
            }
        }

        return blocks.ToArray();
    }

    private async UniTask RunAsync(
        int lifecycleVersion,
        int worldWidth,
        int worldHeight,
        CellType[] blocks,
        CancellationToken cancellationToken)
    {
        while (loopAlive(lifecycleVersion))
        {
            await clock.Delay(IntervalMilliseconds, cancellationToken);
            if (!loopAlive(lifecycleVersion))
            {
                break;
            }

            if (Enabled)
            {
                TrySpawn(worldWidth, worldHeight, blocks);
            }
        }
    }

    private void TrySpawn(int worldWidth, int worldHeight, CellType[] blocks)
    {
        int offsetX;
        int offsetY;
        do
        {
            offsetX = clock.Random.Next(-RadiusCells, RadiusCells + 1);
            offsetY = clock.Random.Next(-RadiusCells, RadiusCells + 1);
        }
        while ((offsetX * offsetX) + (offsetY * offsetY) > RadiusCells * RadiusCells);

        // Клетка робота остаётся свободной: блок в ней замуровал бы игрока.
        if (offsetX == 0 && offsetY == 0)
        {
            return;
        }

        int x = playerState.X + offsetX;
        int y = playerState.Y + offsetY;
        if (x < 0 || y < 0 || x >= worldWidth || y >= worldHeight ||
            !worldState.TryGetCell((ushort)x, (ushort)y, out CellType current))
        {
            return;
        }

        // Меняется только твёрдая клетка. Блок в пустоте за минуту засыпал
        // весь круг вокруг робота: тоннели закрывались, свет снаружи не
        // доходил, и всё дальше лампы робота становилось чёрным.
        if (worldState.GetCellConfig(current) is not { } currentConfig ||
            (currentConfig.Properties & CellConfigProperties.Passable) != 0)
        {
            return;
        }

        CellType block = blocks[clock.Random.Next(blocks.Length)];
        if (block == current)
        {
            return;
        }

        worldState.SetCell((ushort)x, (ushort)y, block);
        sendPacket(new ServerPacket(new HBPacket([
            new MapRegionPacket((ushort)x, (ushort)y, 0, 0, [block]),
        ])));
    }
}
