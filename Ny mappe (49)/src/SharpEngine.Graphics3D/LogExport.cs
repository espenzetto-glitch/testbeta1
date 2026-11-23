using System.Runtime.InteropServices;
using System.Text;
using SharpEngine.Graphics3D.Scripting;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Turns the debug log and the compile problems into one plain-text report that reads well in any editor
/// (Notepad, VS Code, a chat window): a summary, each compile problem with the offending source line, and the log with
/// stack traces indented under their entry. Used by the Copy and Save buttons of the code editor's Log panel.
/// </summary>
public static class LogExport
{
    /// <summary>The report as text. <paramref name="entries"/> defaults to the whole log.</summary>
    public static string Build(IReadOnlyList<LogEntry>? entries = null, string? title = null)
    {
        entries ??= DebugLog.Snapshot();
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append('\n');

        int errors = entries.Where(e => e.Level == LogLevel.Error).Sum(e => e.Count), warnings = entries.Where(e => e.Level == LogLevel.Warning).Sum(e => e.Count), infos = entries.Where(e => e.Level == LogLevel.Info).Sum(e => e.Count);
        Line(title ?? "SharpEngine log report");
        Line(new string('=', (title ?? "SharpEngine log report").Length));
        Line($"Created : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line($"Scene   : {SceneManager.CurrentName}");
        Line($"Project : {GameProject.Root}");
        Line($"System  : {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");
        Line($"Summary : {errors} error(s), {warnings} warning(s), {infos} message(s)");
        Line();

        var build = ScriptManager.Last;
        if (build is not null && build.Diagnostics.Count > 0)
        {
            Line("COMPILE PROBLEMS (latest build)");
            Line("-------------------------------");
            int n = 1;
            foreach (var d in build.Diagnostics.OrderByDescending(x => x.IsError))
            {
                Line($"{n++}. {(d.IsError ? "ERROR  " : "WARNING")} {d.Id}   {d.File}  line {d.Line}, column {d.Column}");
                Line("   " + d.Message);
                foreach (var l in SourceContext(d)) Line("   " + l);
                Line();
            }
        }
        else if (build is not null) { Line($"Latest build: OK ({build.Files} file(s), {build.Milliseconds:0} ms)"); Line(); }

        if (ScriptManager.RuntimeError is { } rt)
        {
            Line("RUNTIME ERROR");
            Line("-------------");
            Line(rt);
            Line();
        }

        Line("LOG (oldest first)");
        Line("------------------");
        if (entries.Count == 0) Line("(empty)");
        foreach (var e in entries)
        {
            string level = e.Level switch { LogLevel.Error => "ERROR  ", LogLevel.Warning => "WARNING", _ => "info   " };
            string repeat = e.Count > 1 ? $"   (x{e.Count})" : "";
            var text = e.Text.Replace("\r", "").Split('\n');
            Line($"[{e.Time:HH:mm:ss}] {level} {e.Source}: {text[0]}{repeat}");
            foreach (var extra in text.Skip(1)) Line("                      " + extra);
            if (!string.IsNullOrWhiteSpace(e.Detail))
                foreach (var d in e.Detail.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Line("      " + d.TrimStart());
        }
        return sb.ToString();
    }

    /// <summary>The report with the line endings of this operating system (so Windows Notepad shows proper lines).</summary>
    public static string ForClipboardOrFile(IReadOnlyList<LogEntry>? entries = null, string? title = null)
    {
        string text = Build(entries, title);
        return Environment.NewLine == "\n" ? text : text.Replace("\n", Environment.NewLine);
    }

    /// <summary>
    /// Writes the report to Logs/log-yyyyMMdd-HHmmss.txt and Logs/latest.txt (UTF-8 with BOM, so every Windows editor reads it correctly).
    /// Returns the path of the timestamped file.
    /// </summary>
    public static string Save(IReadOnlyList<LogEntry>? entries = null)
    {
        string dir = Path.Combine(GameProject.Root, "Logs");
        Directory.CreateDirectory(dir);
        string text = ForClipboardOrFile(entries);
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        string path = Path.Combine(dir, $"log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, text, enc);
        File.WriteAllText(Path.Combine(dir, "latest.txt"), text, enc);
        return path;
    }

    private static long _lastAuto;
    private static int _autoBusy;

    /// <summary>
    /// Keeps Logs/latest.txt up to date after an error (at most once every 2 seconds, never throws), so the file is already there
    /// when you want to send the bug to somebody.
    /// </summary>
    public static void SaveLatestSoon()
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastAuto) < 2000 || Interlocked.Exchange(ref _autoBusy, 1) == 1) return;
        try
        {
            Interlocked.Exchange(ref _lastAuto, now);
            string dir = Path.Combine(GameProject.Root, "Logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "latest.txt"), ForClipboardOrFile(), new UTF8Encoding(true));
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { Console.Error.WriteLine("[log] could not update Logs/latest.txt: " + ex.Message); }
        finally { Interlocked.Exchange(ref _autoBusy, 0); }
    }

    /// <summary>Copies the whole report to the clipboard and saves it. Returns the saved path.</summary>
    public static string ExportAll()
    {
        string path = Save();
        try { Raylib_cs.Raylib.SetClipboardText(ForClipboardOrFile()); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { }
        return path;
    }

    private static IEnumerable<string> SourceContext(ScriptDiagnostic d)
    {
        string? file = null;
        try
        {
            var candidate = Path.Combine(GameProject.Root, "Scripts", d.File);
            if (File.Exists(candidate)) file = candidate;
            else if (Directory.Exists(Path.Combine(GameProject.Root, "Scripts")))
                file = Directory.EnumerateFiles(Path.Combine(GameProject.Root, "Scripts"), Path.GetFileName(d.File), SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        if (file is null || d.Line < 1) yield break;

        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        if (d.Line > lines.Length) yield break;

        string src = lines[d.Line - 1].Replace("\t", "    ");
        string prefix = $"{d.Line,5} | ";
        yield return prefix + src;
        yield return new string(' ', prefix.Length + Math.Max(0, d.Column - 1)) + "^";
    }
}
