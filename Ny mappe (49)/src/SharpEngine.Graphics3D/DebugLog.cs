using System.Text;

namespace SharpEngine.Graphics3D;

public enum LogLevel { Info, Warning, Error }

/// <summary>One line of the debug log. Identical lines that follow each other are merged and counted.</summary>
public sealed class LogEntry
{
    public DateTime Time { get; init; }
    public LogLevel Level { get; init; }
    public string Source { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>Stack trace or other extra lines (shown when the entry is clicked).</summary>
    public string? Detail { get; init; }
    public int Count { get; internal set; } = 1;
}

/// <summary>
/// The debug log shown in the code editor ("Log" button): everything your scripts print with <see cref="Debug"/> or Console.WriteLine,
/// compile results, and every error the engine caught. Thread safe; keeps the newest <see cref="MaxEntries"/> lines.
/// </summary>
public static class DebugLog
{
    public const int MaxEntries = 3000;
    private static readonly List<LogEntry> Items = new();
    private static readonly object Gate = new();

    /// <summary>Increases with every change, so a window can tell when to redraw / scroll.</summary>
    public static int Version { get; private set; }

    public static int Infos { get; private set; }
    public static int Warnings { get; private set; }
    public static int Errors { get; private set; }

    public static void Add(LogLevel level, string source, string text, string? detail = null)
    {
        text ??= "";
        lock (Gate)
        {
            if (Items.Count > 0 && Items[^1] is { } last && last.Level == level && last.Source == source && last.Text == text && last.Detail == detail)
            {
                last.Count++;
            }
            else
            {
                Items.Add(new LogEntry { Time = DateTime.Now, Level = level, Source = source, Text = text, Detail = detail });
                if (Items.Count > MaxEntries) Items.RemoveRange(0, Items.Count - MaxEntries);
            }
            if (level == LogLevel.Error) Errors++; else if (level == LogLevel.Warning) Warnings++; else Infos++;
            Version++;
        }
        if (level == LogLevel.Error) LogExport.SaveLatestSoon();
    }

    /// <summary>A copy of the entries, oldest first.</summary>
    public static List<LogEntry> Snapshot() { lock (Gate) return new List<LogEntry>(Items); }

    public static int Count { get { lock (Gate) return Items.Count; } }

    public static void Clear()
    {
        lock (Gate) { Items.Clear(); Infos = Warnings = Errors = 0; Version++; }
    }

    // ---- Console capture ----------------------------------------------------------------------

    private static bool _captured;

    /// <summary>Copies everything written to Console.Out / Console.Error into the log (the text still reaches the real console).</summary>
    public static void CaptureConsole()
    {
        if (_captured) return;
        _captured = true;
        Console.SetOut(new Tee(Console.Out, LogLevel.Info));
        Console.SetError(new Tee(Console.Error, LogLevel.Warning));
    }

    private sealed class Tee : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly LogLevel _level;
        private readonly StringBuilder _line = new();
        public Tee(TextWriter inner, LogLevel level) { _inner = inner; _level = level; }
        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            lock (_line)
            {
                if (value == '\n') Flush(false);
                else if (value != '\r') _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            _inner.Write(value);
            if (value is null) return;
            lock (_line)
                foreach (char c in value)
                {
                    if (c == '\n') Flush(false);
                    else if (c != '\r') _line.Append(c);
                }
        }

        public override void Flush() => _inner.Flush();

        private void Flush(bool _)
        {
            string text = _line.ToString();
            _line.Clear();
            if (text.Length == 0 || text.StartsWith("[error] ", StringComparison.Ordinal)) return; // ErrorLog adds those itself, with the stack trace
            LogLevel level = _level;
            string source = _level == LogLevel.Info ? "console" : "stderr";
            if (text.StartsWith('[') && text.IndexOf(']') is > 1 and var close) { source = text[1..close]; }
            Add(level, source, text);
        }
    }
}

/// <summary>Unity-style logging for scripts: <c>Debug.Log("hello")</c>, <c>Debug.LogWarning(x)</c>, <c>Debug.LogError(x)</c>.</summary>
public static class Debug
{
    public static void Log(object? message) => DebugLog.Add(LogLevel.Info, "script", Text(message));
    public static void Log(string tag, object? message) => DebugLog.Add(LogLevel.Info, tag, Text(message));
    public static void LogWarning(object? message) => DebugLog.Add(LogLevel.Warning, "script", Text(message));
    public static void LogError(object? message) => DebugLog.Add(LogLevel.Error, "script", Text(message));
    public static void LogException(Exception ex)
    {
        var inner = ErrorLog.Unwrap(ex);
        DebugLog.Add(LogLevel.Error, "script", $"{inner.GetType().Name}: {inner.Message}", inner.StackTrace);
    }

    /// <summary>Logs an error when <paramref name="condition"/> is false.</summary>
    public static void Assert(bool condition, string message = "Assertion failed")
    {
        if (!condition) DebugLog.Add(LogLevel.Error, "assert", message);
    }

    private static string Text(object? o) => o?.ToString() ?? "null";
}
