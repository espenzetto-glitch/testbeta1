using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D;

/// <summary>Where keyboard and mouse input comes from. The desktop player uses Raylib; the browser player (SharpEngine.Web) uses DOM events.</summary>
public interface IInputSource
{
    bool IsKeyDown(KeyboardKey key);
    bool IsKeyPressed(KeyboardKey key);
    /// <summary>Next key that was pressed since the last call (0 when none). Used so quick taps are never missed.</summary>
    int PopKey();
    bool IsMouseDown(MouseButton button);
    Vector2 MouseDelta { get; }
    /// <summary>Hide and lock the cursor while looking around (right mouse button held).</summary>
    void CaptureMouse(bool capture);
}

/// <summary>Holds the active <see cref="IInputSource"/>. Defaults to Raylib.</summary>
public static class Platform
{
    public static IInputSource Input { get; set; } = new RaylibInputSource();
}

internal sealed class RaylibInputSource : IInputSource
{
    public bool IsKeyDown(KeyboardKey key) => Rl.IsKeyDown(key);
    public bool IsKeyPressed(KeyboardKey key) => Rl.IsKeyPressed(key);
    public int PopKey() => Rl.GetKeyPressed();
    public bool IsMouseDown(MouseButton button) => Rl.IsMouseButtonDown(button);
    public Vector2 MouseDelta => Rl.GetMouseDelta();
    public void CaptureMouse(bool capture) { if (capture) Rl.DisableCursor(); else Rl.EnableCursor(); }
}

/// <summary>
/// Receives what a frame looks like instead of drawing it: the browser player turns this into WebGL calls.
/// (The desktop player draws with Raylib directly in <see cref="World.Render"/>.)
/// </summary>
public interface IFrameSink
{
    void BeginFrame(Color skyTop, Color skyBottom, Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUp, float fieldOfView);
    /// <summary>One lit mesh in world space. <paramref name="texture"/> is empty when it has no texture.</summary>
    void Mesh(string texture, float tileU, float tileV, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Color> colors, Vector2[]? uvs, int[] indices);
    void Line(Vector3 from, Vector3 to, Color color);
    void EndFrame();
}
