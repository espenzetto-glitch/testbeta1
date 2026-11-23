using System.Collections.Concurrent;
using System.Numerics;

namespace SharpEngine.Streaming;

/// <summary>What the streamer needs from your engine. Implement it once (see EngineAdapterTemplate).</summary>
public interface IChunkBackend
{
    /// <summary>Read and parse a scene file. Called on a BACKGROUND thread: touch no engine/GL objects here.</summary>
    object Parse(string scene);

    /// <summary>
    /// Start creating the scene's objects under a root object placed at <paramref name="offset"/>.
    /// Called on the MAIN thread; must be cheap (create the root only).
    /// </summary>
    IChunkBuild Begin(ChunkEntry chunk, object parsed, Vector3 offset);
}

/// <summary>An in-progress or finished chunk instance in the shared world.</summary>
public interface IChunkBuild : IDisposable
{
    /// <summary>
    /// Create up to <paramref name="budget"/> more objects (decrement it for each one) on the main thread.
    /// Return true when the whole chunk exists. Dispose destroys the root and everything under it.
    /// </summary>
    bool Step(ref int budget);
}

/// <summary>Runs parsing work off the main thread. Swap for <see cref="InlineRunner"/> in tests.</summary>
public interface IBackgroundRunner
{
    Task<object> Run(Func<object> work);
}

public sealed class ThreadPoolRunner : IBackgroundRunner
{
    public Task<object> Run(Func<object> work) => Task.Run(work);
}

public sealed class InlineRunner : IBackgroundRunner
{
    public Task<object> Run(Func<object> work)
    {
        try { return Task.FromResult(work()); }
        catch (Exception e) { return Task.FromException<object>(e); }
    }
}

public sealed class StreamerSettings
{
    /// <summary>Chunks within this many cells (Chebyshev) of the player's cell are loaded. 1 = 3x3, 2 = 5x5.</summary>
    public int Radius { get; set; } = 1;

    /// <summary>Loaded chunks are only dropped beyond this many cells. Defaults to <see cref="Radius"/>; set higher to keep extra chunks around.</summary>
    public int? UnloadRadius { get; set; }

    /// <summary>
    /// The gap between "load" and "unload": the player's cell only changes once they are this many world units
    /// past a border, so walking back and forth over a border never makes chunks flicker in and out.
    /// </summary>
    public float BorderHysteresis { get; set; } = 2f;

    /// <summary>Max objects created on the main thread per Update call (spreads a chunk over several frames).</summary>
    public int ObjectsPerFrame { get; set; } = 64;

    /// <summary>Scene files parsed at the same time.</summary>
    public int MaxParallelParses { get; set; } = 2;

    public IBackgroundRunner Runner { get; set; } = new ThreadPoolRunner();

    internal int EffectiveUnloadRadius => Math.Max(UnloadRadius ?? Radius, Radius);
}

public enum ChunkState { Parsing, Building, Loaded, Failed }

/// <summary>
/// Keeps the chunks around the player loaded. Call <see cref="Update"/> once per frame on the main thread.
/// </summary>
public sealed class ChunkStreamer : IDisposable
{
    private sealed class Slot
    {
        public required ChunkEntry Entry;
        public ChunkState State = ChunkState.Parsing;
        public IChunkBuild? Build;
    }

    private readonly record struct ParseResult(Slot Slot, object? Parsed, Exception? Error);

    private readonly WorldLayout _layout;
    private readonly IChunkBackend _backend;
    private readonly Dictionary<Cell, Slot> _slots = new();
    private readonly ConcurrentQueue<ParseResult> _parsed = new();
    private int _parsesInFlight;
    private Cell? _center;

    public StreamerSettings Settings { get; }
    public WorldLayout Layout => _layout;

    /// <summary>The (sticky) cell the player is considered to be in.</summary>
    public Cell? CenterCell => _center;

    public event Action<ChunkEntry>? ChunkLoaded;
    public event Action<ChunkEntry>? ChunkUnloaded;
    public event Action<ChunkEntry, Exception>? ChunkFailed;

    public ChunkStreamer(WorldLayout layout, IChunkBackend backend, StreamerSettings? settings = null)
    {
        _layout = layout;
        _backend = backend;
        Settings = settings ?? new StreamerSettings();
    }

    public IEnumerable<ChunkEntry> LoadedChunks =>
        _slots.Values.Where(s => s.State == ChunkState.Loaded).Select(s => s.Entry);

    public ChunkState? StateOf(Cell cell) => _slots.TryGetValue(cell, out var s) ? s.State : null;

    /// <summary>True when nothing is parsing or being built.</summary>
    public bool IsSettled => _slots.Values.All(s => s.State is ChunkState.Loaded or ChunkState.Failed);

    public void Update(Vector3 player)
    {
        UpdateCenter(player);
        if (_center is not { } center) return;

        // 1. Unload what fell out of range.
        int unloadR = Settings.EffectiveUnloadRadius;
        foreach (var cell in _slots.Keys.Where(c => c.ChebyshevTo(center) > unloadR).ToList())
            Unload(cell);

        // 2. Start loading what came into range, nearest first.
        var wanted = _layout.Chunks
            .Where(c => c.Cell.ChebyshevTo(center) <= Settings.Radius && !_slots.ContainsKey(c.Cell))
            .OrderBy(c => c.Cell.ChebyshevTo(center))
            .ThenBy(c => Vector3.DistanceSquared(_layout.OffsetOf(c), player));
        foreach (var chunk in wanted)
        {
            if (Volatile.Read(ref _parsesInFlight) >= Settings.MaxParallelParses) break;
            StartParse(chunk);
        }

        // 3. Turn finished parses into builds (main thread).
        while (_parsed.TryDequeue(out var r))
        {
            Interlocked.Decrement(ref _parsesInFlight);
            if (!_slots.TryGetValue(r.Slot.Entry.Cell, out var live) || !ReferenceEquals(live, r.Slot))
                continue; // unloaded while it was parsing
            if (r.Error is not null) { Fail(r.Slot, r.Error); continue; }
            try
            {
                r.Slot.Build = _backend.Begin(r.Slot.Entry, r.Parsed!, _layout.OffsetOf(r.Slot.Entry));
                r.Slot.State = ChunkState.Building;
            }
            catch (Exception e) { Fail(r.Slot, e); }
        }

        // 4. Spend this frame's object budget, nearest chunks first.
        int budget = Settings.ObjectsPerFrame;
        foreach (var slot in _slots.Values
                     .Where(s => s.State == ChunkState.Building)
                     .OrderBy(s => s.Entry.Cell.ChebyshevTo(center)).ToList())
        {
            if (budget <= 0) break;
            try
            {
                if (slot.Build!.Step(ref budget))
                {
                    slot.State = ChunkState.Loaded;
                    ChunkLoaded?.Invoke(slot.Entry);
                }
            }
            catch (Exception e) { Fail(slot, e); }
        }
    }

    /// <summary>Unload everything (e.g. leaving Preview Streaming).</summary>
    public void Clear()
    {
        foreach (var c in _slots.Keys.ToList()) Unload(c);
        _center = null;
    }

    public void Dispose() => Clear();

    // ---------------------------------------------------------------- seams

    /// <summary>The area the player may walk in: the union of loaded chunks. Null when nothing is loaded yet.</summary>
    public Rect2? LoadedBounds()
    {
        Rect2? acc = null;
        foreach (var c in LoadedChunks)
        {
            var r = _layout.RectOf(c);
            acc = acc is { } a ? a.Union(r) : r;
        }
        return acc;
    }

    /// <summary>
    /// Camera clamping for the whole loaded area: unchanged inside any loaded chunk,
    /// otherwise moved to the closest point of the closest loaded chunk. Borders between loaded chunks are free to cross.
    /// </summary>
    public Vector3 ClampToLoaded(Vector3 p)
    {
        Rect2? best = null;
        float bestD = float.MaxValue;
        foreach (var c in LoadedChunks)
        {
            var r = _layout.RectOf(c);
            if (r.Contains(p.X, p.Z)) return p;
            float d = r.DistanceTo(p.X, p.Z);
            if (d < bestD) { bestD = d; best = r; }
        }
        if (best is not { } b) return p;
        var (x, z) = b.ClosestPoint(p.X, p.Z);
        return new Vector3(x, p.Y, z);
    }

    /// <summary>Lighting/sky for the player's position, blended across chunk borders.</summary>
    public EnvironmentSettings EvaluateEnvironment(
        Vector3 player, Func<ChunkEntry, EnvironmentSettings> environmentOf, float blendDistance, EnvironmentSettings fallback = default)
    {
        var list = LoadedChunks.Select(c => (_layout.RectOf(c), environmentOf(c))).ToList();
        return list.Count == 0 ? fallback : EnvironmentBlender.Evaluate(player, list, blendDistance);
    }

    // ---------------------------------------------------------------- internals

    private void UpdateCenter(Vector3 p)
    {
        var raw = _layout.CellAt(p);
        if (_center is not { } cur) { _center = raw; return; }
        if (raw == cur) return;
        var keep = ChunkPlacement.CellRect(cur, _layout.CellSize, _layout.Pivot).Expand(Settings.BorderHysteresis);
        if (!keep.Contains(p.X, p.Z)) _center = raw;
    }

    private void StartParse(ChunkEntry chunk)
    {
        var slot = new Slot { Entry = chunk };
        _slots[chunk.Cell] = slot;
        Interlocked.Increment(ref _parsesInFlight);
        Settings.Runner.Run(() => _backend.Parse(chunk.Scene))
            .ContinueWith(t => _parsed.Enqueue(t.IsFaulted
                    ? new ParseResult(slot, null, t.Exception!.GetBaseException())
                    : new ParseResult(slot, t.IsCanceled ? null : t.Result, t.IsCanceled ? new OperationCanceledException() : null)),
                TaskContinuationOptions.ExecuteSynchronously);
    }

    private void Unload(Cell cell)
    {
        if (!_slots.Remove(cell, out var slot)) return;
        bool wasLoaded = slot.State == ChunkState.Loaded;
        try { slot.Build?.Dispose(); } catch { /* destroying a half-built chunk must not break streaming */ }
        if (wasLoaded) ChunkUnloaded?.Invoke(slot.Entry);
    }

    private void Fail(Slot slot, Exception e)
    {
        slot.State = ChunkState.Failed; // stays registered so it is not retried every frame; dropped when out of range
        try { slot.Build?.Dispose(); } catch { }
        slot.Build = null;
        ChunkFailed?.Invoke(slot.Entry, e);
    }
}
