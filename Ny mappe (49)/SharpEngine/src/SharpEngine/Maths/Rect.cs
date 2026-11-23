namespace SharpEngine.Maths;

/// <summary>Axis-aligned rectangle (origin = top-left).</summary>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(Vec2 p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public bool Intersects(Rect o) =>
        X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;
}
