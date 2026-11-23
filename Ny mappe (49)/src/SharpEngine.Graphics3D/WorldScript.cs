using System.Reflection;
using SharpEngine.Graphics3D.Editor;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Code that belongs to the whole world instead of one object or scene. One instance of every WorldScript class is created when
/// the game starts playing and lives until it stops, across scene changes, merges and streaming. Write them in the World Map's
/// code editor ("World Code"). The editor's completion list knows every scene, object and component of the project.
/// </summary>
public abstract class WorldScript
{
    /// <summary>The world (scene) the game is running in.</summary>
    public World World { get; internal set; } = null!;

    /// <summary>Turn the script off (it stops getting Update calls). Scripts that throw are switched off automatically.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Name of the scene that is open now.</summary>
    public string CurrentScene => SceneManager.CurrentName;

    /// <summary>Scenes merged into the open scene right now.</summary>
    public IReadOnlyCollection<string> MergedScenes => World.Streamer.MergedScenes;

    /// <summary>Runs once when play starts.</summary>
    public virtual void Start() { }

    /// <summary>Runs every frame while playing. <paramref name="dt"/> is seconds since the last frame.</summary>
    public virtual void Update(float dt) { }

    /// <summary>A scene was opened (also once when play starts, for the first scene).</summary>
    public virtual void OnSceneLoaded(string scene) { }

    /// <summary>Scenes were merged into, or split from, the open scene. <paramref name="merged"/> lists what is merged now.</summary>
    public virtual void OnMergeChanged(IReadOnlyCollection<string> merged) { }

    /// <summary>Play stops.</summary>
    public virtual void OnStop() { }

    // ---- shortcuts -------------------------------------------------------------------------------

    /// <summary>An object of the open scene (or of a streamed or merged scene) by name.</summary>
    protected GameObject? Find(string name) => World.Find(name);

    protected void LoadScene(string scene) => SceneManager.LoadScene(scene);
    protected void Merge(params string[] scenesOrGroups) => SceneManager.Merge(scenesOrGroups);
    protected void Unmerge() => SceneManager.Unmerge();

    /// <summary>Everything the project contains: every scene, object and component, also of scenes that are not loaded.</summary>
    protected static IReadOnlyList<SceneInfo> Scenes => WorldIndex.Scenes;
}

/// <summary>Finds the WorldScript classes of the program (built in and compiled from Scripts/).</summary>
public static class WorldScriptRegistry
{
    private static List<Type>? _builtIn;

    public static IReadOnlyList<Type> Types
    {
        get
        {
            _builtIn ??= Discover();
            Scripting.ScriptManager.EnsureLoaded();
            return _builtIn.Concat(Scripting.ScriptManager.WorldTypes).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }
    }

    private static List<Type> Discover()
    {
        var found = new List<Type>();
        var baseAssembly = typeof(WorldScript).Assembly;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            if (System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(asm) is { } alc && alc.Name == "SharpEngine.Scripts") continue;
            if (asm != baseAssembly && !asm.GetReferencedAssemblies().Any(n => n.Name == baseAssembly.GetName().Name)) continue;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            found.AddRange(types.Where(t => t.IsPublic && !t.IsAbstract && typeof(WorldScript).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null));
        }
        return found;
    }
}

/// <summary>Runs the world scripts of one world. Every call is guarded: a script that throws is reported and switched off.</summary>
internal sealed class WorldScriptHost
{
    private readonly World _world;
    private readonly List<WorldScript> _scripts = new();
    private bool _started;

    public WorldScriptHost(World world) => _world = world;

    public int Count => _scripts.Count;
    public IReadOnlyList<WorldScript> Scripts => _scripts;

    private void Guard(WorldScript s, string what, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            s.Enabled = false;
            ErrorLog.Report($"world script {s.GetType().Name}.{what}", ex);
            Scripting.ScriptManager.ReportWorldScriptError(s, ex);
        }
    }

    public void Update(float dt)
    {
        if (!_started)
        {
            _started = true;
            foreach (var type in WorldScriptRegistry.Types)
            {
                try
                {
                    var s = (WorldScript)Activator.CreateInstance(type)!;
                    s.World = _world;
                    _scripts.Add(s);
                }
                catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { ErrorLog.Report("creating world script " + type.Name, ex); }
            }
            foreach (var s in _scripts.ToList()) Guard(s, "Start", s.Start);
            foreach (var s in _scripts.ToList()) if (s.Enabled) Guard(s, "OnSceneLoaded", () => s.OnSceneLoaded(SceneManager.CurrentName));
        }
        foreach (var s in _scripts.ToList())
            if (s.Enabled) Guard(s, "Update", () => s.Update(dt));
    }

    public void SceneLoaded(string scene)
    {
        if (!_started) return;   // the first scene is announced together with Start
        foreach (var s in _scripts.ToList()) if (s.Enabled) Guard(s, "OnSceneLoaded", () => s.OnSceneLoaded(scene));
    }

    public void MergeChanged(IReadOnlyCollection<string> merged)
    {
        if (!_started) return;
        var copy = merged.ToList();
        foreach (var s in _scripts.ToList()) if (s.Enabled) Guard(s, "OnMergeChanged", () => s.OnMergeChanged(copy));
    }

    public void Stop()
    {
        foreach (var s in _scripts.ToList()) Guard(s, "OnStop", s.OnStop);
        _scripts.Clear();
    }
}
