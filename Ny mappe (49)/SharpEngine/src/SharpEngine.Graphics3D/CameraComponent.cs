using System.Numerics;
using Raylib_cs;

namespace SharpEngine.Graphics3D;

/// <summary>Marks a GameObject as a camera. The first enabled one in the world is used.</summary>
public sealed class CameraComponent : Component
{
    /// <summary>Vertical field of view in degrees.</summary>
    public float FieldOfView { get; set; } = 60f;

    internal Camera3D ToRaylib()
    {
        var t = Transform;
        return new Camera3D(t.Position, t.Position + t.Forward, t.Up, FieldOfView, CameraProjection.Perspective);
    }
}

/// <summary>Scene lighting: one directional light (the "sun") plus a flat ambient term.</summary>
public sealed class DirectionalLight
{
    public Vector3 Direction { get; set; } = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.35f));
    public Color Color { get; set; } = new Color(255, 244, 222, 255);
    public float Intensity { get; set; } = 1.0f;
    public float Ambient { get; set; } = 0.28f;
}
