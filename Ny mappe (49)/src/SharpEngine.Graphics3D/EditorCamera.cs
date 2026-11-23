using System.Numerics;
using Raylib_cs;

namespace SharpEngine.Graphics3D;

/// <summary>
/// The camera you look through while editing (Unity's "scene view"). It is separate from the game camera, which only
/// takes over when you press Play, so you can fly anywhere to place objects without moving the game's camera.
/// It is never saved into a scene.
/// </summary>
public sealed class EditorCamera
{
    private readonly Transform _t = new();

    /// <summary>False = look through the game camera also while editing ("Game View").</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True once the camera has a position (it starts at the game camera and stays where you leave it).</summary>
    public bool Initialised { get; set; }

    public float FieldOfView { get; set; } = 60f;

    /// <summary>Fly speed in units per second (mouse wheel while the right button is held changes it).</summary>
    public float Speed { get; set; } = 8f;

    public Vector3 Position { get => _t.Position; set => _t.Position = value; }
    public float Yaw { get => _t.EulerAngles.Y; set => _t.EulerAngles = new Vector3(_t.EulerAngles.X, value, 0); }
    public float Pitch { get => _t.EulerAngles.X; set => _t.EulerAngles = new Vector3(Math.Clamp(value, -89f, 89f), _t.EulerAngles.Y, 0); }
    public Vector3 EulerAngles => _t.EulerAngles;
    public Vector3 Forward => _t.Forward;
    public Vector3 Right => _t.Right;
    public Vector3 Up => _t.Up;

    public void Set(Vector3 position, Vector3 eulerAngles)
    {
        _t.Position = position;
        // Read the direction the camera looks and rebuild upright angles from it. Copying the angles directly would turn
        // a game camera that has roll (or a pitch past 90 degrees, e.g. 165/180/180) into an upside-down view.
        var look = new Transform { EulerAngles = eulerAngles }.Forward;
        float yaw = MathF.Atan2(-look.X, -look.Z) * 180f / MathF.PI;
        float pitch = MathF.Asin(Math.Clamp(look.Y, -1f, 1f)) * 180f / MathF.PI;
        _t.EulerAngles = new Vector3(Math.Clamp(pitch, -89f, 89f), yaw, 0);
        Initialised = true;
    }

    /// <summary>Turns the view: dx/dy in degrees (mouse movement x sensitivity).</summary>
    public void Look(float dx, float dy)
    {
        Yaw -= dx;
        Pitch -= dy;
    }

    /// <summary>Moves relative to the view: x = right, y = up (world), z = forward.</summary>
    public void Move(Vector3 local, float distance)
    {
        var dir = Right * local.X + Vector3.UnitY * local.Y + Forward * local.Z;
        if (dir.LengthSquared() > 1e-6f) Position += Vector3.Normalize(dir) * distance;
    }

    /// <summary>Slides the view sideways / up (middle mouse drag). dx/dy are mouse pixels.</summary>
    public void Pan(float dx, float dy, float scale) => Position += (-Right * dx + Up * dy) * scale;

    /// <summary>Moves forward or back along the view direction (mouse wheel).</summary>
    public void Dolly(float amount) => Position += Forward * amount;

    /// <summary>Turns around <paramref name="pivot"/> while keeping its distance (Alt + left mouse).</summary>
    public void Orbit(Vector3 pivot, float dx, float dy)
    {
        float dist = MathF.Max(Vector3.Distance(Position, pivot), 0.5f);
        Look(dx, dy);
        Position = pivot - Forward * dist;
    }

    /// <summary>Flies so that a sphere around <paramref name="center"/> fills the view (F key).</summary>
    public void Focus(Vector3 center, float radius)
    {
        radius = MathF.Max(radius, 0.25f);
        float dist = radius / MathF.Sin(MathF.Min(FieldOfView, 100f) * 0.5f * MathF.PI / 180f) * 1.1f;
        Position = center - Forward * dist;
    }

    internal Camera3D ToRaylib() => new(Position, Position + Forward, Up, FieldOfView, CameraProjection.Perspective);
}
