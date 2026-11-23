namespace SharpEngine.Inputs;

/// <summary>
/// Keyboard input from a terminal. Terminals only report key-down (with auto-repeat), so a key
/// counts as "held" for <see cref="HoldTimeoutMs"/> after its last event.
/// </summary>
public sealed class ConsoleInput : IInput
{
    private readonly Dictionary<Key, long> _lastSeen = new();
    private readonly HashSet<Key> _pressed = new();

    public int HoldTimeoutMs { get; set; } = 140;

    public void Poll()
    {
        _pressed.Clear();
        if (Console.IsInputRedirected) return;

        while (Console.KeyAvailable)
        {
            var key = Map(Console.ReadKey(intercept: true).Key);
            if (key == Key.Unknown) continue;
            _pressed.Add(key);
            _lastSeen[key] = Environment.TickCount64;
        }
    }

    public bool IsDown(Key key) =>
        _lastSeen.TryGetValue(key, out long t) && Environment.TickCount64 - t <= HoldTimeoutMs;

    public bool WasPressed(Key key) => _pressed.Contains(key);

    private static Key Map(ConsoleKey k) => k switch
    {
        ConsoleKey.UpArrow => Key.Up,
        ConsoleKey.DownArrow => Key.Down,
        ConsoleKey.LeftArrow => Key.Left,
        ConsoleKey.RightArrow => Key.Right,
        ConsoleKey.W => Key.W,
        ConsoleKey.A => Key.A,
        ConsoleKey.S => Key.S,
        ConsoleKey.D => Key.D,
        ConsoleKey.Spacebar => Key.Space,
        ConsoleKey.Enter => Key.Enter,
        ConsoleKey.Escape => Key.Escape,
        ConsoleKey.Q => Key.Q,
        ConsoleKey.Tab => Key.Tab,
        _ => Key.Unknown
    };
}
