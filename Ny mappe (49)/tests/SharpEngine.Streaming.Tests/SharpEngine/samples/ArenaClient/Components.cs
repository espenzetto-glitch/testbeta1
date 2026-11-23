using SharpEngine;
using SharpEngine.Ecs;
using SharpEngine.Inputs;
using SharpEngine.Maths;
using SharpEngine.Networking;

namespace Demo;

/// <summary>Moves the local player from keyboard input and streams its position to the server.</summary>
public sealed class LocalPlayer : Component
{
    public Vec2 Min { get; init; }
    public Vec2 Max { get; init; }
    public float SpeedX { get; init; } = 32f; // cells are ~2x taller than wide, so x is faster
    public float SpeedY { get; init; } = 16f;
    public float SendInterval { get; init; } = 0.05f;

    private float _sinceSend;
    private Vec2 _lastSent = new(-1, -1);

    public override void OnUpdate(GameTime time)
    {
        var input = Engine.Input;
        var dir = Vec2.Zero;
        if (input.IsDown(Key.A) || input.IsDown(Key.Left)) dir += new Vec2(-1, 0);
        if (input.IsDown(Key.D) || input.IsDown(Key.Right)) dir += new Vec2(1, 0);
        if (input.IsDown(Key.W) || input.IsDown(Key.Up)) dir += new Vec2(0, -1);
        if (input.IsDown(Key.S) || input.IsDown(Key.Down)) dir += new Vec2(0, 1);

        dir = dir.Normalized;
        var velocity = new Vec2(dir.X * SpeedX, dir.Y * SpeedY);
        Entity.Position = Vec2.Clamp(Entity.Position + velocity * time.Delta, Min, Max);

        _sinceSend += time.Delta;
        if (_sinceSend >= SendInterval && Entity.Position != _lastSent &&
            Engine.Net.Send(Protocol.Position, new PacketWriter().Write(Entity.Position.X).Write(Entity.Position.Y)))
        {
            _sinceSend = 0;
            _lastSent = Entity.Position;
        }
    }
}

/// <summary>Smoothly follows the last position received from the server.</summary>
public sealed class RemotePlayer : Component
{
    public Vec2 Target { get; set; }

    public override void OnUpdate(GameTime time)
    {
        float t = Math.Min(1f, 15f * time.Delta);
        Entity.Position = Vec2.Lerp(Entity.Position, Target, t);
    }
}
