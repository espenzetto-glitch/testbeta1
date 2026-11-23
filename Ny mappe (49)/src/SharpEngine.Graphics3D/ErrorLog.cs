namespace SharpEngine.Graphics3D;

/// <summary>
/// One place for "something went wrong but the program should carry on": the error is remembered for the status bar,
/// printed once, and appended to Logs/errors.log in the project folder so it can be looked at (or sent) later.
/// </summary>
public static class ErrorLog
{
    private static readonly Dictionary<string, int> Seen = new();
    private static bool _installed;

    /// <summary>The most recent error, e.g. "update: NullReferenceException: ...".</summary>
    public static string? Last { get; private set; }

    public static int Count { get; private set; }

    public static string FilePath => Path.Combine(GameProject.Root, "Logs", "errors.log");

    /// <summary>Raised for every report (the editor shows it in its status bar).</summary>
    public static event Action<string>? Reported;

    public static Exception Unwrap(Exception ex)
    {
        while (true)
        {
            if (ex is System.Reflection.TargetInvocationException { InnerException: { } t }) ex = t;
            else if (ex is AggregateException { InnerException: { } a }) ex = a;
            else return ex;
        }
    }

    public static void Report(string where, Exception ex)
    {
        var inner = Unwrap(ex);
        Last = $"{where}: {inner.GetType().Name}: {inner.Message}";
        Count++;
        string key = Last + (inner.StackTrace?.Split('\n').FirstOrDefault() ?? "");
        Seen.TryGetValue(key, out int n);
        Seen[key] = n + 1;
        if (n == 0)   // the same error every frame is written once
        {
            Console.Error.WriteLine("[error] " + Last);
            DebugLog.Add(LogLevel.Error, "error", Last, inner.StackTrace);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n    {inner.GetType().Name}: {inner.Message}\n" +
                    string.Join("\n", (inner.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => "      " + l.Trim())) +
                    "\n" + new string('-', 70) + "\n");
            }
            catch (Exception) { /* logging must never be the thing that fails */ }
            try { Reported?.Invoke(Last); } catch (Exception) { }
        }
    }

    /// <summary>Runs <paramref name="action"/>; any exception is reported instead of thrown. Returns true when it ran without error.</summary>
    public static bool Try(string where, Action action)
    {
        try { action(); return true; }
        catch (Exception ex) when (IsRecoverable(ex)) { Report(where, ex); return false; }
    }

    /// <summary>Everything except running out of memory can be survived by skipping the current piece of work.</summary>
    public static bool IsRecoverable(Exception ex) => ex is not (OutOfMemoryException or AccessViolationException or StackOverflowException);

    /// <summary>Writes crashes that cannot be caught (other threads, fatal errors) to the log before the program ends.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Report(e.IsTerminating ? "FATAL" : "background", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { Report("background task", e.Exception); e.SetObserved(); };
    }

    public static void Reset() { Seen.Clear(); Last = null; Count = 0; }
}
