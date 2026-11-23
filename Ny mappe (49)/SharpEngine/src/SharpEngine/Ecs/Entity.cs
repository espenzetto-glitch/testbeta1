using SharpEngine.Maths;
using SharpEngine.Rendering;

namespace SharpEngine.Ecs;

public sealed class Entity
{
    private readonly List<Component> _components = new();

    internal Entity(int id, string name, Scene scene)
    {
        Id = id;
        Name = name;
        Scene = scene;
    }

    public int Id { get; }
    public string Name { get; set; }
    public string? Tag { get; set; }
    public Scene Scene { get; }

    public Vec2 Position { get; set; }

    /// <summary>Draw order: lower layers are drawn first (behind).</summary>
    public int Layer { get; set; }

    public bool Active { get; set; } = true;
    public bool Destroyed { get; internal set; }

    public IReadOnlyList<Component> Components => _components;

    public T Add<T>(T component) where T : Component
    {
        if (component.Owner is not null)
            throw new InvalidOperationException("Component already belongs to an entity.");
        component.Owner = this;
        _components.Add(component);
        return component;
    }

    public T? Get<T>() where T : class
    {
        foreach (var c in _components)
            if (c is T match) return match;
        return null;
    }

    public void Destroy() => Scene.Destroy(this);

    internal void UpdateComponents(GameTime time)
    {
        // index loop: components added during an update are picked up in the same pass
        for (int i = 0; i < _components.Count && !Destroyed; i++)
        {
            var c = _components[i];
            if (!c.Enabled) continue;
            if (!c.Started)
            {
                c.Started = true;
                c.OnStart();
            }
            c.OnUpdate(time);
        }
    }

    internal void RenderComponents(IRenderer renderer)
    {
        for (int i = 0; i < _components.Count; i++)
            if (_components[i].Enabled) _components[i].OnRender(renderer);
    }

    internal void DestroyComponents()
    {
        foreach (var c in _components) c.OnDestroy();
    }
}
