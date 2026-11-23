using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpEngine.Graphics3D.Editor;

namespace SharpEngine.Graphics3D.Scripting;

/// <summary>One compiler message (error or warning) of a script.</summary>
public sealed record ScriptDiagnostic(string File, int Line, int Column, string Id, string Message, bool IsError)
{
    public override string ToString() => $"{File}({Line},{Column}): {(IsError ? "error" : "warning")} {Id}: {Message}";
}

/// <summary>The outcome of compiling the Scripts folder. Nothing changes in the running program until <see cref="ScriptManager.Apply"/>.</summary>
public sealed class ScriptBuild
{
    public bool Success { get; internal set; }
    public List<ScriptDiagnostic> Diagnostics { get; } = new();
    public IReadOnlyList<string> TypeNames { get; internal set; } = Array.Empty<string>();
    public IReadOnlyList<string> WorldTypeNames { get; internal set; } = Array.Empty<string>();
    public double Milliseconds { get; internal set; }
    public int Files { get; internal set; }
    internal byte[]? Image;

    public IEnumerable<ScriptDiagnostic> Errors => Diagnostics.Where(d => d.IsError);
}

/// <summary>
/// C# scripts: every .cs file in the project's Scripts folder is compiled (with Roslyn) into one in-memory assembly,
/// and each public class that derives from <see cref="Component"/> becomes a component you can add to an object or pin to a scene.
/// Public properties of type float, int, bool, string, Vector3 and Color show up in the Inspector and are saved with the scene.
/// The player compiles the same files when the game starts (the build copies the Scripts folder next to the program).
/// </summary>
public static class ScriptManager
{
    public const string FolderName = "Scripts";

    // names usable in every script without a using line
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Numerics;
        global using SharpEngine.Graphics3D;
        global using SharpEngine.Graphics3D.Editor;
        global using Color = Raylib_cs.Color;
        global using KeyboardKey = Raylib_cs.KeyboardKey;
        global using MouseButton = Raylib_cs.MouseButton;
        global using Rl = Raylib_cs.Raylib;
        """;

    internal const string UnionPolyfill = """
        #nullable enable
        namespace System.Runtime.CompilerServices
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
            internal sealed class UnionAttribute : Attribute { }

            internal interface IUnion { object? Value { get; } }
        }
        """;

    private sealed class ScriptContext : AssemblyLoadContext
    {
        public ScriptContext() : base("SharpEngine.Scripts", isCollectible: true) { }
    }

    /// <summary>Compile scripts with C# preview features (default). Set false to stay on the latest released C# version.</summary>
    public static bool PreviewLanguage { get; set; } = true;

    private static ScriptContext? _context;
    private static List<Type> _types = new();
    private static List<Type> _worldTypes = new();
    private static List<Type> _editorTypes = new();
    private static List<MetadataReference>? _references;
    private static bool _loaded;
    private static int _counter;
    private static DateTime _builtStamp;
    private static readonly HashSet<Component> Reported = new();

    public static string Directory => Path.Combine(GameProject.Root, FolderName);

    /// <summary>The component classes found in the compiled scripts.</summary>
    public static IReadOnlyList<Type> Types
    {
        get { EnsureLoaded(); return _types; }
    }

    /// <summary>The <see cref="WorldScript"/> classes found in the compiled scripts.</summary>
    public static IReadOnlyList<Type> WorldTypes
    {
        get { EnsureLoaded(); return _worldTypes; }
    }

    /// <summary>The <see cref="Editor.EditorScript"/> classes found in the compiled scripts.</summary>
    public static IReadOnlyList<Type> EditorTypes
    {
        get { EnsureLoaded(); return _editorTypes; }
    }

    /// <summary>The last compile (startup or the editor), for the code window.</summary>
    public static ScriptBuild? Last { get; private set; }

    /// <summary>Most recent exception thrown by a script while the game ran, shown in the editor.</summary>
    public static string? RuntimeError { get; private set; }

    /// <summary>Raised after new scripts replaced the old ones.</summary>
    public static event Action? Applied;

    public static bool IsScript(Type t) => t.Assembly.IsDynamic == false && AssemblyLoadContext.GetLoadContext(t.Assembly) is ScriptContext;

    public static bool IsScript(object o) => IsScript(o.GetType());

    // ---- files ------------------------------------------------------------------------------

    public static IReadOnlyList<string> Files()
    {
        if (!System.IO.Directory.Exists(Directory)) return Array.Empty<string>();
        return System.IO.Directory.GetFiles(Directory, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"Spin2" is fine, "2spin" and "my script" are not. Returns null when nothing usable is left.</summary>
    public static string? SanitizeName(string name)
    {
        var chars = name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
        var clean = new string(chars);
        if (clean.Length == 0) return null;
        if (char.IsDigit(clean[0])) clean = "_" + clean;
        return clean;
    }

    public static string TemplateFor(string className) => $$"""
        // A component. Add it to any object, or pin it to a scene, from the Inspector.
        // Public properties of type float, int, bool, string, Vector3 and Color show up in the Inspector.
        public class {{className}} : Component
        {
            public float Speed { get; set; } = 1f;

            public override void Start()
            {
            }

            public override void Update(float dt)
            {
                // Transform is the object's position, rotation and scale; World is the scene.
                Transform.Rotate(new Vector3(0f, 45f * Speed * dt, 0f));
            }
        }
        """;

    public static string WorldTemplateFor(string className) => $$"""
        // World code: runs for the whole game while it plays, across scene changes, merges and streaming.
        // Press Ctrl+Space (or type a dot) for suggestions. The Objects panel on the right lists every scene, object and component.
        public class {{className}} : WorldScript
        {
            public override void Start()
            {
            }

            // A scene was opened (also once for the first scene when play starts).
            public override void OnSceneLoaded(string scene)
            {
            }

            // Scenes were merged into, or split from, the open scene.
            public override void OnMergeChanged(IReadOnlyCollection<string> merged)
            {
            }

            public override void Update(float dt)
            {
            }
        }
        """;

    /// <summary>Starter code for one scene: a WorldScript whose callbacks only react to <paramref name="sceneName"/>.</summary>
    public static string SceneTemplateFor(string className, string sceneName) => $$"""
        // Code for the scene "{{sceneName}}". It is a world script, so it lives across scene changes, but every
        // callback below only fires while this scene is the open one. Press Ctrl+Space for suggestions.
        public class {{className}} : WorldScript
        {
            private const string SceneName = "{{sceneName.Replace("\\", "\\\\").Replace("\"", "\\\"")}}";

            private bool Active => string.Equals(CurrentScene, SceneName, StringComparison.OrdinalIgnoreCase);

            // The scene was opened (also once when play starts, if it is the first scene).
            public override void OnSceneLoaded(string scene)
            {
                if (!string.Equals(scene, SceneName, StringComparison.OrdinalIgnoreCase)) return;
            }

            public override void Update(float dt)
            {
                if (!Active) return;
            }
        }
        """;

    public const string WorldFolderName = "World";
    public const string EditorFolderName = "Editor";

    public static string EditorTemplateFor(string className) => $$"""
        // Editor tool: adds buttons, menu commands, panels and shortcuts to the admin editor. Compile with Ctrl+S, no restart needed.
        // Everything registered here disappears and is registered again every time the scripts are recompiled.
        public class {{className}} : EditorScript
        {
            private int _count = 5;

            public override void OnRegister(EditorApi editor)
            {
                // Tools menu entry (toolbar > Tools)
                editor.AddCommand("Say hello", world => editor.Say("Hello from " + CurrentScene));

                // a button in the toolbar
                editor.AddToolbarButton("Add Cube", world =>
                {
                    var go = world.CreatePrimitive("Cube", Color.White, new Vector3(0, 1, 0));
                    editor.Select(go);
                });

                // a section at the bottom of the Inspector, drawn every frame
                editor.AddPanel("My tool", ui =>
                {
                    ui.Slider("Count", ref _count, 1, 50);
                    if (ui.Button($"Spawn {_count} cubes"))
                        for (int i = 0; i < _count; i++)
                            editor.World.CreatePrimitive("Cube", Color.White, new Vector3(i * 2f, 1, 0));
                });

                // a section for selected objects that have a MeshRenderer
                editor.AddObjectInspector<MeshRenderer>("Mesh tools", (ui, go) =>
                {
                    ui.Dim(go.Name);
                    if (ui.Button("Reset scale")) go.Transform.Scale = Vector3.One;
                });

                // Ctrl+F9
                editor.AddShortcut(KeyboardKey.F9, world => editor.Say("Shortcut pressed"), ctrl: true);
            }

            public override void OnSelectionChanged(GameObject? selected)
            {
            }
        }
        """;

    public static string CreateEditorScript(string name)
    {
        var clean = SanitizeName(name) ?? throw new ArgumentException("Use letters, digits and underscores.", nameof(name));
        ProtectProject();
        string folder = Path.Combine(Directory, EditorFolderName);
        System.IO.Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, clean + ".cs");
        if (File.Exists(path)) throw new IOException($"{clean}.cs already exists.");
        File.WriteAllText(path, EditorTemplateFor(clean).Replace("\r\n", "\n"));
        return path;
    }

    /// <summary>Scripts/World/WorldCode.cs, the file the World Map's "World Code" button opens (created on first use).</summary>
    public static string WorldCodePath => Path.Combine(Directory, WorldFolderName, "WorldCode.cs");

    /// <summary>
    /// dotnet compiles every .cs file next to a .csproj, so Scripts/*.cs would end up inside the game itself (and fail:
    /// the scripts rely on the editor's implicit usings). Makes sure the game's project file leaves the folder out.
    /// Returns true when the project file was changed.
    /// </summary>
    public static bool ProtectProject(string? csprojPath = null)
    {
        try
        {
            if ((csprojPath ?? GameProject.CsprojPath) is not { } csproj) return false;
            var doc = System.Xml.Linq.XDocument.Load(csproj, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root is null) return false;
            bool has = root.Descendants().Any(e => e.Name.LocalName == "Compile" && ((string?)e.Attribute("Remove") ?? "").Replace('\\', '/').StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase));
            if (has) return false;
            var group = new System.Xml.Linq.XElement("ItemGroup",
                new System.Xml.Linq.XComment(" Scripts/*.cs are compiled by the in-game code editor, not by dotnet build "),
                new System.Xml.Linq.XElement("Compile", new System.Xml.Linq.XAttribute("Remove", "Scripts/**")),
                new System.Xml.Linq.XElement("None", new System.Xml.Linq.XAttribute("Remove", "Scripts/**")));
            root.Add(new System.Xml.Linq.XText("\n  "), group, new System.Xml.Linq.XText("\n"));
            doc.Save(csproj);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ErrorLog.Report("updating the project file", ex);
            return false;
        }
    }

    /// <summary>Creates Scripts/Name.cs with a starter component. Returns the file path.</summary>
    public static string CreateScript(string name, bool world = false, string? content = null)
    {
        var clean = SanitizeName(name) ?? throw new ArgumentException("Use letters, digits and underscores.", nameof(name));
        ProtectProject();
        string folder = world ? Path.Combine(Directory, WorldFolderName) : Directory;
        System.IO.Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, clean + ".cs");
        if (File.Exists(path)) throw new IOException($"{clean}.cs already exists.");
        File.WriteAllText(path, (content ?? (world ? WorldTemplateFor(clean) : TemplateFor(clean))).Replace("\r\n", "\n"));
        return path;
    }

    /// <summary>Creates Scripts/Name.cs holding a MeshGenerator: the code of <paramref name="mesh"/> when given, otherwise a starter example.</summary>
    public static string CreateMeshScript(string name, MeshData? mesh = null, string? objectName = null)
    {
        var clean = SanitizeName(name) ?? throw new ArgumentException("Use letters, digits and underscores.", nameof(name));
        return CreateScript(clean, content: mesh is null && objectName is null ? MeshCode.Template(clean) : MeshCode.FromMesh(mesh, clean, objectName ?? clean));
    }

    /// <summary>The script file that declares <c>class className</c>, or null.</summary>
    public static string? FindScriptFile(string className)
    {
        var rx = new System.Text.RegularExpressions.Regex(@"\bclass\s+" + System.Text.RegularExpressions.Regex.Escape(className) + @"\b");
        foreach (var f in Files())
        {
            try { if (rx.IsMatch(File.ReadAllText(f))) return f; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>The world code file, created with the starter template when it does not exist yet.</summary>
    public static string EnsureWorldCode()
    {
        if (!File.Exists(WorldCodePath)) CreateScript("WorldCode", world: true);
        return WorldCodePath;
    }

    /// <summary>Scripts/World/Scene_Name.cs, the code file of one scene.</summary>
    public static string SceneCodePath(string sceneName) =>
        Path.Combine(Directory, WorldFolderName, SceneClassName(sceneName) + ".cs");

    private static string SceneClassName(string sceneName) => "Scene_" + (SanitizeName(sceneName) ?? "Unnamed").TrimStart('_');

    /// <summary>The code file of a scene, created from the starter template when it does not exist yet.</summary>
    public static string EnsureSceneCode(string sceneName)
    {
        var path = SceneCodePath(sceneName);
        if (!File.Exists(path))
            CreateScript(SceneClassName(sceneName), world: true, content: SceneTemplateFor(SceneClassName(sceneName), sceneName));
        return path;
    }

    public static bool HasSceneCode(string sceneName) => File.Exists(SceneCodePath(sceneName));

    // ---- compiling --------------------------------------------------------------------------

    private static List<MetadataReference> References()
    {
        if (_references is not null) return _references;
        var list = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in tpa)
        {
            var name = Path.GetFileName(path);
            if (!seen.Add(name)) continue;
            try { list.Add(MetadataReference.CreateFromFile(path)); }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException) { }
        }
        // assemblies loaded from somewhere else (tests, plugins) that the scripts may need
        foreach (var asm in new[] { typeof(Component).Assembly, typeof(Raylib_cs.Color).Assembly })
            if (!asm.IsDynamic && asm.Location.Length > 0 && seen.Add(Path.GetFileName(asm.Location)))
                list.Add(MetadataReference.CreateFromFile(asm.Location));
        return _references = list;
    }

    /// <summary>Compiles every script in the Scripts folder. Does not change anything that is running; call <see cref="Apply"/> to use it.</summary>
    public static ScriptBuild Build()
    {
        try { return BuildCore(); }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            // the compiler itself failed (for example a missing file): report it as a normal compile error
            ErrorLog.Report("script compiler", ex);
            var failed = new ScriptBuild();
            failed.Diagnostics.Add(new ScriptDiagnostic("(compiler)", 1, 1, "INTERNAL", ErrorLog.Unwrap(ex).GetType().Name + ": " + ErrorLog.Unwrap(ex).Message, true));
            return Finish(failed, System.Diagnostics.Stopwatch.StartNew());
        }
    }

    private static ScriptBuild BuildCore()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var build = new ScriptBuild();
        var files = Files();
        build.Files = files.Count;
        if (files.Count == 0)
        {
            build.Success = true;
            build.Image = null;
            build.Milliseconds = watch.Elapsed.TotalMilliseconds;
            return build;
        }

        // Preview = every C# feature the compiler has, including the unfinished next version (C# 15 preview with a new enough Roslyn)
        var parse = new CSharpParseOptions(PreviewLanguage ? LanguageVersion.Preview : LanguageVersion.Latest);
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(GlobalUsings, parse, "GlobalUsings.cs") };
        // C# 15 `union` needs two runtime types; older runtimes lack them, so scripts get their own copy (as Microsoft's blog describes)
        if (Type.GetType("System.Runtime.CompilerServices.UnionAttribute") is null)
            trees.Add(CSharpSyntaxTree.ParseText(UnionPolyfill, parse, "UnionPolyfill.cs"));
        foreach (var f in files)
        {
            string text;
            try { text = File.ReadAllText(f); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                build.Diagnostics.Add(new ScriptDiagnostic(Path.GetFileName(f), 1, 1, "IO", ex.Message, true));
                continue;
            }
            trees.Add(CSharpSyntaxTree.ParseText(text, parse, f));
        }
        if (build.Errors.Any()) return Finish(build, watch);

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Disable, allowUnsafe: true);
        var compilation = CSharpCompilation.Create("GameScripts_" + (++_counter), trees, References(), options);

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        foreach (var d in emit.Diagnostics)
        {
            if (d.Severity < DiagnosticSeverity.Warning || d.Location.SourceTree?.FilePath == "GlobalUsings.cs") continue;
            var pos = d.Location.GetLineSpan().StartLinePosition;
            var file = d.Location.SourceTree?.FilePath is { Length: > 0 } fp ? Path.GetFileName(fp) : "";
            build.Diagnostics.Add(new ScriptDiagnostic(file, pos.Line + 1, pos.Character + 1, d.Id, d.GetMessage(), d.Severity == DiagnosticSeverity.Error));
        }

        if (!emit.Success || build.Errors.Any()) return Finish(build, watch);

        build.Image = ms.ToArray();
        // find the component classes by loading the image into a throw-away context
        var probe = new ScriptContext();
        try
        {
            var asm = probe.LoadFromStream(new MemoryStream(build.Image));
            build.TypeNames = ComponentTypes(asm).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            build.WorldTypeNames = WorldScriptTypes(asm).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            build.Success = true;
        }
        finally { probe.Unload(); }
        return Finish(build, watch);
    }

    private static ScriptBuild Finish(ScriptBuild build, System.Diagnostics.Stopwatch watch)
    {
        build.Milliseconds = watch.Elapsed.TotalMilliseconds;
        Last = build;
        LogBuild(build);
        return build;
    }

    private static IEnumerable<Type> ComponentTypes(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        return types.Where(t => t.IsPublic && !t.IsAbstract && typeof(Component).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null);
    }

    private static IEnumerable<Type> WorldScriptTypes(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        return types.Where(t => t.IsPublic && !t.IsAbstract && typeof(WorldScript).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null);
    }

    private static IEnumerable<Type> EditorScriptTypes(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        return types.Where(t => t.IsPublic && !t.IsAbstract && typeof(Editor.EditorScript).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null);
    }

    /// <summary>
    /// Names of components in <paramref name="used"/> that come from scripts but are missing from the new build
    /// (they would be lost if the new scripts were applied).
    /// </summary>
    public static IReadOnlyList<string> MissingFrom(ScriptBuild build, IEnumerable<string> used) =>
        used.Where(n => _types.Any(t => t.Name == n) && !build.TypeNames.Contains(n)).Distinct().ToList();

    /// <summary>Makes a successful build the running scripts. Components made from the old scripts must be recreated afterwards (the editor reloads the scene).</summary>
    public static void Apply(ScriptBuild build)
    {
        if (!build.Success) throw new InvalidOperationException("Cannot apply scripts that did not compile.");
        var old = _context;
        var oldTypes = _types;
        _context = null;
        _types = new List<Type>();
        _worldTypes = new List<Type>();
        _editorTypes = new List<Type>();
        if (build.Image is not null)
        {
            _context = new ScriptContext();
            var asm = _context.LoadFromStream(new MemoryStream(build.Image));
            _types = ComponentTypes(asm).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            _worldTypes = WorldScriptTypes(asm).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            _editorTypes = EditorScriptTypes(asm).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }
        ComponentRegistry.ScriptsChanged(oldTypes);
        old?.Unload();
        _builtStamp = LatestWrite();
        _loaded = true;
        RuntimeError = null;
        Reported.Clear();
        Applied?.Invoke();
    }

    /// <summary>Compiles and applies the scripts the first time anything needs the component list (game start).</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;   // set first: compiling asks the registry, which asks us
        try
        {
            if (Files().Count == 0) return;
            var build = Build();
            if (build.Success) Apply(build);
            else foreach (var e in build.Errors) Console.Error.WriteLine("[script] " + e);
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            ErrorLog.Report("loading scripts", ex);
        }
        _builtStamp = LatestWrite();
    }

    private static DateTime LatestWrite() =>
        Files().Select(f => File.GetLastWriteTimeUtc(f)).DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>True when a script file changed since the scripts were last compiled.</summary>
    public static bool IsStale => _loaded && LatestWrite() != _builtStamp;

    /// <summary>Forget everything (tests, a different project folder).</summary>
    public static void Reset()
    {
        var old = _context; var oldTypes = _types;
        _context = null; _types = new List<Type>(); _worldTypes = new List<Type>(); _loaded = false; Last = null; RuntimeError = null; Reported.Clear();
        ComponentRegistry.ScriptsChanged(oldTypes);
        old?.Unload();
    }

    public static void ReportRuntimeError(Component c, Exception ex)
    {
        var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
        RuntimeError = $"{c.GetType().Name} on '{c.GameObject?.Name}': {inner.GetType().Name}: {inner.Message}";
        if (Reported.Add(c)) ErrorLog.Report("script " + c.GetType().Name, inner);
    }

    public static void ReportWorldScriptError(WorldScript s, Exception ex)
    {
        var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
        RuntimeError = $"World script {s.GetType().Name}: {inner.GetType().Name}: {inner.Message}";
        DebugLog.Add(LogLevel.Error, "world script", RuntimeError, inner.StackTrace);
    }

    private static void LogBuild(ScriptBuild b)
    {
        foreach (var d in b.Diagnostics.OrderByDescending(x => x.IsError))
            DebugLog.Add(d.IsError ? LogLevel.Error : LogLevel.Warning, "compiler", $"{d.File}({d.Line},{d.Column})  {d.Id}: {d.Message}");
        if (b.Success)
            DebugLog.Add(LogLevel.Info, "compiler", $"Compiled {b.Files} file(s) in {b.Milliseconds:0} ms: {(b.TypeNames.Count > 0 ? string.Join(", ", b.TypeNames) : "no components")}");
        else
            DebugLog.Add(LogLevel.Error, "compiler", $"Compile failed: {b.Errors.Count()} error(s). The old scripts keep running.");
    }

    public static void ClearRuntimeError() => RuntimeError = null;
}
