#nullable enable

using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Core.Lifecycle;

public sealed class ItemRegistryAdapter : IItemCatalog
{
    public IEnumerable<ItemType> AllTypes => ItemRegistry.AllTypes;

    public string GetName(ItemType type) => ItemRegistry.GetName(type);

    public string GetDescription(ItemType type) => ItemRegistry.GetDescription(type);

    public Texture2D? GetIcon(ItemType type) => ItemRegistry.GetIcon(type);
}
