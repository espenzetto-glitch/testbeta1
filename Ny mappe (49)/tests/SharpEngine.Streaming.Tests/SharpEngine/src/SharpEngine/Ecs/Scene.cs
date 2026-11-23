using SharpEngine.Maths;
using SharpEngine.Rendering;

namespace SharpEngine.Ecs;

/// <summary>Container for entities. Destruction is deferred to the end of the update step.</summary>
public sealed class Scene
{
    private readonly List<Entity> _entities = new();
    private readonly List<Entity> _pendingRemove = new();
    private readonly List<Entity> _renderList = new();
    private int _nextId = 1;

    internal Scene(Engine engine) => Engine = engine;

    public Engine Engine { get; }
    public IReadOnlyList<Entity> Entities => _entities;

    /// <summary>Creates an entity. It is visible to Find() immediately and updates this step.</summary>
    public Entity Spawn(string name, Vec2 position = default)
    {
        var e = new Entity(_nextId++, name, this) { Position = position };
        _entities.Add(e);
        return e;
    }

    public void Destroy(Entity entity)
    {
        if (entity.Destroyed) return;
        entity.Destroyed = true;
        _pendingRemove.Add(entity);
    }

    public Entity? Find(string name)
    {
        foreach (var e in _entities)
            if (!e.Destroyed && e.Name == name) return e;
        return null;
    }

    public IEnumerable<Entity> FindByTag(string tag)
    {
        foreach (var e in _entities)
            if (!e.Destroyed && e.Tag == tag) yield return e;
    }

    internal void Update(GameTime time)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Destroyed || !e.Active) continue;
            e.UpdateComponents(time);
        }
        FlushDestroyed();
    }

    internal void Render(IRenderer renderer)
    {
        _renderList.Clear();
        foreach (var e in _entities)
            if (!e.Destroyed && e.Active) _renderList.Add(e);

        _renderList.Sort(static (a, b) =>
            a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Id.CompareTo(b.Id));

        foreach (var e in _renderList)
            e.RenderComponents(renderer);
    }

    internal void Clear()
    {
        foreach (var e in _entities.ToArray()) Destroy(e);
        FlushDestroyed();
    }

    private void FlushDestroyed()
    {
        if (_pendingRemove.Count == 0) return;
        foreach (var e in _pendingRemove)
        {
            e.DestroyComponents();
            _entities.Remove(e);
        }
        _pendingRemove.Clear();
    }
}
