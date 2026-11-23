using Raylib_cs;
using SharpEngine.Graphics3D.Editor;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Scenes are JSON files in the game's Scenes folder. The Build Settings list decides which ones
/// ship in the player and in what order (build index 0 is the first scene). Game code and
/// <see cref="ScenePortal"/> call <see cref="LoadScene(string)"/> to move the player between scenes.
/// </summary>
public static class SceneManager
{
    private static BuildConfig? _config;
    private static string? _pending;
    private static bool _loaded;

    /// <summary>Raised after a scene was loaded into the world.</summary>
    public static event Action<string>? SceneLoaded;

    public static BuildConfig Config => _config ??= LoadConfig();

    /// <summary>Name of the scene currently in the world (no folder, no extension).</summary>
    public static string CurrentName { get; set; } = "Untitled";

    public static string ScenesDirectory => GameProject.ScenesDirectory;

    // ---- config ------------------------------------------------------------------------------

    private static BuildConfig LoadConfig()
    {
        try
        {
            if (File.Exists(GameProject.ConfigPath))
                return BuildConfig.FromJson(File.ReadAllText(GameProject.ConfigPath));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[build] could not read {GameProject.ConfigPath}: {ex.Message}");
        }
        return new BuildConfig();
    }

    public static void SaveConfig() => File.WriteAllText(GameProject.ConfigPath, Config.ToJson());

    /// <summary>Forget the cached config (tests).</summary>
    public static void Reset()
    {
        _config = null;
        _pending = null;
        _loaded = false;
        CurrentName = "Untitled";
        WorldLayout.Reset();
    }

    // ---- files -------------------------------------------------------------------------------

    /// <summary>"Scenes/Main", "Main.json" and "Main" all become "Main".</summary>
    public static string NameOf(string pathOrName)
    {
        var n = Path.GetFileNameWithoutExtension(pathOrName.Replace('\\', '/').TrimEnd('/'));
        return n;
    }

    public static string FileFor(string pathOrName) => Path.Combine(ScenesDirectory, NameOf(pathOrName) + ".json");

    public static string BuildPathFor(string name) => "Scenes/" + NameOf(name);

    public static bool Exists(string pathOrName) => File.Exists(FileFor(pathOrName));

    public static IReadOnlyList<string> AvailableScenes()
    {
        if (!Directory.Exists(ScenesDirectory)) return Array.Empty<string>();
        return Directory.GetFiles(ScenesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    /// <summary>Letters, digits, space, '-' and '_' only. Returns null if nothing usable is left.</summary>
    public static string? SanitizeName(string name)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        return clean.Length == 0 ? null : clean;
    }

    public static string UniqueName(string baseName)
    {
        if (!Exists(baseName)) return baseName;
        for (int i = 1; ; i++)
            if (!Exists(baseName + i)) return baseName + i;
    }

    public static void Save(World world, string? name = null)
    {
        name = NameOf(name ?? CurrentName);
        Directory.CreateDirectory(ScenesDirectory);
        File.WriteAllText(FileFor(name), SceneSerializer.Save(world));
        CurrentName = name;
        WorldLayout.Ensure(name);
    }

    /// <summary>Loads a scene file into the world right now (use <see cref="LoadScene(string)"/> from game code).</summary>
    public static void Open(World world, string pathOrName)
    {
        var name = NameOf(pathOrName);
        var file = FileFor(name);
        if (!File.Exists(file)) throw new FileNotFoundException($"Scene '{name}' not found.", file);
        SceneSerializer.Load(world, File.ReadAllText(file));
        ScenePortal.ResetPrompt();
        CurrentName = name;
        _loaded = true;
        SceneLoaded?.Invoke(name);
    }

    /// <summary>A world with just a Main Camera, ready to start a new scene.</summary>
    public static void CreateEmpty(World world, string name)
    {
        world.Clear();
        var cam = world.Instantiate("Main Camera", new System.Numerics.Vector3(6f, 4f, 9f));
        cam.Transform.LookAt(new System.Numerics.Vector3(0f, 1f, 0f));
        cam.AddComponent<CameraComponent>();
        cam.AddComponent<FlyCamera>();
        world.Flush();
        CurrentName = name;
        _loaded = true;
    }

    /// <summary>Writes a new scene file (camera + ground) without touching the open scene. Returns its name.</summary>
    public static string CreateSceneFile(string baseName = "NewScene")
    {
        var name = UniqueName(SanitizeName(baseName) ?? "NewScene");
        var w = new World();
        var (keepName, keepLoaded) = (CurrentName, _loaded);
        try
        {
            CreateEmpty(w, name);
            var ground = w.CreateMesh("Ground", Primitives.Ground(), Raylib_cs.Color.White);
            ground.Transform.Scale = new System.Numerics.Vector3(w.SizeX, 1f, w.SizeZ);
            ground.GetComponent<MeshRenderer>()!.Specular = 0f;
            w.Flush();
            Directory.CreateDirectory(ScenesDirectory);
            File.WriteAllText(FileFor(name), Editor.SceneSerializer.Save(w));
            WorldLayout.Ensure(name);
        }
        finally { (CurrentName, _loaded) = (keepName, keepLoaded); }
        return name;
    }

    // ---- moving between scenes at runtime --------------------------------------------------------

    /// <summary>Scenes that will be part of the build, in order (the index is the build index).</summary>
    public static IReadOnlyList<string> BuildScenes => Config.EnabledScenes.Select(s => NameOf(s.Path)).ToList();

    public static int CurrentBuildIndex
    {
        get
        {
            var list = BuildScenes;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], CurrentName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    /// <summary>Switches to a scene by name at the end of the current frame. Safe to call from components.</summary>
    public static void LoadScene(string nameOrPath) => _pending = NameOf(nameOrPath);

    /// <summary>Switches to a scene by its Build Settings index.</summary>
    public static void LoadScene(int buildIndex)
    {
        var list = BuildScenes;
        if (buildIndex < 0 || buildIndex >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(buildIndex), $"Build index {buildIndex} is not in the build ({list.Count} scenes).");
        _pending = list[buildIndex];
    }

    public static void LoadNext() => Step(+1);
    public static void LoadPrevious() => Step(-1);

    private static void Step(int delta)
    {
        var list = BuildScenes;
        if (list.Count == 0) return;
        int i = CurrentBuildIndex;
        _pending = list[((i < 0 ? (delta > 0 ? -1 : 0) : i) + delta + list.Count) % list.Count];
    }

    /// <summary>Starts the player: loads build index 0.</summary>
    public static bool LoadFirst(World world)
    {
        var list = BuildScenes;
        if (list.Count == 0) return false;
        Open(world, list[0]);
        return true;
    }

    /// <summary>Applies a requested scene change. Call once per frame outside of component updates.</summary>
    public static void Process(World world)
    {
        if (_pending is not { } name) return;
        _pending = null;
        try
        {
            Open(world, name);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"[scene] could not load '{name}': {ex.Message}");
        }
    }

    /// <summary>Hotkeys for the player: Tab / Shift+Tab step through the build, 1-9 jump to a build index.</summary>
    public static void HandleHotkeys()
    {
        if (!_loaded) return;
        if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.Tab))
        {
            if (Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.LeftShift)) LoadPrevious(); else LoadNext();
        }
        for (int i = 0; i < 9; i++)
            if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.One + i) && i < BuildScenes.Count)
                LoadScene(i);
    }
}

/// <summary>
/// Doorway to another scene. The camera is the player. With <see cref="RequireInteract"/> (default) the player
/// walks up to it and presses the interact key (E); without it the portal fires as soon as the camera is close.
/// It ignores the first moments after a scene loads so you don't bounce straight back.
/// </summary>
public sealed class ScenePortal : Component
{
    /// <summary>Scene name or build path to load.</summary>
    public string TargetScene { get; set; } = "";

    /// <summary>How close the camera must be, in world units.</summary>
    public float Radius { get; set; } = 2.5f;

    /// <summary>True: press the interact key while inside the radius. False: enter automatically.</summary>
    public bool RequireInteract { get; set; } = true;

    /// <summary>Key name from Raylib's KeyboardKey list ("E", "F", "Enter", "Space", ...).</summary>
    public string InteractKey { get; set; } = "E";

    /// <summary>Text shown on screen while in range. Empty = "Press E to enter &lt;scene&gt;".</summary>
    public string Prompt { get; set; } = "";

    /// <summary>Draw the debug rings and label in the editor.</summary>
    public bool ShowDebug { get; set; } = true;

    /// <summary>Distance to the camera measured on the last update (-1 = unknown). Used by the debug label.</summary>
    public float LastDistance { get; private set; } = -1f;

    /// <summary>True while the camera is inside the radius.</summary>
    public bool InRange { get; private set; }

    /// <summary>Prompt of the nearest portal that is in range and waiting for the key (drawn by <see cref="App3D"/>).</summary>
    public static string? ActivePrompt { get; private set; }
    private static float _promptDistance;

    /// <summary>
    /// The key of the prompt that was showing last frame. Other code (the fly camera) must not use it for its own
    /// purpose while a portal is waiting for it, otherwise pressing E to enter a portal would also fly the camera up.
    /// </summary>
    public static KeyboardKey? ReservedKey { get; private set; }
    private static KeyboardKey? _nextReserved;

    public static void ResetPrompt()
    {
        ReservedKey = _nextReserved;
        _nextReserved = null;
        ActivePrompt = null;
        _promptDistance = float.MaxValue;
    }

    public KeyboardKey Key => Enum.TryParse<KeyboardKey>(InteractKey.Trim(), ignoreCase: true, out var k) ? k : KeyboardKey.E;

    public string PromptText => Prompt.Length > 0 ? Prompt : $"Press {Key} to enter {(TargetScene.Length > 0 ? SceneManager.NameOf(TargetScene) : "(no target)")}";

    public override void Update(float dt)
    {
        InRange = false;
        LastDistance = -1f;
        if (!World.TryGetCamera(out var cam)) return;
        LastDistance = System.Numerics.Vector3.Distance(cam.Position, Transform.Position);
        InRange = LastDistance <= Radius;
        // automatic portals wait a second after a scene loads so you do not bounce straight back;
        // portals that need a key press only need a moment so a quick press right after arriving still works
        float grace = RequireInteract ? 0.2f : 1.0f;
        if (TargetScene.Length == 0 || World.Time < grace || !InRange) return;

        if (!RequireInteract)
        {
            SceneManager.LoadScene(TargetScene);
            return;
        }

        if (LastDistance < _promptDistance) { ActivePrompt = PromptText; _promptDistance = LastDistance; }
        _nextReserved = Key;
        if (InteractPressed(Key)) SceneManager.LoadScene(TargetScene);
    }

    /// <summary>Checks whether the interact key was pressed this frame (tests replace it).</summary>
    public static Func<KeyboardKey, bool> InteractPressed { get; set; } = key =>
        !Editor.Gui.WantsKeyboard && KeyEvents.WasPressed(key);   // works while flying too (E also nudges the camera up)
}
