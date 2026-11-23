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
/// Scenes that can be joined into one bigger scene while the game runs. A scene belongs to at most one group.
/// With <see cref="Auto"/> the whole group is merged as soon as the player is in any of its scenes.
/// </summary>
public sealed class MergeGroup
{
    public string Name { get; set; } = "";
    public List<string> Scenes { get; set; } = new();
    public bool Auto { get; set; }
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
    private static List<MergeGroup> _groups = new();
    private static bool _loaded;
    private static readonly Dictionary<string, (DateTime Stamp, float X, float Z)> SizeCache = new(StringComparer.OrdinalIgnoreCase);

    public static string FilePath => Path.Combine(GameProject.Root, FileName);

    public static IReadOnlyList<Chunk> Chunks { get { EnsureLoaded(); return _chunks; } }

    /// <summary>Merge groups (see <see cref="MergeGroup"/>).</summary>
    public static IReadOnlyList<MergeGroup> Groups { get { EnsureLoaded(); return _groups; } }

    /// <summary>Forget everything (a different project folder, or tests).</summary>
    public static void Reset()
    {
        _chunks = new List<Chunk>();
        _groups = new List<MergeGroup>();
        _loaded = false;
        SizeCache.Clear();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _chunks = new List<Chunk>();
        _groups = new List<MergeGroup>();
        try
        {
            if (!File.Exists(FilePath)) return;
            var node = JsonNode.Parse(File.ReadAllText(FilePath));
            if (node?["chunks"] is JsonArray arr)
                foreach (var c in arr)
                    if (c?["scene"]?.GetValue<string>() is { Length: > 0 } scene && c["x"] is { } x && c["y"] is { } y
                        && _chunks.All(o => !string.Equals(o.Scene, scene, StringComparison.OrdinalIgnoreCase) && !(o.X == x.GetValue<int>() && o.Y == y.GetValue<int>())))
                        _chunks.Add(new Chunk { Scene = scene, X = x.GetValue<int>(), Y = y.GetValue<int>() });
            if (node?["groups"] is JsonArray groups)
                foreach (var g in groups)
                    if (g?["name"]?.GetValue<string>() is { Length: > 0 } gname && g["scenes"] is JsonArray gs)
                    {
                        var list = gs.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList();
                        if (list.Count > 0 && _groups.All(o => !string.Equals(o.Name, gname, StringComparison.OrdinalIgnoreCase)))
                            _groups.Add(new MergeGroup { Name = gname, Scenes = list, Auto = g["auto"]?.GetValue<bool>() ?? false });
                    }
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
        File.WriteAllText(FilePath, new JsonObject { ["chunks"] = arr, ["groups"] = GroupsJson(null) }.ToJsonString(Options));
    }

    private static JsonArray GroupsJson(HashSet<string>? only)
    {
        var arr = new JsonArray();
        foreach (var g in _groups)
        {
            var scenes = g.Scenes.Where(n => only is null || only.Contains(n)).ToList();
            if (scenes.Count == 0) continue;
            arr.Add(new JsonObject { ["name"] = g.Name, ["auto"] = g.Auto, ["scenes"] = new JsonArray(scenes.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) });
        }
        return arr;
    }

    // ---- merge groups -----------------------------------------------------------------------------

    public static MergeGroup? GroupNamed(string name) =>
        Groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The group a scene belongs to, or null.</summary>
    public static MergeGroup? GroupOf(string scene)
    {
        scene = SceneManager.NameOf(scene);
        return Groups.FirstOrDefault(g => g.Scenes.Contains(scene, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Puts a scene into a group (created on first use). An empty name takes the scene out of its group.</summary>
    public static void SetGroupOf(string scene, string? groupName)
    {
        EnsureLoaded();
        scene = SceneManager.NameOf(scene);
        foreach (var g in _groups) g.Scenes.RemoveAll(n => string.Equals(n, scene, StringComparison.OrdinalIgnoreCase));
        groupName = groupName?.Trim();
        if (!string.IsNullOrEmpty(groupName))
        {
            var g = GroupNamed(groupName);
            if (g is null) _groups.Add(g = new MergeGroup { Name = groupName });
            g.Scenes.Add(scene);
        }
        _groups.RemoveAll(g => g.Scenes.Count == 0);
        TrySave();
    }

    public static void SetGroupAuto(string groupName, bool auto)
    {
        if (GroupNamed(groupName) is { } g) { g.Auto = auto; TrySave(); }
    }

    /// <summary>Scenes a merge request stands for: the members of a group of that name, or the scene itself.</summary>
    public static IReadOnlyList<string> ResolveMerge(string nameOrGroup)
    {
        if (GroupNamed(nameOrGroup) is { } g) return g.Scenes.ToList();
        string scene = SceneManager.NameOf(nameOrGroup);
        return Of(scene) is { } c ? new List<string> { c.Scene } : new List<string>();
    }

    /// <summary>Writes a WorldMap.json that only holds the given scenes (what a build ships).</summary>
    public static void WriteSubset(string path, IEnumerable<string> scenes)
    {
        var keep = new HashSet<string>(scenes.Select(SceneManager.NameOf), StringComparer.OrdinalIgnoreCase);
        var arr = new JsonArray();
        foreach (var c in Chunks.Where(c => keep.Contains(c.Scene)).OrderBy(c => c.Y).ThenBy(c => c.X))
            arr.Add(new JsonObject { ["scene"] = c.Scene, ["x"] = c.X, ["y"] = c.Y });
        File.WriteAllText(path, new JsonObject { ["chunks"] = arr, ["groups"] = GroupsJson(keep) }.ToJsonString(Options));
    }

    /// <summary>Takes a scene out of the world grid and out of its merge group (the scene file itself is not touched).</summary>
    public static void Remove(string scene)
    {
        EnsureLoaded();
        scene = SceneManager.NameOf(scene);
        _chunks.RemoveAll(c => string.Equals(c.Scene, scene, StringComparison.OrdinalIgnoreCase));
        foreach (var g in _groups) g.Scenes.RemoveAll(n => string.Equals(n, scene, StringComparison.OrdinalIgnoreCase));
        _groups.RemoveAll(g => g.Scenes.Count == 0);
        TrySave();
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
        foreach (var g in _groups) changed |= g.Scenes.RemoveAll(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)) > 0;
        _groups.RemoveAll(g => g.Scenes.Count == 0);
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
