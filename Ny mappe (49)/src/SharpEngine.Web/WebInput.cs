using System.Numerics;
using Raylib_cs;
using SharpEngine.Graphics3D;

namespace SharpEngine.Web;

/// <summary>Keyboard and mouse from the page. sharp.js forwards DOM events here; the engine reads them like Raylib input.</summary>
public sealed class WebInput : IInputSource
{
    private readonly HashSet<KeyboardKey> _down = new();
    private HashSet<KeyboardKey> _pressedNext = new();
    private HashSet<KeyboardKey> _pressed = new();
    private readonly Queue<int> _queue = new();
    private readonly HashSet<MouseButton> _buttons = new();
    private Vector2 _deltaNext;
    private Vector2 _delta;

    public Action<bool>? OnCapture { get; set; }

    public bool IsKeyDown(KeyboardKey key) => _down.Contains(key);
    public bool IsKeyPressed(KeyboardKey key) => _pressed.Contains(key);
    public int PopKey() => _queue.Count > 0 ? _queue.Dequeue() : 0;
    public bool IsMouseDown(MouseButton button) => _buttons.Contains(button);
    public Vector2 MouseDelta => _delta;
    public void CaptureMouse(bool capture) => OnCapture?.Invoke(capture);

    /// <summary>Call once at the start of every frame: events that arrived since the last frame become "pressed this frame".</summary>
    public void BeginFrame()
    {
        (_pressed, _pressedNext) = (_pressedNext, _pressed);
        _pressedNext.Clear();
        _delta = _deltaNext;
        _deltaNext = Vector2.Zero;
    }

    public void Key(string code, bool isDown)
    {
        if (!TryMap(code, out var key)) return;
        if (isDown)
        {
            if (_down.Add(key)) { _pressedNext.Add(key); _queue.Enqueue((int)key); }
        }
        else _down.Remove(key);
    }

    public void Mouse(int button, bool isDown)
    {
        var b = button switch { 0 => MouseButton.Left, 1 => MouseButton.Middle, 2 => MouseButton.Right, _ => (MouseButton)(-1) };
        if ((int)b < 0) return;
        if (isDown) _buttons.Add(b); else _buttons.Remove(b);
    }

    public void MouseMove(float dx, float dy) => _deltaNext += new Vector2(dx, dy);

    /// <summary>Everything is released (the page lost focus), so no key stays "stuck".</summary>
    public void ReleaseAll() { _down.Clear(); _buttons.Clear(); }

    /// <summary>Maps a DOM KeyboardEvent.code ("KeyW", "Digit1", "ShiftLeft", ...) to Raylib's key.</summary>
    public static bool TryMap(string code, out KeyboardKey key)
    {
        key = KeyboardKey.Null;
        if (string.IsNullOrEmpty(code)) return false;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && code[3] is >= 'A' and <= 'Z') { key = KeyboardKey.A + (code[3] - 'A'); return true; }
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && code[5] is >= '0' and <= '9')
        {
            key = code[5] == '0' ? KeyboardKey.Zero : KeyboardKey.One + (code[5] - '1');
            return true;
        }
        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out int f) && f is >= 1 and <= 12) { key = KeyboardKey.F1 + (f - 1); return true; }
        switch (code)
        {
            case "Space": key = KeyboardKey.Space; return true;
            case "Enter": case "NumpadEnter": key = KeyboardKey.Enter; return true;
            case "Tab": key = KeyboardKey.Tab; return true;
            case "Escape": key = KeyboardKey.Escape; return true;
            case "Backspace": key = KeyboardKey.Backspace; return true;
            case "ShiftLeft": key = KeyboardKey.LeftShift; return true;
            case "ShiftRight": key = KeyboardKey.RightShift; return true;
            case "ControlLeft": key = KeyboardKey.LeftControl; return true;
            case "ControlRight": key = KeyboardKey.RightControl; return true;
            case "AltLeft": key = KeyboardKey.LeftAlt; return true;
            case "AltRight": key = KeyboardKey.RightAlt; return true;
            case "ArrowUp": key = KeyboardKey.Up; return true;
            case "ArrowDown": key = KeyboardKey.Down; return true;
            case "ArrowLeft": key = KeyboardKey.Left; return true;
            case "ArrowRight": key = KeyboardKey.Right; return true;
        }
        return false;
    }
}
