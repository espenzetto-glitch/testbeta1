using System.Numerics;
using SharpEngine;
using SharpEngine.Ecs;
using SharpEngine.Inputs;
using SharpEngine.Maths;
using SharpEngine.Networking;
using SharpEngine.Rendering;

namespace Demo;

public sealed class ArenaGame(string host, int port) : Game
{
    private static readonly Color[] Palette =
        [Color.Red, Color.Blue, Color.Yellow, Color.Magenta, Color.Cyan, Color.Orange];

    private readonly Dictionary<int, Entity> _remotes = new();
    private string _status = "connecting...";
    private int _myId;
    private bool _view3D;
    private Rasterizer? _raster;

    public override void Initialize()
    {
        int w = Engine.Renderer.Width, h = Engine.Renderer.Height;

        // arena floor (row 0 is the HUD)
        var floor = Scene.Spawn("floor", new Vec2(0, 1));
        floor.Layer = -10;
        floor.Add(new RectRenderer { Size = new Vec2(w, h - 1), Color = Color.DarkGray });

        // local player
        var me = Scene.Spawn("me", new Vec2(w / 2f, h / 2f));
        me.Layer = 10;
        me.Add(new RectRenderer { Color = Color.Green });
        me.Add(new TextRenderer { Text = "@", Color = Color.Black });
        me.Add(new LocalPlayer { Min = new Vec2(0, 1), Max = new Vec2(w - 1, h - 1) });

        var net = Engine.Net;
        net.Connected += () => _status = "online";
        net.Disconnected += reason =>
        {
            _status = $"offline ({reason})";
            _myId = 0;
            foreach (var e in _remotes.Values) e.Destroy();
            _remotes.Clear();
        };
        net.PacketReceived += OnPacket;
        net.Connect(host, port);
    }

    public override void Update(GameTime time)
    {
        if (Engine.Input.WasPressed(Key.Tab)) _view3D = !_view3D;

        if (Engine.Input.WasPressed(Key.Escape) || Engine.Input.WasPressed(Key.Q))
            Engine.Quit();
    }

    public override void RenderOverlay(IRenderer renderer)
    {
        if (_view3D && renderer is IPixelPresenter presenter)
            RenderTriangle3D(presenter);

        string you = _myId > 0 ? $"you=#{_myId}" : "you=?";
        renderer.DrawText(0, 0,
            $" SharpEngine Arena | {_status} | {you} | others={_remotes.Count} | WASD move, Tab=2D/3D, Esc quits",
            Color.White);
    }

    /// <summary>Spinning vertex-coloured triangle. Your player position tilts and turns it.</summary>
    private void RenderTriangle3D(IPixelPresenter presenter)
    {
        _raster ??= new Rasterizer(presenter.PixelWidth, presenter.PixelHeight);
        _raster.Clear(new Color(14, 16, 32));

        var me = Scene.Find("me")?.Position ?? new Vec2(40, 12);
        float yaw = (float)Engine.TotalTime * 1.2f + (me.X - 40f) * 0.06f;
        float pitch = (me.Y - 12f) * 0.08f;

        var model = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw);
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 3.2f), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, _raster.Aspect, 0.1f, 50f);

        _raster.DrawTriangle(
            new Vector3(0f, 1.1f, 0f), new Vector3(-1.2f, -0.8f, 0f), new Vector3(1.2f, -0.8f, 0f),
            Color.Red, Color.Green, Color.Blue,
            model * view * proj);

        presenter.DrawPixels(_raster.Pixels);
    }

    private void OnPacket(Packet packet)
    {
        var r = new PacketReader(packet.Payload);
        switch (packet.Id)
        {
            case Protocol.Welcome:
                _myId = r.ReadInt();
                break;

            case Protocol.PlayerState:
            {
                int id = r.ReadInt();
                var pos = new Vec2(r.ReadFloat(), r.ReadFloat());
                if (!_remotes.TryGetValue(id, out var entity))
                {
                    entity = Scene.Spawn($"player-{id}", pos);
                    entity.Layer = 5;
                    entity.Add(new RectRenderer { Color = Palette[id % Palette.Length] });
                    entity.Add(new TextRenderer { Text = (id % 10).ToString(), Color = Color.Black });
                    entity.Add(new RemotePlayer { Target = pos });
                    _remotes[id] = entity;
                }
                entity.Get<RemotePlayer>()!.Target = pos;
                break;
            }

            case Protocol.PlayerLeft:
            {
                int id = r.ReadInt();
                if (_remotes.Remove(id, out var entity)) entity.Destroy();
                break;
            }
        }
    }
}
