using SharpEngine.Ecs;
using SharpEngine.Rendering;

namespace SharpEngine;

/// <summary>Derive from this to build a game on top of <see cref="Engine"/>.</summary>
public abstract class Game
{
    private Engine? _engine;

    public Engine Engine => _engine ?? throw new InvalidOperationException("Game is not running.");
    internal void Attach(Engine engine) => _engine = engine;

    protected Scene Scene => Engine.Scene;

    /// <summary>Called once, after the renderer is ready and before the first frame.</summary>
    public virtual void Initialize() { }

    /// <summary>Called every fixed step, before entity components update.</summary>
    public virtual void Update(GameTime time) { }

    /// <summary>Called after the scene has rendered; draw HUD / debug text here.</summary>
    public virtual void RenderOverlay(IRenderer renderer) { }

    /// <summary>Called once when the loop ends (also on exceptions).</summary>
    public virtual void Shutdown() { }
}
