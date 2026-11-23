using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpEngine.Graphics3D;

public sealed class ComponentInfo
{
    public string Type { get; init; } = "";
    /// <summary>Saved property values as text, e.g. Speed = 90.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

public sealed class ObjectInfo
{
    public string Name { get; init; } = "";
    public string Scene { get; init; } = "";
    public Vector3 Position { get; init; }
    public bool Active { get; init; } = true;
    public IReadOnlyList<ComponentInfo> Components { get; init; } = Array.Empty<ComponentInfo>();
}

public sealed class SceneInfo
{
    public string Name { get; init; } = "";
    public int CellX { get; init; }
    public int CellY { get; init; }
    public bool OnMap { get; init; }
    public float SizeX { get; init; }
    public float SizeZ { get; init; }
    public string? Group { get; init; }
    public IReadOnlyList<ObjectInfo> Objects { get; init; } = Array.Empty<ObjectInfo>();
}

/// <summary>
/// A read-only catalogue of the whole project: every scene with its objects, their components and saved values, read from the scene
/// files (so it also knows scenes that are not loaded). The code editor's completion and "Objects" panel use it, and world scripts
/// can ask it questions such as "which scene holds the door?".
/// </summary>
public static class WorldIndex
{
    private static List<SceneInfo> _scenes = new();
    private static string _stamp = "";

    /// <summary>All scenes of the project, in name order. Reloaded when a scene file or the World Map changed.</summary>
    public static IReadOnlyList<SceneInfo> Scenes
    {
        get { Refresh(); return _scenes; }
    }

    public static IEnumerable<ObjectInfo> Objects => Scenes.SelectMany(s => s.Objects);

    public static SceneInfo? Scene(string name) =>
        Scenes.FirstOrDefault(s => string.Equals(s.Name, SceneManager.NameOf(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>Every object with this name, in any scene.</summary>
    public static IReadOnlyList<ObjectInfo> Find(string objectName) =>
        Objects.Where(o => string.Equals(o.Name, objectName, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Every object that has a component of this type, in any scene.</summary>
    public static IReadOnlyList<ObjectInfo> WithComponent(string componentType) =>
        Objects.Where(o => o.Components.Any(c => string.Equals(c.Type, componentType, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>Forget the cache (tests, a different project).</summary>
    public static void Reset() { _scenes = new List<SceneInfo>(); _stamp = ""; }

    public static void Refresh()
    {
        var names = SceneManager.AvailableScenes();
        long total = 0;
        foreach (var n in names)
            try { total += File.GetLastWriteTimeUtc(SceneManager.FileFor(n)).Ticks ^ n.GetHashCode(); } catch (IOException) { }
        string stamp = GameProject.Root + "|" + names.Count + "|" + total + "|" + (File.Exists(WorldLayout.FilePath) ? File.GetLastWriteTimeUtc(WorldLayout.FilePath).Ticks : 0);
        if (stamp == _stamp) return;
        _stamp = stamp;

        var list = new List<SceneInfo>();
        foreach (var name in names)
        {
            try { list.Add(Read(name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
            {
                list.Add(new SceneInfo { Name = name });   // an unreadable scene is still listed
            }
        }
        _scenes = list;
    }

    private static SceneInfo Read(string name)
    {
        var root = JsonNode.Parse(File.ReadAllText(SceneManager.FileFor(name))) as JsonObject ?? new JsonObject();
        var objects = new List<ObjectInfo>();
        if (root["objects"] is JsonArray arr)
            foreach (var o in arr.OfType<JsonObject>())
            {
                bool isScene = o["scene"] is { } f && f.GetValue<bool>();
                var comps = new List<ComponentInfo>();
                if (o["components"] is JsonArray ca)
                    foreach (var c in ca.OfType<JsonObject>())
                    {
                        var props = new Dictionary<string, string>();
                        if (c["properties"] is JsonObject po)
                            foreach (var (k, v) in po) props[k] = v?.ToJsonString().Trim('"') ?? "";
                        comps.Add(new ComponentInfo { Type = c["type"]?.GetValue<string>() ?? "?", Properties = props });
                    }
                objects.Add(new ObjectInfo
                {
                    Name = isScene ? "(scene components)" : o["name"]?.GetValue<string>() ?? "GameObject",
                    Scene = name,
                    Position = ReadVector(o["position"]),
                    Active = o["active"] is not { } a || a.GetValue<bool>(),
                    Components = comps
                });
            }
        var settings = root["settings"] as JsonObject;
        var chunk = WorldLayout.Of(name);
        return new SceneInfo
        {
            Name = name,
            OnMap = chunk is not null,
            CellX = chunk?.X ?? 0,
            CellY = chunk?.Y ?? 0,
            SizeX = settings?["sizeX"] is { } sx ? sx.GetValue<float>() : 60f,
            SizeZ = settings?["sizeZ"] is { } sz ? sz.GetValue<float>() : 60f,
            Group = WorldLayout.GroupOf(name)?.Name,
            Objects = objects
        };
    }

    private static Vector3 ReadVector(JsonNode? n)
    {
        try
        {
            if (n is JsonArray a && a.Count >= 3) return new Vector3(a[0]!.GetValue<float>(), a[1]!.GetValue<float>(), a[2]!.GetValue<float>());
            if (n is JsonObject o) return new Vector3(Num(o, "x"), Num(o, "y"), Num(o, "z"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { }
        return Vector3.Zero;
    }

    private static float Num(JsonObject o, string key) => o[key] is { } v ? v.GetValue<float>() : o[key.ToUpperInvariant()] is { } w ? w.GetValue<float>() : 0f;
}
