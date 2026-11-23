namespace SharpEngine;

/// <summary>Minimal typed publish/subscribe bus for decoupling game systems.</summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Subscribe<T>(Action<T> handler)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list))
            _handlers[typeof(T)] = list = new List<Delegate>();
        list.Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
            list.Remove(handler);
    }

    public void Publish<T>(T message)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) return;
        foreach (var h in list.ToArray())
            ((Action<T>)h)(message);
    }
}
