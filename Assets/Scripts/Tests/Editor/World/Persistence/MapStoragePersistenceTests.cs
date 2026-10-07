#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Kern.Core.Lifecycle;
using Kern.Persistence;
using Kern.World;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kern.Tests.World;

[TestFixture]
public sealed class MapStoragePersistenceTests
{
    [Test]
    public void DamagedPrimaryHeader_RestoresCompatibleBackupAndPreservesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"map_recovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string mapPath = Path.Combine(root, "world.map");
        string backupPath = Path.Combine(root, "world.map.backup");
        File.WriteAllBytes(mapPath, [1, 2, 3, 4]);

        using (var backup = new FileStream(backupPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            WorldLayerFileHeader.WriteHeader(backup, 1, 1, 32, new long[1]);
        }

        byte[] backupBytes = File.ReadAllBytes(backupPath);
        using var operations = new AsyncOperationSupervisor();
        try
        {
            using WorldLayer<CellType> layer = MapStorageDiskWriter.OpenWorldLayer(
                mapPath,
                1,
                1,
                operations,
                path => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite),
                backupPath);

            Assert.That(layer.GetChunkOffsets(), Is.EqualTo(new long[] { -1 }));
            Assert.That(File.ReadAllBytes(mapPath), Is.EqualTo(backupBytes));
            Assert.That(File.ReadAllBytes(backupPath), Is.EqualTo(backupBytes));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void InvalidPrimaryChunkOffset_RestoresCompatibleBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), $"map_offset_recovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string mapPath = Path.Combine(root, "world.map");
        string backupPath = Path.Combine(root, "world.map.backup");

        using (var primary = new FileStream(mapPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            using var writer = new BinaryWriter(primary, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(1);
            writer.Write(1);
            writer.Write(32);
            writer.Write(WorldLayerFileHeader.CurrentFormatVersion);
            writer.Write(1024L);
        }

        using (var backup = new FileStream(backupPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            WorldLayerFileHeader.WriteHeader(backup, 1, 1, 32, new long[1]);
        }

        byte[] backupBytes = File.ReadAllBytes(backupPath);
        using var operations = new AsyncOperationSupervisor();
        try
        {
            using WorldLayer<CellType> layer = MapStorageDiskWriter.OpenWorldLayer(
                mapPath,
                1,
                1,
                operations,
                path => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite),
                backupPath);

            Assert.That(layer.GetChunkOffsets(), Is.EqualTo(new long[] { -1 }));
            Assert.That(File.ReadAllBytes(mapPath), Is.EqualTo(backupBytes));
            Assert.That(File.ReadAllBytes(backupPath), Is.EqualTo(backupBytes));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void InvalidV0PrimaryChunkOffset_RestoresCompatibleBackupBeforeMigration()
    {
        string root = Path.Combine(Path.GetTempPath(), $"map_v0_offset_recovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string mapPath = Path.Combine(root, "world.map");
        string backupPath = Path.Combine(root, "world.map.backup");

        using (var primary = new FileStream(mapPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            using var writer = new BinaryWriter(primary, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(1);
            writer.Write(1);
            writer.Write(32);
            writer.Write(0);
            writer.Write(0L); // Inside the header, not a valid chunk payload offset.
        }

        using (var backup = new FileStream(backupPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            WorldLayerFileHeader.WriteHeader(backup, 1, 1, 32, new long[1]);
        }

        byte[] backupBytes = File.ReadAllBytes(backupPath);
        using var operations = new AsyncOperationSupervisor();
        try
        {
            using WorldLayer<CellType> layer = MapStorageDiskWriter.OpenWorldLayer(
                mapPath,
                1,
                1,
                operations,
                path => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite),
                backupPath);

            Assert.That(layer.GetChunkOffsets(), Is.EqualTo(new long[] { -1 }));
            Assert.That(File.ReadAllBytes(mapPath), Is.EqualTo(backupBytes));
            Assert.That(File.ReadAllBytes(backupPath), Is.EqualTo(backupBytes));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [UnityTest]
    public IEnumerator ConcurrentFlushesAndAsyncDispose_PreserveWorldData()
    {
        string worldCode = $"persistence_test_{Guid.NewGuid():N}";
        string mapPath = string.Empty;
        string backupPath = string.Empty;
        using var operations = new AsyncOperationSupervisor();
        var storage = new MapStorage(operations);
        var reopened = new MapStorage(operations);
        CellType expected = (CellType)123;

        try
        {
            storage.InitWorld(worldCode, width: 64, height: 32);
            mapPath = storage.MapFilePath;
            backupPath = storage.BackupMapFilePath;
            storage.SetCell(0, 0, expected);

            yield return UniTask.WhenAll(
                storage.FlushAsync(durable: false),
                storage.FlushAsync(durable: true)).ToCoroutine();
            var disposeTask = storage.DisposeAsync();
            yield return UniTask.WaitUntil(() => storage.IsDisposed).ToCoroutine();
            Assert.Throws<InvalidOperationException>(() => storage.SetCell(1, 0, expected));
            yield return disposeTask.ToCoroutine();

            reopened.InitWorld(worldCode, width: 64, height: 32);
            Assert.That(reopened.GetCell(0, 0), Is.EqualTo(expected));
        }
        finally
        {
            reopened.Dispose();
            storage.Dispose();
            DeleteIfPresent(mapPath);
            DeleteIfPresent(backupPath);
        }
    }

    // Пакет открытия мира приходит в той же пачке пакетов, что и регионы:
    // слой меняется посреди пачки, а её конец не должен падать и не должен
    // сообщать новому миру области прежнего.
    [TestCase(false)]
    [TestCase(true)]
    public void InitWorldInsideRegionBatch_EndsBatchOnTheNewLayer(bool worldOpenBefore)
    {
        string worldCode = $"batch_reinit_{Guid.NewGuid():N}";
        var paths = new List<string>();
        using var operations = new AsyncOperationSupervisor();
        var storage = new MapStorage(operations);
        var changed = new List<RectInt>();
        storage.RegionChanged += (x, y, w, h) => changed.Add(new RectInt(x, y, w, h));

        try
        {
            if (worldOpenBefore)
            {
                storage.InitWorld(worldCode + "_old", width: 64, height: 64);
                paths.Add(storage.MapFilePath);
                paths.Add(storage.BackupMapFilePath);

                // Чанк уже есть: повторная запись внутри пачки копится в область.
                storage.SetRegion(40, 40, 2, 2, Fill((CellType)5));
            }

            storage.BeginRegionBatch();
            if (worldOpenBefore)
            {
                storage.SetRegion(40, 40, 2, 2, Fill((CellType)6));
            }

            storage.InitWorld(worldCode, width: 32, height: 32);
            paths.Add(storage.MapFilePath);
            paths.Add(storage.BackupMapFilePath);
            storage.SetRegion(0, 0, 2, 2, Fill((CellType)7));
            changed.Clear();

            Assert.DoesNotThrow(storage.EndRegionBatch);
            Assert.That(changed, Is.Empty, "Область прежнего мира попала в новый.");
            Assert.That(storage.GetCell(1, 1), Is.EqualTo((CellType)7));
        }
        finally
        {
            storage.Dispose();
            foreach (string path in paths)
            {
                DeleteIfPresent(path);
            }
        }
    }

    [Test]
    public void BatchWithoutWorldOpened_DoesNotThrow()
    {
        using var operations = new AsyncOperationSupervisor();
        var storage = new MapStorage(operations);
        storage.BeginRegionBatch();
        Assert.DoesNotThrow(storage.EndRegionBatch);
    }

    [Test]
    public void EndBatchWithoutBegin_ThrowsInvalidOperation()
    {
        using var operations = new AsyncOperationSupervisor();
        var storage = new MapStorage(operations);
        Assert.Throws<InvalidOperationException>(storage.EndRegionBatch);
    }

    private static CellType[] Fill(CellType type) => [type, type, type, type];

    private static void DeleteIfPresent(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
