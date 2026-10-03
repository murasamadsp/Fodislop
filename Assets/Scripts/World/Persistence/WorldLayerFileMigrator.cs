#nullable enable

namespace Kern.Persistence;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>Rewrites legacy whole-layer files into the current indexed chunk format.</summary>
internal static class WorldLayerFileMigrator
{
    private sealed class BoundedReadStream(Stream source) : Stream
    {
        private long _remaining;

        public void SetRange(long offset, long length)
        {
            source.Seek(offset, SeekOrigin.Begin);
            _remaining = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int acceptedCount = (int)Math.Min(count, _remaining);
            int read = source.Read(buffer, offset, acceptedCount);
            _remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int acceptedCount = (int)Math.Min(buffer.Length, _remaining);
            int read = source.Read(buffer[..acceptedCount]);
            _remaining -= read;
            return read;
        }

        public override int ReadByte()
        {
            if (_remaining <= 0)
            {
                return -1;
            }

            int value = source.ReadByte();
            if (value >= 0)
            {
                _remaining--;
            }

            return value;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal static int MigrateV1ToCurrent<T>(
        string filePath,
        int expectedWidth,
        int expectedHeight,
        int chunkSize)
        where T : unmanaged
    {
        string tempPath = filePath + ".v3.migrate.tmp";
        string backupPath = filePath + ".v1.backup";
        int migratedChunks = 0;
        try
        {
            using (var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var reader = new BinaryReader(source, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                if (source.Length < WorldLayerFileHeader.HeaderSize)
                {
                    throw new InvalidDataException($"Map file '{filePath}' is not a compatible v1 map.");
                }

                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                int storedChunkSize = reader.ReadInt32();
                int version = reader.ReadInt32();
                if (version != WorldLayerFileHeader.LegacyRLEFormatVersion)
                {
                    throw new InvalidDataException($"Map file '{filePath}' is not a v1 RLE map.");
                }

                if (width != expectedWidth || height != expectedHeight || storedChunkSize != chunkSize)
                {
                    return -1;
                }

                int chunkCount = checked(expectedWidth * expectedHeight);
                long tableEnd = checked(WorldLayerFileHeader.HeaderSize + (long)chunkCount * sizeof(long));
                if (source.Length < tableEnd)
                {
                    throw new InvalidDataException($"Map file '{filePath}' has a truncated offset table.");
                }

                long[] oldOffsets = new long[chunkCount];
                WorldLayerFileHeader.ReadExactly(source, MemoryMarshal.AsBytes(oldOffsets.AsSpan()));
                foreach (long oldOffset in oldOffsets)
                {
                    if (oldOffset != -1 && (oldOffset < tableEnd || oldOffset >= source.Length))
                    {
                        throw new InvalidDataException($"Map file '{filePath}' contains an invalid chunk offset.");
                    }
                }

                long[] sortedOffsets = (long[])oldOffsets.Clone();
                Array.Sort(sortedOffsets);
                HashSet<long>? duplicateOffsets = FindDuplicateOffsets(sortedOffsets);
                var boundedSource = new BoundedReadStream(source);
                using var boundedReader = new BinaryReader(
                    boundedSource,
                    System.Text.Encoding.UTF8,
                    leaveOpen: true);

                using var destination = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                long[] newOffsets = new long[chunkCount];
                WorldLayerFileHeader.WriteHeader(destination, expectedWidth, expectedHeight, chunkSize, newOffsets);
                int chunkArea = checked(chunkSize * chunkSize);
                T[] decoded = new T[chunkArea];
                T[] verified = new T[chunkArea];
                using var destinationWriter = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
                using var destinationReader = new BinaryReader(destination, System.Text.Encoding.UTF8, leaveOpen: true);
                var comparer = EqualityComparer<T>.Default;
                for (int index = 0; index < oldOffsets.Length; index++)
                {
                    long oldOffset = oldOffsets[index];
                    if (oldOffset < 0)
                    {
                        continue;
                    }

                    int nextOffsetIndex = UpperBound(sortedOffsets, oldOffset);
                    long chunkEnd = nextOffsetIndex < sortedOffsets.Length
                        ? sortedOffsets[nextOffsetIndex]
                        : source.Length;
                    boundedSource.SetRange(oldOffset, chunkEnd - oldOffset);
                    if (duplicateOffsets?.Contains(oldOffset) == true)
                    {
                        Array.Clear(decoded, 0, decoded.Length);
                    }
                    else
                    {
                        try
                        {
                            WorldChunkRLECodec.DecodeChunk(boundedReader, chunkArea, decoded);
                        }
                        catch (InvalidDataException)
                        {
                            Array.Clear(decoded, 0, decoded.Length);
                        }
                    }

                    destination.Seek(0, SeekOrigin.End);
                    long newOffset = destination.Position;
                    WorldChunkV2Codec.EncodeChunk(destinationWriter, decoded, chunkArea, index);
                    newOffsets[index] = newOffset;
                    WorldLayerFileHeader.WriteChunkOffset(destination, index, newOffset);
                    destination.Seek(newOffset, SeekOrigin.Begin);
                    WorldChunkV2Codec.DecodeChunk(destinationReader, chunkArea, verified, index);
                    for (int cell = 0; cell < chunkArea; cell++)
                    {
                        if (!comparer.Equals(decoded[cell], verified[cell]))
                        {
                            throw new InvalidDataException($"Map chunk {index} failed current-format migration verification.");
                        }
                    }

                    migratedChunks++;
                }

                destination.Flush(true);
            }

            if (!File.Exists(backupPath))
            {
                string backupTempPath = backupPath + ".tmp";
                try
                {
                    using (var backupSource = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var backup = new FileStream(backupTempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        backupSource.CopyTo(backup);
                        backup.Flush(true);
                    }

                    File.Move(backupTempPath, backupPath);
                }
                finally
                {
                    if (File.Exists(backupTempPath))
                    {
                        File.Delete(backupTempPath);
                    }
                }
            }

            File.Replace(tempPath, filePath, destinationBackupFileName: null);
            return migratedChunks;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    internal static int MigrateV2ToCurrent<T>(
        string filePath,
        int expectedWidth,
        int expectedHeight,
        int chunkSize)
        where T : unmanaged
    {
        string tempPath = filePath + ".v3.migrate.tmp";
        string backupPath = filePath + ".v2.backup";
        int migratedChunks = 0;
        try
        {
            using (var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var reader = new BinaryReader(source, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                if (source.Length < WorldLayerFileHeader.HeaderSize)
                {
                    throw new InvalidDataException($"Map file '{filePath}' is not a compatible v2 map.");
                }

                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                int storedChunkSize = reader.ReadInt32();
                int version = reader.ReadInt32();
                if (version != WorldLayerFileHeader.LegacyFramedFormatVersion)
                {
                    throw new InvalidDataException($"Map file '{filePath}' is not a v2 framed map.");
                }

                if (width != expectedWidth || height != expectedHeight || storedChunkSize != chunkSize)
                {
                    return -1;
                }

                int chunkCount = checked(expectedWidth * expectedHeight);
                long tableEnd = checked(WorldLayerFileHeader.HeaderSize + (long)chunkCount * sizeof(long));
                if (source.Length < tableEnd)
                {
                    throw new InvalidDataException($"Map file '{filePath}' has a truncated offset table.");
                }

                long[] oldOffsets = new long[chunkCount];
                WorldLayerFileHeader.ReadExactly(source, MemoryMarshal.AsBytes(oldOffsets.AsSpan()));
                foreach (long oldOffset in oldOffsets)
                {
                    if (oldOffset != -1 && (oldOffset < tableEnd || oldOffset >= source.Length))
                    {
                        throw new InvalidDataException($"Map file '{filePath}' contains an invalid chunk offset.");
                    }
                }

                long[] sortedOffsets = (long[])oldOffsets.Clone();
                Array.Sort(sortedOffsets);
                HashSet<long>? duplicateOffsets = FindDuplicateOffsets(sortedOffsets);

                using var destination = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                long[] newOffsets = new long[chunkCount];
                WorldLayerFileHeader.WriteHeader(destination, expectedWidth, expectedHeight, chunkSize, newOffsets);
                int chunkArea = checked(chunkSize * chunkSize);
                T[] decoded = new T[chunkArea];
                T[] verified = new T[chunkArea];
                using var destinationWriter = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
                using var destinationReader = new BinaryReader(destination, System.Text.Encoding.UTF8, leaveOpen: true);
                var comparer = EqualityComparer<T>.Default;
                for (int index = 0; index < oldOffsets.Length; index++)
                {
                    long oldOffset = oldOffsets[index];
                    if (oldOffset < 0)
                    {
                        continue;
                    }

                    source.Seek(oldOffset, SeekOrigin.Begin);
                    if (duplicateOffsets?.Contains(oldOffset) == true)
                    {
                        Array.Clear(decoded, 0, decoded.Length);
                    }
                    else
                    {
                        try
                        {
                            WorldChunkV2Codec.DecodeChunk(reader, chunkArea, decoded);
                        }
                        catch (InvalidDataException)
                        {
                            Array.Clear(decoded, 0, decoded.Length);
                        }
                    }

                    destination.Seek(0, SeekOrigin.End);
                    long newOffset = destination.Position;
                    WorldChunkV2Codec.EncodeChunk(destinationWriter, decoded, chunkArea, index);
                    newOffsets[index] = newOffset;
                    WorldLayerFileHeader.WriteChunkOffset(destination, index, newOffset);
                    destination.Seek(newOffset, SeekOrigin.Begin);
                    WorldChunkV2Codec.DecodeChunk(destinationReader, chunkArea, verified, index);
                    for (int cell = 0; cell < chunkArea; cell++)
                    {
                        if (!comparer.Equals(decoded[cell], verified[cell]))
                        {
                            throw new InvalidDataException($"Map chunk {index} failed current-format migration verification.");
                        }
                    }

                    migratedChunks++;
                }

                destination.Flush(true);
            }

            if (!File.Exists(backupPath))
            {
                string backupTempPath = backupPath + ".tmp";
                try
                {
                    using (var backupSource = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var backup = new FileStream(backupTempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        backupSource.CopyTo(backup);
                        backup.Flush(true);
                    }

                    File.Move(backupTempPath, backupPath);
                }
                finally
                {
                    if (File.Exists(backupTempPath))
                    {
                        File.Delete(backupTempPath);
                    }
                }
            }

            File.Replace(tempPath, filePath, destinationBackupFileName: null);
            return migratedChunks;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static int UpperBound(long[] sortedValues, long value)
    {
        int low = 0;
        int high = sortedValues.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (sortedValues[middle] <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static HashSet<long>? FindDuplicateOffsets(long[] sortedOffsets)
    {
        HashSet<long>? duplicates = null;
        for (int index = 1; index < sortedOffsets.Length; index++)
        {
            long offset = sortedOffsets[index];
            if (offset >= 0 && offset == sortedOffsets[index - 1])
            {
                (duplicates ??= new HashSet<long>()).Add(offset);
            }
        }

        return duplicates;
    }

}
