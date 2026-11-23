using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Key presses that cannot be missed. Raylib's IsKeyPressed only compares the key state between two frames,
/// so a quick tap that starts and ends between two frames is lost. The key queue records every press.
/// App3D calls <see cref="Poll"/> once per frame.
/// </summary>
public static class KeyEvents
{
    private static readonly HashSet<KeyboardKey> Pressed = new();

    public static void Poll()
    {
        Pressed.Clear();
        int key;
        while ((key = Rl.GetKeyPressed()) != 0) Pressed.Add((KeyboardKey)key);
    }

    public static bool WasPressed(KeyboardKey key) => Pressed.Contains(key) || Rl.IsKeyPressed(key);
}
