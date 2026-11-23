using System.Numerics;
using Raylib_cs;
using SharpEngine.Graphics3D;
using Rl = Raylib_cs.Raylib;

namespace Game3D;

/// <summary>Rotates the object continuously. DegreesPerSecond is (pitch, yaw, roll).</summary>
public sealed class Spinner : Component
{
    public Vector3 DegreesPerSecond { get; set; } = new(0, 45, 0);

    public override void Update(float dt) => Transform.Rotate(DegreesPerSecond * dt);
}

/// <summary>Moves the object up and down with a sine wave.</summary>
public sealed class Bobber : Component
{
    private float _baseY;

    public float Amplitude { get; set; } = 0.4f;
    public float Speed { get; set; } = 2f;

    public override void Start() => _baseY = Transform.Position.Y;

    public override void Update(float dt)
    {
        var p = Transform.Position;
        p.Y = _baseY + MathF.Sin(World.Time * Speed) * Amplitude;
        Transform.Position = p;
    }
}

/// <summary>Swings the sun around the scene so you can watch the lighting change. Press L to pause.</summary>
public sealed class SunOrbit : Component
{
    private bool _running = true;

    public override void Update(float dt)
    {
        if (Rl.IsKeyPressed(KeyboardKey.L)) _running = !_running;
        if (!_running) return;

        float a = World.Time * 0.5f;
        World.Sun.Direction = Vector3.Normalize(new Vector3(MathF.Cos(a) * 0.8f, -0.9f, MathF.Sin(a) * 0.8f));
    }
}
