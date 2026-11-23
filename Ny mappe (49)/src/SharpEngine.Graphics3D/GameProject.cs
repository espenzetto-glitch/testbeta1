namespace SharpEngine.Graphics3D;

/// <summary>Finds where the game's project files and scenes live (in development) or where the player was installed.</summary>
public static class GameProject
{
    /// <summary>Marker file that exists next to a built player and not in the development output folder.</summary>
    public const string ConfigFileName = "BuildSettings.json";

    private static string? _root;
    private static bool? _isPlayer;

    /// <summary>True when running a built game (BuildSettings.json sits next to the executable).</summary>
    public static bool IsPlayer => _isPlayer ??= File.Exists(Path.Combine(AppContext.BaseDirectory, ConfigFileName));

    /// <summary>Folder that holds Scenes/ and BuildSettings.json.</summary>
    public static string Root => _root ??= IsPlayer ? AppContext.BaseDirectory : (ProjectDirectory ?? Environment.CurrentDirectory);

    /// <summary>Directory of the game's .csproj, found by walking up from the build output.</summary>
    public static string? ProjectDirectory { get; } = FindUp(AppContext.BaseDirectory, "*.csproj");

    public static string? CsprojPath =>
        ProjectDirectory is null ? null : Directory.GetFiles(ProjectDirectory, "*.csproj").FirstOrDefault();

    /// <summary>Folder containing the .sln (or the project folder if there is none).</summary>
    public static string? SolutionDirectory => FindUp(ProjectDirectory ?? "", "*.sln") ?? ProjectDirectory;

    public static string ScenesDirectory => Path.Combine(Root, "Scenes");
    public static string ConfigPath => Path.Combine(Root, ConfigFileName);

    /// <summary>For tests: pretend the game lives in a different folder.</summary>
    public static void OverrideRoot(string root, bool player)
    {
        _root = root;
        _isPlayer = player;
    }

    private static string? FindUp(string start, string pattern)
    {
        if (string.IsNullOrEmpty(start)) return null;
        var dir = new DirectoryInfo(start);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            if (dir.Exists && dir.GetFiles(pattern).Length > 0) return dir.FullName;
        return null;
    }
}
