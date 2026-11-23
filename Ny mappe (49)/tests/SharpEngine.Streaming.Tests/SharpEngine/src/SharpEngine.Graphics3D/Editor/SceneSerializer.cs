using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Raylib_cs;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>Saves and loads a <see cref="World"/> as JSON (objects, transforms, components and scene settings).</summary>
public static class SceneSerializer
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Save(World world)
    {
        var objects = new JsonArray();
        foreach (var go in world.Objects)
            if (!go.Destroyed && go.Chunk is null) objects.Add(WriteObject(go));   // streamed neighbours belong to their own scene file

        var root = new JsonObject
        {
            ["version"] = 1,
            ["settings"] = WriteSettings(world),
            ["objects"] = objects
        };
        return root.ToJsonString(Pretty);
    }

    /// <summary>Replaces the contents of the world with the scene in <paramref name="json"/>.</summary>
    public static void Load(World world, string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Scene file is empty.");
        world.Clear();
        if (root["settings"] is JsonObject settings) ReadSettings(world, settings);
        if (root["objects"] is JsonArray objects)
            foreach (var node in objects)
                if (node is JsonObject o) ReadObject(world, o);
        world.Flush();
    }

    /// <summary>Reads the scene settings (sun, sky, size) of a scene file into <paramref name="world"/> without touching its objects.</summary>
    internal static void ApplySettings(World world, JsonObject root)
    {
        if (root["settings"] is JsonObject settings) ReadSettings(world, settings);
    }

    /// <summary>
    /// Adds one object of a neighbouring scene to the world, moved by <paramref name="offset"/> and tagged with its chunk
    /// so it can be unloaded again. Cameras are skipped: only the main scene's camera is the player. Returns null for skipped objects.
    /// </summary>
    internal static GameObject? ReadChunkObject(World world, JsonObject o, Vector3 offset, string chunk)
    {
        if (o["components"] is JsonArray comps && comps.Any(c => c?["type"]?.GetValue<string>() == nameof(CameraComponent))) return null;
        var go = ReadObject(world, o);
        go.Transform.Position += offset;
        go.Chunk = chunk;
        return go;
    }

    /// <summary>Copies a GameObject (components included) and offsets it slightly.</summary>
    public static GameObject Clone(World world, GameObject source)
    {
        // go through JSON text so values are normalised exactly as when loading a file
        var node = JsonNode.Parse(WriteObject(source).ToJsonString())!.AsObject();
        var copy = ReadObject(world, node);
        copy.Name = source.Name + " (copy)";
        copy.Transform.Position += new Vector3(1f, 0f, 0f);
        return copy;
    }

    // ---- writing ----------------------------------------------------------------------------

    private static JsonObject WriteSettings(World w) => new()
    {
        ["sunDirection"] = ToJson(w.Sun.Direction),
        ["sunColor"] = ToJson(w.Sun.Color),
        ["sunIntensity"] = Num(w.Sun.Intensity),
        ["ambient"] = Num(w.Sun.Ambient),
        ["skyTop"] = ToJson(w.SkyTop),
        ["skyBottom"] = ToJson(w.SkyBottom),
        ["showGrid"] = w.ShowGrid,
        ["showAxes"] = w.ShowAxes,
        ["sizeX"] = Num(w.SizeX),
        ["sizeZ"] = Num(w.SizeZ),
        ["showBounds"] = w.ShowBounds,
        ["clampCamera"] = w.ClampCamera
    };

    private static JsonObject WriteObject(GameObject go)
    {
        var components = new JsonArray();
        foreach (var c in go.Components)
        {
            var props = new JsonObject();
            foreach (var p in ComponentRegistry.Properties(c.GetType()))
            {
                var node = ToJson(p.GetValue(c));
                if (node is not null) props[p.Name] = node;
            }
            components.Add(new JsonObject { ["type"] = c.GetType().Name, ["properties"] = props });
        }

        return new JsonObject
        {
            ["name"] = go.Name,
            ["active"] = go.Active,
            ["position"] = ToJson(go.Transform.Position),
            ["rotation"] = ToJson(go.Transform.EulerAngles),
            ["scale"] = ToJson(go.Transform.Scale),
            ["components"] = components
        };
    }

    // ---- reading ----------------------------------------------------------------------------

    private static void ReadSettings(World w, JsonObject s)
    {
        if (ReadVector(s["sunDirection"]) is { } dir && dir.LengthSquared() > 1e-8f) w.Sun.Direction = Vector3.Normalize(dir);
        if (ReadColor(s["sunColor"]) is { } sc) w.Sun.Color = sc;
        w.Sun.Intensity = ReadFloat(s["sunIntensity"], w.Sun.Intensity);
        w.Sun.Ambient = ReadFloat(s["ambient"], w.Sun.Ambient);
        if (ReadColor(s["skyTop"]) is { } top) w.SkyTop = top;
        if (ReadColor(s["skyBottom"]) is { } bottom) w.SkyBottom = bottom;
        w.ShowGrid = ReadBool(s["showGrid"], w.ShowGrid);
        w.ShowAxes = ReadBool(s["showAxes"], w.ShowAxes);
        // scenes saved before sizes existed keep their old freedom: big and not clamped
        w.SizeX = ReadFloat(s["sizeX"], 200f);
        w.SizeZ = ReadFloat(s["sizeZ"], 200f);
        w.ShowBounds = ReadBool(s["showBounds"], true);
        w.ClampCamera = ReadBool(s["clampCamera"], false);
    }

    private static GameObject ReadObject(World world, JsonObject o)
    {
        var go = world.Instantiate(o["name"]?.GetValue<string>() ?? "GameObject");
        go.Active = ReadBool(o["active"], true);
        go.Transform.Position = ReadVector(o["position"]) ?? Vector3.Zero;
        go.Transform.EulerAngles = ReadVector(o["rotation"]) ?? Vector3.Zero;
        go.Transform.Scale = ReadVector(o["scale"]) ?? Vector3.One;

        if (o["components"] is not JsonArray comps) return go;
        foreach (var node in comps)
        {
            if (node is not JsonObject co) continue;
            var typeName = co["type"]?.GetValue<string>();
            var type = typeName is null ? null : ComponentRegistry.Find(typeName);
            if (type is null)
            {
                Console.Error.WriteLine($"[scene] unknown component '{typeName}' on '{go.Name}', skipped");
                continue;
            }

            var component = go.AddComponent(type);
            if (co["properties"] is not JsonObject props) continue;
            foreach (var p in ComponentRegistry.Properties(type))
            {
                if (props[p.Name] is not { } value) continue;
                try
                {
                    var parsed = FromJson(value, p.PropertyType);
                    if (parsed is not null) p.SetValue(component, parsed);
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or InvalidCastException or ArgumentException)
                {
                    Console.Error.WriteLine($"[scene] bad value for {type.Name}.{p.Name}: {ex.Message}");
                }
            }
        }
        return go;
    }

    // ---- value conversion -------------------------------------------------------------------

    private static JsonNode Num(float f) => JsonValue.Create(float.IsFinite(f) ? f : 0f);

    private static JsonNode? ToJson(object? v) => v switch
    {
        float f => Num(f),
        int i => JsonValue.Create(i),
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),
        Vector3 vec => new JsonArray(Num(vec.X), Num(vec.Y), Num(vec.Z)),
        Color c => new JsonArray(c.R, c.G, c.B, c.A),
        _ => null
    };

    private static object? FromJson(JsonNode node, Type t)
    {
        if (t == typeof(float)) return node.GetValue<float>();
        if (t == typeof(int)) return (int)MathF.Round(node.GetValue<float>());
        if (t == typeof(bool)) return node.GetValue<bool>();
        if (t == typeof(string)) return node.GetValue<string>();
        if (t == typeof(Vector3)) return ReadVector(node);
        if (t == typeof(Color)) return ReadColor(node);
        return null;
    }

    private static Vector3? ReadVector(JsonNode? n) =>
        n is JsonArray { Count: 3 } a ? new Vector3(a[0]!.GetValue<float>(), a[1]!.GetValue<float>(), a[2]!.GetValue<float>()) : null;

    private static Color? ReadColor(JsonNode? n) =>
        n is JsonArray { Count: 4 } a
            ? new Color((byte)a[0]!.GetValue<int>(), (byte)a[1]!.GetValue<int>(), (byte)a[2]!.GetValue<int>(), (byte)a[3]!.GetValue<int>())
            : null;

    private static float ReadFloat(JsonNode? n, float fallback) => n is null ? fallback : n.GetValue<float>();
    private static bool ReadBool(JsonNode? n, bool fallback) => n is null ? fallback : n.GetValue<bool>();
}
