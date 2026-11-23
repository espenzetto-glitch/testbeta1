using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;

namespace SharpEngine.Networking;

/// <summary>
/// Asynchronous TCP client for games. All socket work happens on background tasks; results are
/// queued and delivered on the game thread when the engine calls <see cref="Poll"/>, so event
/// handlers never need locks.
/// </summary>
public sealed class NetClient : IDisposable
{
    private enum Kind { Connected, Disconnected, Packet }

    private readonly record struct NetEvent(Kind Kind, Packet Packet = default, string? Reason = null);

    private readonly ConcurrentQueue<NetEvent> _events = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Channel<byte[]>? _outbox;
    private volatile bool _connected;

    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<Packet>? PacketReceived;

    public bool IsConnected => _connected;

    /// <summary>Starts connecting in the background; returns immediately.</summary>
    public void Connect(string host, int port)
    {
        lock (_gate)
        {
            if (_cts is not null)
                throw new InvalidOperationException("Already connecting or connected. Call Disconnect() first.");

            var cts = _cts = new CancellationTokenSource();
            var outbox = _outbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
            _ = Task.Run(() => RunAsync(host, port, outbox, cts));
        }
    }

    public void Disconnect()
    {
        lock (_gate) _cts?.Cancel();
    }

    /// <summary>Queues a packet for sending. Never blocks. Returns false if not connected.</summary>
    public bool Send(ushort id, byte[] payload)
    {
        Channel<byte[]>? box;
        lock (_gate) box = _outbox;
        if (!_connected || box is null) return false;
        return box.Writer.TryWrite(PacketFraming.Encode(id, payload));
    }

    public bool Send(ushort id, PacketWriter writer) => Send(id, writer.ToArray());

    /// <summary>Dispatches queued events on the calling thread. The engine calls this every frame.</summary>
    public void Poll()
    {
        while (_events.TryDequeue(out var e))
        {
            switch (e.Kind)
            {
                case Kind.Connected: Connected?.Invoke(); break;
                case Kind.Disconnected: Disconnected?.Invoke(e.Reason ?? "Disconnected"); break;
                case Kind.Packet: PacketReceived?.Invoke(e.Packet); break;
            }
        }
    }

    public void Dispose() => Disconnect();

    private async Task RunAsync(string host, int port, Channel<byte[]> outbox, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        string reason = "Disconnected";
        TcpClient? tcp = null;
        Task? writer = null;

        try
        {
            tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
            var stream = tcp.GetStream();

            _connected = true;
            _events.Enqueue(new NetEvent(Kind.Connected));
            writer = WriteLoopAsync(stream, outbox.Reader, ct);

            while (true)
            {
                var packet = await PacketFraming.ReadAsync(stream, ct).ConfigureAwait(false);
                if (packet is null) { reason = "Server closed the connection"; break; }
                _events.Enqueue(new NetEvent(Kind.Packet, packet.Value));
            }
        }
        catch (OperationCanceledException) { reason = "Disconnected by client"; }
        catch (Exception ex) { reason = ex.Message; }
        finally
        {
            _connected = false;
            outbox.Writer.TryComplete();
            tcp?.Dispose();
            if (writer is not null)
            {
                try { await writer.ConfigureAwait(false); } catch { /* socket already gone */ }
            }

            _events.Enqueue(new NetEvent(Kind.Disconnected, Reason: reason));

            lock (_gate)
            {
                if (ReferenceEquals(_cts, cts)) { _cts = null; _outbox = null; }
            }
            cts.Dispose();
        }
    }

    private static async Task WriteLoopAsync(NetworkStream stream, ChannelReader<byte[]> reader, CancellationToken ct)
    {
        await foreach (var buffer in reader.ReadAllAsync(ct).ConfigureAwait(false))
            await stream.WriteAsync(buffer, ct).ConfigureAwait(false);
    }
}
