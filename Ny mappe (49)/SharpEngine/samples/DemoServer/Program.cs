// Tiny relay server for the demo: tells each client its id and rebroadcasts positions.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Demo;
using SharpEngine.Networking;

int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 7777;

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"Demo server listening on port {port} (Ctrl+C to stop)");

var clients = new ConcurrentDictionary<int, Client>();
int nextId = 0;

while (true)
{
    var tcp = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleAsync(tcp));
}

async Task HandleAsync(TcpClient tcp)
{
    tcp.NoDelay = true;
    var stream = tcp.GetStream();
    int id = Interlocked.Increment(ref nextId);
    var me = new Client(id, stream);

    try
    {
        await me.SendAsync(PacketFraming.Encode(Protocol.Welcome, new PacketWriter().Write(id).ToArray()));
        foreach (var other in clients.Values)
            if (other.HasPosition) await me.SendAsync(StateBytes(other));

        clients[id] = me;
        Console.WriteLine($"+ player {id} joined ({clients.Count} online)");

        while (true)
        {
            var packet = await PacketFraming.ReadAsync(stream, CancellationToken.None);
            if (packet is null) break;

            if (packet.Value.Id == Protocol.Position)
            {
                var r = new PacketReader(packet.Value.Payload);
                me.X = r.ReadFloat();
                me.Y = r.ReadFloat();
                me.HasPosition = true;
                await BroadcastAsync(StateBytes(me), except: id);
            }
        }
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or ObjectDisposedException)
    {
        // client dropped; fall through to cleanup
    }
    finally
    {
        clients.TryRemove(id, out _);
        tcp.Dispose();
        Console.WriteLine($"- player {id} left ({clients.Count} online)");
        await BroadcastAsync(PacketFraming.Encode(Protocol.PlayerLeft, new PacketWriter().Write(id).ToArray()), except: id);
    }
}

byte[] StateBytes(Client c) =>
    PacketFraming.Encode(Protocol.PlayerState, new PacketWriter().Write(c.Id).Write(c.X).Write(c.Y).ToArray());

async Task BroadcastAsync(byte[] data, int except)
{
    foreach (var c in clients.Values)
        if (c.Id != except) await c.SendAsync(data);
}

sealed class Client(int id, NetworkStream stream)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public int Id { get; } = id;
    public float X { get; set; }
    public float Y { get; set; }
    public bool HasPosition { get; set; }

    public async Task SendAsync(byte[] data)
    {
        await _sendLock.WaitAsync();
        try { await stream.WriteAsync(data); }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { /* peer is gone */ }
        finally { _sendLock.Release(); }
    }
}
