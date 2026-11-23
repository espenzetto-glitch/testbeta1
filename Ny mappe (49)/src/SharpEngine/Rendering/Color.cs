namespace SharpEngine.Rendering;

public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Red = new(220, 60, 60);
    public static readonly Color Green = new(70, 200, 90);
    public static readonly Color Blue = new(70, 120, 230);
    public static readonly Color Yellow = new(240, 210, 60);
    public static readonly Color Magenta = new(200, 80, 200);
    public static readonly Color Cyan = new(60, 200, 210);
    public static readonly Color Orange = new(240, 150, 50);
    public static readonly Color DarkGray = new(40, 44, 52);
    public static readonly Color Gray = new(128, 128, 128);
}
