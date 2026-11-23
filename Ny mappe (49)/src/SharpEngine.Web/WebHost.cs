using Raylib_cs;
using SharpEngine.Graphics3D;

namespace SharpEngine.Web;

/// <summary>
/// Runs a SharpEngine game in the browser: loads the build's files into the in-memory file system, opens the first scene
/// and then, once per animation frame, runs exactly the same world update (components, streaming, portals) as the desktop player.
/// </summary>
public sealed class WebHost
{
    /// <summary>Where the game's files (Scenes, Assets, BuildSettings.json, WorldMap.json) live in the in-memory file system.</summary>
    public const string Root = "/game";

    private readonly IWebCanvas _canvas;
    private readonly WebSink _sink;

    public WebInput Input { get; } = new();
    public World World { get; private set; } = null!;
    public bool Running { get; private set; }
    public string? Error { get; private set; }

    public WebHost(IWebCanvas canvas)
    {
        _canvas = canvas;
        _sink = new WebSink(canvas);
    }

    /// <summary>Stores one file of the build (path relative to the game folder, e.g. "Scenes/MainScene.json").</summary>
    public static void WriteFile(string relativePath, byte[] data)
    {
        string clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Split('/').Contains("..")) throw new ArgumentException("Path leaves the game folder: " + relativePath);
        string full = Path.Combine(Root, clean);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, data);
    }

    /// <summary>Opens the first scene of the build and starts playing. Call after all files were written.</summary>
    public void Boot()
    {
        try
        {
            Platform.Input = Input;
            GameProject.OverrideRoot(Root, player: true);
            SceneManager.Reset();
            World = new World();
            World.ShowChunkDebug = false;
            World.Streamer.Background = false;   // the browser has a single thread: parse neighbouring scenes right away, create their objects bit by bit
            if (!SceneManager.LoadFirst(World)) throw new InvalidOperationException("The build contains no scenes.");
            World.PlayMode = true;
            Running = true;
            Error = null;
        }
        catch (Exception ex)
        {
            Running = false;
            Error = ex.Message;
            ErrorLog.Report("starting the game", ex);
        }
    }

    /// <summary>One animation frame. <paramref name="seconds"/> is the time since the last one.</summary>
    public void Frame(double seconds)
    {
        if (!Running) return;
        float dt = (float)Math.Clamp(seconds, 0.0, 0.1);
        ErrorLog.Try("update", () =>
        {
            Input.BeginFrame();
            KeyEvents.Poll();
            SceneManager.Process(World);
            SceneManager.HandleHotkeys();
            World.Streamer.Enabled = SceneManager.Config.StreamWorld;
            World.Streamer.Radius = SceneManager.Config.StreamRadius;
            ScenePortal.ResetPrompt();
            World.Update(dt);
            SceneManager.Process(World);
        });
        ErrorLog.Try("drawing", () => World.Collect(_sink));
        int index = SceneManager.CurrentBuildIndex;
        string hud = $"{SceneManager.CurrentName}  ({(index + 1)}/{SceneManager.BuildScenes.Count})";
        if (World.Streamer.Active) hud += $"   chunk: {World.Streamer.CurrentChunk}";
        _canvas.Hud(hud, ScenePortal.ActivePrompt ?? "");
    }
}
