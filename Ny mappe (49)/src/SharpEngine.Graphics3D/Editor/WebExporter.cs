using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// WebGL build: writes a folder with index.html, player.js and data.js (scenes + meshes) that runs in any
/// browser. Open index.html directly or host the folder on any web server.
/// </summary>
public static class WebExporter
{
    /// <summary>Components the browser player understands. Other (C#) scripts cannot run in the browser.</summary>
    public static readonly string[] SupportedComponents =
        { "MeshRenderer", "CameraComponent", "FlyCamera", "ScenePortal", "Spinner", "Bobber", "SunOrbit" };

    /// <summary>Writes the player. Returns warnings (e.g. scripts that are left out).</summary>
    public static List<string> Export(string outDir, BuildConfig cfg, IReadOnlyList<string> sceneNames)
    {
        var warnings = new List<string>();
        Directory.CreateDirectory(outDir);

        var scenes = new JsonArray();
        var meshNames = new SortedSet<string>(StringComparer.Ordinal);
        var modelPaths = new SortedSet<string>(StringComparer.Ordinal);
        var texturePaths = new SortedSet<string>(StringComparer.Ordinal);
        var skipped = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var name in sceneNames)
        {
            var data = JsonNode.Parse(File.ReadAllText(SceneManager.FileFor(name))) ?? throw new InvalidDataException($"Scene {name} is empty.");
            if (data["objects"] is JsonArray objects)
                foreach (var o in objects)
                    if (o?["components"] is JsonArray comps)
                        foreach (var c in comps)
                        {
                            var type = c?["type"]?.GetValue<string>();
                            if (type is null) continue;
                            if (!SupportedComponents.Contains(type)) skipped.Add(type);
                            if (type == "ScenePortal" && c!["properties"]?["Mode"]?.GetValue<string>() is { } mode && !mode.Equals("Load", StringComparison.OrdinalIgnoreCase) && mode.Length > 0)
                                skipped.Add("ScenePortal (merge mode)");
                            if (type != "MeshRenderer") continue;
                            var props = c!["properties"];
                            if (props?["PrimitiveName"]?.GetValue<string>() is { Length: > 0 } prim) meshNames.Add(prim);
                            if (props?["ModelPath"]?.GetValue<string>() is { Length: > 0 } model)
                                modelPaths.Add(props["ModelPart"]?.GetValue<string>() is { Length: > 0 } part ? model + "#" + part : model);
                            if (props?["TexturePath"]?.GetValue<string>() is { Length: > 0 } tex) texturePaths.Add(tex);
                        }
            scenes.Add(new JsonObject { ["name"] = name, ["data"] = data });
        }

        var meshes = new JsonObject();
        foreach (var mesh in meshNames)
        {
            if (!Primitives.TryCreate(mesh, out var m)) { warnings.Add($"Unknown mesh '{mesh}' was left out."); continue; }
            meshes[mesh] = MeshJson(m);
        }
        foreach (var model in modelPaths)
        {
            try
            {
                int hash = model.IndexOf('#');
                meshes["model:" + model] = MeshJson(hash < 0 ? ModelLoader.Load(model) : ModelLoader.Load(model[..hash], model[(hash + 1)..]));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
            { warnings.Add($"Model '{model}' was left out: {ex.Message}"); }
        }

        var textures = new JsonObject();
        foreach (var path in texturePaths)
        {
            string file = Assets.Resolve(path);
            if (!File.Exists(file)) { warnings.Add($"Texture '{path}' not found, left out."); continue; }
            string mime = Path.GetExtension(file).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".bmp" => "image/bmp", ".tga" => "image/x-tga", _ => "image/png" };
            if (mime == "image/x-tga") { warnings.Add($"Texture '{path}' is a .tga, which browsers cannot show. Use .png or .jpg."); continue; }
            textures[path] = $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(file))}";
        }
        foreach (var t in skipped)
            warnings.Add(t.EndsWith("(merge mode)") ? "Portals with a merge mode only load their scene in the Lite web player. Use the real engine (WebAssembly) for merging."
                : $"Component '{t}' is C# code and does not run in the browser (WebGL runs Spinner, Bobber, SunOrbit, ScenePortal and FlyCamera).");

        // where the scenes sit on the World Map, so the browser can stream neighbours like the desktop player does
        var inBuildSet = new HashSet<string>(sceneNames, StringComparer.OrdinalIgnoreCase);
        var chunkList = WorldLayout.Chunks.Where(c => inBuildSet.Contains(c.Scene)).ToList();
        float cellSize = 10f;
        foreach (var c in chunkList) { var (cx, cz) = WorldLayout.SizeOf(c.Scene); cellSize = MathF.Max(cellSize, MathF.Max(cx, cz)); }
        var chunks = new JsonArray();
        foreach (var c in chunkList) chunks.Add(new JsonObject { ["scene"] = c.Scene, ["x"] = c.X, ["y"] = c.Y });
        var worldJson = new JsonObject { ["stream"] = cfg.StreamWorld, ["radius"] = Math.Clamp(cfg.StreamRadius, 0, 4), ["cell"] = cellSize, ["chunks"] = chunks };

        var game = new JsonObject
        {
            ["world"] = worldJson,
            ["product"] = cfg.ProductName,
            ["development"] = cfg.DevelopmentBuild,
            ["order"] = new JsonArray(sceneNames.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
            ["scenes"] = scenes,
            ["meshes"] = meshes,
            ["textures"] = textures
        };

        string title = System.Net.WebUtility.HtmlEncode(cfg.ProductName);
        File.WriteAllText(Path.Combine(outDir, "index.html"), WebPlayerAssets.IndexHtml.Replace("{{TITLE}}", title));
        File.WriteAllText(Path.Combine(outDir, "player.js"), WebPlayerAssets.PlayerJs);
        File.WriteAllText(Path.Combine(outDir, "data.js"), "window.GAME = " + game.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) + ";\n");
        // one self-contained file: paste it into any "HTML" box (CodePen, JSFiddle, itch.io, a paste-to-play site) or upload just this file
        string dataJs = File.ReadAllText(Path.Combine(outDir, "data.js")).Replace("</", "<\\/");
        string playerJs = WebPlayerAssets.PlayerJs.Replace("</", "<\\/");
        string single = WebPlayerAssets.IndexHtml.Replace("{{TITLE}}", title)
            .Replace("<script src=\"data.js\"></script>", "<script>\n" + dataJs + "</script>")
            .Replace("<script src=\"player.js\"></script>", "<script>\n" + playerJs + "</script>");
        File.WriteAllText(Path.Combine(outDir, "game.html"), single);
        File.WriteAllText(Path.Combine(outDir, "index.html"), single);   // index.html is self-contained too, so pasting it anywhere just works
        File.WriteAllText(Path.Combine(outDir, "README.txt"),
            "WebGL build of " + cfg.ProductName + "\n\n" +
            "index.html (and its copy game.html) contains the whole game in ONE file: open it in a browser, upload only it, or paste its whole text into an online HTML editor to get a link.\n" +
            "Controls: hold the right mouse button and use W/A/S/D/Q/E to fly, Tab / Shift+Tab or 1-9 to change scene.\n");
        return warnings;
    }

    private static JsonObject MeshJson(MeshData m)
    {
        static JsonArray Floats(IEnumerable<float> values) =>
            new(values.Select(v => (JsonNode?)JsonValue.Create(Math.Round((double)v, 4))).ToArray());

        var colors = new List<float>(m.Positions.Length * 4);
        for (int i = 0; i < m.Positions.Length; i++)
        {
            var c = m.Colors is { } cols ? cols[i] : Raylib_cs.Color.White;
            colors.Add(c.R / 255f); colors.Add(c.G / 255f); colors.Add(c.B / 255f); colors.Add(c.A / 255f);
        }
        return new JsonObject
        {
            ["p"] = Floats(m.Positions.SelectMany(v => new[] { v.X, v.Y, v.Z })),
            ["n"] = Floats(m.Normals.SelectMany(v => new[] { v.X, v.Y, v.Z })),
            ["c"] = Floats(colors),
            ["t"] = m.UVs is { Length: > 0 } uv ? Floats(uv.SelectMany(v => new[] { v.X, v.Y })) : null,
            ["i"] = new JsonArray(m.Indices.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray())
        };
    }
}
