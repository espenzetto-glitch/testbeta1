namespace SharpEngine.Maths;

/// <summary>Immutable 2D vector.</summary>
public readonly record struct Vec2(float X, float Y)
{
    public static readonly Vec2 Zero = new(0f, 0f);
    public static readonly Vec2 One = new(1f, 1f);

    public float Length => MathF.Sqrt(X * X + Y * Y);

    public Vec2 Normalized
    {
        get
        {
            float l = Length;
            return l > 1e-6f ? new Vec2(X / l, Y / l) : Zero;
        }
    }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(float s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);

    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * t;

    public static Vec2 Clamp(Vec2 v, Vec2 min, Vec2 max) =>
        new(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y));
}
