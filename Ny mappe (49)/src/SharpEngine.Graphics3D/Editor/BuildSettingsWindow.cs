using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// The "Build Settings" dialog: scenes in the build, target platform, options, and the
/// Build / Build And Run buttons. Drawn by <see cref="AdminEditor"/>.
/// </summary>
public static class BuildSettingsWindow
{
    private static readonly Color Body = new(56, 56, 56, 255);
    private static readonly Color Well = new(42, 42, 42, 255);
    private static readonly Color Line = new(24, 24, 24, 255);
    private static readonly Color Label = new(206, 206, 206, 255);
    private static readonly Color LabelOff = new(112, 112, 112, 255);
    private static readonly Color Selected = new(44, 93, 135, 255);
    private static readonly Color LinkBlue = new(110, 170, 255, 255);

    private static readonly string[] PlatformNames =
    {
        "PC, Mac & Linux Standalone", "Universal Windows Platform", "WebGL", "tvOS", "PS4", "iOS", "Xbox One", "Android"
    };

    private static readonly string[] TargetNames = { "Windows", "macOS", "Linux" };
    private static readonly string[] CompressionNames = { "Default", "Zip" };

    private static int _selectedScene = -1;
    private static float _listScroll;
    private static bool _playerSettings;
    private static string _info = "";
    private static Color _infoColor = new(120, 215, 130, 255);

    private static readonly Color Bad = new(240, 120, 120, 255);
    private static readonly Color Good = new(120, 215, 130, 255);
    private static readonly Color Busy = new(240, 210, 90, 255);

    private static void Info(string text, bool error = false)
    {
        _info = text;
        _infoColor = error ? Bad : Good;
    }

    public static bool IsOpen { get; private set; }

    public static void Open()
    {
        IsOpen = true;
        _info = "";
    }

    public static void Close()
    {
        IsOpen = false;
        _playerSettings = false;
    }

    private static bool Clicked(Rectangle r) => Gui.IsHot(r) && Gui.LeftPressed;

    public static void Draw(World world)
    {
        if (!IsOpen) return;
        var cfg = SceneManager.Config;

        float sw = Gui.ScreenWidth, sh = Gui.ScreenHeight;
        float w = MathF.Min(sw - 40f, 900f), h = MathF.Min(sh - 30f, 700f);
        var d = new Rectangle((sw - w) / 2f, (sh - h) / 2f, w, h);

        Rl.DrawRectangle(0, 0, (int)sw, (int)sh, new Color(0, 0, 0, 120));
        Rl.DrawRectangleRec(new Rectangle(d.X + 5, d.Y + 6, d.Width, d.Height), new Color(0, 0, 0, 100));
        Rl.DrawRectangleRec(d, Body);
        Rl.DrawRectangleLinesEx(d, 1f, Line);

        // the Player Settings sub-dialog sits on top and takes all input while it is open
        Gui.Disabled = _playerSettings;
        DrawTitleBar(d);
        DrawScenes(d, cfg, out float y);
        DrawPlatformArea(d, cfg, y);
        DrawBottomBar(world, d, cfg);
        Gui.Disabled = false;

        if (_playerSettings) DrawPlayerSettings(sw, sh, cfg);

        bool escape = Rl.IsKeyPressed(KeyboardKey.Escape) && !Gui.PopupOpen && !Gui.IsEditing && !CodeEditorWindow.IsOpen;
        if (escape)
        {
            if (_playerSettings) _playerSettings = false;
            else Close();
        }
    }

    // ---- title ---------------------------------------------------------------------------------

    private static void DrawTitleBar(Rectangle d)
    {
        Rl.DrawRectangleRec(new Rectangle(d.X, d.Y, d.Width, 56), new Color(242, 242, 242, 255));
        Gui.TextSized(d.X + 10, d.Y + 10, "Build Settings", 34, new Color(112, 112, 112, 255));

        var close = new Rectangle(d.X + d.Width - 48, d.Y + 10, 36, 36);
        bool hover = Gui.IsHot(close);
        Rl.DrawRectangleRec(close, hover ? new Color(205, 205, 205, 255) : new Color(226, 226, 226, 255));
        var x = new Color(160, 160, 160, 255);
        Rl.DrawLineEx(new Vector2(close.X + 12, close.Y + 12), new Vector2(close.X + 24, close.Y + 24), 2f, x);
        Rl.DrawLineEx(new Vector2(close.X + 24, close.Y + 12), new Vector2(close.X + 12, close.Y + 24), 2f, x);
        if (hover && Gui.LeftPressed) Close();
    }

    // ---- scenes in build -------------------------------------------------------------------------

    private static void DrawScenes(Rectangle d, BuildConfig cfg, out float nextY)
    {
        float x = d.X + 16, y = d.Y + 56 + 12;
        Gui.TextSized(x + 4, y, "Scenes In Build", 20, Label, bold: true);
        y += 28;

        float listH = Math.Clamp(d.Height * 0.2f, 88f, 150f);
        var list = new Rectangle(x, y, d.Width - 32, listH);
        Rl.DrawRectangleRec(list, Well);
        Rl.DrawRectangleLinesEx(list, 1f, Line);

        const float rowH = 24f;
        float contentH = MathF.Max(1, cfg.Scenes.Count) * rowH + 4;
        Gui.SetScope(7000);
        Gui.BeginScroll(list, ref _listScroll, contentH);

        float ry = list.Y + 2 - _listScroll;
        int index = 0;
        for (int i = 0; i < cfg.Scenes.Count; i++)
        {
            var scene = cfg.Scenes[i];
            var row = new Rectangle(list.X + 2, ry, list.Width - 12, rowH);
            if (i == _selectedScene) Rl.DrawRectangleRec(row, Selected);

            bool enabled = scene.Enabled;
            var tick = new Rectangle(row.X + 6, row.Y + 2, 20, rowH - 4);
            if (Gui.Checkbox(tick, "scene" + i, "", ref enabled, tick: true)) scene.Enabled = enabled;
            else if (Clicked(row)) _selectedScene = i;

            bool exists = SceneManager.Exists(scene.Path);
            string name = SceneManager.BuildPathFor(scene.Path);
            Gui.TextSized(row.X + 34, row.Y + 3, name, 17, exists ? Color.White : new Color(240, 120, 120, 255));
            if (!exists) Gui.TextSized(row.X + 44 + Gui.MeasureSized(name, 17), row.Y + 3, "(missing file)", 15, new Color(240, 120, 120, 255));

            if (scene.Enabled)
            {
                string num = index.ToString();
                Gui.TextSized(row.X + row.Width - 10 - Gui.MeasureSized(num, 17), row.Y + 3, num, 17, Color.White);
                index++;
            }
            ry += rowH;
        }
        if (cfg.Scenes.Count == 0)
            Gui.TextSized(list.X + 12, list.Y + 8, "No scenes yet. Open a scene and press \"Add Open Scenes\".", 16, LabelOff);
        Gui.EndScroll(list);

        if (_selectedScene >= cfg.Scenes.Count) _selectedScene = cfg.Scenes.Count - 1;

        // buttons under the list
        float by = list.Y + list.Height + 6;
        Gui.SetScope(7100);
        bool has = _selectedScene >= 0 && _selectedScene < cfg.Scenes.Count;
        if (Gui.Button(new Rectangle(x, by, 80, 26), "Remove", enabled: has) ||
            (has && Rl.IsKeyPressed(KeyboardKey.Delete) && !Gui.IsEditing))
        {
            cfg.Scenes.RemoveAt(_selectedScene);
            if (_selectedScene >= cfg.Scenes.Count) _selectedScene = cfg.Scenes.Count - 1;
            has = _selectedScene >= 0;
        }
        if (Gui.Button(new Rectangle(x + 86, by, 56, 26), "Up", enabled: has && _selectedScene > 0))
        {
            (cfg.Scenes[_selectedScene], cfg.Scenes[_selectedScene - 1]) = (cfg.Scenes[_selectedScene - 1], cfg.Scenes[_selectedScene]);
            _selectedScene--;
        }
        if (Gui.Button(new Rectangle(x + 148, by, 66, 26), "Down", enabled: has && _selectedScene < cfg.Scenes.Count - 1))
        {
            (cfg.Scenes[_selectedScene], cfg.Scenes[_selectedScene + 1]) = (cfg.Scenes[_selectedScene + 1], cfg.Scenes[_selectedScene]);
            _selectedScene++;
        }
        float rightX = list.X + list.Width;
        if (Gui.Button(new Rectangle(rightX - 150, by, 150, 26), "Add Open Scenes"))
            AddOpenScene(cfg);
        if (Gui.Button(new Rectangle(rightX - 150 - 6 - 104, by, 104, 26), "New Scene"))
            AddNewScene(cfg);

        var inBuild = cfg.Scenes.Select(sc => SceneManager.NameOf(sc.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = SceneManager.AvailableScenes().Where(n => !inBuild.Contains(n)).ToArray();
        int pick = Gui.Menu(new Rectangle(rightX - 150 - 6 - 104 - 6 - 136, by, 136, 26), "existing", "Add Existing",
            others.Length > 0 ? others : new[] { "(all scenes are in the build)" });
        if (pick >= 0 && pick < others.Length)
        {
            cfg.Scenes.Add(new BuildScene { Path = SceneManager.BuildPathFor(others[pick]), Enabled = true });
            _selectedScene = cfg.Scenes.Count - 1;
            Info($"Added {others[pick]} (build index {cfg.EnabledScenes.Count() - 1}).");
            TrySave();
        }

        nextY = by + 26 + 12;
    }

    private static void AddNewScene(BuildConfig cfg)
    {
        try
        {
            string name = SceneManager.CreateSceneFile("NewScene");
            cfg.Scenes.Add(new BuildScene { Path = SceneManager.BuildPathFor(name), Enabled = true });
            _selectedScene = cfg.Scenes.Count - 1;
            Info($"Created {name} and added it to the build. Open it from the Scenes menu to edit it.");
            TrySave();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Info("Could not create the scene: " + ex.Message, error: true);
        }
    }

    private static void AddOpenScene(BuildConfig cfg)
    {
        if (!AdminEditor.SaveCurrentScene(out string name))
        {
            Info("Could not save the open scene, so it was not added.", error: true);
            return;
        }
        string path = SceneManager.BuildPathFor(name);
        if (cfg.Scenes.Any(s => string.Equals(SceneManager.NameOf(s.Path), name, StringComparison.OrdinalIgnoreCase)))
        {
            Info($"{name} is already in the build.", error: true);
            return;
        }
        cfg.Scenes.Add(new BuildScene { Path = path, Enabled = true });
        _selectedScene = cfg.Scenes.Count - 1;
        Info($"Added {name} (build index {cfg.EnabledScenes.Count() - 1}).");
        TrySave();
    }

    // ---- platform + options -------------------------------------------------------------------------

    private static void DrawPlatformArea(Rectangle d, BuildConfig cfg, float y)
    {
        float x = d.X + 16;
        Gui.TextSized(x + 4, y, "Platform", 20, Label, bold: true);
        y += 28;

        float bottomBar = 48f;
        float avail = d.Y + d.Height - bottomBar - y;
        float leftW = (d.Width - 32) * 0.42f;

        // --- platform list
        var list = new Rectangle(x, y, leftW, avail);
        Rl.DrawRectangleRec(list, Well);
        Rl.DrawRectangleLinesEx(list, 1f, Line);
        float rowH = Math.Clamp((avail - 2) / PlatformNames.Length, 26f, 46f);
        Gui.SetScope(7200);
        for (int i = 0; i < PlatformNames.Length; i++)
        {
            var row = new Rectangle(list.X + 1, list.Y + 1 + i * rowH, list.Width - 2, rowH);
            bool supported = i == 0 || i == 2;
            bool isSelected = i == (cfg.Platform == BuildPlatform.WebGL ? 2 : 0);
            if (isSelected) Rl.DrawRectangleRec(row, Selected);
            else if (Gui.IsHot(row)) Rl.DrawRectangleRec(row, new Color(60, 60, 60, 255));

            var iconColor = supported ? Color.White : new Color(150, 150, 150, 255);
            DrawIcon(i, new Rectangle(row.X + 8, row.Y + (rowH - 30) / 2f, 30, 30), iconColor);
            Gui.TextSized(row.X + 52, row.Y + (rowH - 20) / 2f, PlatformNames[i], 20, supported ? Color.White : new Color(165, 165, 165, 255));

            if (isSelected) // small badge on the selected platform
            {
                var c = new Vector2(row.X + row.Width - 22, row.Y + rowH / 2f);
                Rl.DrawPoly(c, 6, 11, 0, new Color(235, 235, 235, 255));
                Rl.DrawPoly(c, 6, 6, 0, Selected);
            }
            else if (supported && Gui.IsHot(row) && Gui.LeftPressed)
            {
                cfg.Platform = i == 2 ? BuildPlatform.WebGL : BuildPlatform.Standalone;
                Info($"Platform: {PlatformNames[i]}.");
                TrySave();
            }
            else if (Gui.IsHot(row) && Gui.LeftPressed)
            {
                Info($"{PlatformNames[i]} is not supported by SharpEngine yet.", error: true);
            }
        }

        // --- settings column
        float rx = x + leftW + 22f;
        float rw = d.X + d.Width - 16 - rx;
        bool web = cfg.Platform == BuildPlatform.WebGL;
        int shownPlatform = web ? 2 : 0;
        DrawIcon(shownPlatform, new Rectangle(rx, y, 34, 34), new Color(190, 190, 190, 255));
        Gui.TextSized(rx + 50, y + 6, PlatformNames[shownPlatform], 22, new Color(222, 222, 222, 255));

        float rowH2 = Math.Clamp((avail - 52f) / 14f, 20f, 28f);
        float ry = y + 46f;
        float labelW = MathF.Min(250f, rw * 0.54f);
        float controlW = rw - labelW;

        Rectangle Control(float yy) => new(rx + labelW, yy, controlW, rowH2 - 3f);
        void RowLabel(string text, bool on = true) =>
            Gui.TextSized(rx, ry + (rowH2 - 17f) / 2f, text, 17, on ? Label : LabelOff);

        Gui.SetScope(7300);

        if (!web)
        {
        RowLabel("Target Platform");
        int target = (int)cfg.Target;
        if (Gui.Dropdown(Control(ry), "target", TargetNames, ref target))
        {
            cfg.Target = (TargetPlatform)target;
            cfg.FixArchitecture();
        }
        ry += rowH2;

        RowLabel("Architecture");
        var archs = BuildConfig.SupportedArchitectures(cfg.Target);
        var archNames = archs.Select(a => a switch { CpuArchitecture.X86 => "x86", CpuArchitecture.Arm64 => "ARM64", _ => "x64" }).ToArray();
        int archIndex = Math.Max(0, Array.IndexOf(archs, cfg.Architecture));
        if (Gui.Dropdown(Control(ry), "arch", archNames, ref archIndex)) cfg.Architecture = archs[archIndex];
        ry += rowH2;

        bool server = false;
        RowLabel("Server Build", on: false);
        Gui.Checkbox(Control(ry), "server", "", ref server, enabled: false);
        ry += rowH2;

        RowLabel("Copy PDB files");
        bool pdb = cfg.CopyPdbFiles;
        if (Gui.Checkbox(Control(ry), "pdb", "", ref pdb)) cfg.CopyPdbFiles = pdb;
        ry += rowH2;

        RowLabel("Create Visual Studio Solution");
        bool vs = cfg.CreateVisualStudioSolution;
        if (Gui.Checkbox(Control(ry), "vs", "", ref vs)) cfg.CreateVisualStudioSolution = vs;
        ry += rowH2;

        }   // end standalone-only rows

        if (web)
        {
            RowLabel("Web Engine");
            int engine = (int)cfg.WebEngine;
            if (Gui.Dropdown(Control(ry), "webengine", new[] { "Real engine (WebAssembly)", "Lite (JavaScript)" }, ref engine)) cfg.WebEngine = (WebEngine)engine;
            ry += rowH2;
        }

        RowLabel("Development Build");
        bool dev = cfg.DevelopmentBuild;
        if (Gui.Checkbox(Control(ry), "dev", "", ref dev)) cfg.DevelopmentBuild = dev;
        ry += rowH2;

        foreach (var name in web ? Array.Empty<string>() : new[] { "Autoconnect Profiler", "Deep Profiling", "Script Debugging", "Scripts Only Build" })
        {
            bool off = false;
            RowLabel(name, on: false);
            Gui.Checkbox(Control(ry), name, "", ref off, enabled: false);
            ry += rowH2;
        }

        ry += 6;
        RowLabel("Compression Method");
        int comp = (int)cfg.Compression;
        if (Gui.Dropdown(Control(ry), "comp", CompressionNames, ref comp)) cfg.Compression = (CompressionMethod)comp;
        ry += rowH2;

        RowLabel("Output Folder");
        string output = cfg.OutputFolder;
        var field = Control(ry);
        if (Gui.TextField(field, "out", ref output, placeholder: ShortPath(BuildPipeline.ResolveOutputDirectory(cfg), field.Width)))
            cfg.OutputFolder = output.Trim();
        ry += rowH2 + 4;

        // link-style button, like "Learn about Unity Cloud Build"
        const string link = "Show Build Folder";
        int lw = Gui.MeasureSized(link, 17);
        var linkRect = new Rectangle(rx + rw - lw - 2, ry, lw + 4, 22);
        Gui.TextSized(linkRect.X, linkRect.Y + 2, link, 17, LinkBlue);
        Rl.DrawLine((int)linkRect.X, (int)linkRect.Y + 20, (int)(linkRect.X + lw), (int)linkRect.Y + 20, LinkBlue);
        if (Clicked(linkRect)) ShowBuildFolder(cfg);
    }

    private static string ShortPath(string path, float width)
    {
        int max = Math.Max(12, (int)(width / 8));
        return path.Length <= max ? path : "..." + path[^(max - 3)..];
    }

    private static void ShowBuildFolder(BuildConfig cfg)
    {
        var dir = BuildPipeline.LastOutputDirectory ?? BuildPipeline.ResolveOutputDirectory(cfg);
        if (!Directory.Exists(dir))
        {
            Info("Nothing built yet: " + dir, error: true);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Info("Could not open the folder: " + ex.Message, error: true);
        }
    }

    // ---- bottom bar ------------------------------------------------------------------------------------

    private static void DrawBottomBar(World world, Rectangle d, BuildConfig cfg)
    {
        float y = d.Y + d.Height - 42f;
        Gui.SetScope(7400);

        if (Gui.Button(new Rectangle(d.X + 16, y, 150, 28), "Player Settings...")) _playerSettings = true;

        bool building = BuildPipeline.IsBuilding;
        float bx = d.X + d.Width - 16 - 160f;
        bool run = Gui.Button(new Rectangle(bx, y, 160, 28), "Build And Run", enabled: !building);
        bool build = Gui.Button(new Rectangle(bx - 166, y, 160, 28), "Build", enabled: !building);

        if (build || run)
        {
            _info = "";
            if (!AdminEditor.PrepareForBuild(out string problem)) Info(problem, error: true);
            else BuildPipeline.Start(runAfter: run);
        }

        // status text between the buttons
        string text;
        Color color;
        if (building)
        {
            text = BuildPipeline.Status.TrimEnd('.', ' ') + new string('.', 1 + (int)(Rl.GetTime() * 3) % 3);
            color = Busy;
        }
        else if (_info.Length > 0)
        {
            text = _info;
            color = _infoColor;
        }
        else
        {
            text = BuildPipeline.Status;
            color = BuildPipeline.StatusIsError ? Bad : Good;
        }

        var area = new Rectangle(d.X + 180, y, bx - 166 - (d.X + 180) - 10, 28);
        if (text.Length > 0) Gui.TextSized(area.X, area.Y + 6, FitText(text, area.Width, 15), 15, color);
    }

    private static string FitText(string text, float width, int size)
    {
        while (text.Length > 4 && Gui.MeasureSized(text, size) > width) text = text[..^4] + "...";
        return text;
    }

    private static void TrySave()
    {
        try { SceneManager.SaveConfig(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Info("Could not save BuildSettings.json: " + ex.Message, error: true); }
    }

    // ---- player settings ------------------------------------------------------------------------------

    private static void DrawPlayerSettings(float sw, float sh, BuildConfig cfg)
    {
        float w = MathF.Min(sw - 60f, 520f), h = 330f;
        var d = new Rectangle((sw - w) / 2f, (sh - h) / 2f, w, h);
        Rl.DrawRectangle(0, 0, (int)sw, (int)sh, new Color(0, 0, 0, 120));
        Rl.DrawRectangleRec(d, Body);
        Rl.DrawRectangleLinesEx(d, 1f, Line);
        Rl.DrawRectangleRec(new Rectangle(d.X, d.Y, d.Width, 48), new Color(242, 242, 242, 255));
        Gui.TextSized(d.X + 10, d.Y + 8, "Player Settings", 30, new Color(112, 112, 112, 255));

        float x = d.X + 20, y = d.Y + 66, lw = 190, fw = d.Width - 40 - lw;
        Gui.SetScope(7500);

        void Row(string label, Action<Rectangle> control)
        {
            Gui.TextSized(x, y + 3, label, 17, Label);
            control(new Rectangle(x + lw, y, fw, 24));
            y += 32;
        }

        Row("Product Name", r =>
        {
            string v = cfg.ProductName;
            if (Gui.TextField(r, "product", ref v) && v.Trim().Length > 0) cfg.ProductName = v.Trim();
        });
        Row("Window Width", r =>
        {
            float v = cfg.WindowWidth;
            if (Gui.FloatField(r, "ww", ref v, 4f, integer: true)) cfg.WindowWidth = Math.Clamp((int)v, 320, 7680);
        });
        Row("Window Height", r =>
        {
            float v = cfg.WindowHeight;
            if (Gui.FloatField(r, "wh", ref v, 4f, integer: true)) cfg.WindowHeight = Math.Clamp((int)v, 240, 4320);
        });
        Row("Start Fullscreen", r =>
        {
            bool v = cfg.StartFullscreen;
            if (Gui.Checkbox(r, "full", "", ref v)) cfg.StartFullscreen = v;
        });
        Row("Bundle .NET runtime", r =>
        {
            bool v = cfg.SelfContained;
            if (Gui.Checkbox(r, "self", "", ref v)) cfg.SelfContained = v;
        });
        Gui.TextSized(x, y, "Off: smaller build, needs .NET 8 installed. On: larger, runs anywhere.", 14, LabelOff);

        if (Gui.Button(new Rectangle(d.X + d.Width - 116, d.Y + d.Height - 42, 100, 28), "Done"))
        {
            _playerSettings = false;
            TrySave();
        }
    }

    // ---- icons -----------------------------------------------------------------------------------------

    private static void DrawIcon(int kind, Rectangle r, Color c)
    {
        float cx = r.X + r.Width / 2f;
        switch (kind)
        {
            case 0: // monitor
                Rl.DrawRectangleLinesEx(new Rectangle(r.X + 1, r.Y + 3, r.Width - 2, r.Height * 0.62f), 2f, c);
                Rl.DrawRectangle((int)cx - 2, (int)(r.Y + 3 + r.Height * 0.62f), 4, 5, c);
                Rl.DrawRectangle((int)cx - 8, (int)(r.Y + 8 + r.Height * 0.62f), 16, 2, c);
                break;
            case 1: // windows: four panes
                float p = r.Width / 2f - 2;
                Rl.DrawRectangleRec(new Rectangle(r.X + 2, r.Y + 3, p, p - 1), c);
                Rl.DrawRectangleRec(new Rectangle(cx + 1, r.Y + 3, p, p - 1), c);
                Rl.DrawRectangleRec(new Rectangle(r.X + 2, r.Y + 3 + p + 1, p, p - 1), c);
                Rl.DrawRectangleRec(new Rectangle(cx + 1, r.Y + 3 + p + 1, p, p - 1), c);
                break;
            case 2:
                Rl.DrawRectangleLinesEx(new Rectangle(r.X + 3, r.Y + 2, r.Width - 6, r.Height - 4), 2f, c);
                Gui.TextSized(cx - 4, r.Y + 7, "5", 18, c, bold: true);
                break;
            case 6: // xbox: ring with cross
                Rl.DrawRing(new Vector2(cx, r.Y + r.Height / 2f), r.Width / 2f - 3, r.Width / 2f - 1, 0, 360, 24, c);
                Rl.DrawLineEx(new Vector2(cx - 5, r.Y + r.Height / 2f - 5), new Vector2(cx + 5, r.Y + r.Height / 2f + 5), 2f, c);
                Rl.DrawLineEx(new Vector2(cx + 5, r.Y + r.Height / 2f - 5), new Vector2(cx - 5, r.Y + r.Height / 2f + 5), 2f, c);
                break;
            default:
                string t = kind switch { 3 => "tvOS", 4 => "PS4", 5 => "iOS", _ => "Android" };
                int size = kind == 7 ? 11 : 15;
                Gui.TextSized(cx - Gui.MeasureSized(t, size) / 2f, r.Y + (r.Height - size) / 2f, t, size, c, bold: true);
                break;
        }
    }
}
