#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyInventoryResponder
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Action<string, int, System.Drawing.Color, string> _activateBuff;
    private readonly List<(ushort X, ushort Y)> _teleportPositions;
    private readonly Action<int> _setHealth;
    private ItemType? _selectedItemType;

    public DummyInventoryResponder(
        Action<ServerPacket> sendPacket,
        Action<string, int, System.Drawing.Color, string> activateBuff,
        List<(ushort X, ushort Y)> teleportPositions,
        Action<int> setHealth)
    {
        _sendPacket = sendPacket ?? throw new ArgumentNullException(nameof(sendPacket));
        _activateBuff = activateBuff ?? throw new ArgumentNullException(nameof(activateBuff));
        _teleportPositions = teleportPositions ??
            throw new ArgumentNullException(nameof(teleportPositions));
        _setHealth = setHealth ?? throw new ArgumentNullException(nameof(setHealth));
    }

    public Dictionary<ItemType, long> Items { get; } = new();

    public void ReplaceItems(IEnumerable<KeyValuePair<ItemType, long>> items)
    {
        Items.Clear();
        foreach (KeyValuePair<ItemType, long> item in items)
        {
            Items[item.Key] = item.Value;
        }
    }

    public void Select(ItemType item)
    {
        _selectedItemType = item;
        var (name, description) = DummyItemInfo.GetItemInfo(item);
        _sendPacket(new ServerPacket(
            new MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket(
                item,
                name,
                description,
                1,
                1,
                3,
                false,
                new BitArray(0))));
    }

    public void Deselect()
    {
        _selectedItemType = null;
        _sendPacket(new ServerPacket(default(DeselectItemPacket)));
    }

    public void Use(ushort playerX, ushort playerY, Direction direction)
    {
        if (_selectedItemType is not { } selectedType)
        {
            return;
        }

        if (DummyItemInfo.IsBuildingPack(selectedType))
        {
            UseBuildingPack(selectedType, playerX, playerY, direction);
            return;
        }

        if (selectedType == ItemType.Rem)
        {
            _setHealth(500);
            _sendPacket(new ServerPacket(new HealthPacket(500, 500)));
        }
        else if (selectedType == ItemType.UpgradeBooster)
        {
            _activateBuff("xp3", 86400, System.Drawing.Color.FromArgb(0, 200, 0), "Прокачка x3");
        }
        else if (selectedType == ItemType.FreeUp)
        {
            _activateBuff("freeup", 43200, System.Drawing.Color.Cyan, "Freeup");
        }
        else if (selectedType == ItemType.MineBooster)
        {
            _activateBuff("x4", 43200, System.Drawing.Color.FromArgb(255, 165, 0), "Добыча x4");
        }
        else if (selectedType == ItemType.Battery)
        {
            _activateBuff("battery", 3600, System.Drawing.Color.FromArgb(65, 105, 225), "Аккумулятор");
        }

        DummyItemInfo.ConsumeItem(Items, selectedType, 1);
    }

    private void UseBuildingPack(
        ItemType selectedType,
        ushort playerX,
        ushort playerY,
        Direction direction)
    {
        PackType packType = DummyItemInfo.ItemTypeToPackType(selectedType);
        if (packType == PackType.None)
        {
            return;
        }

        ushort frontX = playerX;
        ushort frontY = playerY;
        switch (direction)
        {
            case Direction.Up:
                frontY--;
                break;
            case Direction.Down:
                frontY++;
                break;
            case Direction.Left:
                frontX--;
                break;
            case Direction.Right:
                frontX++;
                break;
        }

        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new PackPacket(frontX, frontY, packType, 0, 0),
        })));
        if (packType == PackType.Teleport)
        {
            _teleportPositions.Add((frontX, frontY));
        }

        DummyItemInfo.ConsumeItem(Items, selectedType, 1);
    }
}
