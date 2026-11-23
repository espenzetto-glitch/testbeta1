using SharpEngine.Rendering;

namespace SharpEngine.Ecs;

/// <summary>Behaviour or data attached to an <see cref="Entity"/>.</summary>
public abstract class Component
{
    internal Entity? Owner;
    internal bool Started;

    public Entity Entity => Owner ?? throw new InvalidOperationException("Component is not attached to an entity.");
    public Engine Engine => Entity.Scene.Engine;
    public bool Enabled { get; set; } = true;

    /// <summary>Runs once, right before the first <see cref="OnUpdate"/>.</summary>
    public virtual void OnStart() { }

    /// <summary>Runs every fixed update step.</summary>
    public virtual void OnUpdate(GameTime time) { }

    /// <summary>Runs every rendered frame.</summary>
    public virtual void OnRender(IRenderer renderer) { }

    /// <summary>Runs when the entity is destroyed or the scene is cleared.</summary>
    public virtual void OnDestroy() { }
}
