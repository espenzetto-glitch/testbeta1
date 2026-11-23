namespace SharpEngine.Inputs;

public enum Key
{
    Unknown,
    Up, Down, Left, Right,
    W, A, S, D,
    Space, Enter, Escape, Q, Tab
}

public interface IInput
{
    /// <summary>Refresh state. The engine calls this before every fixed update step.</summary>
    void Poll();

    /// <summary>True while the key is held.</summary>
    bool IsDown(Key key);

    /// <summary>True only on the step in which the key went down.</summary>
    bool WasPressed(Key key);
}
