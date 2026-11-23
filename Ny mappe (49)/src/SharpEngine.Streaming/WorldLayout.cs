using System.Numerics;
using System.Text.Json;

namespace SharpEngine.Streaming;

/// <summary>One scene on the World Map.</summary>
public sealed record ChunkEntry(string Scene, Cell Cell, float Width, float Depth);

/// <summary>The World Map as the streamer sees it: which scene lives in which cell.</summary>
public sealed class WorldLayout
{
    private readonly Dictionary<Cell, ChunkEntry> _byCell = new();

    public IReadOnlyList<ChunkEntry> Chunks { get; }
    public ChunkPivot Pivot { get; }

    /// <summary>Size of one grid cell: the biggest scene dimension, like the World Map uses.</summary>
    public float CellSize { get; }

    public WorldLayout(IEnumerable<ChunkEntry> chunks, ChunkPivot pivot = ChunkPivot.Center, float? cellSize = null)
    {
        var list = chunks.ToList();
        foreach (var c in list)
        {
            if (!_byCell.TryAdd(c.Cell, c))
                throw new ArgumentException($"Cell {c.Cell} is used by both '{_byCell[c.Cell].Scene}' and '{c.Scene}'.");
        }
        Chunks = list;
        Pivot = pivot;
        float biggest = list.Count == 0 ? 1f : list.Max(c => MathF.Max(c.Width, c.Depth));
        CellSize = cellSize ?? biggest;
        if (CellSize <= 0f) throw new ArgumentException("Cell size must be positive.");
    }

    public bool TryGet(Cell cell, out ChunkEntry entry) => _byCell.TryGetValue(cell, out entry!);

    public Cell CellAt(float x, float z) => ChunkPlacement.CellAt(x, z, CellSize, Pivot);
    public Cell CellAt(Vector3 p) => CellAt(p.X, p.Z);

    public Vector3 OffsetOf(ChunkEntry c) => ChunkPlacement.Offset(c.Cell, CellSize);

    public Rect2 RectOf(ChunkEntry c) => ChunkPlacement.SceneRect(c.Cell, c.Width, c.Depth, CellSize, Pivot);

    // ---------------------------------------------------------------- JSON

    private static readonly string[] SceneKeys = { "scene", "name", "sceneName", "id", "path", "file" };
    private static readonly string[] XKeys = { "x", "cellX", "cx", "col", "column", "gridX" };
    private static readonly string[] YKeys = { "y", "cellY", "cy", "row", "gridY", "z", "cellZ" };
    private static readonly string[] WKeys = { "width", "w", "sizeX", "worldWidth", "widthX" };
    private static readonly string[] DKeys = { "depth", "d", "sizeZ", "worldDepth", "depthZ", "length" };

    /// <summary>
    /// Reads a layout from JSON. Accepts either a list of chunk objects (root array, or the first array property)
    /// or an object keyed by scene name. Field names are matched case-insensitively against common spellings
    /// (scene/name, x/cellX/col, y/cellY/row/z, width/w, depth/d); a nested "cell"/"pos" object is understood too.
    /// Missing sizes fall back to <paramref name="defaultSize"/>.
    /// </summary>
    public static WorldLayout FromJson(string json, float defaultSize = 60f, ChunkPivot pivot = ChunkPivot.Center)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var entries = new List<ChunkEntry>();
        foreach (var (key, el) in EnumerateChunkElements(doc.RootElement))
        {
            string? scene = key ?? GetString(el, SceneKeys);
            if (scene is null) throw new FormatException("A chunk entry has no scene name.");

            JsonElement cellEl = el;
            foreach (var nested in new[] { "cell", "pos", "position", "grid" })
                if (TryProp(el, nested, out var n) && n.ValueKind == JsonValueKind.Object) { cellEl = n; break; }

            if (!TryNumber(cellEl, XKeys, out float cx) || !TryNumber(cellEl, YKeys, out float cy))
                throw new FormatException($"Chunk '{scene}' has no cell coordinates.");

            TryNumber(el, WKeys, out float w);
            TryNumber(el, DKeys, out float d);
            if (w <= 0f) w = defaultSize;
            if (d <= 0f) d = defaultSize;
            entries.Add(new ChunkEntry(scene, new Cell((int)cx, (int)cy), w, d));
        }
        return new WorldLayout(entries, pivot);
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        chunks = Chunks.Select(c => new { scene = c.Scene, x = c.Cell.X, y = c.Cell.Y, width = c.Width, depth = c.Depth }),
    }, new JsonSerializerOptions { WriteIndented = true });

    private static IEnumerable<(string? Key, JsonElement El)> EnumerateChunkElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in root.EnumerateArray()) if (e.ValueKind == JsonValueKind.Object) yield return (null, e);
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("World map JSON must be an object or an array.");

        // Prefer a property that holds a list of objects.
        foreach (var p in root.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0
                && p.Value[0].ValueKind == JsonValueKind.Object)
            {
                foreach (var e in p.Value.EnumerateArray()) yield return (null, e);
                yield break;
            }
        }
        // Then a property that is a dictionary of scene name -> object, or the root itself being one.
        foreach (var container in root.EnumerateObject()
                     .Where(p => p.Value.ValueKind == JsonValueKind.Object).Select(p => p.Value).Append(root))
        {
            var kids = container.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object).ToList();
            if (kids.Count > 0 && kids.All(k => TryNumber(k.Value, XKeys, out _) || HasNestedCell(k.Value)))
            {
                foreach (var k in kids) yield return (k.Name, k.Value);
                yield break;
            }
        }
        throw new FormatException("Could not find a list of chunks in the world map JSON.");
    }

    private static bool HasNestedCell(JsonElement e) =>
        new[] { "cell", "pos", "position", "grid" }.Any(n => TryProp(e, n, out var v) && v.ValueKind == JsonValueKind.Object);

    private static bool TryProp(JsonElement e, string name, out JsonElement value)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement e, string[] names)
    {
        foreach (var n in names)
            if (TryProp(e, n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        return null;
    }

    private static bool TryNumber(JsonElement e, string[] names, out float value)
    {
        value = 0f;
        if (e.ValueKind != JsonValueKind.Object) return false;
        foreach (var n in names)
            if (TryProp(e, n, out var v) && v.ValueKind == JsonValueKind.Number) { value = v.GetSingle(); return true; }
        return false;
    }
}
