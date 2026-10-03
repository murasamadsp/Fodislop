#nullable enable

namespace Kern.Tests.World;

using System;
using System.IO;
using Kern.Persistence;
using NUnit.Framework;

[TestFixture]
public sealed class WorldChunkV2CodecTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void EncodeDecode_RepresentativeData_RoundTrips(int pattern)
    {
        const int area = 1024;
        byte[] expected = new byte[area];
        var random = new Random(741);
        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = pattern switch
            {
                0 => 7,
                1 => (byte)(i % 2),
                2 => (byte)(i % 7),
                _ => (byte)random.Next(0, 123),
            };
        }

        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.EncodeChunk(writer, expected, area, chunkIndex: 0);
        }

        memory.Position = 0;
        byte[] actual;
        using (var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            actual = WorldChunkV2Codec.DecodeChunk<byte>(reader, area, chunkIndex: 0);
        }

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    public void VisitChunkRuns_EmitsAdjacentEqualValuesAndValidatesBeforeCallbacks()
    {
        byte[] expected = [3, 3, 3, 9, 9, 2, 2, 2];
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.EncodeChunk(writer, expected, expected.Length, chunkIndex: 12);
        }

        memory.Position = 0;
        var runs = new System.Collections.Generic.List<(byte Value, int Count)>();
        using (var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.VisitChunkRuns<byte>(reader, expected.Length, 12,
                (index, value, count) =>
                {
                    Assert.AreEqual(12, index);
                    runs.Add((value, count));
                });
        }

        CollectionAssert.AreEqual(new[] { ((byte)3, 3), ((byte)9, 2), ((byte)2, 3) }, runs);
    }

    [Test]
    public void VisitChunkRuns_BytePalette_ReconstructsCellsWithoutChunkArray()
    {
        const int area = 1024;
        var random = new Random(2891);
        byte[] expected = new byte[area];
        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = (byte)random.Next(0, 4);
        }

        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.EncodeChunk(writer, expected, area, chunkIndex: 4);
        }

        Assert.AreEqual(3, memory.GetBuffer()[4]);
        memory.Position = 0;
        var visited = new System.Collections.Generic.List<byte>(area);
        using (var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.VisitChunkRuns<byte>(reader, area, 4,
                (_, value, count) =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        visited.Add(value);
                    }
                });
        }

        CollectionAssert.AreEqual(expected, visited);
    }

    [Test]
    public void DecodeChunk_CorruptPayload_RejectsChecksum()
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.EncodeChunk(writer, new byte[] { 1, 2, 3, 4 }, 4, chunkIndex: 0);
        }

        byte[] bytes = memory.ToArray();
        bytes[^1] ^= 0x40;
        memory.Position = 0;
        memory.Write(bytes);
        memory.Position = 0;
        using var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.Throws<InvalidDataException>(() => WorldChunkV2Codec.DecodeChunk<byte>(reader, 4, chunkIndex: 0));

        memory.Position = 0;
        int visitorCalls = 0;
        using var visitReader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.Throws<InvalidDataException>(() =>
            WorldChunkV2Codec.VisitChunkRuns<byte>(visitReader, 4, 0, (_, _, _) => visitorCalls++));
        Assert.AreEqual(0, visitorCalls);
    }

    [Test]
    public void IndexedFrame_RejectsBeingAddressedAsAnotherChunk()
    {
        byte[] expected = [4, 4, 7, 9];
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.EncodeChunk(writer, expected, expected.Length, chunkIndex: 12);
        }

        memory.Position = 0;
        using (var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Assert.Throws<InvalidDataException>(() =>
                WorldChunkV2Codec.DecodeChunk<byte>(reader, expected.Length, chunkIndex: 13));
        }

        memory.Position = 0;
        using (var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            CollectionAssert.AreEqual(
                expected,
                WorldChunkV2Codec.DecodeChunk<byte>(reader, expected.Length, chunkIndex: 12));
        }

        memory.Position = 0;
        int visitorCalls = 0;
        using var visitReader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.Throws<InvalidDataException>(() => WorldChunkV2Codec.VisitChunkRuns<byte>(
            visitReader,
            expected.Length,
            13,
            (_, _, _) => visitorCalls++));
        Assert.AreEqual(0, visitorCalls);
    }

    [Test]
    public void LegacyV2Frame_IsRejectedByCurrentIndexedReader()
    {
        byte[] expected = [4, 4, 7, 9];
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkV2Codec.EncodeLegacyV2Chunk(writer, expected, expected.Length);
        }

        memory.Position = 0;
        using (var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Assert.Throws<InvalidDataException>(() =>
                WorldChunkV2Codec.DecodeChunk<byte>(reader, expected.Length, chunkIndex: 0));
        }

        memory.Position = 0;
        using var legacyReader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        CollectionAssert.AreEqual(expected, WorldChunkV2Codec.DecodeChunk<byte>(legacyReader, expected.Length));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void DecodeChunk_NonPositiveArea_ThrowsArgumentOutOfRange(int chunkArea)
    {
        using var reader = new BinaryReader(new MemoryStream());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldChunkV2Codec.DecodeChunk<byte>(reader, chunkArea));
    }

    [Test]
    public void MigrateV1ToCurrent_RewritesPayloadAndPreservesBackup()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "kern_v1_to_v2_" + Guid.NewGuid().ToString("N") + ".map");
        const int width = 1;
        const int height = 1;
        const int chunkSize = 2;
        byte[] expected = [5, 5, 8, 9];
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Write(BitConverter.GetBytes(width));
                stream.Write(BitConverter.GetBytes(height));
                stream.Write(BitConverter.GetBytes(chunkSize));
                stream.Write(BitConverter.GetBytes(WorldLayerFileHeader.LegacyRLEFormatVersion));
                stream.Write(BitConverter.GetBytes(24L));
                stream.Position = 24;
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                WorldChunkRLECodec.EncodeChunk(writer, expected, expected.Length);
                stream.Flush(true);
            }

            Assert.AreEqual(1, WorldLayerFileMigrator.MigrateV1ToCurrent<byte>(filePath, width, height, chunkSize));
            Assert.IsTrue(File.Exists(filePath + ".v1.backup"));
            using var migrated = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long[] offsetsAfter = new long[1];
            Assert.IsTrue(WorldLayerFileHeader.TryReadHeader(migrated, width, height, chunkSize, offsetsAfter));
            migrated.Position = offsetsAfter[0];
            using var reader = new BinaryReader(migrated, System.Text.Encoding.UTF8, leaveOpen: true);
            CollectionAssert.AreEqual(expected, WorldChunkV2Codec.DecodeChunk<byte>(reader, expected.Length, chunkIndex: 0));
        }
        finally
        {
            foreach (string path in new[] { filePath, filePath + ".v1.backup", filePath + ".v3.migrate.tmp" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Test]
    public void MigrateV2ToCurrent_BindsFramesToTheirChunkIndices()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "kern_v2_to_v3_" + Guid.NewGuid().ToString("N") + ".map");
        const int width = 1;
        const int height = 1;
        const int chunkSize = 2;
        byte[] expected = [1, 1, 2, 3];
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(width);
                writer.Write(height);
                writer.Write(chunkSize);
                writer.Write(WorldLayerFileHeader.LegacyFramedFormatVersion);
                writer.Write(24L);
                stream.Position = 24;
                WorldChunkV2Codec.EncodeLegacyV2Chunk(writer, expected, expected.Length);
                writer.Flush();
                stream.Flush(true);
            }

            Assert.AreEqual(1, WorldLayerFileMigrator.MigrateV2ToCurrent<byte>(filePath, width, height, chunkSize));
            Assert.IsTrue(File.Exists(filePath + ".v2.backup"));
            using var migrated = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long[] offsets = new long[1];
            Assert.IsTrue(WorldLayerFileHeader.TryReadHeader(migrated, width, height, chunkSize, offsets));
            Assert.AreEqual(WorldLayerFileHeader.CurrentFormatVersion, WorldLayerFileHeader.TryReadFormatVersion(migrated));
            migrated.Position = offsets[0];
            using var reader = new BinaryReader(migrated, System.Text.Encoding.UTF8, leaveOpen: true);
            CollectionAssert.AreEqual(expected, WorldChunkV2Codec.DecodeChunk<byte>(reader, expected.Length, 0));
        }
        finally
        {
            foreach (string path in new[] { filePath, filePath + ".v2.backup", filePath + ".v3.migrate.tmp" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestCase(WorldLayerFileHeader.LegacyRLEFormatVersion)]
    [TestCase(WorldLayerFileHeader.LegacyFramedFormatVersion)]
    public void LegacyMigration_DuplicateChunkOffsetsProduceZeroChunks(int formatVersion)
    {
        string filePath = Path.Combine(Path.GetTempPath(), "kern_duplicate_offsets_" + Guid.NewGuid().ToString("N") + ".map");
        const int width = 2;
        const int height = 1;
        const int chunkSize = 2;
        const int chunkArea = chunkSize * chunkSize;
        const long sharedChunkOffset = WorldLayerFileHeader.HeaderSize + (width * sizeof(long));
        byte[] duplicatedCells = [7, 7, 7, 7];
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(width);
                writer.Write(height);
                writer.Write(chunkSize);
                writer.Write(formatVersion);
                writer.Write(sharedChunkOffset);
                writer.Write(sharedChunkOffset);
                stream.Position = sharedChunkOffset;
                if (formatVersion == WorldLayerFileHeader.LegacyRLEFormatVersion)
                {
                    WorldChunkRLECodec.EncodeChunk(writer, duplicatedCells, chunkArea);
                }
                else
                {
                    WorldChunkV2Codec.EncodeLegacyV2Chunk(writer, duplicatedCells, chunkArea);
                }

                writer.Flush();
                stream.Flush(true);
            }

            int migratedChunks = formatVersion == WorldLayerFileHeader.LegacyRLEFormatVersion
                ? WorldLayerFileMigrator.MigrateV1ToCurrent<byte>(filePath, width, height, chunkSize)
                : WorldLayerFileMigrator.MigrateV2ToCurrent<byte>(filePath, width, height, chunkSize);
            Assert.That(migratedChunks, Is.EqualTo(2));

            using var migrated = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long[] offsets = new long[width * height];
            Assert.That(WorldLayerFileHeader.TryReadHeader(migrated, width, height, chunkSize, offsets), Is.True);
            using var reader = new BinaryReader(migrated, System.Text.Encoding.UTF8, leaveOpen: true);
            for (int chunkIndex = 0; chunkIndex < offsets.Length; chunkIndex++)
            {
                migrated.Position = offsets[chunkIndex];
                CollectionAssert.AreEqual(
                    new byte[chunkArea],
                    WorldChunkV2Codec.DecodeChunk<byte>(reader, chunkArea, chunkIndex));
            }
        }
        finally
        {
            string backupSuffix = formatVersion == WorldLayerFileHeader.LegacyRLEFormatVersion
                ? ".v1.backup"
                : ".v2.backup";
            foreach (string path in new[] { filePath, filePath + backupSuffix, filePath + ".v3.migrate.tmp" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestCase(0)]
    [TestCase(5)]
    public void MigrateV1ToCurrent_ReplacesMalformedChunkWithZeroes(int malformedRunCount)
    {
        string filePath = Path.Combine(Path.GetTempPath(), "kern_v1_corrupt_" + Guid.NewGuid().ToString("N") + ".map");
        const int chunkArea = 4;
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1);
                writer.Write(1);
                writer.Write(2);
                writer.Write(WorldLayerFileHeader.LegacyRLEFormatVersion);
                writer.Write(24L);
                stream.Position = 24;
                writer.Write((ushort)malformedRunCount);
                writer.Write((byte)7);
                writer.Flush();
            }

            Assert.AreEqual(1, WorldLayerFileMigrator.MigrateV1ToCurrent<byte>(filePath, 1, 1, 2));
            using var migrated = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long[] offsets = new long[1];
            Assert.IsTrue(WorldLayerFileHeader.TryReadHeader(migrated, 1, 1, 2, offsets));
            migrated.Position = offsets[0];
            using var reader = new BinaryReader(migrated, System.Text.Encoding.UTF8, leaveOpen: true);
            CollectionAssert.AreEqual(new byte[chunkArea], WorldChunkV2Codec.DecodeChunk<byte>(reader, chunkArea, chunkIndex: 0));
        }
        finally
        {
            foreach (string path in new[] { filePath, filePath + ".v1.backup", filePath + ".v1.backup.tmp", filePath + ".v3.migrate.tmp" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Test]
    public void MigrateV1ToCurrent_DoesNotDecodeAcrossTheNextStoredChunkOffset()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "kern_v1_cross_chunk_" + Guid.NewGuid().ToString("N") + ".map");
        const int chunkArea = 4;
        const long firstChunkOffset = 32;
        const long secondChunkOffset = 35;
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(2);
                writer.Write(1);
                writer.Write(2);
                writer.Write(WorldLayerFileHeader.LegacyRLEFormatVersion);
                writer.Write(firstChunkOffset);
                writer.Write(secondChunkOffset);
                stream.Position = firstChunkOffset;
                writer.Write((ushort)2);
                writer.Write((byte)7);
                stream.Position = secondChunkOffset;
                WorldChunkRLECodec.EncodeChunk(writer, new byte[] { 9, 9, 9, 9 }, chunkArea);
                writer.Flush();
            }

            Assert.AreEqual(2, WorldLayerFileMigrator.MigrateV1ToCurrent<byte>(filePath, 2, 1, 2));
            using var migrated = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long[] offsets = new long[2];
            Assert.IsTrue(WorldLayerFileHeader.TryReadHeader(migrated, 2, 1, 2, offsets));
            using var reader = new BinaryReader(migrated, System.Text.Encoding.UTF8, leaveOpen: true);

            migrated.Position = offsets[0];
            CollectionAssert.AreEqual(new byte[chunkArea], WorldChunkV2Codec.DecodeChunk<byte>(reader, chunkArea, chunkIndex: 0));
            migrated.Position = offsets[1];
            CollectionAssert.AreEqual(
                new byte[] { 9, 9, 9, 9 },
                WorldChunkV2Codec.DecodeChunk<byte>(reader, chunkArea, chunkIndex: 1));
        }
        finally
        {
            foreach (string path in new[] { filePath, filePath + ".v1.backup", filePath + ".v1.backup.tmp", filePath + ".v3.migrate.tmp" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

}
