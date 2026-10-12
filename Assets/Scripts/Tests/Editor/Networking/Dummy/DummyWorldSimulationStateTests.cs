#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kern.Tests.Networking;

public sealed class DummyWorldSimulationStateTests
{
    [Test]
    public void EmptyState_ReturnsUnloadedCellsAndNoConfiguration()
    {
        using var state = new DummyWorldSimulationState(new StubSupervisor(), new Kern.Tests.Networking.UnavailableDummyWorldMapSource());

        Assert.That(state.HasLayer, Is.False);
        Assert.That(state.GetCell(10, 20), Is.EqualTo(CellType.Unloaded));
        Assert.That(state.GetCellConfig(CellType.Empty), Is.Null);
    }

    [Test]
    public async Task FailedInitialization_CanBeRetried()
    {
        using var state = new DummyWorldSimulationState(new StubSupervisor(), new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        int attempts = 0;

        UniTask FailOnce()
        {
            attempts++;
            return UniTask.FromException(new InvalidOperationException("injected failure"));
        }

        async UniTask<bool> FailsAsExpected()
        {
            try
            {
                await state.EnsureInitializedAsync(FailOnce);
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("injected failure"));
        bool failedAsExpected = await FailsAsExpected();
        Assert.That(failedAsExpected, Is.True);

        await state.EnsureInitializedAsync(() =>
            {
                attempts++;
                return UniTask.CompletedTask;
            });

        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task SuccessfulInitialization_IsIdempotentUntilReset()
    {
        using var state = new DummyWorldSimulationState(new StubSupervisor(), new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        int calls = 0;
        UniTask Initialize()
        {
            calls++;
            return UniTask.CompletedTask;
        }

        await state.EnsureInitializedAsync(Initialize);
        await state.EnsureInitializedAsync(Initialize);
        state.Reset();
        await state.EnsureInitializedAsync(Initialize);

        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void StartupInventory_SeedsCatalogAndDoublesBattery()
    {
        Dictionary<ItemType, long> inventory =
            DummyWorldStartupResponder.CreateInitialInventory(new StubItemCatalog());

        Assert.That(inventory[ItemType.Rem], Is.EqualTo(1));
        Assert.That(inventory[ItemType.Battery], Is.EqualTo(2));
    }

    [Test]
    public void CreateCellConfigurations_ConfiguresAllGameplayCellTypes()
    {
        CellConfigurationPacket[] configs = DummyCellConfigurationUtilities.CreateCellConfigurations();
        Assert.That(configs, Is.Not.Null);
        Assert.That(configs.Length, Is.EqualTo(256));

        foreach (CellType type in Enum.GetValues(typeof(CellType)))
        {
            Assert.That(
                BlockRegistry.Blocks.ContainsKey(type),
                Is.True,
                $"CellType '{type}' must be present in BlockRegistry.");
        }
    }

    [Test]
    public void CreateMovementSpeeds_ProvidesValidSpeedsForGameplayCellTypes()
    {
        CellConfigurationPacket[] configs = DummyCellConfigurationUtilities.CreateCellConfigurations();
        Dictionary<CellType, ushort> speeds = DummyCellConfigurationUtilities.CreateMovementSpeeds(configs);

        Assert.That(speeds, Is.Not.Null);
        Assert.That(speeds.ContainsKey(CellType.BackgroundWithLightTraces), Is.True);
        Assert.That(speeds.ContainsKey(CellType.BackgroundWithHeavyTraces), Is.True);
        Assert.That(speeds.ContainsKey(CellType.Skull), Is.True);
        Assert.That(speeds[CellType.BackgroundWithLightTraces], Is.GreaterThan(0));
        Assert.That(speeds[CellType.BackgroundWithHeavyTraces], Is.GreaterThan(0));
        Assert.That(speeds[CellType.Skull], Is.GreaterThan(0));

        foreach (CellType type in Enum.GetValues(typeof(CellType)))
        {
            Assert.That(
                speeds.TryGetValue(type, out ushort speed) && speed > 0,
                Is.True,
                $"CellType '{type}' must have a movement speed greater than zero.");
        }
    }

    [Test]
    public void GetBlockDefinition_MatchesConfiguredPropertiesAndCrystalBaskets()
    {
        Assert.That(DummyCellConfigurationUtilities.GetMinedCrystal(CellType.Green), Is.EqualTo(CrystalType.Green));
        Assert.That(DummyCellConfigurationUtilities.GetMinedCrystal(CellType.Cyan), Is.EqualTo(CrystalType.Cyan));
        Assert.That(DummyCellConfigurationUtilities.GetMinedCrystal(CellType.Road), Is.EqualTo(CrystalType.Unknown));

        BlockDefinition green = DummyCellConfigurationUtilities.GetBlockDefinition(CellType.Green);
        Assert.That(green.IsPassable, Is.False);
        Assert.That(green.Outline, Is.EqualTo(CellOutline.Wavy));
        Assert.That(green.MapColor, Is.EqualTo(new Color32(0x08, 0xD7, 0x64, 0xFF)));

        BlockDefinition road = DummyCellConfigurationUtilities.GetBlockDefinition(CellType.Road);
        Assert.That(road.IsPassable, Is.True);
        Assert.That(road.MapColor, Is.EqualTo(new Color32(0x44, 0x44, 0x44, 0xFF)));
        Assert.That(road.DecalAtlas, Is.EqualTo(CellDecalAtlas.None));

        BlockDefinition buildingRoad = DummyCellConfigurationUtilities.GetBlockDefinition(CellType.BuildingRoad);
        Assert.That(buildingRoad.Outline, Is.EqualTo(CellOutline.Rigid));

        BlockDefinition lava = DummyCellConfigurationUtilities.GetBlockDefinition(CellType.Lava);
        Assert.That(lava.Outline, Is.EqualTo(CellOutline.Round));
        Assert.That(lava.SurfaceEffect, Is.EqualTo(CellSurfaceEffect.Molten));

        BlockDefinition xgreen = DummyCellConfigurationUtilities.GetBlockDefinition(CellType.XGreen);
        Assert.That(xgreen.SurfaceEffect, Is.EqualTo(CellSurfaceEffect.Prismatic));
        Assert.That(xgreen.SurfaceEffectPalette, Is.EqualTo(1));

        byte[][] tileGroups = DummyCellConfigurationUtilities.CreateTileGroups();
        Assert.That(tileGroups.Length, Is.EqualTo(1));
        Assert.That(tileGroups[0], Is.EquivalentTo(new byte[] { 37, 38, 106 }));

        CellConfigurationPacket[] configs = DummyCellConfigurationUtilities.CreateCellConfigurations();
        Dictionary<CellType, ushort> speeds = DummyCellConfigurationUtilities.CreateMovementSpeeds(configs);
        Assert.That(speeds.ContainsKey(CellType.Unloaded), Is.True);
        Assert.That(speeds.ContainsKey(CellType.Pregener), Is.True);
    }

    private sealed class StubSupervisor : IAsyncOperationSupervisor
    {
        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
            throw new AssertionException($"Unexpected operation '{operationName}'.");
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.CompletedTask;
        }
    }

    private sealed class StubItemCatalog : IItemCatalog
    {
        public IEnumerable<ItemType> AllTypes => [ItemType.Rem, ItemType.Battery];

        public string GetName(ItemType type) => type.ToString();

        public string GetDescription(ItemType type) => string.Empty;

        public Texture2D? GetIcon(ItemType type) => null;
    }
}
