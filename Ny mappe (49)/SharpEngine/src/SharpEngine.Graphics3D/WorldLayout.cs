using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpEngine.Graphics3D;

/// <summary>One scene's place in the big world: a cell of the world grid.</summary>
public sealed class Chunk
{
    public string Scene { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// The world is a grid of chunks and every chunk is one scene (its own editable world with its own size).
/// Adding a scene adds a chunk, so the world grows as the game grows. Saved as WorldMap.json in the project.
/// </summary>
public static class WorldLayout
{
    public const string FileName = "WorldMap.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static List<Chunk> _chunks = new();
    private static bool _loaded;
    private static readonly Dictionary<string, (DateTime Stamp, float X, float Z)> SizeCache = new(StringComparer.OrdinalIgnoreCase);

    public static string FilePath => Path.Combine(GameProject.Root, FileName);

    public static IReadOnlyList<Chunk> Chunks { get { EnsureLoaded(); return _chunks; } }

    /// <summary>Forget everything (a different project folder, or tests).</summary>
    public static void Reset()
    {
        _chunks = new List<Chunk>();
        _loaded = false;
        SizeCache.Clear();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _chunks = new List<Chunk>();
        try
        {
            if (!File.Exists(FilePath)) return;
            var node = JsonNode.Parse(File.ReadAllText(FilePath));
            if (node?["chunks"] is JsonArray arr)
                foreach (var c in arr)
                    if (c?["scene"]?.GetValue<string>() is { Length: > 0 } scene && c["x"] is { } x && c["y"] is { } y
                        && _chunks.All(o => !string.Equals(o.Scene, scene, StringComparison.OrdinalIgnoreCase) && !(o.X == x.GetValue<int>() && o.Y == y.GetValue<int>())))
                        _chunks.Add(new Chunk { Scene = scene, X = x.GetValue<int>(), Y = y.GetValue<int>() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            Console.Error.WriteLine($"[world] could not read {FileName}: {ex.Message}");
        }
    }

    public static void Save()
    {
        EnsureLoaded();
        var arr = new JsonArray();
        foreach (var c in _chunks.OrderBy(c => c.Y).ThenBy(c => c.X))
            arr.Add(new JsonObject { ["scene"] = c.Scene, ["x"] = c.X, ["y"] = c.Y });
        Directory.CreateDirectory(GameProject.Root);
        File.WriteAllText(FilePath, new JsonObject { ["chunks"] = arr }.ToJsonString(Options));
    }

    public static Chunk? Of(string scene) =>
        Chunks.FirstOrDefault(c => string.Equals(c.Scene, SceneManager.NameOf(scene), StringComparison.OrdinalIgnoreCase));

    public static Chunk? At(int x, int y) => Chunks.FirstOrDefault(c => c.X == x && c.Y == y);

    /// <summary>
    /// Makes the layout match the Scenes folder: every scene file has a chunk (new ones go to the next free cell),
    /// chunks of deleted files disappear. Returns true when something changed.
    /// </summary>
    public static bool Sync()
    {
        EnsureLoaded();
        bool changed = false;
        var names = SceneManager.AvailableScenes();
        changed |= _chunks.RemoveAll(c => !names.Contains(c.Scene, StringComparer.OrdinalIgnoreCase)) > 0;
        // scenes that are in the build come first (in build order), so a fresh world follows the level order
        var inBuild = SceneManager.Config.Scenes.Select(sc => SceneManager.NameOf(sc.Path)).ToList();
        var ordered = names.OrderBy(n => { int i = inBuild.FindIndex(b => string.Equals(b, n, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })
                           .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var n in ordered)
            if (Of(n) is null) { PlaceInFreeCell(n); changed = true; }
        if (changed) TrySave();
        return changed;
    }

    /// <summary>Gives a scene a chunk if it has none. Called whenever a scene is saved or created.</summary>
    public static Chunk Ensure(string scene)
    {
        scene = SceneManager.NameOf(scene);
        if (Of(scene) is { } existing) return existing;
        var c = PlaceInFreeCell(scene);
        TrySave();
        return c;
    }

    private static Chunk PlaceInFreeCell(string scene)
    {
        // the grid stays roughly square: it grows by a row or a column as scenes are added
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(_chunks.Count + 1)));
        for (int y = 0; ; y++)
            for (int x = 0; x < cols; x++)
                if (At(x, y) is null)
                {
                    var c = new Chunk { Scene = scene, X = x, Y = y };
                    _chunks.Add(c);
                    return c;
                }
    }

    /// <summary>Puts a scene on a specific cell (creating its chunk if needed). An occupied cell swaps places.</summary>
    public static void Place(string scene, int x, int y)
    {
        scene = SceneManager.NameOf(scene);
        var mine = Of(scene);
        var other = At(x, y);
        if (mine is null) { _chunks.Add(new Chunk { Scene = scene, X = x, Y = y }); TrySave(); return; }
        if (other is not null && !ReferenceEquals(other, mine)) { other.X = mine.X; other.Y = mine.Y; }
        mine.X = x; mine.Y = y;
        TrySave();
    }

    /// <summary>Cell range that contains every chunk (inclusive). An empty world is one cell.</summary>
    public static (int MinX, int MinY, int MaxX, int MaxY) Extent()
    {
        var list = Chunks;
        if (list.Count == 0) return (0, 0, 0, 0);
        return (list.Min(c => c.X), list.Min(c => c.Y), list.Max(c => c.X), list.Max(c => c.Y));
    }

    // ---- sizes ------------------------------------------------------------------------------------

    /// <summary>Width and depth of a scene, read from its file (the open scene should be asked of the World instead).</summary>
    public static (float X, float Z) SizeOf(string scene)
    {
        string file = SceneManager.FileFor(scene);
        if (!File.Exists(file)) return (60f, 60f);
        var stamp = File.GetLastWriteTimeUtc(file);
        if (SizeCache.TryGetValue(scene, out var c) && c.Stamp == stamp) return (c.X, c.Z);

        float x = 200f, z = 200f;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file))?["settings"] is JsonObject s)
            {
                if (s["sizeX"] is { } sx) x = sx.GetValue<float>();
                if (s["sizeZ"] is { } sz) z = sz.GetValue<float>();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException) { }
        x = Math.Clamp(x, World.MinSize, World.MaxSize);
        z = Math.Clamp(z, World.MinSize, World.MaxSize);
        SizeCache[scene] = (stamp, x, z);
        return (x, z);
    }

    /// <summary>Edits the size stored in a scene file (for a scene that is not open).</summary>
    public static void SetSizeInFile(string scene, float x, float z)
    {
        string file = SceneManager.FileFor(scene);
        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("Scene file is empty.");
        var settings = root["settings"] as JsonObject ?? new JsonObject();
        settings["sizeX"] = Math.Clamp(x, World.MinSize, World.MaxSize);
        settings["sizeZ"] = Math.Clamp(z, World.MinSize, World.MaxSize);
        root["settings"] = settings;
        File.WriteAllText(file, root.ToJsonString(Options));
        SizeCache.Remove(scene);
    }

    /// <summary>Edge length of one grid cell: the biggest scene decides, so every scene fits in its cell.</summary>
    public static float CellSize(Func<string, (float X, float Z)>? sizeOf = null)
    {
        sizeOf ??= SizeOf;
        float cell = 10f;
        foreach (var c in Chunks)
        {
            var (x, z) = sizeOf(c.Scene);
            cell = MathF.Max(cell, MathF.Max(x, z));
        }
        return cell;
    }

    /// <summary>Total size of the world in units (all chunks together).</summary>
    public static (float Width, float Depth) WorldSize(Func<string, (float X, float Z)>? sizeOf = null)
    {
        var (minX, minY, maxX, maxY) = Extent();
        float cell = CellSize(sizeOf);
        return ((maxX - minX + 1) * cell, (maxY - minY + 1) * cell);
    }

    private static void TrySave()
    {
        try { Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"[world] could not save {FileName}: {ex.Message}"); }
    }
}
