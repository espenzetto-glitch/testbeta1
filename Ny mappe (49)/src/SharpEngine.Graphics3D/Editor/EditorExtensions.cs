using System.Numerics;
using Raylib_cs;
using SharpEngine.Graphics3D.Scripting;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

// ====================================================================================================================
//  Dynamic editor: everything the admin editor shows beyond its fixed layout is an EditorItem registered at run time.
//  The engine's own tools (BuiltInTools) and your scripts (Scripts/Editor/*.cs, class X : EditorScript) use the same API,
//  and a script reload re-registers all of it without restarting the editor.
// ====================================================================================================================

/// <summary>
/// One thing a tool adds to the editor. A closed set: <see cref="ToolbarButton"/>, <see cref="MenuCommand"/>, <see cref="PanelItem"/>,
/// <see cref="ObjectInspector"/> or <see cref="ShortcutItem"/>. In C# 15 this is exactly
/// <c>union EditorItem(ToolbarButton, MenuCommand, PanelItem, ObjectInspector, ShortcutItem)</c>; the engine uses a closed record
/// hierarchy so it builds on every SDK, and the editor handles every case with one exhaustive <c>switch</c>.
/// </summary>
public abstract record EditorItem
{
    private protected EditorItem() { }

    /// <summary>The tool that registered it (class name), for the error log.</summary>
    public string Owner { get; internal set; } = "";

    /// <summary>Set when its code threw; it is skipped until the scripts are reloaded.</summary>
    public bool Faulted { get; internal set; }
}

/// <summary>A button in the top toolbar.</summary>
public sealed record ToolbarButton(string Label, Action<World> Run) : EditorItem;

/// <summary>An entry in the toolbar's Tools menu.</summary>
public sealed record MenuCommand(string Name, Action<World> Run) : EditorItem;

/// <summary>A collapsible section at the bottom of the Inspector, always visible. <paramref name="Draw"/> runs every frame.</summary>
public sealed record PanelItem(string Title, Action<PanelBuilder> Draw) : EditorItem
{
    public bool Collapsed { get; set; }
}

/// <summary>
/// A section in the Inspector for the selected object. <paramref name="Component"/> limits it to objects that have that component
/// (null = every object).
/// </summary>
public sealed record ObjectInspector(Type? Component, string Title, Action<PanelBuilder, GameObject> Draw) : EditorItem
{
    public bool Collapsed { get; set; }
}

/// <summary>A keyboard shortcut (works while the editor is visible and no text box has focus).</summary>
public sealed record ShortcutItem(KeyboardKey Key, bool Ctrl, bool Shift, string Name, Action<World> Run) : EditorItem;

/// <summary>
/// Base class of editor tools. Write them in Scripts/Editor (Code editor: Create Editor Script). One instance is created when the
/// editor starts and again every time the scripts are recompiled; register what you need in <see cref="OnRegister"/>.
/// </summary>
public abstract class EditorScript
{
    /// <summary>The scene being edited.</summary>
    public World World { get; internal set; } = null!;

    /// <summary>Turn the tool off (no more OnUpdate calls). A tool that throws is switched off automatically.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Name of the open scene.</summary>
    public string CurrentScene => SceneManager.CurrentName;

    /// <summary>Called once after the tool was created. Add buttons, commands, panels and shortcuts to <paramref name="editor"/>.</summary>
    public virtual void OnRegister(EditorApi editor) { }

    /// <summary>Every frame while the editor runs (edit mode and play mode).</summary>
    public virtual void OnUpdate(float dt) { }

    /// <summary>The selected object changed (null = nothing selected).</summary>
    public virtual void OnSelectionChanged(GameObject? selected) { }

    /// <summary>A scene was opened.</summary>
    public virtual void OnSceneLoaded(string scene) { }
}

/// <summary>What an <see cref="EditorScript"/> uses to add things to the editor.</summary>
public sealed class EditorApi
{
    private readonly string _owner;
    internal EditorApi(string owner, World world) { _owner = owner; World = world; }

    public World World { get; }

    /// <summary>The selected object (null when none or a folder is selected).</summary>
    public GameObject? Selected => AdminEditor.Selected;

    /// <summary>The selected hierarchy folder (like "Town/Houses") or null.</summary>
    public string? SelectedFolder => AdminEditor.SelectedFolder;

    public void Select(GameObject? go) => AdminEditor.Selected = go;

    /// <summary>Flies the editor view to the selection (same as pressing F).</summary>
    public void FocusSelection() => AdminEditor.FocusSelectedPublic(World);

    /// <summary>Shows a message in the editor's status bar.</summary>
    public void Say(string message) => AdminEditor.SayPublic(message);

    private T Add<T>(T item) where T : EditorItem
    {
        item.Owner = _owner;
        EditorExtensions.AddItem(item);
        return item;
    }

    /// <summary>A button in the toolbar.</summary>
    public ToolbarButton AddToolbarButton(string label, Action<World> run) => Add(new ToolbarButton(label, run));

    /// <summary>An entry in the toolbar's Tools menu.</summary>
    public MenuCommand AddCommand(string name, Action<World> run) => Add(new MenuCommand(name, run));

    /// <summary>A section at the bottom of the Inspector. Its <paramref name="draw"/> runs every frame, so it can show live values.</summary>
    public PanelItem AddPanel(string title, Action<PanelBuilder> draw) => Add(new PanelItem(title, draw));

    /// <summary>A section in the Inspector for every selected object.</summary>
    public ObjectInspector AddObjectInspector(string title, Action<PanelBuilder, GameObject> draw) => Add(new ObjectInspector(null, title, draw));

    /// <summary>A section in the Inspector for selected objects that have component <typeparamref name="T"/>.</summary>
    public ObjectInspector AddObjectInspector<T>(string title, Action<PanelBuilder, GameObject> draw) where T : Component =>
        Add(new ObjectInspector(typeof(T), title, draw));

    /// <summary>A keyboard shortcut.</summary>
    public ShortcutItem AddShortcut(KeyboardKey key, Action<World> run, bool ctrl = false, bool shift = false, string name = "") =>
        Add(new ShortcutItem(key, ctrl, shift, name.Length > 0 ? name : key.ToString(), run));
}

/// <summary>Draws labelled controls one below the other inside a panel. Every method returns true when the user changed something.</summary>
public sealed class PanelBuilder
{
    private readonly float _x, _w;
    private int _n;
    private const float LabelW = 112f;

    internal PanelBuilder(float x, float y, float w, int scope)
    {
        _x = x; Y = y; _w = w;
        Gui.SetScope(scope);
    }

    /// <summary>Where the next control is drawn.</summary>
    public float Y { get; private set; }

    /// <summary>Width available for controls.</summary>
    public float Width => _w;

    private string Key() => "x" + (_n++);

    public void Space(float height = 6f) => Y += height;

    public void Heading(string text)
    {
        var r = new Rectangle(_x - 4, Y, _w + 8, Gui.Row);
        Rl.DrawRectangleRec(r, Gui.PanelHeader);
        Gui.TextLeft(r, Gui.FitText(text, r.Width - 8), Gui.TextCol, 6);
        Y += Gui.Row + 4;
    }

    public void Label(string text, bool dim = false)
    {
        Gui.TextLeft(new Rectangle(_x, Y, _w, Gui.Row), Gui.FitText(text, _w), dim ? Gui.TextDim : Gui.TextCol);
        Y += Gui.Row + 2;
    }

    public void Dim(string text) => Label(text, dim: true);

    public void Separator()
    {
        Rl.DrawLine((int)_x, (int)(Y + 3), (int)(_x + _w), (int)(Y + 3), new Color(70, 72, 80, 255));
        Y += 8;
    }

    /// <summary>A full-width button. True on the frame it is clicked.</summary>
    public bool Button(string text)
    {
        bool hit = Gui.Button(new Rectangle(_x, Y, _w, Gui.Row + 2), text);
        Y += Gui.Row + 6;
        return hit;
    }

    /// <summary>Several buttons side by side. Returns the index of the clicked one, or -1.</summary>
    public int Buttons(params string[] labels)
    {
        int clicked = -1;
        float gap = 4f, bw = (_w - gap * (labels.Length - 1)) / Math.Max(1, labels.Length);
        for (int i = 0; i < labels.Length; i++)
            if (Gui.Button(new Rectangle(_x + i * (bw + gap), Y, bw, Gui.Row + 2), labels[i])) clicked = i;
        Y += Gui.Row + 6;
        return clicked;
    }

    private Rectangle Field(string label)
    {
        Gui.Text(_x, Y + (Gui.Row - Gui.FontSize) / 2f, Gui.FitText(label, LabelW - 4), Gui.TextDim);
        var r = new Rectangle(_x + LabelW, Y, _w - LabelW, Gui.Row);
        Y += Gui.Row + 3;
        return r;
    }

    public bool Toggle(string label, ref bool value) => Gui.Checkbox(Field(label), Key(), "", ref value);

    public bool Slider(string label, ref float value, float min, float max) => Gui.Slider(Field(label), Key(), ref value, min, max);

    public bool Slider(string label, ref int value, int min, int max)
    {
        float f = value;
        if (!Gui.Slider(Field(label), Key(), ref f, min, max)) return false;
        value = (int)MathF.Round(f);
        return true;
    }

    public bool Text(string label, ref string value) => Gui.TextField(Field(label), Key(), ref value);

    public bool Dropdown(string label, string[] options, ref int index) => Gui.Dropdown(Field(label), Key(), options, ref index);
}

/// <summary>The registry the admin editor draws from.</summary>
public static class EditorExtensions
{
    private static readonly List<EditorItem> _items = new();
    private static readonly List<EditorScript> _scripts = new();
    private static bool _dirty = true;
    private static World? _world;
    private static GameObject? _lastSelected;
    private static string _lastScene = "";

    static EditorExtensions() => ScriptManager.Applied += () => _dirty = true;

    public static IReadOnlyList<EditorItem> Items => _items;
    public static IReadOnlyList<EditorScript> Scripts => _scripts;

    /// <summary>Forget everything and register all tools again on the next frame.</summary>
    public static void Reload() => _dirty = true;

    internal static void AddItem(EditorItem item) => _items.Add(item);

    /// <summary>Every EditorScript class: the engine's own and the ones compiled from Scripts/.</summary>
    public static IReadOnlyList<Type> ScriptTypes()
    {
        var found = new List<Type>(typeof(EditorScript).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(EditorScript).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null));
        found.AddRange(ScriptManager.EditorTypes);
        return found.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    private static void Rebuild(World world)
    {
        _items.Clear();
        _scripts.Clear();
        _dirty = false;
        _world = world;
        foreach (var type in ScriptTypes())
        {
            try
            {
                var s = (EditorScript)Activator.CreateInstance(type)!;
                s.World = world;
                _scripts.Add(s);
                s.OnRegister(new EditorApi(type.Name, world));
            }
            catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
            {
                ErrorLog.Report("editor script " + type.Name + ".OnRegister", ex);
            }
        }
    }

    private static void Guard(EditorScript s, string what, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            s.Enabled = false;
            ErrorLog.Report($"editor script {s.GetType().Name}.{what}", ex);
        }
    }

    /// <summary>Runs an item's code; when it throws the item is switched off and the error goes to the log.</summary>
    internal static void Run(EditorItem item, Action action)
    {
        if (item.Faulted) return;
        try { action(); }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            item.Faulted = true;
            ErrorLog.Report($"editor tool {item.Owner} ({item.GetType().Name})", ex);
        }
    }

    /// <summary>Once per frame: (re)registers tools after a script reload and lets them update.</summary>
    public static void Update(World world, float dt)
    {
        if (_dirty || !ReferenceEquals(_world, world)) Rebuild(world);
        string scene = SceneManager.CurrentName;
        var sel = AdminEditor.Selected;
        bool selChanged = !ReferenceEquals(sel, _lastSelected), sceneChanged = scene != _lastScene;
        _lastSelected = sel; _lastScene = scene;
        foreach (var s in _scripts.ToList())
        {
            if (!s.Enabled) continue;
            if (selChanged) Guard(s, "OnSelectionChanged", () => s.OnSelectionChanged(sel));
            if (sceneChanged) Guard(s, "OnSceneLoaded", () => s.OnSceneLoaded(scene));
            Guard(s, "OnUpdate", () => s.OnUpdate(dt));
        }
    }

    /// <summary>Checks the shortcuts. <paramref name="queue"/> receives the code to run after the frame's drawing.</summary>
    internal static void HandleShortcuts(World world, bool ctrl, bool shift, Action<Action> queue)
    {
        foreach (var item in _items.ToList())
            if (item is ShortcutItem sc && !sc.Faulted && sc.Ctrl == ctrl && sc.Shift == shift && Rl.IsKeyPressed(sc.Key))
                queue(() => Run(sc, () => sc.Run(world)));
    }

    /// <summary>One exhaustive switch over every kind of item: what the editor shows for each.</summary>
    public static string Describe(EditorItem item) => item switch
    {
        ToolbarButton b => $"toolbar button '{b.Label}'",
        MenuCommand c => $"Tools command '{c.Name}'",
        PanelItem p => $"inspector panel '{p.Title}'",
        ObjectInspector o => $"object inspector '{o.Title}'" + (o.Component is null ? "" : $" for {o.Component.Name}"),
        ShortcutItem s => $"shortcut {(s.Ctrl ? "Ctrl+" : "")}{(s.Shift ? "Shift+" : "")}{s.Key}",
        _ => item.GetType().Name,
    };

    // ---- drawing (called by AdminEditor) -------------------------------------------------------------------------

    internal static IEnumerable<ToolbarButton> ToolbarButtons => _items.OfType<ToolbarButton>().Where(b => !b.Faulted);
    internal static List<MenuCommand> Commands => _items.OfType<MenuCommand>().Where(c => !c.Faulted).ToList();

    /// <summary>Draws the object inspectors for <paramref name="selected"/> and then the panels. Returns the new y.</summary>
    internal static float DrawInspector(World world, GameObject? selected, float x, float y, float w)
    {
        int scope = 20000;
        foreach (var item in _items.ToList())
        {
            if (item.Faulted) continue;
            switch (item)
            {
                case ObjectInspector oi when selected is not null && !selected.IsScene && (oi.Component is null || selected.Components.Any(c => oi.Component.IsInstanceOfType(c))):
                    y = Section(oi.Title, oi.Collapsed, c => oi.Collapsed = c, x, y, w, scope++, ui => Run(oi, () => oi.Draw(ui, selected)));
                    break;
                case PanelItem p:
                    y = Section(p.Title, p.Collapsed, c => p.Collapsed = c, x, y, w, scope++, ui => Run(p, () => p.Draw(ui)));
                    break;
                case ObjectInspector or ToolbarButton or MenuCommand or ShortcutItem:
                    break;   // not drawn in the inspector
            }
        }
        return y;
    }

    private static float Section(string title, bool collapsed, Action<bool> setCollapsed, float x, float y, float w, int scope, Action<PanelBuilder> draw)
    {
        y += 6;
        Gui.SetScope(scope + 5000);
        var head = new Rectangle(x - 4, y, w + 8, Gui.Row);
        bool hot = Gui.IsHot(head);
        Rl.DrawRectangleRec(head, hot ? Gui.Hover : Gui.PanelHeader);
        Gui.TextLeft(head, (collapsed ? "[+] " : "[-] ") + Gui.FitText(title, head.Width - 30), Gui.TextCol, 6);
        if (hot && Gui.LeftPressed && !Gui.Busy) setCollapsed(!collapsed);
        y += Gui.Row + 4;
        if (collapsed) return y;
        var ui = new PanelBuilder(x, y, w, scope);
        draw(ui);
        return ui.Y;
    }
}
