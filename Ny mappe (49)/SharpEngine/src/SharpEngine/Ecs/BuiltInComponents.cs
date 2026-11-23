using SharpEngine.Maths;
using SharpEngine.Rendering;

namespace SharpEngine.Ecs;

/// <summary>Draws a filled rectangle at the entity's position.</summary>
public sealed class RectRenderer : Component
{
    public Vec2 Size { get; set; } = Vec2.One;
    public Color Color { get; set; } = Color.White;
    public Vec2 Offset { get; set; }

    public override void OnRender(IRenderer renderer)
    {
        var p = Entity.Position + Offset;
        renderer.FillRect(p.X, p.Y, Size.X, Size.Y, Color);
    }
}

/// <summary>Draws text at the entity's position.</summary>
public sealed class TextRenderer : Component
{
    public string Text { get; set; } = "";
    public Color Color { get; set; } = Color.White;
    public Vec2 Offset { get; set; }

    public override void OnRender(IRenderer renderer)
    {
        var p = Entity.Position + Offset;
        renderer.DrawText(p.X, p.Y, Text, Color);
    }
}
