#nullable enable

namespace Fodinae.Tests.World;

using System;
using System.IO;
using Fodinae.Persistence;
using NUnit.Framework;

[TestFixture]
public class WorldLayerFileHeaderTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fodinae_header_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void WriteHeader_ThenTryReadHeader_SucceedsAndMatches()
    {
        const int width = 4;
        const int height = 8;
        const int chunkSize = 16;
        long[] offsets = new long[width * height];

        using var memory = new MemoryStream();
        WorldLayerFileHeader.WriteHeader(memory, width, height, chunkSize, offsets);

        long[] readOffsets = new long[width * height];
        bool success = WorldLayerFileHeader.TryReadHeader(memory, width, height, chunkSize, readOffsets);

        Assert.IsTrue(success);
        for (int i = 0; i < offsets.Length; i++)
        {
            Assert.AreEqual(-1L, readOffsets[i]);
        }
    }

    [Test]
    public void TryReadHeader_DimensionMismatch_ReturnsFalse()
    {
        const int width = 4;
        const int height = 8;
        const int chunkSize = 16;
        long[] offsets = new long[width * height];

        using var memory = new MemoryStream();
        WorldLayerFileHeader.WriteHeader(memory, width, height, chunkSize, offsets);

        long[] readOffsets = new long[width * height];
        bool success = WorldLayerFileHeader.TryReadHeader(memory, width + 1, height, chunkSize, readOffsets);

        Assert.IsFalse(success);
    }

    [Test]
    public void TryReadHeader_TruncatedFile_ReturnsFalse()
    {
        using var memory = new MemoryStream();
        memory.Write(new byte[8]); // Less than 16 bytes

        long[] readOffsets = new long[32];
        bool success = WorldLayerFileHeader.TryReadHeader(memory, 4, 8, 16, readOffsets);

        Assert.IsFalse(success);
    }

    [Test]
    public void WriteChunkOffset_UpdatesSpecifiedEntry()
    {
        const int width = 2;
        const int height = 2;
        const int chunkSize = 16;
        long[] offsets = new long[width * height];

        using var memory = new MemoryStream();
        WorldLayerFileHeader.WriteHeader(memory, width, height, chunkSize, offsets);

        WorldLayerFileHeader.WriteChunkOffset(memory, 1, 1024L);
        WorldLayerFileHeader.WriteChunkOffset(memory, 3, 2048L);

        long[] readOffsets = new long[width * height];
        bool success = WorldLayerFileHeader.TryReadHeader(memory, width, height, chunkSize, readOffsets);

        Assert.IsTrue(success);
        Assert.AreEqual(-1L, readOffsets[0]);
        Assert.AreEqual(1024L, readOffsets[1]);
        Assert.AreEqual(-1L, readOffsets[2]);
        Assert.AreEqual(2048L, readOffsets[3]);
    }

    [Test]
    public void MigrateLegacyFormat_V0Format_MigratesToV1AndBackupsOriginal()
    {
        string filePath = Path.Combine(_tempDir, "legacy.map");
        const int width = 2;
        const int height = 2;
        const int chunkSize = 16;

        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs))
        {
            writer.Write(width);
            writer.Write(height);
            writer.Write(chunkSize);
            writer.Write(0); // v0 format
            writer.Write(-1L);
            writer.Write(-1L);
            writer.Write(-1L);
            writer.Write(-1L);
        }

        WorldLayerFileHeader.MigrateLegacyFormatIfRequired(filePath, width, height, chunkSize);

        // Verify that backup file was created
        string backupPath = filePath + ".v0.backup";
        Assert.IsTrue(File.Exists(backupPath));

        // Verify migrated file has version 1
        long[] offsets = new long[4];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            bool success = WorldLayerFileHeader.TryReadHeader(fs, width, height, chunkSize, offsets);
            Assert.IsTrue(success);
        }
    }

    [Test]
    public void MigrateLegacyFormat_AlreadyV1_LeavesUntouched()
    {
        string filePath = Path.Combine(_tempDir, "v1.map");
        const int width = 2;
        const int height = 2;
        const int chunkSize = 16;

        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            long[] offsets = new long[4];
            WorldLayerFileHeader.WriteHeader(fs, width, height, chunkSize, offsets);
        }

        WorldLayerFileHeader.MigrateLegacyFormatIfRequired(filePath, width, height, chunkSize);

        string backupPath = filePath + ".v0.backup";
        Assert.IsFalse(File.Exists(backupPath));
    }
}
