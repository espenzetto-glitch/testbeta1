namespace SharpEngine.Graphics3D;

/// <summary>Base class for behaviour attached to a <see cref="GameObject"/> (like Unity's MonoBehaviour).</summary>
public abstract class Component
{
    internal bool Started;

    public GameObject GameObject { get; internal set; } = null!;
    public Transform Transform => GameObject.Transform;
    public World World => GameObject.World;
    public bool Enabled { get; set; } = true;

    /// <summary>When true the component also updates while the editor is in edit mode (not playing).</summary>
    public virtual bool RunInEditMode => false;

    /// <summary>Runs once, before the first Update.</summary>
    public virtual void Start() { }

    /// <summary>Runs every frame. <paramref name="dt"/> is seconds since the last frame.</summary>
    public virtual void Update(float dt) { }

    public virtual void OnDestroy() { }
}

public sealed class GameObject
{
    private readonly List<Component> _components = new();

    internal GameObject(string name, World world)
    {
        Name = name;
        World = world;
    }

    public string Name { get; set; }
    public World World { get; }
    public Transform Transform { get; } = new();
    public bool Active { get; set; } = true;
    public bool Destroyed { get; internal set; }

    /// <summary>Name of the streamed chunk (scene) this object came from, or null for objects of the main scene.</summary>
    public string? Chunk { get; internal set; }
    public IReadOnlyList<Component> Components => _components;

    public T AddComponent<T>() where T : Component, new() => AddComponent(new T());

    public T AddComponent<T>(T component) where T : Component
    {
        if (component.GameObject is not null)
            throw new InvalidOperationException("Component is already attached to a GameObject.");
        component.GameObject = this;
        _components.Add(component);
        return component;
    }

    public Component AddComponent(Type type)
    {
        if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract)
            throw new ArgumentException($"{type.Name} is not a concrete Component.", nameof(type));
        return AddComponent((Component)Activator.CreateInstance(type)!);
    }

    public void RemoveComponent(Component component)
    {
        if (!_components.Remove(component)) return;
        component.OnDestroy();
        component.GameObject = null!;
    }

    public T? GetComponent<T>() where T : class
    {
        foreach (var c in _components)
            if (c is T match) return match;
        return null;
    }

    public void Destroy() => World.Destroy(this);

    internal void UpdateComponents(float dt, bool playMode)
    {
        for (int i = 0; i < _components.Count && !Destroyed; i++)
        {
            var c = _components[i];
            if (!c.Enabled) continue;
            if (!playMode && !c.RunInEditMode) continue;
            if (!c.Started)
            {
                c.Started = true;
                c.Start();
            }
            c.Update(dt);
        }
    }

    internal void DestroyComponents()
    {
        foreach (var c in _components) c.OnDestroy();
    }
}
