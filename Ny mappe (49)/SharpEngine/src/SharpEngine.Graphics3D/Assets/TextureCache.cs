using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D;

/// <summary>Loads image files as GPU textures once and keeps them (needs the window to be open).</summary>
public static class TextureCache
{
    private static readonly Dictionary<string, Texture2D?> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> Stamps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Last problem (missing file, ...) so the editor can show it.</summary>
    public static string? LastError { get; private set; }

    public static bool TryGet(string path, out Texture2D texture)
    {
        texture = default;
        if (string.IsNullOrWhiteSpace(path)) return false;

        string full = Assets.Resolve(path);
        if (!Loaded.TryGetValue(path, out var cached))
        {
            cached = Load(path, full);
            Loaded[path] = cached;
        }
        if (cached is not { } t) return false;
        texture = t;
        return true;
    }

    private static Texture2D? Load(string path, string full)
    {
        if (!File.Exists(full)) { LastError = $"Texture not found: {path}"; Console.Error.WriteLine("[texture] " + LastError); return null; }
        var tex = Rl.LoadTexture(full);
        if (tex.Id == 0) { LastError = $"Could not load texture: {path}"; Console.Error.WriteLine("[texture] " + LastError); return null; }
        Rl.GenTextureMipmaps(ref tex);
        Rl.SetTextureFilter(tex, TextureFilter.Trilinear);
        Rl.SetTextureWrap(tex, TextureWrap.Repeat);
        Stamps[path] = File.GetLastWriteTimeUtc(full);
        return tex;
    }

    /// <summary>Forget everything so changed files are loaded again.</summary>
    public static void Clear()
    {
        foreach (var t in Loaded.Values) if (t is { } tex) Rl.UnloadTexture(tex);
        Loaded.Clear();
        Stamps.Clear();
    }

    /// <summary>Reloads textures whose file changed on disk (cheap; call now and then).</summary>
    public static void Refresh()
    {
        foreach (var path in Stamps.Keys.ToList())
        {
            string full = Assets.Resolve(path);
            if (!File.Exists(full) || File.GetLastWriteTimeUtc(full) == Stamps[path]) continue;
            if (Loaded.TryGetValue(path, out var old) && old is { } tex) Rl.UnloadTexture(tex);
            Loaded.Remove(path);
            Stamps.Remove(path);
        }
    }
}
