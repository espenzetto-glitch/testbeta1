namespace SharpEngine.Inputs;

/// <summary>
/// Input driven by a function of the poll index (= update step). Used for tests, bots and replays.
/// </summary>
public sealed class ScriptedInput : IInput
{
    private readonly Func<long, IReadOnlyCollection<Key>> _script;
    private HashSet<Key> _current = new();
    private HashSet<Key> _previous = new();
    private long _step = -1;

    public ScriptedInput(Func<long, IReadOnlyCollection<Key>> script) => _script = script;

    public void Poll()
    {
        _previous = _current;
        _current = new HashSet<Key>(_script(++_step));
    }

    public bool IsDown(Key key) => _current.Contains(key);
    public bool WasPressed(Key key) => _current.Contains(key) && !_previous.Contains(key);
}
