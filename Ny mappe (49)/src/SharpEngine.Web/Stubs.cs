// The browser player has no editor and no Roslyn: these tiny stand-ins keep the shared engine code compiling.
// Game components are compiled into this assembly ahead of time (see Build Settings > WebAssembly), so there is nothing to load.
using SharpEngine.Graphics3D;

namespace SharpEngine.Graphics3D.Editor
{
    /// <summary>The editor's keyboard focus flag. There is no editor in the browser, so the game always gets the keys.</summary>
    internal static class Gui
    {
        public static bool WantsKeyboard => false;
    }
}

namespace SharpEngine.Graphics3D.Scripting
{
    /// <summary>Scripts are part of the compiled game in the browser. Nothing is compiled while the game runs.</summary>
    internal static class ScriptManager
    {
        public static IReadOnlyList<Type> Types { get; } = Array.Empty<Type>();
        public static IReadOnlyList<Type> WorldTypes { get; } = Array.Empty<Type>();
        public static void ReportWorldScriptError(WorldScript s, Exception ex) { }
        public static void EnsureLoaded() { }
        public static bool IsScript(Component c) => false;
        public static void ReportRuntimeError(Component c, Exception ex) => ErrorLog.Report(c.GetType().Name, ex);
    }
}
