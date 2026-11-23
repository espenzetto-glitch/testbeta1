namespace SharpEngine.Graphics3D;

/// <summary>The project's Assets folder (textures, models). Paths are stored relative to the project, e.g. "Assets/Textures/brick.png".</summary>
public static class Assets
{
    public static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
    public static readonly string[] ModelExtensions = { ".fbx", ".obj" };

    public static string Folder => Path.Combine(GameProject.Root, "Assets");

    public static bool IsTexture(string file) => TextureExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    public static bool IsModel(string file) => ModelExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    /// <summary>Full path of an asset path (relative paths are relative to the project root).</summary>
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(GameProject.Root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>All assets of the given kinds, as relative paths with '/' separators.</summary>
    public static IReadOnlyList<string> List(bool textures, bool models)
    {
        if (!Directory.Exists(Folder)) return Array.Empty<string>();
        return Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
            .Where(f => (textures && IsTexture(f)) || (models && IsModel(f)))
            .Select(f => Path.GetRelativePath(GameProject.Root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Copies a file into Assets/&lt;subfolder&gt; (unless it is already there) and returns its relative path.</summary>
    public static string Import(string sourceFile, string subfolder)
    {
        string full = Path.GetFullPath(sourceFile);
        string folderFull = Path.GetFullPath(Folder) + Path.DirectorySeparatorChar;
        if (full.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(GameProject.Root, full).Replace('\\', '/');

        string dir = Path.Combine(Folder, subfolder);
        Directory.CreateDirectory(dir);
        string name = SafeName(Path.GetFileName(full));
        string target = Path.Combine(dir, name);
        if (File.Exists(target) && !SameContent(full, target))
        {
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 1; File.Exists(target); i++) target = Path.Combine(dir, $"{stem}{i}{ext}");
        }
        if (!File.Exists(target)) File.Copy(full, target);
        return Path.GetRelativePath(GameProject.Root, target).Replace('\\', '/');
    }

    /// <summary>Writes bytes (for example a texture embedded in an FBX) into Assets/&lt;subfolder&gt;.</summary>
    public static string ImportBytes(byte[] data, string fileName, string subfolder)
    {
        string dir = Path.Combine(Folder, subfolder);
        Directory.CreateDirectory(dir);
        string name = SafeName(fileName);
        string target = Path.Combine(dir, name);
        if (!File.Exists(target)) File.WriteAllBytes(target, data);
        return Path.GetRelativePath(GameProject.Root, target).Replace('\\', '/');
    }

    private static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray());
        return clean.Length == 0 ? "file" : clean;
    }

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
