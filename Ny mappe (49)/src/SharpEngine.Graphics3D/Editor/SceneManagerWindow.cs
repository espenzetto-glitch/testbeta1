
using Raylib_cs;
using SharpEngine.Graphics3D.Scripting;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// Manage Scenes: every scene file in a list with check boxes, so many scenes can be opened, selected and deleted at once.
/// Deleted scenes are moved to Backups/DeletedScenes (not erased), and are taken out of the build list, the world map and merge groups.
/// Open with the toolbar's Manage button or Ctrl+Shift+M.
/// Each scene has a Code button: it opens that scene's own C# 15 code (Scripts/World/Scene_Name.cs) in a code editor inside this window.
/// </summary>
public static class SceneManagerWindow
{
    private static readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    private static float _scroll;
    private static string _info = "";
    private static bool _infoBad;
    private static bool _confirm;
    private static string? _lastClicked;
    private static double _lastClickTime;
    private static string? _openRequest;
    // code pane (the engine's code editor inside this window)
    private static readonly CodePane _pane = new(9320);

    public static bool IsOpen { get; private set; }

    public static void Open()
    {
        IsOpen = true; _confirm = false; _info = ""; _checked.RemoveWhere(n => !SceneManager.Exists(n));
        var own = ScriptManager.SceneCodePath(SceneManager.CurrentName);
        _pane.Show(File.Exists(own) ? own : null);   // the code editor is shown right away, on the open scene's code when it has some
    }
    public static void Close() { IsOpen = false; _confirm = false; _pane.Hide(); }

    /// <summary>The scene the user double-clicked / pressed Open for (taken once by the editor).</summary>
    internal static string? TakeOpenRequest() { var r = _openRequest; _openRequest = null; return r; }

    public static void Draw(World world)
    {
        if (!IsOpen) return;
        float sw = Gui.ScreenWidth, sh = Gui.ScreenHeight;
        bool code = _pane.IsOpen;
        float w = code ? MathF.Min(sw - 30f, 1500f) : MathF.Min(sw - 40f, 560f), h = code ? MathF.Min(sh - 30f, 820f) : MathF.Min(sh - 40f, 560f);
        var full = new Rectangle((sw - w) / 2f, (sh - h) / 2f, w, h);
        // d is the scene list: the whole window, or its left part while the code editor pane is open
        var d = code ? new Rectangle(full.X, full.Y, MathF.Min(400f, full.Width * 0.45f), full.Height) : full;
        Rl.DrawRectangle(0, 0, (int)sw, (int)sh, new Color(0, 0, 0, 130));
        Rl.DrawRectangleRec(full, new Color(44, 46, 52, 255));
        Rl.DrawRectangleLinesEx(full, 1f, new Color(20, 20, 24, 255));
        Gui.SetScope(9300);

        Rl.DrawRectangleRec(new Rectangle(full.X, full.Y, full.Width, 34), new Color(32, 34, 40, 255));
        Gui.Text(full.X + 10, full.Y + 7, "Manage Scenes", Gui.TextCol);
        if (Gui.Button(new Rectangle(full.X + full.Width - 30, full.Y + 5, 24, 24), "x")) { Close(); return; }
        bool compAtStart = CodeEditorWindow.CompletionOpen;   // Esc that closes the suggestion list must not also close this window

        var scenes = SceneManager.AvailableScenes();
        var inBuild = SceneManager.Config.Scenes.Select(s => SceneManager.NameOf(s.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _checked.RemoveWhere(n => !scenes.Contains(n, StringComparer.OrdinalIgnoreCase));

        float y = d.Y + 42;
        if (Gui.Button(new Rectangle(d.X + 8, y, 90, 26), "Select all")) { foreach (var n in scenes) if (!IsCurrent(n)) _checked.Add(n); _confirm = false; }
        if (Gui.Button(new Rectangle(d.X + 104, y, 90, 26), "Select none")) { _checked.Clear(); _confirm = false; }
        Gui.Text(d.X + 204, y + 5, Gui.FitText($"{scenes.Count} scene(s), {_checked.Count} selected", d.Width - 214), Gui.TextDim);

        y += 34;
        var list = new Rectangle(d.X + 8, y, d.Width - 16, d.Height - (y - d.Y) - 96);
        Rl.DrawRectangleRec(list, new Color(36, 38, 44, 255));
        Gui.SetScope(9310);
        const float rowH = 26;
        Gui.BeginScroll(list, ref _scroll, MathF.Max(1, scenes.Count) * rowH + 4);
        float ry = list.Y + 2 - _scroll;
        string? openNow = null, codeFor = null;
        foreach (var name in scenes)
        {
            if (ry + rowH >= list.Y && ry <= list.Y + list.Height)
            {
                var row = new Rectangle(list.X + 2, ry, list.Width - 12, rowH);
                bool current = IsCurrent(name);
                bool on = _checked.Contains(name);
                if (on) Rl.DrawRectangleRec(row, new Color(120, 50, 50, 255)); else if (Gui.IsHot(row)) Rl.DrawRectangleRec(row, Gui.Hover);

                var box = new Rectangle(row.X + 6, row.Y + 3, 20, rowH - 6);
                bool v = on;
                if (Gui.Checkbox(box, "msc" + name, "", ref v, enabled: !current, tick: true)) { if (v) _checked.Add(name); else _checked.Remove(name); _confirm = false; }
                else if (Gui.IsHot(row) && Gui.LeftPressed && Gui.Mouse.X > box.X + box.Width + 4 && Gui.Mouse.X < row.X + row.Width - 62)
                {
                    double now = Rl.GetTime();
                    if (_lastClicked == name && now - _lastClickTime < 0.45) openNow = name;
                    else if (!current) { if (!_checked.Remove(name)) _checked.Add(name); _confirm = false; }
                    _lastClicked = name; _lastClickTime = now;
                }

                string tags = (current ? "  [open]" : "") + (inBuild.Contains(name) ? "  [in build]" : "");
                const float codeW = 54;
                bool hasCode = ScriptManager.HasSceneCode(name);
                Gui.TextLeft(new Rectangle(row.X + 34, row.Y, row.Width - 40 - codeW, row.Height), Gui.FitText(name + tags + (hasCode ? "  [code]" : ""), row.Width - 44 - codeW),
                    current ? new Color(150, 200, 255, 255) : Gui.TextCol);
                if (Gui.Button(new Rectangle(row.X + row.Width - codeW - 4, row.Y + 2, codeW, rowH - 4), "Code", _pane.IsOpen && string.Equals(_pane.Current, ScriptManager.SceneCodePath(name), StringComparison.OrdinalIgnoreCase))) codeFor = name;
            }
            ry += rowH;
        }
        if (scenes.Count == 0) Gui.Text(list.X + 10, list.Y + 8, "No saved scenes.", Gui.TextDim);
        Gui.EndScroll(list);

        // bottom
        float by = d.Y + d.Height - 84;
        Gui.Text(d.X + 10, by, Gui.FitText("Click rows to tick them. Double-click opens a scene. Code edits its C# code. Deleted scenes go to Backups/DeletedScenes.", d.Width - 20), Gui.TextDim);
        if (_info.Length > 0) Gui.Text(d.X + 10, by + 20, Gui.FitText(_info, d.Width - 20), _infoBad ? new Color(240, 120, 120, 255) : new Color(120, 215, 130, 255));

        float bb = d.Y + d.Height - 38;
        bool any = _checked.Count > 0;
        string label = !any ? "Delete Selected" : _confirm ? $"Really delete {_checked.Count}? Click again" : $"Delete Selected ({_checked.Count})";
        if (Gui.Button(new Rectangle(d.X + 8, bb, 260, 28), label, _confirm, any))
        {
            if (!_confirm) _confirm = true;
            else
            {
                int done = SceneManager.DeleteMany(_checked.ToList(), out var problems);
                _checked.Clear(); _confirm = false;
                _infoBad = problems.Count > 0;
                _info = $"Deleted {done} scene(s)." + (problems.Count > 0 ? " " + problems[0] : "");
            }
        }
        if (Gui.Button(new Rectangle(d.X + d.Width - 88, bb, 80, 28), "Close")) Close();

        if (_pane.IsOpen)
        {
            float px = d.X + d.Width + 8;
            Rl.DrawRectangleRec(new Rectangle(d.X + d.Width, full.Y + 34, 1, full.Height - 34), new Color(20, 20, 24, 255));
            _pane.Draw(new Rectangle(px, full.Y + 42, full.X + full.Width - px - 8, full.Height - 50), ScriptManager.SceneCodePath(SceneManager.CurrentName), "Code (C# 15 preview)");
        }

        if (codeFor is not null) OpenSceneCode(codeFor);
        if (openNow is not null) { _openRequest = openNow; Close(); return; }
        if (Rl.IsKeyPressed(KeyboardKey.Escape) && !Gui.PopupOpen && !Gui.IsEditing && !compAtStart)
        {
            if (!CodeEditorWindow.HasFocus) Close();   // Esc while typing in the code leaves the window open
        }
    }

    /// <summary>Shows the code of a scene in the editor, creating its script from the template the first time.</summary>
    private static void OpenSceneCode(string name)
    {
        try { _pane.Select(ScriptManager.EnsureSceneCode(name)); _info = ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _info = "Could not create the scene code: " + ex.Message; _infoBad = true;
        }
    }

    private static bool IsCurrent(string name) => string.Equals(name, SceneManager.CurrentName, StringComparison.OrdinalIgnoreCase);
}