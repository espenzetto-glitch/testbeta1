using System.Buffers.Binary;
using System.IO.Compression;

namespace SharpEngine.Graphics3D;

/// <summary>Writes small RGB PNG files without any graphics library (used for the sample textures).</summary>
public static class PngWriter
{
    public static void Write(string path, int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var raw = new byte[(width * 3 + 1) * height];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0;                       // filter: none
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                raw[o++] = r; raw[o++] = g; raw[o++] = b;
            }
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        f.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = 2;                // 8 bit, RGB
        Chunk(f, "IHDR", ihdr);
        Chunk(f, "IDAT", compressed.ToArray());
        Chunk(f, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        s.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        s.Write(crc);
    }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
