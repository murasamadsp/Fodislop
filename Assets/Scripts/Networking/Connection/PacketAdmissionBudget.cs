#nullable enable

using System;

namespace Kern.Networking.Connection;

/// <summary>
/// Потокобезопасный лимит входной очереди по количеству пакетов и байтам.
/// Резервирование и освобождение отделены от самой очереди, чтобы владелец
/// мог атомарно защитить пару «резерв + enqueue» общей блокировкой.
/// </summary>
public sealed class PacketAdmissionBudget
{
    private readonly object _gate = new();
    private readonly int _maximumPacketCount;
    private readonly long _maximumPacketBytes;
    private int _packetCount;
    private long _packetBytes;

    public PacketAdmissionBudget(int maximumPacketCount, long maximumPacketBytes)
    {
        if (maximumPacketCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPacketCount));
        }

        if (maximumPacketBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPacketBytes));
        }

        _maximumPacketCount = maximumPacketCount;
        _maximumPacketBytes = maximumPacketBytes;
    }

    public int PacketCount
    {
        get
        {
            lock (_gate)
            {
                return _packetCount;
            }
        }
    }

    public long PacketBytes
    {
        get
        {
            lock (_gate)
            {
                return _packetBytes;
            }
        }
    }

    /// <summary>
    /// Зарезервировать место, если оно есть. В пустую очередь пакет входит
    /// всегда, даже больше байтового лимита: иначе такой пакет не вошёл бы
    /// никогда, и ждущий его поток чтения стоял бы вечно.
    /// </summary>
    public bool TryReserve(int packetBytes)
    {
        int normalizedBytes = Math.Max(1, packetBytes);
        lock (_gate)
        {
            if (_packetCount > 0 &&
                (_packetCount >= _maximumPacketCount ||
                 normalizedBytes > _maximumPacketBytes - _packetBytes))
            {
                return false;
            }

            _packetCount++;
            _packetBytes += normalizedBytes;
            return true;
        }
    }

    /// <summary>Зарезервировать сверх лимита: производитель, который не может ждать.</summary>
    public void Reserve(int packetBytes)
    {
        int normalizedBytes = Math.Max(1, packetBytes);
        lock (_gate)
        {
            _packetCount++;
            _packetBytes += normalizedBytes;
        }
    }

    public void Release(int packetBytes)
    {
        int normalizedBytes = Math.Max(1, packetBytes);
        lock (_gate)
        {
            if (_packetCount <= 0 || _packetBytes < normalizedBytes)
            {
                throw new InvalidOperationException(
                    "Packet admission release does not match a reserved packet.");
            }

            _packetCount--;
            _packetBytes -= normalizedBytes;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _packetCount = 0;
            _packetBytes = 0;
        }
    }
}
