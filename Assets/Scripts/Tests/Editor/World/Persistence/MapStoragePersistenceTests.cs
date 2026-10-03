#nullable enable

using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Kern.Core.Lifecycle;
using Kern.Persistence;
using Kern.World;
using MinesServer.Data;
using NUnit.Framework;
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

    private static void DeleteIfPresent(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
