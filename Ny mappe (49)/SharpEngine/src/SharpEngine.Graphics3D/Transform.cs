using System.Numerics;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Position, rotation and scale of a <see cref="GameObject"/> (right-handed, Y up, forward = -Z).
/// EulerAngles are degrees: X = pitch, Y = yaw, Z = roll.
/// </summary>
public sealed class Transform
{
    private const float Deg2Rad = MathF.PI / 180f;

    public Vector3 Position { get; set; }
    public Vector3 EulerAngles { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;

    public Quaternion Rotation =>
        Quaternion.CreateFromYawPitchRoll(EulerAngles.Y * Deg2Rad, EulerAngles.X * Deg2Rad, EulerAngles.Z * Deg2Rad);

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    /// <summary>Local-to-world matrix (System.Numerics row-vector convention).</summary>
    public Matrix4x4 WorldMatrix =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public void Translate(Vector3 delta) => Position += delta;

    public void Rotate(Vector3 eulerDeltaDegrees) => EulerAngles += eulerDeltaDegrees;

    /// <summary>Turns so that <see cref="Forward"/> points at the target (roll is reset to 0).</summary>
    public void LookAt(Vector3 target)
    {
        var d = target - Position;
        if (d.LengthSquared() < 1e-12f) return;
        d = Vector3.Normalize(d);
        float yaw = MathF.Atan2(-d.X, -d.Z) / Deg2Rad;
        float pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f)) / Deg2Rad;
        EulerAngles = new Vector3(pitch, yaw, 0f);
    }
}
