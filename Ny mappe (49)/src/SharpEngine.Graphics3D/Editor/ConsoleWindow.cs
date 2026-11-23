using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SharpEngine.Graphics3D.Scripting;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// A command prompt inside the editor (press F2 or ` or the toolbar's Console button). A few engine commands (help, scenes, open, play, stop, build, new)
/// work directly; everything else runs in the system shell (cmd on Windows) in the project folder, with its output shown live:
/// dotnet build, dir, git status, ... While a command runs, Enter sends the typed line to it and Ctrl+C stops it. Prefix a line with ! to force the shell.
/// </summary>
public static class ConsoleWindow
{
    private static readonly object Gate = new();
    private static readonly List<(string Text, Color Color)> Lines = new();
    private static readonly List<string> History = new();
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private static readonly Color Normal = new(214, 216, 222, 255);
    private static readonly Color Dim = new(130, 135, 148, 255);
    private static readonly Color Prompt = new(120, 200, 255, 255);
    private static readonly Color Good = new(120, 215, 130, 255);
    private static readonly Color Bad = new(240, 130, 130, 255);
    private static readonly Color Warn = new(240, 200, 110, 255);

    private const int MaxLines = 3000;
    private const int LineH = 18;

    private static string _input = "";
    private static int _caret;
    private static int _historyAt = -1;
    private static string _cwd = "";
    private static Process? _proc;
    private static int _scrollUp;
    private static bool _justOpened;
    private static string? _openRequest;
    private static bool _playRequest;

    public static bool IsOpen { get; private set; }

    public static bool IsRunning
    {
        get { var p = _proc; try { return p is not null && !p.HasExited; } catch (InvalidOperationException) { return false; } }
    }

    public static void Open()
    {
        IsOpen = true;
        _justOpened = true;   // the key that opened the console must not be typed into it
        if (_cwd.Length == 0) _cwd = GameProject.Root;
        lock (Gate) if (Lines.Count == 0)
        {
            Lines.Add(("SharpEngine console. Type help for the engine commands.", Good));
            Lines.Add(("Anything else runs in the system shell (cmd) in your project folder, for example: dotnet build", Dim));
        }
    }

    public static void Close() { IsOpen = false; }

    /// <summary>A scene the user asked to open with "open name" (the editor takes it once).</summary>
    public static string? TakeOpenRequest() { var r = _openRequest; _openRequest = null; return r; }

    /// <summary>True once when the user typed play or stop (the editor toggles play mode).</summary>
    public static bool TakePlayRequest() { var r = _playRequest; _playRequest = false; return r; }

    // ---- drawing -------------------------------------------------------------------------------------------

    public static void Draw(World world)
    {
        if (!IsOpen) return;
        if (_justOpened) { _justOpened = false; DrainChars(); }
        else HandleInput(world);
        if (!IsOpen) return;

        float sw = Gui.ScreenWidth, sh = Gui.ScreenHeight;
        float h = MathF.Max(240f, sh * 0.45f);
        var d = new Rectangle(0, sh - h, sw, h);
        Rl.DrawRectangleRec(d, new Color(16, 17, 21, 248));
        Rl.DrawRectangleRec(new Rectangle(d.X, d.Y, d.Width, 28), new Color(32, 34, 40, 255));
        Gui.Text(d.X + 10, d.Y + 6, "Console", Gui.TextCol);
        string where = _cwd + (IsRunning ? "   (running: Ctrl+C stops it)" : "");
        Gui.Text(d.X + 90, d.Y + 6, Gui.FitText(where, d.Width - 250), Gui.TextDim);
        Gui.SetScope(9400);
        if (Gui.Button(new Rectangle(d.X + d.Width - 160, d.Y + 2, 70, 24), "Clear")) lock (Gate) Lines.Clear();
        if (Gui.Button(new Rectangle(d.X + d.Width - 84, d.Y + 2, 74, 24), "Close")) { Close(); return; }

        var area = new Rectangle(d.X + 8, d.Y + 32, d.Width - 16, d.Height - 32 - 32);
        DrawLog(d, area);
        DrawInput(new Rectangle(d.X + 8, d.Y + d.Height - 28, d.Width - 16, 24));
    }

    private static void DrawLog(Rectangle panel, Rectangle area)
    {
        int rows = Math.Max(1, (int)(area.Height / LineH));
        float charW = MathF.Max(4f, Rl.MeasureText("abcdefghijklmnopqrstuvwxyz0123456789", Gui.FontSize) / 36f);
        int maxChars = Math.Max(20, (int)(area.Width / charW));

        if (Rl.CheckCollisionPointRec(Gui.Mouse, panel))
        {
            float wheel = Rl.GetMouseWheelMove();
            if (wheel != 0) _scrollUp = Math.Max(0, _scrollUp + (int)MathF.Round(wheel * 3f));
        }

        var wrapped = new List<(string Text, Color Color)>();   // wrapped[0] is the bottom-most line
        lock (Gate)
        {
            for (int i = Lines.Count - 1; i >= 0 && wrapped.Count < rows + _scrollUp; i--)
            {
                var parts = Wrap(Lines[i].Text, maxChars);
                for (int s = parts.Count - 1; s >= 0; s--) wrapped.Add((parts[s], Lines[i].Color));
            }
        }
        _scrollUp = Math.Clamp(_scrollUp, 0, Math.Max(0, wrapped.Count - rows));

        Rl.BeginScissorMode((int)(area.X * Gui.EffectiveScale), (int)(area.Y * Gui.EffectiveScale), (int)(area.Width * Gui.EffectiveScale), (int)(area.Height * Gui.EffectiveScale));
        for (int r = 0; r < rows; r++)
        {
            int idx = _scrollUp + r;
            if (idx >= wrapped.Count) break;
            Gui.Text(area.X, area.Y + area.Height - (r + 1) * LineH, wrapped[idx].Text, wrapped[idx].Color);
        }
        Rl.EndScissorMode();
        if (_scrollUp > 0) Gui.Text(area.X + area.Width - 150, area.Y, $"scrolled up {_scrollUp} (End)", Warn);
    }

    private static void DrawInput(Rectangle r)
    {
        Rl.DrawRectangleRec(r, new Color(26, 27, 31, 255));
        Rl.DrawRectangleLinesEx(r, 1f, new Color(60, 110, 190, 255));
        string prompt = IsRunning ? "input> " : "> ";
        float x0 = r.X + 6 + Rl.MeasureText(prompt, Gui.FontSize);
        Gui.Text(r.X + 6, r.Y + 4, prompt, Prompt);

        // keep the caret inside the box: show the part of the text that ends at the caret
        float room = r.X + r.Width - x0 - 8;
        int start = 0;
        while (start < _caret && Rl.MeasureText(_input.Substring(start, _caret - start), Gui.FontSize) > room) start++;
        Rl.BeginScissorMode((int)(x0 * Gui.EffectiveScale), (int)(r.Y * Gui.EffectiveScale), (int)(room * Gui.EffectiveScale), (int)(r.Height * Gui.EffectiveScale));
        Gui.Text(x0, r.Y + 4, _input.Substring(start), Normal);
        if (((int)(Rl.GetTime() * 2)) % 2 == 0)
        {
            float cx = x0 + Rl.MeasureText(_input.Substring(start, _caret - start), Gui.FontSize);
            Rl.DrawRectangle((int)cx, (int)r.Y + 3, 2, (int)r.Height - 6, Normal);
        }
        Rl.EndScissorMode();
    }

    private static List<string> Wrap(string text, int maxChars)
    {
        var parts = new List<string>();
        if (text.Length <= maxChars) { parts.Add(text); return parts; }
        for (int i = 0; i < text.Length; i += maxChars) parts.Add(text.Substring(i, Math.Min(maxChars, text.Length - i)));
        return parts;
    }

    // ---- keyboard ------------------------------------------------------------------------------------------

    private static void DrainChars() { while (Rl.GetCharPressed() != 0) { } }

    private static bool Pressed(KeyboardKey key) => Rl.IsKeyPressed(key) || Rl.IsKeyPressedRepeat(key);

    private static void HandleInput(World world)
    {
        bool ctrl = Rl.IsKeyDown(KeyboardKey.LeftControl) || Rl.IsKeyDown(KeyboardKey.RightControl);

        if (Rl.IsKeyPressed(KeyboardKey.Grave) || Rl.IsKeyPressed(KeyboardKey.F2) || Rl.IsKeyPressed(KeyboardKey.Escape)) { DrainChars(); Close(); return; }

        if (ctrl && Rl.IsKeyPressed(KeyboardKey.C)) { if (IsRunning) Kill(); else { _input = ""; _caret = 0; } DrainChars(); return; }
        if (ctrl && Rl.IsKeyPressed(KeyboardKey.L)) { lock (Gate) Lines.Clear(); DrainChars(); return; }
        if (ctrl && Rl.IsKeyPressed(KeyboardKey.V)) { Insert(Rl.GetClipboardText_() ?? ""); DrainChars(); return; }

        int c;
        while ((c = Rl.GetCharPressed()) != 0)
            if (!ctrl && c >= 32 && c != 127) Insert(char.ConvertFromUtf32(c));

        if (Pressed(KeyboardKey.Backspace) && _caret > 0) { _input = _input.Remove(_caret - 1, 1); _caret--; }
        if (Pressed(KeyboardKey.Delete) && _caret < _input.Length) _input = _input.Remove(_caret, 1);
        if (Pressed(KeyboardKey.Left) && _caret > 0) _caret--;
        if (Pressed(KeyboardKey.Right) && _caret < _input.Length) _caret++;
        if (Rl.IsKeyPressed(KeyboardKey.Home)) _caret = 0;
        if (Rl.IsKeyPressed(KeyboardKey.End)) { _caret = _input.Length; _scrollUp = 0; }
        if (Pressed(KeyboardKey.PageUp)) _scrollUp += 8;
        if (Pressed(KeyboardKey.PageDown)) _scrollUp = Math.Max(0, _scrollUp - 8);

        if (Pressed(KeyboardKey.Up) && History.Count > 0)
        {
            _historyAt = _historyAt < 0 ? History.Count - 1 : Math.Max(0, _historyAt - 1);
            _input = History[_historyAt]; _caret = _input.Length;
        }
        if (Pressed(KeyboardKey.Down) && _historyAt >= 0)
        {
            _historyAt++;
            if (_historyAt >= History.Count) { _historyAt = -1; _input = ""; } else _input = History[_historyAt];
            _caret = _input.Length;
        }

        if (Rl.IsKeyPressed(KeyboardKey.Enter) || Rl.IsKeyPressed(KeyboardKey.KpEnter)) Submit(world);
    }

    private static void Insert(string text)
    {
        text = text.Replace("\r", "").Replace("\n", " ");
        _input = _input.Insert(_caret, text);
        _caret += text.Length;
    }

    // ---- commands ------------------------------------------------------------------------------------------

    private static void Submit(World world)
    {
        string line = _input;
        _input = ""; _caret = 0; _historyAt = -1; _scrollUp = 0;

        if (IsRunning)   // a program is waiting for input
        {
            Print("> " + line, Dim);
            try { _proc!.StandardInput.WriteLine(line); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
            return;
        }

        Print(ShortCwd() + "> " + line, Prompt);
        line = line.Trim();
        if (line.Length == 0) return;
        if (History.Count == 0 || History[^1] != line) History.Add(line);

        if (line.StartsWith('!')) { RunShell(line.Substring(1).Trim()); return; }

        int space = line.IndexOf(' ');
        string cmd = (space < 0 ? line : line.Substring(0, space)).ToLowerInvariant();
        string rest = space < 0 ? "" : line.Substring(space + 1).Trim();

        switch (cmd)
        {
            case "help": case "?": Help(); break;
            case "clear": case "cls": lock (Gate) Lines.Clear(); break;
            case "exit": case "close": Close(); break;
            case "cd": case "chdir": ChangeDirectory(rest); break;
            case "scenes": ListScenes(); break;
            case "open": OpenScene(rest); break;
            case "play":
                if (world.PlayMode) Print("Already playing. Type stop to stop.", Warn); else { _playRequest = true; Print("Starting play mode.", Good); }
                break;
            case "stop":
                if (!world.PlayMode) Print("Not playing.", Warn); else { _playRequest = true; Print("Stopping play mode.", Good); }
                break;
            case "build": Build(world); break;
            case "new": NewScript(rest); break;
            default:
                if (Regex.IsMatch(line, @"^[A-Za-z]:$")) ChangeDirectory(line);   // "D:" changes the drive like in cmd
                else RunShell(line);
                break;
        }
    }

    private static void Help()
    {
        Print("Engine commands:", Good);
        Print("  help               this list", Normal);
        Print("  scenes             list the scenes (the open one is marked with *)", Normal);
        Print("  open <scene>       open a scene in the editor", Normal);
        Print("  play / stop        start or stop play mode", Normal);
        Print("  build              start a build with the current Build Settings", Normal);
        Print("  new <Name>         create a script in Scripts/ (then edit it with F4)", Normal);
        Print("  cd <folder>        change the folder commands run in;  clear / cls;  exit", Normal);
        Print("Everything else runs in the system shell, for example:  dotnet build   dir   git status", Normal);
        Print("  !<command> forces the shell.  Up/Down = history, Ctrl+C = stop a running command, Ctrl+L = clear, PageUp/PageDown = scroll.", Dim);
    }

    private static void ListScenes()
    {
        var names = SceneManager.AvailableScenes();
        if (names.Count == 0) { Print("No scenes yet.", Warn); return; }
        foreach (var n in names)
            Print((string.Equals(n, SceneManager.CurrentName, StringComparison.OrdinalIgnoreCase) ? "* " : "  ") + n, Normal);
    }

    private static void OpenScene(string name)
    {
        if (name.Length == 0) { Print("Usage: open <scene>", Warn); return; }
        var match = SceneManager.AvailableScenes().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) { Print($"No scene named \"{name}\". Type scenes to list them.", Bad); return; }
        _openRequest = match;
        Print($"Opening {match} ...", Good);
        Close();
    }

    private static void Build(World world)
    {
        if (world.PlayMode) { Print("Stop play mode before building.", Warn); return; }
        bool started = BuildPipeline.Start(false);
        Print(started ? "Build started. Progress shows in Build Settings (Ctrl+Shift+B)." : BuildPipeline.Status, started ? Good : Bad);
    }

    private static void NewScript(string name)
    {
        var clean = ScriptManager.SanitizeName(name);
        if (clean is null) { Print("Usage: new <Name> (letters, digits and underscores)", Warn); return; }
        try { Print("Created " + Path.GetRelativePath(GameProject.Root, ScriptManager.CreateScript(clean)) + ". Edit it with F4.", Good); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Print(ex.Message, Bad); }
    }

    private static void ChangeDirectory(string arg)
    {
        arg = arg.Trim();
        if (arg.StartsWith("/d ", StringComparison.OrdinalIgnoreCase)) arg = arg.Substring(3).Trim();
        arg = arg.Trim('"');
        if (arg.Length == 0) { Print(_cwd, Normal); return; }
        try
        {
            string target = Path.GetFullPath(Path.Combine(_cwd, arg));
            if (Directory.Exists(target)) _cwd = target;
            else Print("The system cannot find the path specified.", Bad);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Print("Not a valid folder: " + arg, Bad);
        }
    }

    private static string ShortCwd() => Path.GetFileName(_cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : _cwd;

    // ---- the system shell ----------------------------------------------------------------------------------

    private static Encoding ShellEncoding()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);   // what cmd writes
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        return Encoding.UTF8;
    }

    private static void RunShell(string command)
    {
        if (command.Length == 0) return;
        var encoding = ShellEncoding();
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = _cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = "/d /s /c \"" + command + "\"";
        }
        else
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("The shell did not start."); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Print("Could not start the shell: " + ex.Message, Bad);
            return;
        }

        _proc = process;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Print(e.Data, Normal); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Print(e.Data, new Color(240, 160, 130, 255)); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Task.Run(() =>
        {
            int code;
            try { process.WaitForExit(); code = process.ExitCode; }   // returns after the output has been read to its end
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { code = -1; }
            Print($"[exit code {code}]", code == 0 ? Good : Bad);
            _proc = null;
            try { process.Dispose(); } catch (InvalidOperationException) { }
        });
    }

    private static void Kill()
    {
        try { _proc?.Kill(entireProcessTree: true); Print("^C", Warn); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }

    private static void Print(string text, Color color)
    {
        text = Ansi.Replace(text, "").Replace("\t", "    ");
        int cr = text.LastIndexOf('\r');
        if (cr >= 0) text = text.Substring(cr + 1);   // progress lines overwrite themselves
        lock (Gate)
        {
            Lines.Add((text, color));
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, 500);
        }
    }
}