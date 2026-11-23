using System.Text;

namespace SharpEngine.Networking;

/// <summary>Fluent builder for packet payloads (little-endian primitives, UTF-8 strings).</summary>
public sealed class PacketWriter
{
    private readonly MemoryStream _ms = new();
    private readonly BinaryWriter _bw;

    public PacketWriter() => _bw = new BinaryWriter(_ms, Encoding.UTF8, leaveOpen: true);

    public PacketWriter Write(byte v) { _bw.Write(v); return this; }
    public PacketWriter Write(bool v) { _bw.Write(v); return this; }
    public PacketWriter Write(int v) { _bw.Write(v); return this; }
    public PacketWriter Write(float v) { _bw.Write(v); return this; }
    public PacketWriter Write(string v) { _bw.Write(v); return this; }

    public byte[] ToArray()
    {
        _bw.Flush();
        return _ms.ToArray();
    }
}

/// <summary>Reads values written by <see cref="PacketWriter"/>, in the same order.</summary>
public sealed class PacketReader
{
    private readonly BinaryReader _br;

    public PacketReader(byte[] payload) => _br = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);

    public byte ReadByte() => _br.ReadByte();
    public bool ReadBool() => _br.ReadBoolean();
    public int ReadInt() => _br.ReadInt32();
    public float ReadFloat() => _br.ReadSingle();
    public string ReadString() => _br.ReadString();
}
