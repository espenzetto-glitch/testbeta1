using Rl = Raylib_cs.Raylib;
using Raylib_cs;
using SharpEngine.Inputs;

namespace SharpEngine.Backends.RaylibBackend;

/// <summary>Real key-down / key-up keyboard state (no timeout tricks needed, unlike the console).</summary>
public sealed class RaylibInput : IInput
{
    // Raylib reads the keyboard inside EndDrawing(), so state is always current when the engine polls.
    public void Poll() { }

    public bool IsDown(Key key) => Map(key) is { } k && Rl.IsKeyDown(k);
    public bool WasPressed(Key key) => Map(key) is { } k && Rl.IsKeyPressed(k);

    private static KeyboardKey? Map(Key key) => key switch
    {
        Key.Up => KeyboardKey.Up,
        Key.Down => KeyboardKey.Down,
        Key.Left => KeyboardKey.Left,
        Key.Right => KeyboardKey.Right,
        Key.W => KeyboardKey.W,
        Key.A => KeyboardKey.A,
        Key.S => KeyboardKey.S,
        Key.D => KeyboardKey.D,
        Key.Space => KeyboardKey.Space,
        Key.Enter => KeyboardKey.Enter,
        Key.Escape => KeyboardKey.Escape,
        Key.Q => KeyboardKey.Q,
        Key.Tab => KeyboardKey.Tab,
        _ => null
    };
}
