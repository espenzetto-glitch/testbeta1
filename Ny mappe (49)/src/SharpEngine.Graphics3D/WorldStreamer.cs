using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Raylib_cs;
using SharpEngine.Graphics3D.Editor;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Turns the World Map into one continuous world while playing.
///
/// The scene you opened is the anchor: it stays where it is. Every other scene is a chunk on the World Map grid,
/// and while the player is near it the streamer loads that scene's objects into the same world, moved to the chunk's
/// place on the grid (cell offset x cell size). Chunks that the player has left behind are unloaded again.
///
/// Rules:
///  - Cell size is the biggest scene on the map (<see cref="WorldLayout.CellSize"/>), the same size the World Map draws.
///  - Cells within <see cref="Radius"/> of the player's cell are loaded (1 = 3x3). A loaded chunk is only unloaded when it is
///    more than Radius + 1 cells away, so walking back and forth over a border never makes a chunk flicker.
///  - Scene files are read and parsed on a background thread; the objects are then created a few per frame so a load never stalls.
///  - Cameras of neighbouring scenes are ignored, the player keeps the camera of the main scene.
///  - While a chunk's neighbours are streamed the camera is kept inside the whole map instead of inside the main scene,
///    and sky, sun colour, sun strength and ambient light blend smoothly to those of the chunk the player stands in.
///  - Saving a scene never writes streamed objects into it: they belong to their own scene file.
/// </summary>
public sealed class WorldStreamer
{
    private sealed class LoadedChunk
    {
        public string Scene = "";
        public int X, Y;
        public Vector3 Offset;
        public readonly List<GameObject> Objects = new();
        public World Settings = new();
        public bool Complete;
    }

    private sealed class PendingLoad
    {
        public string Scene = "";
        public int X, Y;
        public Task<JsonObject?>? Parse;
        public JsonObject? Root;
        public LoadedChunk? Chunk;
        public int Next;
        public JsonArray? Objects;
    }

    private readonly World _world;
    private readonly List<LoadedChunk> _loaded = new();
    private readonly List<PendingLoad> _pending = new();
    private Chunk? _anchor;
    private bool _initialised;
    private float _cell = 60f;
    private (int MinX, int MinY, int MaxX, int MaxY) _extent;
    private World? _blendTarget;
    private List<Chunk> _candidates = new();
    private readonly HashSet<string> _merged = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoChecked;

    public WorldStreamer(World world) => _world = world;

    /// <summary>Turn streaming on or off (the Build Settings value is applied by <see cref="App3D"/>).</summary>
    public bool Enabled { get => (_enabled || _merged.Count > 0) && !Failed; set => _enabled = value; }
    private bool _enabled;

    /// <summary>Set when streaming threw an error: it stays off until the next scene load or Play so one bad chunk cannot stop the game.</summary>
    public bool Failed { get; set; }

    /// <summary>Cells kept loaded around the player. 1 = 3x3 chunks, 2 = 5x5.</summary>
    public int Radius { get => _radius; set => _radius = Math.Clamp(value, 0, 4); }
    private int _radius = 1;

    /// <summary>How far around the player chunks are streamed: <see cref="Radius"/>, or -1 (only merged scenes) when streaming is off.</summary>
    private int Reach => _enabled ? _radius : -1;

    // ---- merging scenes into a bigger one ----------------------------------------------------------

    /// <summary>
    /// Scenes that are merged into the open one right now. They are loaded wherever the player is and are never unloaded,
    /// so together with the open scene they act as one bigger scene. Temporary: it ends with <see cref="Unmerge"/>,
    /// when another scene is opened or when play stops.
    /// </summary>
    public IReadOnlyCollection<string> MergedScenes => _merged;

    public bool IsMerged(string scene) => _merged.Contains(SceneManager.NameOf(scene));

    /// <summary>Merges scenes (or a merge group by name) into the open scene. Returns how many scenes joined.</summary>
    public int Merge(params string[] namesOrGroups)
    {
        int added = 0;
        var anchor = _anchor?.Scene ?? SceneManager.CurrentName;
        foreach (var name in namesOrGroups)
            foreach (var scene in WorldLayout.ResolveMerge(name))
            {
                if (string.Equals(scene, anchor, StringComparison.OrdinalIgnoreCase) || !SceneManager.Exists(scene)) continue;
                if (_merged.Add(scene)) added++;
            }
        if (added > 0) _world.NotifyMergeChanged(_merged);
        return added;
    }

    /// <summary>Splits everything that was merged. Chunks that are too far away unload; with streaming off the camera returns to the open scene.</summary>
    public void Unmerge()
    {
        if (_merged.Count == 0) return;
        _merged.Clear();
        _world.NotifyMergeChanged(_merged);
    }

    /// <summary>Total size in cells of the open scene plus the merged scenes (what the bigger scene covers).</summary>
    public (int MinX, int MinY, int MaxX, int MaxY)? MergedExtent()
    {
        if (_anchor is null) return null;
        var cells = _candidates.Where(c => IsMerged(c.Scene)).Select(c => (c.X, c.Y)).Append((_anchor.X, _anchor.Y)).ToList();
        return (cells.Min(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.X), cells.Max(c => c.Y));
    }

    private void AutoMerge()
    {
        if (WorldLayout.GroupOf(SceneManager.CurrentName) is { Auto: true } g) Merge(g.Name);
    }

    /// <summary>Parse scene files on a background thread (turn off for tests).</summary>
    public bool Background { get; set; } = true;

    /// <summary>How many objects of a loading chunk are created per frame.</summary>
    public int ObjectsPerFrame { get; set; } = 80;

    /// <summary>Seconds-to-1/e for the sky and light blend between chunks.</summary>
    public float BlendSpeed { get; set; } = 2.5f;

    /// <summary>True while the streamer controls the world (enabled, playing, and the open scene is on the map).</summary>
    public bool Active => Enabled && _initialised && _anchor is not null;

    public string? AnchorScene => _anchor?.Scene;

    /// <summary>Scene the player is standing in, or null when not streaming.</summary>
    public string? CurrentChunk { get; private set; }

    public float CellSize => _cell;

    public int PendingCount => _pending.Count;

    public IEnumerable<string> LoadedScenes => _loaded.Where(c => c.Complete).Select(c => c.Scene);

    /// <summary>World position of the middle of a map cell.</summary>
    public Vector3 CellCenter(int x, int y) =>
        _anchor is null ? Vector3.Zero : new Vector3((x - _anchor.X) * _cell, 0f, (y - _anchor.Y) * _cell);

    /// <summary>Map cell that contains a world position.</summary>
    public (int X, int Y) CellAt(Vector3 p) =>
        _anchor is null ? (0, 0) : (_anchor.X + (int)MathF.Floor(p.X / _cell + 0.5f), _anchor.Y + (int)MathF.Floor(p.Z / _cell + 0.5f));

    /// <summary>Drops all loaded chunks (called when the world is cleared or play stops).</summary>
    public void Reset()
    {
        ResetChunks();
        _merged.Clear();
        _autoChecked = false;
    }

    private void ResetChunks()
    {
        foreach (var c in _loaded)
            foreach (var o in c.Objects)
                if (!o.Destroyed) _world.Destroy(o);
        _loaded.Clear();
        _pending.Clear();
        _anchor = null;
        _initialised = false;
        CurrentChunk = null;
        _blendTarget = null;
    }

    /// <summary>Starts streaming around the scene that is open now. Called automatically on the first update.</summary>
    public void Begin()
    {
        ResetChunks();   // merges requested before the start stay
        _initialised = true;
        if (!Enabled) return;
        var anchor = WorldLayout.Of(SceneManager.CurrentName);
        if (anchor is null) return;
        _anchor = new Chunk { Scene = anchor.Scene, X = anchor.X, Y = anchor.Y };
        _candidates = Candidates();
        _cell = MathF.Max(10f, _candidates.Max(c =>
        {
            // the open scene may have an unsaved size, so ask the world instead of its file
            var (x, z) = string.Equals(c.Scene, anchor.Scene, StringComparison.OrdinalIgnoreCase) ? (_world.SizeX, _world.SizeZ) : WorldLayout.SizeOf(c.Scene);
            return MathF.Max(x, z);
        }));
        _extent = (_candidates.Min(c => c.X), _candidates.Min(c => c.Y), _candidates.Max(c => c.X), _candidates.Max(c => c.Y));
        CurrentChunk = anchor.Scene;
        _blendTarget = CaptureLook();
        QueueAroundPlayer();
        FinishLoading();   // the first ring is there on the first frame
    }

    /// <summary>Blocks until every queued chunk is in the world.</summary>
    public void FinishLoading()
    {
        for (int guard = 0; _pending.Count > 0 && guard < 100000; guard++)
            Pump(int.MaxValue, wait: true);
        _world.Flush();
    }

    public void Update(float dt)
    {
        if (!_initialised && !_autoChecked) { _autoChecked = true; AutoMerge(); }   // a group marked "auto" merges when the player is in one of its scenes
        if (!Enabled) { if (_initialised) ResetChunks(); return; }
        if (!_initialised) Begin();
        if (_anchor is null) return;

        QueueAroundPlayer();
        UnloadFar();
        Pump(ObjectsPerFrame, wait: false);
        ClampCamera();
        Blend(dt);
    }

    // ---- which chunks -----------------------------------------------------------------------------

    /// <summary>
    /// The chunks that take part in streaming: the scenes of the build (so the editor and the built player agree),
    /// or every chunk when the build list is empty. The main scene is always part of it.
    /// </summary>
    private List<Chunk> Candidates()
    {
        var inBuild = SceneManager.Config.EnabledScenes.Select(b => SceneManager.NameOf(b.Path)).ToList();
        return WorldLayout.Chunks
            .Where(c => inBuild.Count == 0
                        || inBuild.Contains(c.Scene, StringComparer.OrdinalIgnoreCase)
                        || string.Equals(c.Scene, _anchor?.Scene, StringComparison.OrdinalIgnoreCase)
                        || IsMerged(c.Scene))
            .ToList();
    }

    private Vector3 PlayerPosition => _world.TryGetCameraPosition(out var p) ? p : CellCenter(_anchor!.X, _anchor.Y);

    private static int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    private void QueueAroundPlayer()
    {
        var (px, py) = CellAt(PlayerPosition);
        var here = WorldLayout.At(px, py);
        CurrentChunk = here?.Scene ?? CurrentChunk;

        foreach (var chunk in _candidates
                     .Where(c => Distance(c.X, c.Y, px, py) <= Reach || IsMerged(c.Scene))
                     .OrderBy(c => Distance(c.X, c.Y, px, py)))
        {
            if (string.Equals(chunk.Scene, _anchor!.Scene, StringComparison.OrdinalIgnoreCase)) continue;
            if (_loaded.Any(l => l.X == chunk.X && l.Y == chunk.Y) || _pending.Any(p => p.X == chunk.X && p.Y == chunk.Y)) continue;
            if (!SceneManager.Exists(chunk.Scene)) continue;
            Queue(chunk);
        }
    }

    private void Queue(Chunk chunk)
    {
        var pl = new PendingLoad { Scene = chunk.Scene, X = chunk.X, Y = chunk.Y };
        string file = SceneManager.FileFor(chunk.Scene);
        pl.Parse = Background ? Task.Run(() => ParseFile(file)) : Task.FromResult(ParseFile(file));
        _pending.Add(pl);
    }

    private static JsonObject? ParseFile(string file)
    {
        try { return JsonNode.Parse(File.ReadAllText(file)) as JsonObject; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"[stream] could not read {file}: {ex.Message}");
            return null;
        }
    }

    private void UnloadFar()
    {
        var (px, py) = CellAt(PlayerPosition);
        for (int i = _loaded.Count - 1; i >= 0; i--)
        {
            var c = _loaded[i];
            if (IsMerged(c.Scene) || Distance(c.X, c.Y, px, py) <= Reach + 1) continue;
            foreach (var o in c.Objects) if (!o.Destroyed) _world.Destroy(o);
            _loaded.RemoveAt(i);
        }
        // queued chunks the player has already walked away from are not worth loading
        _pending.RemoveAll(p => p.Chunk is null && !IsMerged(p.Scene) && Distance(p.X, p.Y, px, py) > Reach + 1);
    }

    // ---- creating the objects ---------------------------------------------------------------------

    private void Pump(int budget, bool wait)
    {
        for (int i = 0; i < _pending.Count && budget > 0; )
        {
            var p = _pending[i];
            if (p.Root is null && p.Chunk is null)
            {
                if (!p.Parse!.IsCompleted && !wait) { i++; continue; }
                p.Root = p.Parse.GetAwaiter().GetResult();
                if (p.Root is null) { _pending.RemoveAt(i); continue; }   // unreadable: skip this chunk

                var chunk = new LoadedChunk { Scene = p.Scene, X = p.X, Y = p.Y, Offset = CellCenter(p.X, p.Y) };
                SceneSerializer.ApplySettings(chunk.Settings, p.Root);
                p.Chunk = chunk;
                p.Objects = p.Root["objects"] as JsonArray;
                _loaded.Add(chunk);
            }

            var lc = p.Chunk!;
            var arr = p.Objects;
            while (arr is not null && p.Next < arr.Count && budget > 0)
            {
                budget--;
                if (arr[p.Next++] is not JsonObject o) continue;
                try
                {
                    if (SceneSerializer.ReadChunkObject(_world, o, lc.Offset, lc.Scene) is { } go) lc.Objects.Add(go);
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or InvalidCastException or ArgumentException or KeyNotFoundException)
                {
                    Console.Error.WriteLine($"[stream] bad object in '{lc.Scene}': {ex.Message}");
                }
            }

            if (arr is null || p.Next >= arr.Count)
            {
                lc.Complete = true;
                _pending.RemoveAt(i);
            }
            else i++;
        }
    }

    // ---- camera and light -------------------------------------------------------------------------

    /// <summary>Keeps the camera inside the whole map (every cell of the grid) instead of inside the main scene.</summary>
    private void ClampCamera()
    {
        if (!_world.ClampCamera || _world.FindCameraObject() is not { } cam) return;
        float half = _cell / 2f;
        var ext = _enabled ? _extent : MergedExtent() ?? _extent;   // streaming off: only the open scene plus what is merged into it
        float minX = (ext.MinX - _anchor!.X) * _cell - half, maxX = (ext.MaxX - _anchor.X) * _cell + half;
        float minZ = (ext.MinY - _anchor.Y) * _cell - half, maxZ = (ext.MaxY - _anchor.Y) * _cell + half;
        var p = cam.Transform.Position;
        var c = new Vector3(Math.Clamp(p.X, minX, maxX), p.Y, Math.Clamp(p.Z, minZ, maxZ));
        if (c != p) cam.Transform.Position = c;
    }

    /// <summary>Sky, sun colour, sun strength and ambient light move toward the chunk the player is in.</summary>
    private void Blend(float dt)
    {
        if (CurrentChunk is null) return;
        World? target = string.Equals(CurrentChunk, _anchor!.Scene, StringComparison.OrdinalIgnoreCase)
            ? _blendTarget   // the main scene's own look, remembered when streaming began
            : _loaded.FirstOrDefault(c => c.Complete && string.Equals(c.Scene, CurrentChunk, StringComparison.OrdinalIgnoreCase))?.Settings;
        if (target is null) return;

        float k = 1f - MathF.Exp(-BlendSpeed * dt);
        _world.SkyTop = Lerp(_world.SkyTop, target.SkyTop, k);
        _world.SkyBottom = Lerp(_world.SkyBottom, target.SkyBottom, k);
        _world.Sun.Color = Lerp(_world.Sun.Color, target.Sun.Color, k);
        _world.Sun.Intensity += (target.Sun.Intensity - _world.Sun.Intensity) * k;
        _world.Sun.Ambient += (target.Sun.Ambient - _world.Sun.Ambient) * k;
    }

    private World CaptureLook()
    {
        var w = new World { SkyTop = _world.SkyTop, SkyBottom = _world.SkyBottom };
        w.Sun.Color = _world.Sun.Color;
        w.Sun.Intensity = _world.Sun.Intensity;
        w.Sun.Ambient = _world.Sun.Ambient;
        return w;
    }

    private static Color Lerp(Color a, Color b, float t) => new(
        (byte)Math.Clamp((int)MathF.Round(a.R + (b.R - a.R) * t), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(a.G + (b.G - a.G) * t), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(a.B + (b.B - a.B) * t), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(a.A + (b.A - a.A) * t), 0, 255));

    // ---- chunk debugger ---------------------------------------------------------------------------

    /// <summary>One chunk as the debug view shows it.</summary>
    public readonly record struct DebugChunk(string Scene, int X, int Y, Vector3 Center, float SizeX, float SizeZ, string State, int Objects);

    private List<DebugChunk> _debugCache = new();
    private long _debugStamp;
    private float _debugCell = 60f;

    /// <summary>Player cell and loaded radius for the debug view (radius -1 when not streaming).</summary>
    public (int X, int Y, int Radius) DebugPlayer { get; private set; }

    public float DebugCellSize => Active ? _cell : _debugCell;

    /// <summary>
    /// Every chunk around the open scene with its state: main (the open scene), here (the player stands in it),
    /// loaded, merged (joined into the open scene), loading, far (streamed scene that is not loaded right now) or map (only shown, not playing).
    /// </summary>
    public IReadOnlyList<DebugChunk> Describe()
    {
        long now = Environment.TickCount64;
        if (!Active && now - _debugStamp < 500 && _debugCache.Count > 0) return _debugCache;
        _debugStamp = now;

        var list = new List<DebugChunk>();
        var anchor = Active ? _anchor : WorldLayout.Of(SceneManager.CurrentName);
        if (anchor is null) { _debugCache = list; DebugPlayer = (0, 0, -1); return list; }

        float SizeXOf(string sc, out float z)
        {
            if (string.Equals(sc, anchor.Scene, StringComparison.OrdinalIgnoreCase)) { z = _world.SizeZ; return _world.SizeX; }
            var (x, zz) = WorldLayout.SizeOf(sc); z = zz; return x;
        }

        var chunks = Active ? _candidates : WorldLayout.Chunks.ToList();
        float cell = 10f;
        foreach (var c in chunks) { float x = SizeXOf(c.Scene, out float z); cell = MathF.Max(cell, MathF.Max(x, z)); }
        if (Active) cell = _cell;
        _debugCell = cell;

        var (px, py) = Active ? CellAt(PlayerPosition) : (anchor.X, anchor.Y);
        DebugPlayer = (px, py, Active ? Math.Max(0, Reach) : -1);

        foreach (var c in chunks)
        {
            float sx = SizeXOf(c.Scene, out float sz);
            var center = new Vector3((c.X - anchor.X) * cell, 0f, (c.Y - anchor.Y) * cell);
            bool isAnchor = string.Equals(c.Scene, anchor.Scene, StringComparison.OrdinalIgnoreCase);
            string state;
            int objects = 0;
            if (!Active) state = isAnchor ? "main" : "map";
            else if (isAnchor) { state = "main"; objects = _world.Objects.Count(o => o.Chunk is null && !o.Destroyed); }
            else if (_loaded.FirstOrDefault(l => l.X == c.X && l.Y == c.Y) is { } l) { state = l.Complete ? (IsMerged(c.Scene) ? "merged" : "loaded") : "loading"; objects = l.Objects.Count(o => !o.Destroyed); }
            else state = _pending.Any(p => p.X == c.X && p.Y == c.Y) ? "loading" : IsMerged(c.Scene) ? "loading" : "far";
            list.Add(new DebugChunk(c.Scene, c.X, c.Y, center, sx, sz, state, objects));
        }
        _debugCache = list;
        return list;
    }

    public static Color DebugColor(string state, bool here) =>
        here ? new Color(255, 220, 70, 255) :
        state switch
        {
            "main" => new Color(90, 160, 255, 255),
            "loaded" => new Color(90, 230, 120, 255),
            "merged" => new Color(200, 120, 255, 255),
            "loading" => new Color(255, 160, 50, 255),
            _ => new Color(150, 150, 165, 255),
        };

    /// <summary>Draws chunk borders in the 3D view (call inside BeginMode3D).</summary>
    public void DrawDebug()
    {
        var chunks = Describe();
        if (chunks.Count == 0) return;
        float cell = DebugCellSize, half = cell / 2f;
        var (px, py, radius) = DebugPlayer;
        var anchorChunk = chunks.FirstOrDefault(c => c.State == "main");

        foreach (var c in chunks)
        {
            bool here = radius >= 0 && c.X == px && c.Y == py;
            var col = DebugColor(c.State, here);
            // the cell (what the map reserves for the scene) in dim colour, the scene's own rectangle bright
            DrawRect(c.Center, half, half, col with { A = 70 }, 0.04f);
            DrawRect(c.Center, c.SizeX / 2f, c.SizeZ / 2f, col, 0.06f);
            if (c.State != "far" && c.State != "map")
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var b = c.Center + new Vector3(sx * c.SizeX / 2f, 0.06f, sz * c.SizeZ / 2f);
                    Rl.DrawLine3D(b, b + new Vector3(0, here ? 14f : 8f, 0), col);
                }
        }

        if (radius >= 0 && anchorChunk.Scene is not null)
        {
            // the loaded area: cells within the radius of the player's cell
            var mid = anchorChunk.Center + new Vector3((px - anchorChunk.X) * cell, 0f, (py - anchorChunk.Y) * cell);
            float r = (radius + 0.5f) * cell;
            DrawRect(mid, r, r, new Color(255, 255, 255, 200), 0.1f);
            if (_world.TryGetCameraPosition(out var cam))
            {
                Rl.DrawLine3D(new Vector3(cam.X - 2, 0.12f, cam.Z), new Vector3(cam.X + 2, 0.12f, cam.Z), Color.White);
                Rl.DrawLine3D(new Vector3(cam.X, 0.12f, cam.Z - 2), new Vector3(cam.X, 0.12f, cam.Z + 2), Color.White);
                Rl.DrawLine3D(new Vector3(cam.X, 0.12f, cam.Z), cam, new Color(255, 255, 255, 90));
            }
        }
    }

    private static void DrawRect(Vector3 c, float hx, float hz, Color col, float y)
    {
        var a = new Vector3(c.X - hx, y, c.Z - hz); var b = new Vector3(c.X + hx, y, c.Z - hz);
        var d = new Vector3(c.X + hx, y, c.Z + hz); var e = new Vector3(c.X - hx, y, c.Z + hz);
        Rl.DrawLine3D(a, b, col); Rl.DrawLine3D(b, d, col); Rl.DrawLine3D(d, e, col); Rl.DrawLine3D(e, a, col);
    }

    /// <summary>Names and a status line on screen (call after the 3D pass).</summary>
    public void DrawDebugOverlay()
    {
        var chunks = Describe();
        if (chunks.Count == 0 || !_world.TryGetCamera(out var camera)) return;
        var (px, py, radius) = DebugPlayer;
        var fwd = Vector3.Normalize(camera.Target - camera.Position);
        int sw = Rl.GetScreenWidth(), sh = Rl.GetScreenHeight();

        foreach (var c in chunks)
        {
            var pos = c.Center + new Vector3(0, 3f, 0);
            if (Vector3.Dot(pos - camera.Position, fwd) <= 0.1f) continue;   // behind the camera
            var s = Rl.GetWorldToScreen(pos, camera);
            if (s.X < -200 || s.X > sw + 200 || s.Y < -50 || s.Y > sh + 50) continue;
            bool here = radius >= 0 && c.X == px && c.Y == py;
            var col = DebugColor(c.State, here);
            string line1 = c.Scene, line2 = $"({c.X}, {c.Y})  {(here ? "YOU ARE HERE  " : "")}{c.State}" + (c.Objects > 0 ? $"  {c.Objects} obj" : "");
            int w1 = Rl.MeasureText(line1, 22), w2 = Rl.MeasureText(line2, 16);
            Rl.DrawRectangle((int)s.X - Math.Max(w1, w2) / 2 - 8, (int)s.Y - 6, Math.Max(w1, w2) + 16, 50, new Color(0, 0, 0, 170));
            Rl.DrawText(line1, (int)s.X - w1 / 2, (int)s.Y, 22, col);
            Rl.DrawText(line2, (int)s.X - w2 / 2, (int)s.Y + 26, 16, Color.White);
        }

        string head = radius >= 0
            ? $"In {CurrentChunk ?? "-"} | cell {px},{py} | loaded {chunks.Count(c => c.State is "loaded" or "main" or "merged")}/{chunks.Count} | radius {radius}{(_merged.Count > 0 ? " | merged: " + string.Join("+", _merged) : "")} | F3 hides"
            : "Chunk map (press Play to stream) | F3 hides";
        int hw = Rl.MeasureText(head, 16), hx = Math.Max(8, (int)(sw * 0.44f) - hw / 2), hy = sh - 170;
        Rl.DrawRectangle(hx - 12, hy - 6, hw + 24, 30, new Color(0, 0, 0, 180));
        Rl.DrawText(head, hx, hy, 16, Color.White);
    }
}
