#nullable enable

using System;
using System.Buffers.Binary;

namespace Kern;

/// <summary>
/// Размер закодированной картинки по её заголовку, без распаковки.
/// </summary>
///
/// LoadImage распаковывает сразу во весь размер из заголовка, а заголовок
/// пишет отправитель. Поэтому размер читается и проверяется до вызова.
/// Форматы — те, что пропускает AssetBatchDispatcher.IsTextureFile.
public static class EncodedImageHeader
{
    private static readonly byte[] s_pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] s_exrMagic = { 0x76, 0x2F, 0x31, 0x01 };

    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.StartsWith(s_pngSignature))
        {
            return TryReadPng(data, out width, out height);
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return TryReadJpeg(data, out width, out height);
        }

        if (data.StartsWith(s_exrMagic))
        {
            return TryReadExr(data, out width, out height);
        }

        return false;
    }

    // Первый чанк PNG обязан быть IHDR: ширина и высота — big-endian сразу за типом.
    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24 ||
            data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
        {
            return false;
        }

        uint pngWidth = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        uint pngHeight = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        if (pngWidth > int.MaxValue || pngHeight > int.MaxValue)
        {
            return false;
        }

        width = (int)pngWidth;
        height = (int)pngHeight;
        return true;
    }

    // Размер JPEG лежит в кадре SOFn; до него идут сегменты с длиной.
    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        int index = 2;
        while (index + 3 < data.Length)
        {
            if (data[index] != 0xFF)
            {
                return false;
            }

            byte marker = data[index + 1];
            if (marker == 0xFF)
            {
                index++;
                continue;
            }

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                index += 2;
                continue;
            }

            // Начало данных скана или конец файла без кадра — размера нет.
            if (marker == 0xDA || marker == 0xD9)
            {
                return false;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 2)..]);
            if (segmentLength < 2)
            {
                return false;
            }

            bool isFrame = marker >= 0xC0 && marker <= 0xCF &&
                marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isFrame)
            {
                if (index + 9 > data.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 7)..]);
                return true;
            }

            index += 2 + segmentLength;
        }

        return false;
    }

    // Заголовок EXR — атрибуты «имя\0 тип\0 размер значение» до пустого имени.
    // Размер картинки — атрибут dataWindow типа box2i: xMin, yMin, xMax, yMax.
    private static bool TryReadExr(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        int index = 8;
        while (index < data.Length)
        {
            int nameEnd = data[index..].IndexOf((byte)0);
            // Пустое имя закрывает заголовок, а dataWindow так и не встретился.
            if (nameEnd <= 0)
            {
                return false;
            }

            ReadOnlySpan<byte> name = data.Slice(index, nameEnd);
            index += nameEnd + 1;
            int typeEnd = index < data.Length ? data[index..].IndexOf((byte)0) : -1;
            if (typeEnd < 0)
            {
                return false;
            }

            ReadOnlySpan<byte> type = data.Slice(index, typeEnd);
            index += typeEnd + 1;
            if (index + 4 > data.Length)
            {
                return false;
            }

            int valueLength = BinaryPrimitives.ReadInt32LittleEndian(data[index..]);
            index += 4;
            if (valueLength < 0 || valueLength > data.Length - index)
            {
                return false;
            }

            if (name.SequenceEqual("dataWindow"u8) && type.SequenceEqual("box2i"u8) && valueLength == 16)
            {
                long xMin = BinaryPrimitives.ReadInt32LittleEndian(data[index..]);
                long yMin = BinaryPrimitives.ReadInt32LittleEndian(data[(index + 4)..]);
                long xMax = BinaryPrimitives.ReadInt32LittleEndian(data[(index + 8)..]);
                long yMax = BinaryPrimitives.ReadInt32LittleEndian(data[(index + 12)..]);
                long exrWidth = xMax - xMin + 1;
                long exrHeight = yMax - yMin + 1;
                if (exrWidth <= 0 || exrHeight <= 0 || exrWidth > int.MaxValue || exrHeight > int.MaxValue)
                {
                    return false;
                }

                width = (int)exrWidth;
                height = (int)exrHeight;
                return true;
            }

            index += valueLength;
        }

        return false;
    }
}
