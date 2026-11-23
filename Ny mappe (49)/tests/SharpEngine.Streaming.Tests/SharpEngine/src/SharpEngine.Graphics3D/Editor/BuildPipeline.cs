using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>What the compiler step needs to know. Filled in by <see cref="BuildPipeline"/>.</summary>
public sealed record PublishRequest(string CsprojPath, string RuntimeId, string Configuration, string OutputDirectory, bool SelfContained, bool KeepSymbols);

/// <summary>
/// Builds a standalone player: dotnet publish for the chosen platform, then the enabled scenes and
/// BuildSettings.json next to it. Runs on a background thread; the UI polls <see cref="IsBuilding"/> and <see cref="Status"/>.
/// </summary>
public static class BuildPipeline
{
    private static readonly object Gate = new();
    private static readonly StringBuilder Log = new();
    private static volatile bool _building;
    private static string _status = "";
    private static bool _statusIsError;
    private static string? _lastOutput;

    public static bool IsBuilding => _building;
    public static string Status { get { lock (Gate) return _status; } }
    public static bool StatusIsError { get { lock (Gate) return _statusIsError; } }
    public static string? LastOutputDirectory { get { lock (Gate) return _lastOutput; } }

    /// <summary>
    /// Compiles the game. Returns the process exit code (0 = success). The default runs
    /// <c>dotnet publish</c>; tests replace it.
    /// </summary>
    public static Func<PublishRequest, Action<string>, int> Publisher { get; set; } = DotnetPublish;

    /// <summary>Starts a build on a background thread. Returns false (and sets a message) if it cannot start.</summary>
    public static bool Start(bool runAfter)
    {
        if (_building) return false;

        var cfg = SceneManager.Config;
        var scenes = cfg.EnabledScenes.Select(s => SceneManager.NameOf(s.Path)).ToList();

        if (scenes.Count == 0) return Fail("No scenes in the build. Open a scene and press Add Open Scenes.");
        var missing = scenes.Where(n => !SceneManager.Exists(n)).ToList();
        if (missing.Count > 0) return Fail($"Scene file missing: {missing[0]}. Open it and press Save.");
        if (cfg.Platform == BuildPlatform.WebGL) return StartWeb(cfg, scenes, runAfter);
        if (GameProject.CsprojPath is not { } csproj) return Fail("Could not find the game's .csproj next to the program.");
        var rid = BuildConfig.RuntimeIdentifier(cfg.Target, cfg.Architecture);
        if (rid is null) return Fail($"{cfg.Target} {cfg.Architecture} is not supported (no Raylib native library).");

        string outDir = ResolveOutputDirectory(cfg);
        var request = new PublishRequest(csproj, rid, cfg.DevelopmentBuild ? "Debug" : "Release", outDir, cfg.SelfContained, cfg.CopyPdbFiles);
        var snapshot = JsonSnapshot(cfg);

        try { SceneManager.SaveConfig(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fail($"Could not save BuildSettings.json: {ex.Message}"); }

        lock (Gate) Log.Clear();
        SetStatus($"Building {rid} ...", false);
        _building = true;
        Task.Run(() => Execute(request, snapshot, scenes, runAfter));
        return true;
    }

    private static bool StartWeb(BuildConfig cfg, List<string> scenes, bool runAfter)
    {
        string outDir = ResolveOutputDirectory(cfg);
        var snapshot = JsonSnapshot(cfg);
        try { SceneManager.SaveConfig(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fail($"Could not save BuildSettings.json: {ex.Message}"); }

        lock (Gate) Log.Clear();
        SetStatus("Building WebGL ...", false);
        _building = true;
        Task.Run(() => ExecuteWeb(outDir, snapshot, scenes, runAfter));
        return true;
    }

    private static void ExecuteWeb(string outDir, BuildConfig cfg, List<string> scenes, bool runAfter)
    {
        var watch = Stopwatch.StartNew();
        var req = new PublishRequest("", "web", cfg.DevelopmentBuild ? "Debug" : "Release", outDir, false, false);
        try
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
            Directory.CreateDirectory(outDir);

            SetStatus("Exporting scenes and meshes ...", false);
            var warnings = WebExporter.Export(outDir, cfg, scenes);
            foreach (var w in warnings) lock (Gate) Log.AppendLine("warning: " + w);

            string result = outDir;
            if (cfg.Compression == CompressionMethod.Zip)
            {
                SetStatus("Compressing ...", false);
                result = outDir.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
                if (File.Exists(result)) File.Delete(result);
                ZipFile.CreateFromDirectory(outDir, result);
            }

            WriteLog(req);
            lock (Gate) _lastOutput = outDir;
            string text = $"WebGL build succeeded in {watch.Elapsed.TotalSeconds:0.#}s ({FormatSize(DirectorySize(outDir))}): {result}";
            if (warnings.Count > 0) text += $" ({warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}, see Builds/last-build.log)";
            if (runAfter) text += " " + OpenInBrowser(Path.Combine(outDir, "index.html"));
            SetStatus(text, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            lock (Gate) Log.AppendLine(ex.ToString());
            try { WriteLog(req); } catch (IOException) { }
            SetStatus($"WebGL build failed: {ex.Message}", true);
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>Starts the default browser on the page (tests replace this).</summary>
    public static Func<string, string> OpenInBrowser { get; set; } = path =>
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return "Opened in your browser.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "(could not open the browser: " + ex.Message + ")";
        }
    };

    public static string ResolveOutputDirectory(BuildConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.OutputFolder))
            return Path.GetFullPath(cfg.OutputFolder, GameProject.SolutionDirectory ?? Environment.CurrentDirectory);

        string product = SafeFileName(cfg.ProductName);
        string rid = cfg.Platform == BuildPlatform.WebGL ? "WebGL" : BuildConfig.RuntimeIdentifier(cfg.Target, cfg.Architecture) ?? "unknown";
        return Path.Combine(GameProject.SolutionDirectory ?? Environment.CurrentDirectory, "Builds", $"{product}_{rid}");
    }

    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "Game" : clean;
    }

    // ---- the build ---------------------------------------------------------------------------

    private static void Execute(PublishRequest req, BuildConfig cfg, List<string> scenes, bool runAfter)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(req.OutputDirectory)) Directory.Delete(req.OutputDirectory, recursive: true);
            Directory.CreateDirectory(req.OutputDirectory);

            SetStatus("Compiling (dotnet publish) ...", false);
            int code = Publisher(req, line => { lock (Gate) Log.AppendLine(line); });
            if (code != 0)
            {
                WriteLog(req);
                SetStatus($"Build failed (exit code {code}). See Builds/last-build.log", true);
                return;
            }

            SetStatus("Copying scenes ...", false);
            var scenesOut = Path.Combine(req.OutputDirectory, "Scenes");
            Directory.CreateDirectory(scenesOut);
            foreach (var name in scenes)
                File.Copy(SceneManager.FileFor(name), Path.Combine(scenesOut, name + ".json"), overwrite: true);

            WorldLayout.WriteSubset(Path.Combine(req.OutputDirectory, WorldLayout.FileName), scenes);   // where the scenes sit in the world

            var assetsSource = Path.Combine(GameProject.Root, "Assets");
            if (Directory.Exists(assetsSource))
            {
                SetStatus("Copying assets ...", false);
                CopyTree(assetsSource, Path.Combine(req.OutputDirectory, "Assets"), req.OutputDirectory);
            }

            // the file's presence next to the program is what switches it into player mode
            var playerCfg = cfg;
            playerCfg.Scenes = scenes.Select(n => new BuildScene { Path = SceneManager.BuildPathFor(n), Enabled = true }).ToList();
            File.WriteAllText(Path.Combine(req.OutputDirectory, GameProject.ConfigFileName), playerCfg.ToJson());

            RenameExecutable(req, cfg);

            if (!cfg.CopyPdbFiles)
                foreach (var pdb in Directory.GetFiles(req.OutputDirectory, "*.pdb", SearchOption.AllDirectories))
                    File.Delete(pdb);

            if (cfg.CreateVisualStudioSolution)
            {
                SetStatus("Exporting Visual Studio solution ...", false);
                var root = GameProject.SolutionDirectory;
                if (root is not null)
                    CopyTree(root, req.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar) + "_VisualStudio", req.OutputDirectory);
            }

            string result = req.OutputDirectory;
            if (cfg.Compression == CompressionMethod.Zip)
            {
                SetStatus("Compressing ...", false);
                result = req.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
                if (File.Exists(result)) File.Delete(result);
                ZipFile.CreateFromDirectory(req.OutputDirectory, result);
            }

            WriteLog(req);
            lock (Gate) _lastOutput = req.OutputDirectory;
            string size = FormatSize(DirectorySize(req.OutputDirectory));
            string text = $"Build succeeded in {watch.Elapsed.TotalSeconds:0.#}s ({size}): {result}";

            if (runAfter) text += " " + Launch(req, cfg);
            SetStatus(text, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            lock (Gate) Log.AppendLine(ex.ToString());
            try { WriteLog(req); } catch (IOException) { }
            SetStatus($"Build failed: {ex.Message}", true);
        }
        finally
        {
            _building = false;
        }
    }

    private static string ExecutableName(BuildConfig cfg, PublishRequest req)
    {
        string baseName = SafeFileName(cfg.ProductName);
        return cfg.Target == TargetPlatform.Windows ? baseName + ".exe" : baseName;
    }

    /// <summary>The published apphost is named after the project; give it the product name.</summary>
    private static void RenameExecutable(PublishRequest req, BuildConfig cfg)
    {
        string project = Path.GetFileNameWithoutExtension(req.CsprojPath);
        string ext = cfg.Target == TargetPlatform.Windows ? ".exe" : "";
        string from = Path.Combine(req.OutputDirectory, project + ext);
        string to = Path.Combine(req.OutputDirectory, ExecutableName(cfg, req));
        if (File.Exists(from) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            File.Move(from, to, overwrite: true);
    }

    private static string Launch(PublishRequest req, BuildConfig cfg)
    {
        if (cfg.Target != BuildConfig.HostPlatform())
            return $"(built for {cfg.Target}, which cannot run on this computer)";

        string exe = Path.Combine(req.OutputDirectory, ExecutableName(cfg, req));
        if (!File.Exists(exe)) return "(executable not found, nothing to run)";

        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute); }
            catch (IOException) { }
        }
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = req.OutputDirectory, UseShellExecute = false });
        return "Started.";
    }

    // ---- dotnet publish ----------------------------------------------------------------------

    private static int DotnetPublish(PublishRequest req, Action<string> log)
    {
        string host = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? Environment.ProcessPath! : "dotnet";

        var psi = new ProcessStartInfo(host)
        {
            WorkingDirectory = Path.GetDirectoryName(req.CsprojPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[]
                 {
                     "publish", req.CsprojPath, "-c", req.Configuration, "-r", req.RuntimeId,
                     "--self-contained", req.SelfContained ? "true" : "false",
                     "-o", req.OutputDirectory, "-nologo"
                 })
            psi.ArgumentList.Add(a);

        if (!req.KeepSymbols)
        {
            psi.ArgumentList.Add("-p:DebugType=None");
            psi.ArgumentList.Add("-p:DebugSymbols=false");
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start dotnet.");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static BuildConfig JsonSnapshot(BuildConfig cfg) => BuildConfig.FromJson(cfg.ToJson());

    private static bool Fail(string message)
    {
        SetStatus(message, true);
        return false;
    }

    private static void SetStatus(string text, bool error)
    {
        lock (Gate)
        {
            _status = text;
            _statusIsError = error;
        }
    }

    private static void WriteLog(PublishRequest req)
    {
        var dir = Path.Combine(GameProject.SolutionDirectory ?? Path.GetDirectoryName(req.OutputDirectory) ?? ".", "Builds");
        Directory.CreateDirectory(dir);
        string text;
        lock (Gate) text = Log.ToString();
        File.WriteAllText(Path.Combine(dir, "last-build.log"), text);
    }

    private static long DirectorySize(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):0.#} MB" : $"{bytes / 1024.0:0.#} KB";

    private static readonly string[] SkipFolders = { "bin", "obj", "Builds", ".git", ".vs", "node_modules" };

    private static void CopyTree(string source, string target, string buildOutput)
    {
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (SkipFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(buildOutput), StringComparison.OrdinalIgnoreCase)) continue;
            CopyTree(dir, Path.Combine(target, name), buildOutput);
        }
    }
}
