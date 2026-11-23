using System.Buffers.Binary;

namespace SharpEngine.Networking;

/// <summary>A framed message: a 16-bit message id plus an opaque payload.</summary>
public readonly record struct Packet(ushort Id, byte[] Payload);

/// <summary>
/// Wire format: [int32 length, big-endian][uint16 id, big-endian][payload...]
/// where length = 2 + payload size.
/// </summary>
public static class PacketFraming
{
    public const int MaxPayload = 1 << 20;

    public static byte[] Encode(ushort id, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload) throw new ArgumentException("Payload too large.", nameof(payload));
        var buf = new byte[6 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(buf, 2 + payload.Length);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(4), id);
        payload.CopyTo(buf.AsSpan(6));
        return buf;
    }

    /// <summary>Reads one packet. Returns null on a clean end-of-stream.</summary>
    public static async Task<Packet?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 2 || length > MaxPayload + 2)
            throw new InvalidDataException($"Invalid packet length {length}.");

        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
            throw new EndOfStreamException("Connection closed mid-packet.");

        return new Packet(BinaryPrimitives.ReadUInt16BigEndian(body), body.AsSpan(2).ToArray());
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (offset == 0) return false;
                throw new EndOfStreamException();
            }
            offset += n;
        }
        return true;
    }
}
