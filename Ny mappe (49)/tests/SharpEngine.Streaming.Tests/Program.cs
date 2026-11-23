using System.Numerics;
using SharpEngine.Streaming;

// Dependency-free test runner: `dotnet run --project tests/SharpEngine.Streaming.Tests`
// Exit code 0 = all passed.

int passed = 0, failed = 0;

void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine($"  ok    {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"  FAIL  {name}\n        {e.Message}"); }
}

void True(bool cond, string msg = "expected true") { if (!cond) throw new Exception(msg); }
void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected {expected}, got {actual}"); }
void Near(float expected, float actual, float eps = 0.001f) { if (MathF.Abs(expected - actual) > eps) throw new Exception($"expected {expected}, got {actual}"); }

WorldLayout Grid(int n, float size = 60f, ChunkPivot pivot = ChunkPivot.Center)
{
    var list = new List<ChunkEntry>();
    for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
            list.Add(new ChunkEntry($"S{x}_{y}", new Cell(x, y), size, size));
    return new WorldLayout(list, pivot);
}

Console.WriteLine("Placement");

Test("offset is cell * cellSize on XZ", () =>
{
    var o = ChunkPlacement.Offset(new Cell(2, -3), 60f);
    Near(120f, o.X); Near(0f, o.Y); Near(-180f, o.Z);
});

Test("cell size is the biggest scene dimension", () =>
{
    var l = new WorldLayout(new[]
    {
        new ChunkEntry("A", new Cell(0, 0), 60, 40),
        new ChunkEntry("B", new Cell(1, 0), 100, 80),
    });
    Near(100f, l.CellSize);
});

Test("CellAt (centre pivot) puts borders half a cell out", () =>
{
    Eq(new Cell(0, 0), ChunkPlacement.CellAt(29.9f, -29.9f, 60f, ChunkPivot.Center));
    Eq(new Cell(1, -1), ChunkPlacement.CellAt(30.1f, -30.1f, 60f, ChunkPivot.Center));
});

Test("CellAt (corner pivot) handles negatives", () =>
{
    Eq(new Cell(-1, 0), ChunkPlacement.CellAt(-0.1f, 0.1f, 60f, ChunkPivot.Corner));
});

Test("neighbouring ground meshes line up exactly", () =>
{
    var l = Grid(2);
    l.TryGet(new Cell(0, 0), out var a); l.TryGet(new Cell(1, 0), out var b);
    Near(l.RectOf(a).MaxX, l.RectOf(b).MinX, 0f);
});

Test("duplicate cells are rejected", () =>
{
    bool threw = false;
    try { _ = new WorldLayout(new[] { new ChunkEntry("A", new Cell(0, 0), 1, 1), new ChunkEntry("B", new Cell(0, 0), 1, 1) }); }
    catch (ArgumentException) { threw = true; }
    True(threw);
});

Console.WriteLine("Layout JSON");

Test("reads list form with varied field names", () =>
{
    var l = WorldLayout.FromJson("""{ "chunks": [ {"scene":"Main","x":0,"y":0,"width":60,"depth":60}, {"Name":"L2","CellX":1,"CellY":0,"w":60} ] }""");
    Eq(2, l.Chunks.Count);
    Eq(new Cell(1, 0), l.Chunks[1].Cell);
    Near(60f, l.Chunks[1].Depth); // falls back to default
});

Test("reads dictionary-by-scene form with nested cell", () =>
{
    var l = WorldLayout.FromJson("""{ "scenes": { "Main": {"cell":{"x":0,"y":0}}, "L2": {"cell":{"x":0,"y":1}, "width":80, "depth":80} } }""");
    Eq(2, l.Chunks.Count);
    Near(80f, l.CellSize);
});

Test("round-trips through ToJson", () =>
{
    var l = Grid(2);
    var back = WorldLayout.FromJson(l.ToJson());
    Eq(4, back.Chunks.Count);
});

Console.WriteLine("Streaming");

// Fake backend recording what exists in the "world".
var world = new HashSet<string>();
var offsets = new Dictionary<string, Vector3>();
var mainThreadViolations = 0;
var mainThread = Environment.CurrentManagedThreadId;

IChunkBackend MakeBackend(int objectsPerChunk = 10, Func<string, bool>? failParse = null) => new FakeBackend(world, offsets, objectsPerChunk, failParse);

Test("first update loads the 3x3 around the player, placed at offsets", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(5), MakeBackend(), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100 });
    s.Update(new Vector3(120, 0, 120)); // centre of cell (2,2)
    Eq(9, s.LoadedChunks.Count());
    True(world.Contains("S1_1") && world.Contains("S3_3") && !world.Contains("S0_0"));
    Near(120f, offsets["S2_2"].X); Near(180f, offsets["S3_3"].Z);
});

Test("moving across a border loads the new column and unloads the old one", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(5), MakeBackend(), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100 });
    s.Update(new Vector3(60, 0, 120));  // cell (1,2): columns 0..2
    True(world.Contains("S0_2") && !world.Contains("S3_2"));
    s.Update(new Vector3(120, 0, 120)); // cell (2,2): columns 1..3
    True(!world.Contains("S0_2") && world.Contains("S3_2"));
    Eq(9, s.LoadedChunks.Count());
});

Test("hysteresis stops flicker on a border", () =>
{
    world.Clear(); offsets.Clear();
    int loads = 0, unloads = 0;
    var s = new ChunkStreamer(Grid(5), MakeBackend(), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100, BorderHysteresis = 2f });
    s.ChunkLoaded += _ => loads++; s.ChunkUnloaded += _ => unloads++;
    s.Update(new Vector3(89f, 0, 120));        // cell (1,2), just inside its east edge (90)
    int l0 = loads, u0 = unloads;
    for (int i = 0; i < 20; i++)                // jitter 1 unit over the border and back
    {
        s.Update(new Vector3(90.5f + (i % 2) * 0.5f, 0, 120));
        s.Update(new Vector3(89.5f, 0, 120));
    }
    Eq(l0, loads); Eq(u0, unloads);
    s.Update(new Vector3(93f, 0, 120));         // clearly past the border + gap
    True(loads > l0, "should have switched cells");
});

Test("radius setting changes how many chunks load", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(7), MakeBackend(), new StreamerSettings { Radius = 2, Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100 });
    s.Update(new Vector3(180, 0, 180));
    Eq(25, s.LoadedChunks.Count());
});

Test("edges of the map only load chunks that exist", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(3), MakeBackend(), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100 });
    s.Update(Vector3.Zero);
    Eq(4, s.LoadedChunks.Count()); // (0..1, 0..1)
});

Test("object budget spreads a chunk over several frames", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(1), MakeBackend(objectsPerChunk: 25), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 10 });
    int frames = 0;
    do { s.Update(Vector3.Zero); frames++; } while (!s.IsSettled && frames < 20);
    Eq(3, frames); // 10 + 10 + 5
    Eq(1, s.LoadedChunks.Count());
});

Test("parsing really happens off the main thread with the default runner", () =>
{
    world.Clear(); offsets.Clear();
    var be = new FakeBackend(world, offsets, 5, null);
    var s = new ChunkStreamer(Grid(1), be, new StreamerSettings());
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (!s.IsSettled || s.LoadedChunks.Count() == 0)
    {
        s.Update(Vector3.Zero);
        if (sw.ElapsedMilliseconds > 5000) throw new Exception("timed out");
        Thread.Sleep(1);
    }
    True(be.ParseThreadId != mainThread, "Parse ran on the main thread");
    Eq(mainThread, be.BeginThreadId);
});

Test("a chunk unloaded while parsing is discarded, not built", () =>
{
    world.Clear(); offsets.Clear();
    var gate = new ManualResetEventSlim(false);
    var be = new FakeBackend(world, offsets, 5, null) { ParseGate = gate };
    var s = new ChunkStreamer(Grid(6), be, new StreamerSettings { MaxParallelParses = 1 });
    s.Update(Vector3.Zero);                    // starts parsing (0,0)
    s.Update(new Vector3(300, 0, 300));        // player far away: (0,0) out of range -> unloaded
    gate.Set();
    for (int i = 0; i < 200; i++) { s.Update(new Vector3(300, 0, 300)); Thread.Sleep(2); }
    True(!world.Contains("S0_0"), "stale chunk was built");
});

Test("failed parse is reported and does not retry every frame", () =>
{
    world.Clear(); offsets.Clear();
    var be = new FakeBackend(world, offsets, 5, name => name == "S0_0");
    var s = new ChunkStreamer(Grid(1), be, new StreamerSettings { Runner = new InlineRunner() });
    int fails = 0; s.ChunkFailed += (_, _) => fails++;
    for (int i = 0; i < 10; i++) s.Update(Vector3.Zero);
    Eq(1, fails); Eq(1, be.ParseCalls);
});

Test("Clear destroys every object", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(3), MakeBackend(), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100 });
    s.Update(Vector3.Zero); s.Clear();
    Eq(0, world.Count);
});

Console.WriteLine("Seams");

Test("clamp lets the player cross a border between loaded chunks", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(3), MakeBackend(), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100 });
    s.Update(Vector3.Zero);
    var p = new Vector3(55, 3, 10); // inside chunk (1,0), outside chunk (0,0)
    Eq(p, s.ClampToLoaded(p));
});

Test("clamp pulls the player back at the edge of the loaded area", () =>
{
    world.Clear(); offsets.Clear();
    var s = new ChunkStreamer(Grid(3), MakeBackend(), new StreamerSettings { Runner = new InlineRunner(), ObjectsPerFrame = 1000, MaxParallelParses = 100 });
    s.Update(Vector3.Zero);
    var c = s.ClampToLoaded(new Vector3(-500, 7, 0));
    Near(-30f, c.X); Near(7f, c.Y);
    var b = s.LoadedBounds()!.Value;
    Near(-30f, b.MinX); Near(90f, b.MaxX);
});

Test("environment is continuous across a border and picks the local chunk deep inside", () =>
{
    var l = Grid(2);
    var day = new EnvironmentSettings(new(0.2f, 0.4f, 1f), new(0.8f, 0.9f, 1f), new(1, 1, 0.9f), 1f, 0.4f, 0.0f);
    var night = new EnvironmentSettings(new(0, 0, 0.1f), new(0.1f, 0.1f, 0.2f), new(0.3f, 0.3f, 0.6f), 0.1f, 0.1f, 0.02f);
    var chunks = l.Chunks.Select(c => (l.RectOf(c), c.Cell.X == 0 ? day : night)).ToList();
    Near(day.SunIntensity, EnvironmentBlender.Evaluate(new(0, 0, 0), chunks, 10f).SunIntensity);
    Near(night.SunIntensity, EnvironmentBlender.Evaluate(new(60, 0, 0), chunks, 10f).SunIntensity);
    float atBorder = EnvironmentBlender.Evaluate(new(30, 0, 0), chunks, 10f).SunIntensity;
    Near((day.SunIntensity + night.SunIntensity) / 2f, atBorder, 0.01f);
    float prev = 1f; // monotonic, no snap: every 0.25 units the change stays tiny
    for (float x = 15; x <= 45; x += 0.25f)
    {
        float v = EnvironmentBlender.Evaluate(new(x, 0, 0), chunks, 10f).SunIntensity;
        True(MathF.Abs(v - prev) < 0.05f || x == 15, $"jump at x={x}");
        True(v <= prev + 1e-4f, "not monotonic");
        prev = v;
    }
});

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FakeBackend : IChunkBackend
{
    private readonly HashSet<string> _world; private readonly Dictionary<string, Vector3> _offsets;
    private readonly int _objects; private readonly Func<string, bool>? _failParse;
    public int ParseThreadId, BeginThreadId, ParseCalls;
    public ManualResetEventSlim? ParseGate;

    public FakeBackend(HashSet<string> w, Dictionary<string, Vector3> o, int objects, Func<string, bool>? failParse)
    { _world = w; _offsets = o; _objects = objects; _failParse = failParse; }

    public object Parse(string scene)
    {
        Interlocked.Increment(ref ParseCalls);
        ParseThreadId = Environment.CurrentManagedThreadId;
        ParseGate?.Wait();
        if (_failParse?.Invoke(scene) == true) throw new InvalidDataException("bad scene");
        return scene;
    }

    public IChunkBuild Begin(ChunkEntry chunk, object parsed, Vector3 offset)
    {
        BeginThreadId = Environment.CurrentManagedThreadId;
        _offsets[chunk.Scene] = offset;
        return new Build(this, chunk.Scene);
    }

    private sealed class Build : IChunkBuild
    {
        private readonly FakeBackend _b; private readonly string _name; private int _made;
        public Build(FakeBackend b, string name) { _b = b; _name = name; _b._world.Add(name); }
        public bool Step(ref int budget)
        {
            while (budget > 0 && _made < _b._objects) { _made++; budget--; }
            return _made >= _b._objects;
        }
        public void Dispose() => _b._world.Remove(_name);
    }
}
